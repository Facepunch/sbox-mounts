namespace Source1;

/// <summary>Velocity correction used by Source physics controllers to carry a body toward a target.</summary>
static class ShadowController
{
	public static Vector3 AngularError( Rotation current, Rotation target )
	{
		var delta = Rotation.Difference( current, target );
		var axis = new Vector3( delta.x, delta.y, delta.z ).Normal;
		var angle = delta.Angle().DegreeToRadian();
		return axis * (delta.w < 0 ? -angle : angle);
	}

	public static Vector3 Velocity( Vector3 current, Vector3 error, float maxSpeed, float maxDamping, float arrivalTime = 0, float damping = 1 )
	{
		var correction = error / System.MathF.Max( arrivalTime, Time.Delta );
		return current - (current * damping).ClampLength( maxDamping ) + correction.ClampLength( maxSpeed );
	}
}
