namespace Halo2;

public sealed class ContrailAppearance
{
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

	LineRenderer line;

	internal static ProjectileContrail Create( ContrailAppearance appearance, Vector3 position, Vector3 velocity )
	{
		var go = new GameObject( false, "Projectile contrail" );
		go.NetworkMode = NetworkMode.Object;
		go.WorldPosition = position;
		var trail = go.AddComponent<ProjectileContrail>();
		trail.Appearance = appearance;
		trail.FiredAt = Time.NowDouble;
		trail.SetSegment( position, velocity, trail.FiredAt );
		go.NetworkSpawn( true, null );
		return trail;
	}

	internal void SetSegment( Vector3 position, Vector3 velocity, double time )
	{
		origin = position;
		Velocity = velocity;
		segmentTime = time;
	}

	internal void Finish( Vector3 position, double time )
	{
		SetSegment( position, Velocity, time );
		endTime = time;
	}

	protected override void OnStart()
	{
		if ( Application.IsDedicatedServer || Appearance is null ) return;
		line = GameObject.AddComponent<LineRenderer>();
		line.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
		line.UseVectorPoints = true;
		line.VectorPoints = [origin, origin, origin];
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
		line.VectorPoints[0] = origin + Velocity * (float)(head - segmentTime);
		double transition = System.Math.Clamp( now - Appearance.Transition, tail, head );
		line.VectorPoints[1] = origin + Velocity * (float)(transition - segmentTime);
		line.VectorPoints[2] = origin + Velocity * (float)(tail - segmentTime);
	}
}
