using Sandbox.Engine.Resources;
using Sandbox.Npcs.Layers;
using System;

namespace Overgrowth;

public sealed class CharacterNavigation : NavigationLayer
{
	bool followingJump;

	protected override void OnUpdate()
	{
		if ( Networking.IsHost && LinkTraversal is { } crossing && crossing.Type == NavMeshLinkType.Jump )
		{
			if ( TryJumpVelocity( WorldPosition, crossing.End, out var velocity ) )
			{
				Npc.Launch( velocity );
				followingJump = true;
			}
			else AbortLinkTraversal();
		}
		base.OnUpdate();
		if ( !followingJump ) return;
		if ( !Npc.IsAirborne ) { followingJump = false; return; }
		Npc.Animation.Traversal = NavMeshLinkType.Jump;
	}

	protected override void OnDisabled()
	{
		followingJump = false;
		base.OnDisabled();
	}

	// GetVelocityForTarget bounds the flight time by running speed and maximum launch height.
	// JumpToTarget tries the lowest arc first and accepts the first collision near the destination.
	internal bool TryJumpVelocity( Vector3 start, Vector3 end, out Vector3 velocity )
	{
		velocity = default;
		const float maxVertical = 5f * 1.7f * 40f;
		const float radius = 0.45f * 40f;
		const float toleranceSquared = 0.5f * 40f * 40f;
		float gravity = -Scene.PhysicsWorld.Gravity.z * RabbitGuard.FallGravityScale( Scene.PhysicsWorld );
		var delta = end - start;
		float discriminant = maxVertical * maxVertical - 2f * gravity * delta.z;
		if ( gravity <= 0f || discriminant <= 0f ) return false;
		if ( Npc is not RabbitGuard fighter ) return false;
		float earliest = delta.WithZ( 0f ).Length / (fighter.GroundRunSpeed * 1.5f);
		float latest = (MathF.Sqrt( discriminant ) + maxVertical) / gravity;
		if ( earliest > latest ) return false;
		start += Vector3.Up * radius;
		end += Vector3.Up * radius;
		for ( int arc = 0; arc < 3; arc++ )
		{
			float time = earliest + (latest - earliest) * (arc == 0 ? 0.15f : arc == 1 ? 0.55f : 1f);
			var launch = delta / time + Vector3.Up * (gravity * time * 0.5f);
			// A touching Box3D sweep reports the takeoff floor as an initial overlap.
			var from = start + Vector3.Up * 0.1f;
			for ( int i = 1; i <= 400; i++ )
			{
				float t = i * (20f / 120f);
				var to = start + launch * t + Vector3.Down * (0.5f * gravity * t * t);
				var hit = Scene.Trace.Sphere( radius, from, to ).IgnoreGameObjectHierarchy( GameObject )
					.IgnoreDynamic().WithoutTags( "trigger", "player", "npc" ).Run();
				if ( hit.Hit )
				{
					if ( !hit.StartedSolid && hit.EndPosition.DistanceSquared( end ) < toleranceSquared )
					{
						velocity = launch;
						return true;
					}
					break;
				}
				if ( to.z < end.z - MathF.Sqrt( toleranceSquared ) && to.z < from.z ) break;
				from = to;
			}
		}
		return false;
	}
}
