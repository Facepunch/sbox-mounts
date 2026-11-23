using System;

namespace Overgrowth;

internal sealed class RagdollDamping : Component, IScenePhysicsEvents
{
	ModelPhysics physics;
	WaterHotspot water;
	float stillTime;
	bool settled;
	Transform[] placedPose;
	internal Vector3 AverageVelocity { get; private set; }

	internal void EnterWater( WaterHotspot volume )
	{
		if ( physics.IsValid() && CharacterLoader.RagdollCenter( physics ) is { } center && volume.HeightAboveSurface( center ) < -0.8f )
			water = volume;
		else if ( water == volume ) water = null;
	}

	internal void SetPlacedPose()
	{
		placedPose = physics.Bodies.Select( part => part.Component.WorldTransform ).ToArray();
	}

	// New shapes establish contacts on their first physics step. Install the saved sleeping
	// pose on that island, so the initial contacts cannot displace a level-authored body.
	void IScenePhysicsEvents.PostPhysicsStep()
	{
		if ( placedPose is null || physics.Joints.Any( joint => !joint.Component.Body1.IsValid() ) ) return;
		for ( int i = 0; i < physics.Bodies.Count; i++ )
		{
			var body = physics.Bodies[i].Component;
			body.WorldTransform = placedPose[i];
			body.Velocity = body.AngularVelocity = Vector3.Zero;
		}
		placedPose = null;
		stillTime = 2f;
		settled = true;
		foreach ( var part in physics.Bodies )
			part.Component.Sleeping = true;
	}

	internal void Refresh()
	{
		placedPose = null;
		stillTime = 0f;
		settled = false;
		foreach ( var part in physics.Bodies )
		{
			if ( !part.Component.IsValid() ) continue;
			part.Component.LinearDamping = part.Component.AngularDamping = 0f;
			part.Component.Sleeping = false;
		}
	}

	internal void Reset( ModelPhysics model )
	{
		physics = model;
		stillTime = 0f;
		settled = false;
		var loader = CharacterLoader.FromModel( model.Model );
		loader?.FilterRagdollContacts( model );
		float gravityScale = RabbitGuard.FallGravityScale( Scene.PhysicsWorld );
		float scale = model.WorldScale.z;
		var parts = model.Model.Physics.Parts;
		for ( int i = 0; i < physics.Bodies.Count; i++ )
		{
			var part = physics.Bodies[i];
			if ( !part.Component.IsValid() ) continue;
			part.Component.MassOverride = parts[i].Mass * scale * scale * scale;
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
		AverageVelocity = mass > 0f ? momentum / mass : Vector3.Zero;
		if ( mass <= 0f || (settled && !awake) ) return;
		if ( settled ) stillTime = 0f;
		stillTime = AverageVelocity.Length < 0.4f * 40f ? stillTime + Time.Delta : 0f;
		settled = stillTime >= 2f;
		// Bullet's damping argument is the fraction of velocity lost over one second.
		// s&box takes a decay rate. At full damping Overgrowth puts the entire skeleton to sleep.
		if ( !water.IsValid() || !water.Contains( this ) ) water = null;
		float retention = (1f - stillTime * 0.5f) * (water is null ? 1f : 0.01f);
		float rate = settled ? 0f : -MathF.Log( retention );
		foreach ( var part in physics.Bodies )
		{
			var body = part.Component;
			if ( !body.IsValid() ) continue;
			body.LinearDamping = body.AngularDamping = rate;
			if ( settled ) body.Sleeping = true;
		}
	}
}
