using System;

namespace Overgrowth;

internal sealed class RagdollInjury : Component
{
	ModelPhysics physics;
	RagdollDamping damping;
	readonly RagdollMotorPose injuryMotor = new();
	RagdollFallPose injuryPose;
	float injuryAge, writheTime;
	float injuryMouth;
	[Sync( SyncFlags.Interpolate )] float MouthOpening { get; set; }
	float displayedMouth;

	protected override void OnUpdate()
	{
		if ( MouthOpening == displayedMouth ) return;
		if ( GetComponent<SkinnedModelRenderer>() is not { } renderer ) return;
		renderer.Morphs.Set( "mouth_open", MouthOpening, 0f );
		displayedMouth = MouthOpening;
	}

	internal void StartInjury( ModelPhysics model, RagdollFallPose pose, float age )
	{
		physics = model;
		damping = GetComponent<RagdollDamping>();
		damping.Enabled = false;
		injuryPose = pose;
		injuryAge = age;
		writheTime = 0f;
		injuryMouth = 0f;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || injuryPose is null || !physics.IsValid() ) return;
		Vector3 momentum = default;
		float mass = 0f;
		foreach ( var part in physics.Bodies )
		{
			if ( !part.Component.IsValid() ) continue;
			momentum += part.Component.Velocity * part.Component.Mass;
			mass += part.Component.Mass;
		}
		if ( mass <= 0f ) return;
		writheTime += Time.Delta;
		injuryAge += Time.Delta;
		float speed = momentum.Length / (mass * 40f);
		float strength = Math.Clamp( Math.Clamp( 2f - speed * 0.3f, 0.2f, 1f ) * (12f - injuryAge) * 0.1f, 0f, 0.9f );
		float open = MathF.Sin( Time.Now * 4f ) * 0.5f + MathF.Sin( Time.Now * 6.3f ) * 0.5f;
		injuryMouth += (open - injuryMouth) * (1f - MathF.Pow( 1f - strength, Time.Delta * 120f ));
		MouthOpening = Math.Max( 0f, injuryMouth );
		if ( strength > 0f )
		{
			float phase = (writheTime / injuryPose.Writhe.Clip.Duration) % 1f;
			injuryMotor.Sample( physics.Model, injuryPose, phase, 0f, 0f, injured: true );
			foreach ( var part in physics.Bodies )
				if ( part.Component.IsValid() && part.Component.Sleeping ) part.Component.Sleeping = false;
		}
		injuryMotor.Apply( physics, strength );
		if ( injuryAge < 12f ) return;
		Enabled = false;
	}

	protected override void OnDisabled()
	{
		if ( !Networking.IsHost || injuryPose is null ) return;
		injuryPose = null;
		if ( physics.IsValid() ) injuryMotor.Apply( physics, 0f );
		if ( damping.IsValid() ) damping.Enabled = true;
	}
}
