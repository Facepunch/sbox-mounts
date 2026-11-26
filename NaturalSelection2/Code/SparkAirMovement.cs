using System;

namespace NS2;

internal static class SparkAirMovement
{
	// Spark turns existing momentum toward the input before adding regular air acceleration.
	internal static Vector3 Steer( Vector3 velocity, Rotation view, Vector3 direction, float maxSpeed, float control, float acceleration, float dt )
	{
		var forward = view.Forward.WithZ( 0 ).Normal;
		var left = view.Left.WithZ( 0 ).Normal;
		float ahead = Math.Clamp( direction.Dot( forward ), -1, 1 );
		float sideways = direction.Dot( left );

		var planar = velocity.WithZ( 0 );
		float startSpeed = planar.Length;
		var wish = direction;
		if ( ahead != 0 && ahead * forward.Dot( planar.Normal ) >= -0.8f )
		{
			float lateralSpeed = planar.Dot( left );
			if ( sideways != 0 ) wish = left * Math.Sign( sideways );
			else wish = Math.Abs( lateralSpeed ) > 0.2f * maxSpeed ? -left * Math.Sign( lateralSpeed ) : Vector3.Zero;
		}

		float addSpeed = 1.3f - planar.Dot( wish );
		if ( ahead == 1 ) planar = (planar + forward * (2 * control * dt)).Normal * startSpeed;
		if ( addSpeed > 0 ) planar += wish * Math.Min( addSpeed, control * dt * 1.3f );
		planar += planar.Normal * (Math.Max( -velocity.z, 0 ) * dt * 0.34f);
		planar = planar.ClampLength( startSpeed + dt * 0.54f );

		float fraction = Math.Min( planar.Length / maxSpeed, 1 );
		var move = (view.Forward * ahead + view.Left * sideways).WithZ( 0 ).Normal;
		float speedLimit = Math.Max( planar.Length, maxSpeed );
		planar += move * (acceleration * (1 - fraction * fraction) * dt);
		return planar.ClampLength( speedLimit ).WithZ( velocity.z );
	}
}
