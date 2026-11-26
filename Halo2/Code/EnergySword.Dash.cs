using System;

namespace Halo2;

public sealed partial class EnergySword
{
	[Property] public float TargetAngle { get; set; }
	[Property] public float TargetRange { get; set; }

	sealed record Dash( PlayerController Player, Vector3 EntryVelocity );
	Dash activeDash;

	public override bool CanSecondaryAttack() => activeDash is null && base.CanSecondaryAttack();

	bool CanInterruptLunge => (activeDash is not null || MeleeActive)
		&& MeleeAction is "melee_dash" or "melee_dash_airborne" or "melee_lunge" or "melee_lunge_airborne";

	public override bool CanReload() => IsActive && CanInterruptLunge;

	public override void Reload()
	{
		if ( Networking.IsHost )
		{
			if ( CanReload() ) InterruptLunge();
		}
		else RequestInterrupt();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestInterrupt() => Reload();

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void InterruptLunge() => InterruptMelee();

	protected override string PrimaryMeleeAction()
	{
		if ( LungeDamage is null || Holder is not PlayerController player ) return null;
		var ray = player.EyeTransform.ForwardRay;
		var target = FindAttackTarget( player, ray, new( TargetAngle, TargetRange ), out var point );
		if ( !target.IsValid() ) return null;
		float distance = point.Distance( ray.Position );
		if ( distance <= MeleeRange ) return LungeAction( player );
		StartDash( target, target.WorldTransform.PointToLocal( point ), distance );
		return player.IsOnGround ? "melee_dash" : "melee_dash_airborne";
	}

	static string LungeAction( PlayerController player ) => player.IsOnGround ? "melee_lunge" : "melee_lunge_airborne";

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void StartDash( GameObject target, Vector3 localPoint, float distance )
	{
		if ( Holder is not PlayerController player || (!Networking.IsHost && player.IsProxy) ) return;
		var dash = new Dash( player, player.Body.Velocity );
		activeDash = dash;
		_ = RunDash( dash, target, localPoint, distance );
	}

	async System.Threading.Tasks.Task RunDash( Dash dash, GameObject target, Vector3 localPoint, float distance )
	{
		var player = dash.Player;
		var clock = TaskSource.Create( player.GameObject.EnabledToken );
		// Halo caps sword speed at 12 world units/s and builds up to it in three 30 Hz ticks.
		float step = MathF.Min( 12f * 120 / 30, distance * 0.375f );
		float duration = CooldownSeconds( 13 + (int)MathF.Round( MathF.Max( distance - 3 * step, 0 ) / step ) );
		TimeSince started = 0;
		try
		{
			// Movement belongs to the player and continues after the weapon is put away.
			while ( true )
			{
				await clock.FixedUpdate();
				if ( !ReferenceEquals( activeDash, dash ) ) return;
				var eye = player.EyeTransform;
				var delta = target.IsValid() ? target.WorldTransform.PointToWorld( localPoint ) - eye.Position : Vector3.Zero;
				if ( delta.Length <= MeleeRange || started >= duration ) break;
				if ( player.IsProxy ) continue;
				var direction = delta.Normal;
				if ( Vector3.Dot( eye.Forward, direction ) < 0.8660254f ) direction = eye.Forward;
				float ticks = MathF.Floor( started * 30 ) + 1;
				player.Body.Velocity = direction * (step * 30 * MathF.Min( 1, ticks / 3 ));
			}
		}
		finally
		{
			if ( ReferenceEquals( activeDash, dash ) ) StopMovement();
		}
		if ( !IsValid || !Networking.IsHost || Holder != player ) return;
		if ( player.IsProxy ) EndOwnerDash();
		if ( IsActive && MeleeAction is ("melee_dash" or "melee_dash_airborne") )
			PerformMeleeAction( LungeAction( player ), FirstPersonAnimations()?.Actions );
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	void EndOwnerDash() => StopMovement();

	void StopMovement()
	{
		if ( activeDash is not {} dash ) return;
		activeDash = null;
		var player = dash.Player;
		if ( player.IsValid() && !player.IsProxy && player.Body.IsValid() )
		{
			// Exit velocity is bounded against the velocity on entry to melee physics.
			var velocity = player.Body.Velocity.ClampLength( dash.EntryVelocity.Length + 0.8f * 120 );
			player.Body.Velocity = velocity.WithZ( MathF.Min( velocity.z, dash.EntryVelocity.z + 0.3f * 120 ) );
		}
	}

	protected override void OnHolstered()
	{
		if ( CanInterruptLunge ) InterruptMelee();
		base.OnHolstered();
	}
}
