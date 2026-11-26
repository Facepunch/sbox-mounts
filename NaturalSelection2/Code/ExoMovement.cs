namespace NS2;

public sealed class ExoMovement : SparkGroundMovement
{
	protected override float GroundAcceleration => 12;
	protected override float GroundFriction => 8;

	protected override float GetGroundSpeed( Vector3 direction, float speed )
	{
		// Spark's diagonal multiplier brings either backward strafe back to full speed.
		var forward = WorldRotation.Forward.WithZ( 0 ).Normal;
		return (direction + forward).IsNearlyZero( 0.000001f ) ? speed * 0.8f : speed;
	}
}
