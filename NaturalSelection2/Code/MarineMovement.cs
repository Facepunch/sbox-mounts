namespace NS2;

public sealed class MarineMovement : SparkGroundMovement
{
	protected override float GroundAcceleration => 13;
	protected override float GroundFriction => 9;

	protected override float GetGroundSpeed( Vector3 direction, float speed )
	{
		var forward = WorldRotation.Forward.WithZ( 0 ).Normal;
		if ( Vector3.Dot( direction, forward ) >= -0.000001f ) return speed;
		return speed * ((direction + forward).IsNearlyZero( 0.000001f ) ? 0.4f : 0.56f);
	}
}
