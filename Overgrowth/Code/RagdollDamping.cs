using System;

namespace Overgrowth;

internal sealed class RagdollDamping : Component, IScenePhysicsEvents
{
	ModelPhysics physics;
	WaterHotspot water;
	float hotspotDepth;
	TimeSince? stillSince;
	[Sync] internal bool Settled { get; private set; }
	Transform[] placedPose;

	internal void EnterWater( WaterHotspot volume )
	{
		GetComponent<CharacterRemains>()?.EnterWater( volume );
		if ( CharacterLoader.RagdollCenter( physics ) is not { } center ) return;
		water = volume;
		hotspotDepth = RabbitGuard.LegRadius / 40f - volume.HeightAboveSurface( center );
	}

	bool InWater => water.IsValid() && water.Contains( this );

	internal float WaterDepth( Vector3 center ) => InWater ? hotspotDepth
		: RabbitGuard.WaterDepthAt( Scene, center - Vector3.Up * RabbitGuard.LegRadius );

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
		stillSince = (TimeSince)2f;
		Settled = true;
		foreach ( var part in physics.Bodies )
			part.Component.Sleeping = true;
	}

	internal void Refresh()
	{
		placedPose = null;
		stillSince = null;
		Settled = false;
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
		stillSince = null;
		Settled = false;
		var loader = CharacterLoader.FromModel( model.Model );
		loader?.FilterRagdollContacts( model );
		CharacterCollisionRules.Apply( Scene.PhysicsWorld );
		float gravityScale = RabbitGuard.FallGravityScale( Scene.PhysicsWorld );
		float scale = model.WorldScale.z;
		var parts = model.Model.Physics.Parts;
		for ( int i = 0; i < physics.Bodies.Count; i++ )
		{
			var part = physics.Bodies[i];
			if ( !part.Component.IsValid() ) continue;
			part.Component.GameObject.Tags.Add( CharacterCollisionRules.RagdollPart );
			part.Component.MassOverride = parts[i].Mass * scale * scale * scale;
			if ( loader is not null )
				part.Component.PhysicsBody.SetInertiaTensor( loader.RagdollInertiaPerMass( part.Bone ) * (part.Component.Mass * scale * scale), Rotation.Identity );
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
		if ( Settled && !physics.Bodies.Any( part => part.Component.IsValid() && !part.Component.Sleeping ) ) return;
		if ( CharacterLoader.RagdollVelocity( physics ) is not { } velocity ) return;
		if ( Settled ) stillSince = null;
		if ( velocity.Length < 0.4f * 40f ) stillSince ??= (TimeSince)Time.Delta;
		else stillSince = null;
		float stillTime = stillSince?.Relative ?? 0f;
		Settled = stillTime >= 2f;
		// Bullet's damping argument is the fraction of velocity lost over one second.
		// s&box takes a decay rate. At full damping Overgrowth puts the entire skeleton to sleep.
		bool submerged = InWater && hotspotDepth > RabbitGuard.LegRadius / 40f + 0.8f;
		float retention = (1f - stillTime * 0.5f) * (submerged ? 0.01f : 1f);
		float rate = Settled ? 0f : -MathF.Log( retention );
		foreach ( var part in physics.Bodies )
		{
			var body = part.Component;
			if ( !body.IsValid() ) continue;
			body.LinearDamping = body.AngularDamping = rate;
			if ( Settled ) body.Sleeping = true;
		}
	}
}
