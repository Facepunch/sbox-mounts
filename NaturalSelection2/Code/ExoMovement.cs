namespace NS2;

public sealed class ExoMovement : SparkGroundMovement
{
	protected override float GroundAcceleration => 12;
	protected override float GroundFriction => 8;
}
