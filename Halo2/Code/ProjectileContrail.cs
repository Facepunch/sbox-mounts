namespace Halo2;

public sealed class ContrailAppearance
{
	public string Marker { get; set; }
	public string TexturePath { get; set; }
	public float Width { get; set; }
	public float Lifetime { get; set; }
	public float Transition { get; set; }
	public float PointRate { get; set; }
	public float Repeats { get; set; }
	public Color StartColor { get; set; }
	public Color EndColor { get; set; }
}

[Hide]
public sealed class ProjectileContrail : Component
{
	[Property] public ContrailAppearance Appearance { get; set; }
	[Property] public double FiredAt { get; set; }
	[Sync( SyncFlags.FromHost )] public Vector3 Velocity { get; private set; }
	[Sync( SyncFlags.FromHost )] Vector3 origin { get; set; }
	[Sync( SyncFlags.FromHost )] double segmentTime { get; set; }
	[Sync( SyncFlags.FromHost )] double endTime { get; set; }

	readonly record struct Segment( double Time, Vector3 Position, Vector3 Velocity );
	readonly List<Segment> history = new();
	LineRenderer line;
	GameObject projectile;
	Vector3 markerOffset;
	Vector3 worldOffset;

	internal static ProjectileContrail Create( ContrailAppearance appearance, Vector3 position, Vector3 velocity, GameObject projectile, Vector3 markerOffset )
	{
		var go = new GameObject( false, "Projectile contrail" );
		go.NetworkMode = NetworkMode.Object;
		go.WorldPosition = position;
		var trail = go.AddComponent<ProjectileContrail>();
		trail.Appearance = appearance;
		trail.FiredAt = Time.NowDouble;
		trail.projectile = projectile;
		trail.markerOffset = markerOffset;
		trail.SetSegment( position, velocity, trail.FiredAt );
		go.NetworkSpawn( true, null );
		return trail;
	}

	Vector3 MarkerOffset() => markerOffset == Vector3.Zero ? Vector3.Zero :
		projectile.IsValid() ? projectile.WorldRotation * markerOffset : worldOffset;

	internal void SetSegment( Vector3 position, Vector3 velocity, double time )
	{
		var offset = MarkerOffset();
		if ( Velocity.AlmostEqual( velocity ) && worldOffset.AlmostEqual( offset ) ) return;
		worldOffset = offset;
		origin = position + offset;
		Velocity = velocity;
		segmentTime = time;
		RememberSegment();
	}

	internal void Finish( Vector3 position, double time )
	{
		origin = position + MarkerOffset();
		segmentTime = time;
		endTime = time;
		RememberSegment();
	}

	void RememberSegment()
	{
		if ( Application.IsDedicatedServer ) return;
		var segment = new Segment( segmentTime, origin, Velocity );
		if ( history.Count > 0 && history[^1].Time >= segmentTime )
		{
			if ( history[^1].Time == segmentTime ) history[^1] = segment;
			return;
		}
		history.Add( segment );
	}

	protected override void OnStart()
	{
		if ( Application.IsDedicatedServer || Appearance is null ) return;
		line = GameObject.AddComponent<LineRenderer>();
		line.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
		line.UseVectorPoints = true;
		line.VectorPoints = [origin, origin];
		line.Opaque = line.CastShadows = false;
		line.Additive = true;
		// The line shader extends its vertices by this radius on either side of the centerline.
		line.Width = Appearance.Width * 0.5f;
		line.Color = new Gradient( new Gradient.ColorFrame( 0, Appearance.StartColor ),
			new Gradient.ColorFrame( 0.5f, Appearance.EndColor ), new Gradient.ColorFrame( 1, Appearance.EndColor ) );
		line.Texturing = TrailTextureConfig.Default with
		{
			Texture = Texture.Load( Appearance.TexturePath ), UnitsPerTexture = Velocity.Length / (Appearance.PointRate * Appearance.Repeats)
		};
	}

	protected override void OnUpdate()
	{
		if ( Appearance is null ) return;
		double now = Time.NowDouble;
		if ( endTime > 0 && now >= endTime + Appearance.Lifetime )
		{
			if ( Networking.IsHost ) GameObject.Destroy();
			else if ( line.IsValid() ) line.Enabled = false;
			return;
		}
		if ( !line.IsValid() ) return;
		double head = endTime > 0 ? System.Math.Min( endTime, now ) : now;
		double tail = System.Math.Min( head, System.Math.Max( FiredAt, now - Appearance.Lifetime ) );
		if ( history.Count == 0 || history[^1].Time < segmentTime ) RememberSegment();
		int expired = 0;
		while ( expired + 1 < history.Count && history[expired + 1].Time <= tail ) expired++;
		if ( expired > 0 ) history.RemoveRange( 0, expired );

		var points = line.VectorPoints;
		points.Clear();
		// The line samples from head to tail, so each segment is visited at most once.
		int index = history.Count - 1;
		Vector3 PositionAt( double time )
		{
			while ( index > 0 && history[index].Time > time ) index--;
			var segment = history[index];
			return segment.Position + segment.Velocity * (float)(time - segment.Time);
		}
		points.Add( PositionAt( head ) );
		if ( history.Count == 1 )
		{
			points.Add( PositionAt( System.Math.Clamp( now - Appearance.Transition, tail, head ) ) );
			points.Add( PositionAt( tail ) );
			return;
		}
		double interval = 1.0 / Appearance.PointRate;
		long birth = (long)System.Math.Ceiling( (head - FiredAt) / interval ) - 1;
		for ( double t = FiredAt + birth * interval; t > tail; t -= interval )
			points.Add( PositionAt( t ) );
		points.Add( PositionAt( tail ) );
	}
}
