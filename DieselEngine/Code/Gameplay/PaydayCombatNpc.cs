using Sandbox.Npcs;
using Sandbox.Npcs.Schedules;

namespace Diesel;

/// <summary>Local law enforcement escalates against detected offenders, not every player.</summary>
[Title( "PAYDAY Armed NPC" ), Group( "PAYDAY" )]
public sealed class PaydayCombatNpc : BaseCombatNpc
{
	[Property] public bool InitiallyHostile { get; set; }
	[Property] public bool RescueCivilians { get; set; } = true;
	[Property] public bool UseRadio { get; set; } = true;
	[Property] public float RadioRange { get; set; } = 1800;
	[Property] public float RadioDelay { get; set; } = 2.5f;
	[Sync( SyncFlags.FromHost )] public bool CallingBackup { get; private set; }
	public Vector3? ReportedPosition { get; private set; }
	readonly HashSet<GameObject> radioKnown = [];
	GameObject radioSuspect;
	Vector3 radioPosition;
	TimeUntil radioDelay, radioRetry, reportExpiry;
	int reportRevision;
	TimeUntil rescueScan;
	readonly Dictionary<PaydayCivilian, float> failedRescues = [];

	protected override void OnStart()
	{
		// This mount's delayed radio replaces the base's instant, repeated faction callout.
		CalloutRange = -1;
		base.OnStart();
	}

	protected override void SetupRelationships()
	{
		base.SetupRelationships();
		if ( !InitiallyHostile ) SetRelationship( Factions.Player, Disposition.Neutral );
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost && Health > 0 ) UpdateRadio();
		if ( Networking.IsHost && Health > 0 && !InitiallyHostile )
		{
			foreach ( var player in Scene.GetAllComponents<PlayerController>() )
			{
				if ( !player.Active || GetDisposition( player.GameObject ) == Disposition.Hostile ) continue;
				if ( PaydayPerception.Armed( player ) && PaydayPerception.CanSee( this, player.GameObject, player.EyePosition ) )
					Escalate( player.GameObject );
			}
			if ( Senses?.Disturbance is { Kind: StimulusKinds.Gunshot } sound )
				Escalate( sound.Source );
		}
		if ( Networking.IsHost && Health > 0 && rescueScan <= 0 )
		{
			rescueScan = 1;
			foreach ( var old in failedRescues.Keys.Where( c => !c.IsValid() ).ToArray() ) failedRescues.Remove( old );
			if ( ActiveSchedule is { Priority: < SchedulePriorities.Ambient } && FindRescue().IsValid() )
				EndCurrentSchedule();
		}
		base.OnUpdate();
	}

	PaydayCivilian FindRescue()
	{
		if ( !RescueCivilians || Leader.IsValid() ) return null;
		return Scene.GetAllComponents<PaydayCivilian>()
			.Where( c => c.CanRescue( this ) && c.WorldPosition.Distance( WorldPosition ) < 600
				&& (!failedRescues.TryGetValue( c, out var until ) || Time.Now >= until)
				&& PaydayPerception.CanSee( this, c.GameObject, c.EyePosition ) )
			.OrderBy( c => c.WorldPosition.Distance( WorldPosition ) ).FirstOrDefault();
	}

	protected override BaseSchedule SelectSchedule()
	{
		var normal = base.SelectSchedule();
		if ( normal.Priority < SchedulePriorities.Combat && ReportedPosition is { } position && reportExpiry > 0 )
		{
			var investigate = GetSchedule<PaydayInvestigateReport>();
			investigate.TargetPosition = position;
			investigate.Revision = reportRevision;
			return investigate;
		}
		if ( normal.Priority >= SchedulePriorities.Alert || Leader.IsValid() ) return normal;
		var civilian = FindRescue();
		if ( !civilian.IsValid() ) return normal;
		var rescue = GetSchedule<PaydayRescueCivilian>();
		rescue.Civilian = civilian;
		return rescue;
	}

	internal void FinishRescue( PaydayCivilian civilian )
	{
		if ( civilian.IsValid() ) failedRescues[civilian] = Time.Now + 20;
	}
	void Escalate( GameObject offender )
	{
		if ( !offender.IsValid() || offender.Scene != Scene ) return;
		var player = offender.GetComponent<PlayerController>() ?? offender.Root?.GetComponent<PlayerController>();
		if ( !player.IsValid() || GetDisposition( player.GameObject ) == Disposition.Hostile ) return;
		SetRelationship( player.GameObject, Disposition.Hostile );
		EndCurrentSchedule();
	}

	protected override void OnDamaged( IDamageable.DamageEvent e )
	{
		CancelRadio();
		radioRetry = 5;
		Escalate( e.Instigator );
		base.OnDamaged( e );
	}

	void UpdateRadio()
	{
		radioKnown.RemoveWhere( p => !p.IsValid() );
		if ( reportExpiry <= 0 ) ReportedPosition = null;
		if ( !UseRadio ) { CancelRadio(); return; }
		if ( radioRetry > 0 ) return;
		if ( !radioSuspect.IsValid() )
		{
			CancelRadio();
			foreach ( var player in Scene.GetAllComponents<PlayerController>() )
			{
				if ( !player.Active || radioKnown.Contains( player.GameObject )
					|| (!PaydayPerception.Armed( player ) && GetDisposition( player.GameObject ) != Disposition.Hostile)
					|| !PaydayPerception.CanSee( this, player.GameObject, player.EyePosition ) ) continue;
				radioSuspect = player.GameObject;
				radioPosition = player.WorldPosition;
				radioDelay = Math.Max( .1f, RadioDelay );
				CallingBackup = true;
				break;
			}
		}
		if ( !radioSuspect.IsValid() ) return;
		var suspect = radioSuspect.GetComponent<PlayerController>();
		if ( suspect.IsValid() && PaydayPerception.CanSee( this, radioSuspect, suspect.EyePosition ) )
			radioPosition = suspect.WorldPosition;
		if ( radioDelay > 0 ) return;
		radioKnown.Add( radioSuspect );
		foreach ( var officer in Scene.GetAllComponents<PaydayCombatNpc>() )
		{
			if ( officer == this || !officer.Active || officer.Health <= 0 || !officer.UseRadio || officer.Faction != Faction
				|| officer.WorldPosition.Distance( WorldPosition ) > RadioRange ) continue;
			officer.ReceiveReport( radioSuspect, radioPosition );
		}
		CancelRadio();
	}

	void ReceiveReport( GameObject offender, Vector3 position )
	{
		if ( !offender.IsValid() ) return;
		radioKnown.Add( offender ); // Receiving a report is not a fresh eyewitness sighting.
		SetRelationship( offender, Disposition.Hostile );
		if ( Senses?.VisibleHostiles.Count > 0 ) return;
		ReportedPosition = position; reportExpiry = 20; reportRevision++;
		EndCurrentSchedule();
	}

	internal void FinishReport( int revision )
	{
		if ( reportRevision == revision ) ReportedPosition = null;
	}

	void CancelRadio()
	{
		CallingBackup = false; radioSuspect = null;
	}

	protected override void OnDisabled()
	{
		if ( Networking.IsHost ) CancelRadio();
		base.OnDisabled();
	}
}
