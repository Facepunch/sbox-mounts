// Adapted from AnimationClient::GetMatrices and ASChar::SetActiveRagdollFallPose.
// Copyright 2022 Wolfire Games LLC. Apache-2.0; see LICENSE.
using System;

namespace Overgrowth;

internal sealed record RagdollFallPose( int[] Parents, RagdollFallPose.Layer Flail, RagdollFallPose.Layer Fetal, RagdollFallPose.Layer Catch, RagdollFallPose.Layer Writhe )
{
	internal sealed record Layer( RabbitClip Clip, float[] Weights );

	internal Rotation Sample( int bone, float phase, float protect, float front, out float weight )
	{
		var rotation = Flail.Clip.Animation.GetLocalTransform( phase, bone ).Rotation;
		float curl = Fetal.Weights[bone] * protect;
		float reach = Catch.Weights[bone] * front;
		// Both protective layers are single authored poses. The last added layer is mixed first.
		if ( curl > 0f ) rotation = RabbitClip.MixRotation( rotation, Fetal.Clip.Animation.GetLocalTransform( 0f, bone ).Rotation, curl );
		if ( reach > 0f ) rotation = RabbitClip.MixRotation( rotation, Catch.Clip.Animation.GetLocalTransform( 0f, bone ).Rotation, reach );
		weight = Math.Max( Flail.Weights[bone], Math.Max( curl, reach ) );
		return rotation.Normal;
	}
}

public sealed partial class RabbitGuard
{
	readonly RagdollMotorPose fallMotor = new();
	internal RagdollInjury ActiveInjury { get; set; }
	readonly List<(Vector3 Normal, float Depth)> fallContacts = [];

	Vector3 FallDanger( Vector3 center )
	{
		const float range = 4f * 40f;
		for ( float radius = range / 4f; radius <= range; radius *= 2f )
		{
			var trace = Scene.Trace.IgnoreDynamic().IgnoreKeyframed().WithoutTags( "trigger" ).UseHitPosition( true );
			fallContacts.Clear();
			foreach ( var overlap in trace.Sphere( radius, center, center ).RunAll() )
			{
				// Initial overlaps return a contact point. Recover the surface normal with a ray.
				var toward = (overlap.HitPosition - center).Normal;
				var hit = trace.Ray( center, overlap.HitPosition + toward ).Run();
				if ( !hit.Hit || hit.StartedSolid ) continue;
				float depth = radius - hit.Normal.Dot( center - hit.HitPosition );
				if ( depth > 0f ) fallContacts.Add( (hit.Normal, depth) );
			}
			if ( fallContacts.Count == 0 ) continue;
			fallContacts.Sort( static (a, b) => b.Depth.CompareTo( a.Depth ) );
			Vector3 slide = default;
			foreach ( var contact in fallContacts )
				slide += contact.Normal * Math.Max( 0f, contact.Depth - contact.Normal.Dot( slide ) );
			return -slide - slide.Normal * (range - radius);
		}
		return default;
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( Networking.IsHost && (dragWindow > 0f || draggedPhysics is not null) ) UpdateBodyDrag();
		if ( Downed && Networking.IsHost && ActiveInjury is not { Active: true } ) DriveFallingPose();
	}

	void DriveFallingPose()
	{
		if ( !ragdoll.IsValid() || boundAnimations?.FallPose is not { } pose ) return;
		Vector3 momentum = default;
		float mass = 0f;
		foreach ( var body in ragdoll.Bodies )
		{
			var rb = body.Component;
			if ( !rb.IsValid() ) continue;
			momentum += rb.Velocity * rb.Mass;
			mass += rb.Mass;
		}
		if ( mass <= 0f ) return;
		float strength = Math.Max( 0f, Math.Min( 0.8f, momentum.Length / mass / 400f ) - Math.Max( 0f, limpStun ) );
		if ( Unconscious ) strength *= Math.Clamp( 1f - (float)sinceUnconscious, 0f, 1f );
		if ( strength > 0f && CharacterLoader.RagdollCenter( ragdoll ) is { } center )
		{
			var danger = FallDanger( center );
			float protect = 1f + (Math.Clamp( danger.Length / 40f - 2f, 0f, 1f ) - 1f ) * strength / 0.8f;
			float front = Math.Max( 0f, GetTorsoDirections().Front.Dot( danger.Normal ) ) * protect;
			float fade = Math.Min( 1f, (float)fallAge * 4f );
			float phase = ((float)fallAge / pose.Flail.Clip.Duration) % 1f;
			fallMotor.Sample( Renderer.Model, pose, phase, protect * fade, front * fade );
		}
		fallMotor.Apply( ragdoll, strength );
	}
}
