using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	float underwaterBreath = 10f;
	bool zoneKilled;
	sealed class WaterDamage : BluntDamage;

	internal void WaterIntersect( WaterHotspot water, float dt )
	{
		if ( Health <= 0f || boundAnimations is null || !Renderer.TryGetBoneTransform( boundAnimations.HeadBone, out var head ) ) return;
		var headPoint = head.Position - head.Rotation.Forward * (boundAnimations.Head.HeadHalfLength * WorldScale.z);
		underwaterBreath = water.HeightAboveSurface( headPoint ) < 0f ? underwaterBreath - dt : 10f;
		float height = water.HeightAboveSurface( CharacterCenter( GameObject ) );
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

	void TakeWaterDamage()
	{
		zoneKilled = true;
		((IDamageable)this).OnDamage( new WaterDamage { Damage = 100f, Position = CharacterCenter( GameObject ) } );
	}
}
