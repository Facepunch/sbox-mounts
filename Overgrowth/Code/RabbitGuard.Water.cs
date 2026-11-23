using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	float underwaterBreath = 10f;
	TimeUntil nextWadingSound, nextSplashSound;
	bool zoneKilled;
	sealed class WaterDamage : BluntDamage;

	internal void WaterIntersect( WaterHotspot water, float dt )
	{
		if ( Health <= 0f || boundAnimations is null || !Renderer.TryGetBoneTransform( boundAnimations.HeadBone, out var head ) ) return;
		var headPoint = head.Position - head.Rotation.Forward * (boundAnimations.Head.HeadHalfLength * WorldScale.z);
		float headHeight = water.HeightAboveSurface( headPoint );
		underwaterBreath = headHeight < 0f ? underwaterBreath - dt : 10f;
		var center = CharacterCenter( GameObject );
		float height = water.HeightAboveSurface( center );
		var velocity = BodyVelocity;
		UpdateWaterSounds( center, LegRadius / 40f - height, headHeight, velocity / 40f );
		if ( height < 0.1f )
		{
			float depth = Math.Min( 1f, 0.1f - height );
			float drag = MathF.Pow( 1f - 0.1f * depth, dt * 120f );
			Vector3 Slow( Vector3 v ) => new( v.x * drag, v.y * drag, v.z );
			if ( !Downed )
			{
				if ( GetComponent<Rigidbody>() is { MotionEnabled: true } body ) body.Velocity = Slow( body.Velocity );
				else if ( GetComponent<NavMeshAgent>() is { Active: true } agent ) agent.Velocity = Slow( agent.Velocity );
			}
			jumpFuel = Math.Min( jumpFuel, JumpSustain * 40f * (1f - depth) );
		}
		if ( underwaterBreath < 0f || height < -3f || (water.Lethal && height < -1f) )
		{
			KnockoutShield = 0;
			TakeWaterDamage();
			if ( IsValid && Health > 0f ) GoLimp();
		}
		// Both wet hotspot scripts also damage every overlapping fighter marked by a lethal volume.
		if ( water.Lethal && IsValid && Health > 0f ) TakeWaterDamage();
	}

	void UpdateWaterSounds( Vector3 center, float depth, float headHeight, Vector3 velocity )
	{
		float speed = velocity.Length;
		if ( depth < 2f && speed > 4f && (!IsOnGround || Downed || RollProgress < 1f) && nextSplashSound <= 0f )
		{
			nextSplashSound = 0.5f;
			PlayAttachedSound( "Data/Sounds/water_foley/bodyfall_water.xml", center );
			ReportSound( GameObject, center, HeardSound.LoudFoley, 5f );
		}
		if ( depth > 0.25f && headHeight > -0.2f && speed > 1f && nextWadingSound <= 0f )
		{
			nextWadingSound = 0.3f / (1f + velocity.WithZ( 0f ).Length * 0.05f);
			PlayAttachedSound( depth > 0.7f ? "Data/Sounds/water_foley/wading_through_water.xml" : "Data/Sounds/water_foley/steps_water.xml", center );
		}
	}

	void TakeWaterDamage()
	{
		zoneKilled = true;
		((IDamageable)this).OnDamage( new WaterDamage { Damage = 100f, Position = CharacterCenter( GameObject ) } );
	}
}
