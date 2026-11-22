using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string StandFromFront = "r_standfromfront", StandFromBack = "r_standfromback2";
	[Property] public CapsuleCollider MovementCapsule { get; set; }
	[Sync] bool Downed { get; set; }
	ModelPhysics ragdoll;
	TimeSince sinceKnockdown;

	protected override void OnDamaged( Component.IDamageable.DamageEvent hit )
	{
		base.OnDamaged( hit );
		if ( Health < 1f || hit.AcceptedDamage <= 0 || !Renderer.IsValid() ) return;
		var attacker = hit.Instigator;
		var move = attacker.IsValid() ? attacker.GetComponent<RabbitGuard>()?.strike : null;
		if ( CanAttack( attacker ) ) Remember( attacker );
		if ( !Downed )
		{
			EndGetUpBlend();
			// Older saved instances predate the explicit prefab reference.
			MovementCapsule ??= GetComponentInChildren<CapsuleCollider>();
			Movement.Stop();
			Movement.Enabled = false;
			MovementCapsule.Enabled = false;
			strike = null;
			blockHealth = 0;
			// Copy the live pose before letting the physics skeleton drive it.
			ragdoll = Renderer.GetOrAddComponent<ModelPhysics>( false );
			ragdoll.Renderer = Renderer;
			ragdoll.Model = Renderer.Model;
			ragdoll.IgnoreRoot = true;
			ragdoll.CopyBonesFrom( Renderer, true );
			ragdoll.Enabled = true;
			Downed = true;
			Speak( "hit" );
		}

		sinceKnockdown = 0;
		if ( move is null ) return;
		var direction = (WorldPosition - attacker.WorldPosition).Normal;
		var right = new Vector3( -direction.y, direction.x, direction.z );
		var impact = direction * move.ImpactDirection.x + right * move.ImpactDirection.y + Vector3.Up * move.ImpactDirection.z;
		// Overgrowth applies this force for one 120 Hz physics step.
		ApplyHitImpulse( hit.DamageInfo.Position, impact * (move.Force * 40f / 120f) );
	}

	void ApplyHitImpulse( Vector3 point, Vector3 impulse )
	{
		float Weight( Rigidbody rb ) => rb.Mass / (rb.WorldPosition.DistanceSquared( point ) + 16f);
		float total = ragdoll.Bodies.Where( b => b.Component.IsValid() ).Sum( b => Weight( b.Component ) );
		if ( total <= 0 ) return;
		foreach ( var body in ragdoll.Bodies )
			if ( body.Component.IsValid() ) body.Component.ApplyImpulse( impulse * (Weight( body.Component ) / total) );
	}

	void UpdateKnockdown()
	{
		if ( sinceKnockdown < 1f || !ragdoll.IsValid() || boundAnimations is null ) return;
		Vector3 center = default, velocity = default;
		float mass = 0;
		int count = 0;
		foreach ( var body in ragdoll.Bodies )
		{
			var rb = body.Component;
			if ( !rb.IsValid() ) continue;
			center += rb.WorldPosition;
			velocity += rb.Velocity * rb.Mass;
			mass += rb.Mass;
			count++;
		}
		if ( count == 0 || mass <= 0 || (velocity / mass).LengthSquared >= 20f * 40f * 40f ) return;
		center /= count;
		var extent = MovementCapsule.WorldScale;
		var capsule = new Capsule( MovementCapsule.Start * extent, MovementCapsule.End * extent, MovementCapsule.Radius * extent.x );
		var ground = Scene.Trace.Ray( center, center + Vector3.Down * 40f )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		if ( !ground.Hit || ground.StartedSolid || ground.Normal.z <= 0.5f ) return;

		// On a slope the bottom sphere touches uphill of the point directly below its center.
		var lift = capsule.Radius * (1f / ground.Normal.z - 1f);
		var feet = ground.EndPosition + Vector3.Up * lift;
		var floor = Scene.Trace.Capsule( capsule, feet + Vector3.Up * 0.5f, feet + Vector3.Down )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		if ( !floor.Hit || floor.StartedSolid || floor.Normal.z <= 0.5f ) return;

		Vector3 front = default, up = default;
		foreach ( var name in boundAnimations.TorsoBones )
		{
			if ( !Renderer.TryGetBoneTransform( name, out var bone ) ) continue;
			front += bone.Rotation.Up;
			up += bone.Rotation.Forward;
		}
		var facing = -up.WithZ( 0 );
		CapturePhysicsPose();
		ragdoll.Enabled = false;
		Renderer.ClearPhysicsBones();
		WorldPosition = floor.EndPosition;
		if ( !facing.IsNearlyZero() ) WorldRotation = Rotation.LookAt( facing );
		BeginGetUpBlend();
		MovementCapsule.Enabled = true;
		Movement.Enabled = true;
		var agent = GetComponent<NavMeshAgent>( true );
		agent.Enabled = true;
		agent.SetAgentPosition( WorldPosition );
		Stance = front.z < 0f ? StandFromFront : StandFromBack;
		reactionDuration = boundAnimations.Clips[Stance].Duration;
		if ( Movement is Sandbox.Npcs.Layers.NavigationLayer navigation )
			navigation.HoldMovement( reactionDuration );
		ReactionPhase = 0;
		Downed = false;
	}
}
