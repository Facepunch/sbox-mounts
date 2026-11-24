using Sandbox.Npcs;
using Sandbox.Npcs.Schedules;

namespace Diesel;

/// <summary>Local law enforcement escalates against detected offenders, not every player.</summary>
[Title( "PAYDAY Armed NPC" ), Group( "PAYDAY" )]
public sealed class PaydayCombatNpc : BaseCombatNpc
{
	[Property] public bool InitiallyHostile { get; set; }
	[Property] public bool RescueCivilians { get; set; } = true;
	TimeUntil rescueScan;
	readonly Dictionary<PaydayCivilian, float> failedRescues = [];

	protected override void SetupRelationships()
	{
		base.SetupRelationships();
		if ( !InitiallyHostile ) SetRelationship( Factions.Player, Disposition.Neutral );
	}

	protected override void OnUpdate()
	{
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
		Escalate( e.Instigator );
		base.OnDamaged( e );
	}
}
