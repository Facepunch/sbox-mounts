using Sandbox.Npcs;

namespace Diesel;

/// <summary>Local law enforcement escalates against detected offenders, not every player.</summary>
[Title( "PAYDAY Armed NPC" ), Group( "PAYDAY" )]
public sealed class PaydayCombatNpc : BaseCombatNpc
{
	[Property] public bool InitiallyHostile { get; set; }

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
				var item = player.GetComponent<BaseInventoryComponent>()?.ActiveItem;
				if ( item is BaseCombatWeapon { IsHeld: true, IsActive: true } && CanSeePlayer( player ) )
					Escalate( player.GameObject );
			}
			if ( Senses?.Disturbance is { Kind: StimulusKinds.Gunshot } sound )
				Escalate( sound.Source );
		}
		base.OnUpdate();
	}

	bool CanSeePlayer( PlayerController player )
	{
		var delta = player.EyePosition - EyePosition;
		if ( delta.Length > (Senses?.SightRange ?? 1600) ) return false;
		if ( Vector3.Dot( WorldRotation.Forward, delta.Normal ) < MathF.Cos( (Senses?.FieldOfView ?? 140) * MathF.PI / 360 ) ) return false;
		var trace = Scene.Trace.Ray( EyePosition, player.EyePosition ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		return !trace.Hit || PaydayInteraction.BelongsTo( trace.GameObject, player.GameObject );
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
