using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string StandFromFront = "r_standfromfront", StandFromBack = "r_standfromback2", BlockFromGround = "r_blockfrontfromground";
	internal static bool IsGetUp( string stance ) => stance is StandFromFront or StandFromBack or BlockFromGround;
	[ConVar( "overgrowth_sparring", ConVarFlags.Replicated, Help = "Overgrowth fighters recover their health on a lethal hit and can keep fighting." )]
	public static bool Sparring { get; set; }
	[Property] public CapsuleCollider MovementCapsule { get; set; }
	[Sync] bool Downed { get; set; }
	ModelPhysics ragdoll;
	TimeSince sinceKnockdown;
	sealed class StrikeDamage : DamageInfo
	{
		public Vector3 Impulse { get; init; }
	}

	StrikeDamage lethalBlow;

	protected override void OnDamaged( Component.IDamageable.DamageEvent hit )
	{
		base.OnDamaged( hit );
		if ( Sparring && Health < 1f && hit.AcceptedDamage > 0 ) Health = MaxHealth;
		if ( Health < 1f ) return;
		if ( hit.AcceptedDamage <= 0 || !Renderer.IsValid() ) return;
		var attacker = hit.Instigator;

		NoticeAttacker( attacker );
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
			ReleaseLegs();
			ragdoll.Enabled = true;
			Downed = true;
			Speak( "hit" );
		}

		sinceKnockdown = 0;
		if ( hit.DamageInfo is StrikeDamage blow ) ApplyHitImpulse( ragdoll, blow.Position, blow.Impulse );
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		lethalBlow = death.DamageInfo as StrikeDamage;
		base.OnDeath( death );
	}

	protected override GameObject CreateRagdoll()
	{
		var corpse = base.CreateRagdoll();
		if ( lethalBlow is not null && corpse.IsValid() && corpse.GetComponent<ModelPhysics>() is { } physics )
			ApplyHitImpulse( physics, lethalBlow.Position, lethalBlow.Impulse );
		return corpse;
	}

	static void ApplyHitImpulse( ModelPhysics ragdoll, Vector3 point, Vector3 impulse )
	{
		float Weight( Rigidbody rb ) => rb.Mass / (rb.PhysicsBody.MassCenter.DistanceSquared( point ) + 16f);
		float total = ragdoll.Bodies.Where( b => b.Component.IsValid() ).Sum( b => Weight( b.Component ) );
		if ( total <= 0 ) return;
		foreach ( var body in ragdoll.Bodies )
			if ( body.Component.IsValid() ) body.Component.ApplyImpulse( impulse * (Weight( body.Component ) / total) );
	}

	bool TryGetUp( Vector3? blockFacing = null )
	{
		if ( !ragdoll.IsValid() || boundAnimations is null ) return false;
		if ( !blockFacing.HasValue && sinceKnockdown < 1f ) return false;
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
		if ( count == 0 || mass <= 0 ) return false;
		if ( !blockFacing.HasValue && (velocity / mass).LengthSquared >= 20f * 40f * 40f ) return false;
		center /= count;
		var extent = MovementCapsule.WorldScale;
		var capsule = new Capsule( MovementCapsule.Start * extent, MovementCapsule.End * extent, MovementCapsule.Radius * extent.x );
		var ground = Scene.Trace.Ray( center, center + Vector3.Down * 40f )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		if ( !ground.Hit || ground.StartedSolid || ground.Normal.z <= 0.5f ) return false;

		// On a slope the bottom sphere touches uphill of the point directly below its center.
		var lift = capsule.Radius * (1f / ground.Normal.z - 1f);
		var feet = ground.EndPosition + Vector3.Up * lift;
		var floor = Scene.Trace.Capsule( capsule, feet + Vector3.Up * 0.5f, feet + Vector3.Down )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		if ( !floor.Hit || floor.StartedSolid || floor.Normal.z <= 0.5f ) return false;

		Vector3 front = default, up = default;
		foreach ( var name in boundAnimations.TorsoBones )
		{
			if ( !Renderer.TryGetBoneTransform( name, out var bone ) ) continue;
			front += bone.Rotation.Up;
			up += bone.Rotation.Forward;
		}
		var facing = blockFacing ?? -up.WithZ( 0 );
		CapturePhysicsPose();
		ragdoll.Enabled = false;
		Renderer.ClearPhysicsBones();
		WorldPosition = floor.EndPosition;
		if ( !facing.IsNearlyZero() ) WorldRotation = Rotation.LookAt( facing );
		MovementCapsule.Enabled = true;
		Movement.Enabled = true;
		var agent = GetComponent<NavMeshAgent>( true );
		agent.Enabled = true;
		agent.SetAgentPosition( WorldPosition );
		StartGetUp( blockFacing.HasValue ? BlockFromGround : front.z < 0f ? StandFromFront : StandFromBack );
		Downed = false;
		return true;
	}

	void StartGetUp( string stance )
	{
		duckAmount = 1f;
		duckVelocity = 0f;
		Stance = stance;
		BeginGetUpBlend();
		Renderer.Animation.Parameters.Set( "stance", Stance );
		Renderer.Animation.Parameters.Set( "reaction_phase", 0f );
		playing = Stance;
		reactionDuration = boundAnimations.Clips[Stance].Duration;
		if ( Movement is Sandbox.Npcs.Layers.NavigationLayer navigation )
			navigation.HoldMovement( reactionDuration );
		ReactionPhase = 0;
	}
}
