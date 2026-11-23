using System;

namespace Overgrowth;

internal sealed class RagdollDamping : Component
{
	ModelPhysics physics;
	float stillTime;
	bool settled;
	internal void Reset( ModelPhysics model )
	{
		physics = model;
		stillTime = 0f;
		settled = false;
		var loader = Sandbox.Mounting.Directory.Get( "overgrowth" )?.GetByPath( model.Model.ResourcePath ) as CharacterLoader;
		float gravityScale = RabbitGuard.FallGravityScale( Scene.PhysicsWorld );
		foreach ( var part in physics.Bodies )
		{
			if ( !part.Component.IsValid() ) continue;
			// The source keeps mass and joints on FZX bones that have no collision geometry.
			if ( loader?.NonCollidingBones.Contains( part.Bone ) == true )
			{
				foreach ( var collider in part.Component.GetComponents<Collider>() ) collider.ColliderFlags |= ColliderFlags.IgnoreTraces;
				foreach ( var shape in part.Component.PhysicsBody.Shapes ) shape.DisableAllCollision();
			}
			part.Component.GravityScale = gravityScale;
			part.Component.LinearDamping = part.Component.AngularDamping = 0f;
			part.Component.Sleeping = false;
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy || !physics.IsValid() || !physics.Active ) return;
		Vector3 momentum = default;
		float mass = 0f;
		bool awake = false;
		foreach ( var part in physics.Bodies )
		{
			var body = part.Component;
			if ( !body.IsValid() ) continue;
			momentum += body.Velocity * body.Mass;
			mass += body.Mass;
			awake |= !body.Sleeping;
		}
		if ( mass <= 0f || (settled && !awake) ) return;
		if ( settled ) stillTime = 0f;
		stillTime = (momentum / mass).Length < 0.4f * 40f ? stillTime + Time.Delta : 0f;
		settled = stillTime >= 2f;
		// Bullet's damping argument is the fraction of velocity lost over one second.
		// s&box takes a decay rate. At full damping Overgrowth puts the entire skeleton to sleep.
		float rate = settled ? 0f : -MathF.Log( 1f - stillTime * 0.5f );
		foreach ( var part in physics.Bodies )
		{
			var body = part.Component;
			if ( !body.IsValid() ) continue;
			body.LinearDamping = body.AngularDamping = rate;
			if ( settled ) body.Sleeping = true;
		}
	}
}
