using System;

namespace Overgrowth;

internal static class FlightPath
{
	// GetVelocityForTarget is shared by the character and item launch code in aschar.as.
	internal static Vector3 Velocity( Vector3 delta, float gravity, float horizontalSpeed, float verticalSpeed, float arc, out float time )
	{
		time = 0f;
		float discriminant = verticalSpeed * verticalSpeed - 2f * gravity * delta.z;
		if ( horizontalSpeed <= 0f || gravity <= 0f || discriminant <= 0f ) return default;
		float earliest = delta.WithZ( 0f ).Length / horizontalSpeed;
		float latest = (MathF.Sqrt( discriminant ) + verticalSpeed) / gravity;
		if ( earliest > latest ) return default;
		time = earliest + (latest - earliest) * arc;
		return time > 0f ? delta / time + Vector3.Up * (gravity * time * 0.5f) : default;
	}
}
