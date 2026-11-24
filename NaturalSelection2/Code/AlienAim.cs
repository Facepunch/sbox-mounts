using System;

namespace NS2;

internal static class AlienAim
{
	// Spark's SlerpRadians advances by a fixed number of radians along the shortest arc.
	internal static float ApproachAngle( float from, float to, float radians )
	{
		float step = radians.RadianToDegree();
		return from + Math.Clamp( Angles.NormalizeAngle( to - from ), -step, step );
	}

	internal static Rotation Turn( Rotation view, Vector3 aim, float turnRate )
	{
		if ( aim.IsNearlyZero() ) return view;
		var from = view.Angles();
		var to = Rotation.LookAt( aim ).Angles();
		// BotMotion scales turn speed when fighting or tracking difficult targets.
		float dt = Time.Delta is > 0 and <= 0.2f ? Time.Delta : 1f / 30;
		float step = turnRate * dt;
		float yaw = ApproachAngle( from.yaw, to.yaw, step );
		float pitch = ApproachAngle( from.pitch, to.pitch, step ).DegreeToRadian();
		// BotMotion keeps the desired direction's horizontal length while easing its vertical component.
		float horizontal = aim.Normal.WithZ( 0 ).Length;
		return Rotation.From( MathF.Atan2( MathF.Sin( pitch ), horizontal ).RadianToDegree(), yaw, 0 );
	}
}
