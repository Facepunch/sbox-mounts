namespace Source1;

/// <summary>Velocity correction used by Source physics controllers to carry a body toward a target.</summary>
static class ShadowController
{
	public static Vector3 Velocity( Vector3 current, Vector3 error, float maxSpeed, float maxDamping, float arrivalTime = 0, float damping = 1 )
	{
		var correction = error / System.MathF.Max( arrivalTime, Time.Delta );
		return current - (current * damping).ClampLength( maxDamping ) + correction.ClampLength( maxSpeed );
	}
}
