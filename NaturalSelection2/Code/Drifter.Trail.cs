using System;

namespace NS2;

public sealed partial class Drifter
{
	const int TrailSegments = 8;
	const float SegmentLength = 0.5f * Scale;
	const double VisibilityDuration = 1.2;
	static readonly Vector3 TrailOffset = new Vector3( -0.9f, 0, 0.3f ) * Scale;

	struct TrailSegment
	{
		public Vector3 Position;
		public GameObject Effect;
	}

	TrailSegment[] trail;
	Vector3? previousTrailOrigin;
	Vector3 lastAttachment;
	bool trailMoving, trailSettled;
	double trailChanged;

	void UpdateTrail()
	{
		if ( !Graphics.IsAvailable || !Renderer.IsValid() ) return;
		var basis = Renderer.WorldTransform;
		var attachment = basis.PointToWorld( TrailOffset );
		bool moving = previousTrailOrigin.HasValue && previousTrailOrigin.Value != basis.Position && Renderer.Active && Renderer.RenderOptions.Game;
		if ( moving != trailMoving )
		{
			trailMoving = moving;
			trailChanged = Time.NowDouble;
		}
		else if ( previousTrailOrigin.HasValue && attachment == lastAttachment && !moving && trailSettled )
		{
			return;
		}
		previousTrailOrigin = basis.Position;
		lastAttachment = attachment;
		trail ??= new TrailSegment[TrailSegments];

		double visibleThrough = Math.Ceiling( (Time.NowDouble - trailChanged) / VisibilityDuration * TrailSegments );
		trailSettled = !moving && visibleThrough >= TrailSegments;
		var position = attachment;
		var rotation = basis.Rotation;
		for ( int i = 0; i < trail.Length; i++ )
		{
			ref var segment = ref trail[i];
			segment.Position = position;
			if ( i + 1 < trail.Length )
			{
				// Drifters have zero bend stiffness. Each segment stays put until the one ahead pulls it.
				var back = (trail[i + 1].Position - position).ClampLength( SegmentLength );
				position += back;
				rotation = back.LengthSquared > 0 ? Rotation.LookAt( -back ) : Rotation.Identity;
			}

			if ( i + 1 <= visibleThrough && !trailMoving && segment.Effect.IsValid() )
			{
				CinematicLoader.Play( "cinematics/alien/drifter/trail_fadeout.cinematic", segment.Effect.WorldPosition, segment.Effect.WorldRotation );
				segment.Effect.Destroy();
				segment.Effect = null;
			}
			if ( segment.Effect.IsValid() )
			{
				segment.Effect.WorldTransform = new Transform( segment.Position, rotation );
			}
			else if ( i + 1 <= visibleThrough && trailMoving )
			{
				var effect = new GameObject( true, "Drifter trail" ) { NetworkMode = NetworkMode.Never, WorldTransform = new Transform( segment.Position, rotation ) };
				var clip = i < 2 ? "trail1" : i < 5 ? "trail2" : "trail3";
				CinematicLoader.AttachEmitters( $"cinematics/alien/drifter/{clip}.cinematic", effect );
				segment.Effect = effect;
			}
		}
	}

	void ClearTrail()
	{
		if ( trail is not null )
			foreach ( var segment in trail )
				if ( segment.Effect.IsValid() ) segment.Effect.Destroy();

		trail = null;
		previousTrailOrigin = null;
		trailMoving = trailSettled = false;
	}
}
