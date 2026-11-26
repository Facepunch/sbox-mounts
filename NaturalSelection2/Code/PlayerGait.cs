using Sandbox.Animation;
using Sandbox.Npcs;
using System;

namespace NS2;

internal static class PlayerGait
{
	internal static bool IsEnemyListener( BaseNpc npc )
	{
		foreach ( var player in npc.Scene.GetAllComponents<PlayerController>() )
			if ( player.Active && !player.IsProxy )
				return npc.GetDisposition( player.GameObject ) == Disposition.Hostile;

		return false;
	}

	internal static GraphParameters UpdatePose( BaseNpc npc, float maxSpeed, Rotation facing )
	{
		var output = npc.Renderer.Animation.IsValid ? npc.Renderer.Animation.Parameters : null;
		var velocity = npc.Animation.MoveVelocity.WithZ( 0 );
		output?.Set( "move_speed", velocity.Length / (maxSpeed * MapLoader.Scale) );
		output?.Set( "move_yaw", velocity.IsNearlyZero() ? 0f :
			MathF.Atan2( Vector3.Dot( velocity, facing.Forward ), Vector3.Dot( velocity, facing.Left ) ).RadianToDegree() );
		return output;
	}

	internal static bool CanPlayStep( BaseNpc npc ) => npc.Animation.IsGrounded &&
		npc.Animation.MoveVelocity.LengthSquared > 0.75f * 0.75f * MapLoader.Scale * MapLoader.Scale;
}
