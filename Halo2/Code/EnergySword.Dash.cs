using System;

namespace Halo2;

public sealed partial class EnergySword
{
	[Property] public float TargetAngle { get; set; }
	[Property] public float TargetRange { get; set; }

	GameObject dashTarget;
	Vector3 targetPoint, entryVelocity;
	float peakSpeed;
	TimeSince dashStarted;
	float dashDuration;
	bool dashing;

	public override bool CanSecondaryAttack() => !dashing && base.CanSecondaryAttack();

	protected override string PrimaryMeleeAction()
	{
		if ( LungeDamage is null || Holder is not PlayerController player ) return null;
		var ray = player.EyeTransform.ForwardRay;
		var contact = BulletTrace( ray, TargetRange, 0 ).Run();
		GameObject target = contact.GameObject;
		Vector3 point = contact.HitPosition;
		if ( !contact.Hit || !IsEnemy( target, player ) )
		{
			FindAimTarget( player, new( TargetAngle, TargetRange ), default, 1, out target, out var offset );
			if ( !target.IsValid() ) return null;
			point = ray.Position + offset;
		}
		float distance = point.Distance( ray.Position );
		if ( distance <= MeleeRange ) return LungeAction( player );
		StartDash( target, target.WorldTransform.PointToLocal( point ), distance );
		return player.IsOnGround ? "melee_dash" : "melee_dash_airborne";
	}

	static bool IsEnemy( GameObject target, PlayerController player )
	{
		if ( !target.IsValid() ) return false;
		if ( target.GetComponentInParent<Sandbox.Npcs.BaseNpc>() is {} npc )
			return npc.Health > 0 && npc.GetDisposition( player.GameObject ) == Sandbox.Npcs.Disposition.Hostile;
		return target.GetComponentInParent<PlayerController>().IsValid();
	}

	static string LungeAction( PlayerController player ) => player.IsOnGround ? "melee_lunge" : "melee_lunge_airborne";

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void StartDash( GameObject target, Vector3 localPoint, float distance )
	{
		if ( Holder is not PlayerController player || (!Networking.IsHost && player.IsProxy) ) return;
		dashTarget = target;
		targetPoint = localPoint;
		entryVelocity = player.Body.Velocity;
		// Halo caps sword speed at 12 world units/s and builds up to it in three 30 Hz ticks.
		float step = MathF.Min( 12f * 120 / 30, distance * 0.375f );
		peakSpeed = step * 30;
		dashDuration = CooldownSeconds( 13 + (int)MathF.Round( MathF.Max( distance - 3 * step, 0 ) / step ) );
		dashStarted = 0;
		dashing = true;
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( !dashing || Holder is not PlayerController player ) return;
		var eye = player.EyeTransform;
		var delta = dashTarget.IsValid() ? dashTarget.WorldTransform.PointToWorld( targetPoint ) - eye.Position : Vector3.Zero;
		float distance = delta.Length;
		if ( !IsActive || distance <= MeleeRange || dashStarted >= dashDuration )
		{
			StopMovement( player );
			if ( Networking.IsHost && IsActive )
			{
				if ( player.IsProxy ) EndOwnerDash();
				PerformMeleeAction( LungeAction( player ), FirstPersonAnimations()?.Actions );
			}
			return;
		}
		if ( player.IsProxy ) return;
		var direction = delta / distance;
		if ( Vector3.Dot( eye.Forward, direction ) < 0.8660254f ) direction = eye.Forward;
		float elapsedTicks = MathF.Floor( dashStarted * 30 ) + 1;
		float speed = peakSpeed * MathF.Min( 1, elapsedTicks / 3 );
		player.Body.Velocity = direction * speed;
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	void EndOwnerDash()
	{
		if ( dashing && Holder is PlayerController player ) StopMovement( player );
	}

	void StopMovement( PlayerController player )
	{
		dashing = false;
		dashTarget = null;
		if ( player.IsValid() && !player.IsProxy && player.Body.IsValid() )
		{
			// Exit velocity is bounded against the velocity on entry to melee physics.
			var velocity = player.Body.Velocity.ClampLength( entryVelocity.Length + 0.8f * 120 );
			player.Body.Velocity = velocity.WithZ( MathF.Min( velocity.z, entryVelocity.z + 0.3f * 120 ) );
		}
	}

	protected override void OnHolstered()
	{
		if ( dashing ) StopMovement( Holder as PlayerController );
		base.OnHolstered();
	}
}
