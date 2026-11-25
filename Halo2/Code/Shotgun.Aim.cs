namespace Halo2;

public sealed partial class Shotgun
{
	[Property] public float AutoAimAngle { get; set; } = 8 * System.MathF.PI / 180;
	[Property] public float MagnetismAngle { get; set; } = 16 * System.MathF.PI / 180;
	[Property] public float MagnetismRange { get; set; } = 720;
	[Property] public float MagnetismFriction { get; set; } = 0.6f;
	[Property] public float MagnetismAdhesion { get; set; } = 0.7f;
	[Property] public float InconsequentialTargetScale { get; set; } = 0.5f;


	void PlayerController.IEvents.OnEyeAngles( ref Angles angles )
	{
		if ( !Input.UsingController || Input.MouseDelta != Vector2.Zero || Holder is not PlayerController player ) return;
		// Halo checks the look rates in radians per second and the two movement axes independently.
		const float threshold = 0.0001f;
		float frame = RealTime.Delta;
		if ( frame <= 0 ) return;
		float lookThreshold = threshold.RadianToDegree() * frame;
		var move = Input.AnalogMove;
		if ( System.MathF.Abs( angles.yaw ) <= lookThreshold && System.MathF.Abs( angles.pitch ) <= lookThreshold &&
			System.MathF.Abs( move.x ) <= threshold && System.MathF.Abs( move.y ) <= threshold ) return;
		float confidence = FindAimTarget( player, new( AutoAimAngle, AutoAimRange ), new( MagnetismAngle, MagnetismRange ),
			InconsequentialTargetScale, out var target, out var offset );
		if ( confidence <= 0 ) return;
		angles *= 1 - MagnetismFriction.Clamp( 0, 1 ) * confidence;
		var velocity = Sandbox.Npcs.BaseNpc.GetVelocity( target ) - player.Velocity;
		angles += TrackingDelta( offset, velocity ) * (MagnetismAdhesion.Clamp( 0, 1 ) * confidence);
	}

	static Angles TrackingDelta( Vector3 offset, Vector3 velocity )
	{
		float horizontalSquared = offset.x * offset.x + offset.y * offset.y;
		// TU1.5 suppresses tracking at the yaw singularity (0.0001 square world units).
		if ( horizontalSquared <= 0.0001f * 120 * 120 ) return default;
		float horizontal = System.MathF.Sqrt( horizontalSquared );
		float yawRate = (offset.x * velocity.y - offset.y * velocity.x) / horizontalSquared;
		float radialSpeed = (offset.x * velocity.x + offset.y * velocity.y) / horizontal;
		float pitchRate = (offset.z * radialSpeed - velocity.z * horizontal) / offset.LengthSquared;
		float frame = RealTime.Delta;
		return new Angles( (pitchRate.RadianToDegree() * Time.Delta).Clamp( -90 * frame, 90 * frame ),
			(yawRate.RadianToDegree() * Time.Delta).Clamp( -180 * frame, 180 * frame ), 0 );
	}
}
