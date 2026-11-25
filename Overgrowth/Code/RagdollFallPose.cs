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
	readonly List<(Vector3 Normal, float Depth)> slideContacts = [];

	Vector3 FallDanger( Vector3 center )
	{
		const float range = 4f * 40f;
		for ( float radius = range / 4f; radius <= range; radius *= 2f )
		{
			var slide = SlideSphere( center, radius );
			if ( slideContacts.Count == 0 ) continue;
			return -slide - slide.Normal * (range - radius);
		}
		return default;
	}

	Vector3 SlideSphere( Vector3 center, float radius )
	{
		slideContacts.Clear();
		var trace = Scene.Trace.IgnoreDynamic().IgnoreKeyframed().WithoutTags( "trigger" ).UseHitPosition();
		foreach ( var hit in trace.Sphere( radius, center, center ).RunAll() )
		{
			var separation = center - hit.HitPosition;
			float distance = separation.Length;
			if ( distance <= 0f || distance >= radius ) continue;
			var normal = separation / distance;
			// A mesh overlap can report a neighbouring triangle edge even on a flat tessellated floor.
			if ( hit.Triangle >= 0 )
			{
				var surface = trace.Ray( center, hit.HitPosition - normal ).Run();
				if ( surface.Hit && !surface.StartedSolid && surface.Shape == hit.Shape )
				{
					var face = trace.Ray( center, center - surface.Normal * radius ).Run();
					if ( face.Hit && face.Shape == hit.Shape && !face.StartedSolid && face.Normal.Dot( surface.Normal ) > 0.9999f )
					{
						slideContacts.Add( (face.Normal, radius - face.Normal.Dot( center - face.HitPosition )) );
						continue;
					}
				}
			}
			slideContacts.Add( (normal, radius - separation.Dot( normal )) );
		}
		return SphereSlideOffset();
	}

	// BulletWorld::ApplySphereSlide resolves the deepest contact plane first.
	Vector3 SphereSlideOffset()
	{
		slideContacts.Sort( static (a, b) => b.Depth.CompareTo( a.Depth ) );
		Vector3 offset = default;
		foreach ( var contact in slideContacts )
		{
			float depth = contact.Depth - contact.Normal.Dot( offset );
			if ( depth > 0.00001f * 40f ) offset += contact.Normal * depth;
		}
		return offset;
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( Networking.IsHost )
		{
			UpdateAirborneVelocity();
			AdvanceAirborneAction();
		}
		UpdateCharacterContacts();
		if ( Networking.IsHost && (dragWindow > 0f || draggedPhysics is not null) ) UpdateBodyDrag();
		if ( Downed && Networking.IsHost && ActiveInjury is not { Active: true } ) DriveFallingPose();
	}

	void DriveFallingPose()
	{
		if ( !ragdoll.IsValid() || boundAnimations?.FallPose is not { } pose ) return;
		if ( CharacterLoader.RagdollVelocity( ragdoll ) is not { } velocity ) return;
		float strength = Math.Max( 0f, Math.Min( 0.8f, velocity.Length / 400f ) - RagdollStun );
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
		else strength = 0f;
		ragdoll.GetComponent<RagdollHands>()?.Advance( pose.Flail.Clip, fallAge, Time.Delta, strength );
		fallMotor.Apply( ragdoll, strength );
	}
}
