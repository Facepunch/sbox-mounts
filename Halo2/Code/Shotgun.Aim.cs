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
		float confidence = LookConfidence( player, out var target, out var offset );
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

	// Both angular and distance confidence are full up to half the authored limit.
	static float Falloff( float value, float limit ) => limit > 0 ? (2 * (1 - value / limit)).Clamp( 0, 1 ) : 0;

	float LookConfidence( PlayerController player, out GameObject targetObject, out Vector3 targetOffset )
	{
		targetObject = null;
		targetOffset = default;
		var ray = player.EyeTransform.ForwardRay;
		float bestAutoAim = 0, bestMagnetism = 0;
		float bestDistance = float.PositiveInfinity, bestAngle = float.PositiveInfinity;
		foreach ( var obj in Scene.FindInPhysics( new Sphere( ray.Position, System.MathF.Max( AutoAimRange, MagnetismRange ) ) ) )
		{
			var npc = obj.GetComponentInParent<Sandbox.Npcs.BaseNpc>();
			if ( !npc.IsValid() || npc.Health <= 0 || !npc.Renderer.IsValid() ) continue;
			if ( npc.GetDisposition( player.GameObject ) != Sandbox.Npcs.Disposition.Hostile ) continue;
			var renderer = npc.Renderer;
			if ( renderer.Model is not {} model ) continue;
			foreach ( var target in ModelTarget.For( model ) )
			{
				if ( !target.TryAimPoint( renderer, ray, out var point, out _ ) ) continue;
				var offset = point - ray.Position;
				float distance = offset.Length;
				var direction = offset.Normal;
				float angle = System.MathF.Acos( Vector3.Dot( ray.Forward, direction ).Clamp( -1, 1 ) );
				float autoAim = Falloff( angle, AutoAimAngle ) * Falloff( distance, AutoAimRange );
				float magnetism = Falloff( angle, MagnetismAngle ) * Falloff( distance, MagnetismRange );
				magnetism *= target.MagnetismWeight * (target.Inconsequential ? InconsequentialTargetScale : 1);
				if ( autoAim == 0 && magnetism == 0 ) continue;
				if ( autoAim < bestAutoAim || (autoAim == bestAutoAim && magnetism < bestMagnetism) ) continue;
				if ( autoAim == bestAutoAim && magnetism == bestMagnetism &&
					(distance > bestDistance || (distance == bestDistance && angle >= bestAngle)) ) continue;
				var obstruction = BulletTrace( new Ray( ray.Position, direction ), distance, 0 ).IgnoreGameObjectHierarchy( npc.GameObject ).Run();
				if ( obstruction.Hit || obstruction.StartedSolid ) continue;
				bestAutoAim = autoAim;
				bestMagnetism = magnetism;
				bestDistance = distance;
				bestAngle = angle;
				targetObject = npc.GameObject;
				targetOffset = offset;
			}
		}
		return bestMagnetism;
	}
}
