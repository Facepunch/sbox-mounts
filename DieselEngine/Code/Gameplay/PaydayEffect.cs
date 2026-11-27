namespace Diesel;

/// <summary>Bounded, cosmetic playback of original Diesel billboard particle stacks.</summary>
[Title( "PAYDAY Effect" ), Group( "PAYDAY" )]
public sealed partial class PaydayEffect : Component
{
	[Property] public string EffectPath { get; set; }
	[Property] public bool DestroyWhenFinished { get; set; } = true;
	[Property] public int ParticleLimit { get; set; } = 512;
	public int ParticleCount => particles.Sum( p => p.Views.Count );
	public int RespawnCount { get; private set; }
	public int ChildSpawnCount { get; private set; }
	[Property] public GameObject Source { get; set; }
	[Property] public Transform SourceTransform { get; set; } = global::Transform.Zero;
	public string[] UnsupportedOperators => definition?.Unsupported ?? [];
	EffectDefinition definition;
	readonly List<Particle> particles = [];
	readonly HashSet<XmlNode> started = [];
	float elapsed;
	int generation;
	bool ready;
	bool hadSource;

	sealed class Particle
	{
		public XmlNode Atom;
		public List<View> Views = [];
		public Vector3 Position, Velocity, Size;
		public Vector3 Color;
		public float Opacity, TickCredit;
		public float Age, Angle, Spin, Life;
		public int Frame;
	}
	sealed record View( XmlNode Visual, SpriteRenderer Renderer );
	static float N( XmlNode n, string key, float fallback = 0 ) => EffectDefinition.Number( n, key, fallback );
	static Vector3 V( XmlNode n, string key, Vector3 fallback = default ) => EffectDefinition.Vector( n, key, fallback );
	static Vector3 C( XmlNode n, string key, float time, Vector3 fallback ) => EffectDefinition.Curve( n, key, time, fallback );
	static float R( XmlNode n, string min = "min", string max = "max", float fallback = 0 )
	{
		float a = N( n, min, fallback ), b = N( n, max, a );
		return Game.Random.Float( Math.Min( a, b ), Math.Max( a, b ) );
	}
	static Vector3 RV( XmlNode n ) => Vector3.Lerp( V( n, "min" ), V( n, "max" ), new Vector3( Game.Random.Float(), Game.Random.Float(), Game.Random.Float() ) );

	protected override void OnEnabled() => _ = Initialize();
	async Task Initialize()
	{
		int token = ++generation;
		hadSource = Source is not null;
		try
		{
			var mount = await Sandbox.Mounting.Directory.Mount( "payday" ) as PaydayMount;
			if ( !IsValid || token != generation ) return;
			definition = mount.GetEffect( EffectPath );
			if ( !Application.IsDedicatedServer )
				foreach ( var path in definition.Textures )
					mount.EffectTextures[path] = await ResourceLibrary.LoadAsync<Texture>( $"mount://payday/{path}.texture.vtex" )
						?? throw new InvalidDataException( $"Missing effect texture {path}." );
			if ( !IsValid || !Active || token != generation ) return;
			elapsed = 0; started.Clear(); ready = true;
			RespawnCount = ChildSpawnCount = 0;
		}
		catch ( Exception e )
		{
			Log.Warning( $"PAYDAY effect {EffectPath}: {e.Message}" );
			if ( IsValid && token == generation ) Stop();
		}
	}
	protected override void OnUpdate()
	{
		if ( !ready ) return;
		if ( hadSource )
		{
			if ( !Source.IsValid() || !Source.Active )
			{
				Stop();
				if ( DestroyWhenFinished && (Networking.IsHost || GameObject.NetworkMode == NetworkMode.Never) ) GameObject.Destroy();
				return;
			}
			WorldTransform = Source.WorldTransform.ToWorld( SourceTransform );
		}
		children.RemoveWhere( g => !g.IsValid() );
		elapsed += Time.Delta;
		foreach ( var atom in definition.Atoms )
		{
			if ( started.Contains( atom ) || elapsed < N( atom, "start_time" ) ) continue;
			started.Add( atom );
			SpawnAtom( atom );
		}
		UpdateRespawns();
		foreach ( var p in particles.ToArray() )
		{
			float atomAge = elapsed - N( p.Atom, "start_time" );
			if ( p.Atom.Element( "simulatorstack" )?.Element( "ager" ) is not null ) p.Age += Time.Delta;
			if ( atomAge >= p.Life )
			{
				foreach ( var view in p.Views ) if ( view.Renderer.IsValid() ) view.Renderer.GameObject.Destroy();
				particles.Remove( p ); continue;
			}
			var sim = p.Atom.Element( "simulatorstack" );
			foreach ( var op in sim?.Children ?? [] )
			{
				float time = op.Attribute( "per_particle_age" ) == "true" ? p.Age : atomAge;
				switch ( op.Name )
				{
					case "constantacceleration":
						var acceleration = Coordinates.Position( V( op, "acceleration" ) );
						if ( op.Attribute( "relative" ) == "effect" ) acceleration = WorldRotation * acceleration;
						p.Velocity += acceleration * Time.Delta;
						break;
					case "velocityintegrator": p.Position += p.Velocity * Time.Delta; break;
					case "scaledvelocityintegrator": p.Position += p.Velocity * C( op, "scale_keys", time, Vector3.One ).x * Time.Delta; break;
					case "anglevelocityintegrator": p.Angle += p.Spin * Time.Delta; break;
					case "eventtick": TickChildren( p, op ); break;
				}
			}
			foreach ( var view in p.Views ) if ( view.Renderer.IsValid() ) Render( p, view, atomAge );
		}
		float end = definition.Atoms.Select( a => N( a, "lifetime", 1 ) < 0 ? float.PositiveInfinity
			: N( a, "start_time" ) + N( a, "lifetime", 1 ) ).DefaultIfEmpty().Max();
		if ( DestroyWhenFinished && elapsed >= end && particles.Count == 0 && children.Count == 0
			&& (Networking.IsHost || GameObject.NetworkMode == NetworkMode.Never) ) GameObject.Destroy();
	}
	void SpawnAtom( XmlNode atom )
	{
		if ( Application.IsDedicatedServer ) return;
		var initial = atom.Element( "initializerstack" );
		var visuals = atom.Element( "visualizerstack" )?.Elements( "billboard" ).ToArray() ?? [];
		int count = (int)Math.Clamp( R( atom, "min_size", "max_size", 1 ), 0, 512 );
		int available = Math.Min( Math.Clamp( ParticleLimit, 0, 2048 ) - particles.Sum( p => Math.Max( 1, p.Views.Count ) ),
			2048 - Scene.GetAllComponents<PaydayEffect>().Sum( e => e.particles.Sum( p => Math.Max( 1, p.Views.Count ) ) ) );
		count = Math.Min( count, available / Math.Max( 1, visuals.Length ) );
		for ( int i = 0; i < count; i++ )
		{
			var positionNode = initial?.Element( "boxrandomposition" ) ?? initial?.Element( "localboxrandomposition" );
			var position = Coordinates.Position( RV( positionNode ) );
			var velocityNode = initial?.Element( "boxrandomvelocity" );
			var velocity = Coordinates.Position( RV( velocityNode ) );
			if ( velocityNode?.Attribute( "relative" ) != "world" ) velocity = WorldRotation * velocity;
			var sizeNode = initial?.Element( "boxrandomaspectsize" );
			var size = sizeNode is not null ? V( sizeNode, "size", Vector3.One ) * R( sizeNode, fallback: 1 )
				: RV( initial?.Element( "boxrandomsize" ) );
			float age = R( initial?.Element( "boxrandomage" ) );
			if ( initial?.Element( "constantage" ) is { } constantAge ) age = N( constantAge, "age" );
			float angle = R( initial?.Element( "boxrandomangle" ) );
			float spin = R( initial?.Element( "boxrandomanglevelocity" ) );
			int frame = Game.Random.Int( 0, Math.Max( 0, (int)N( initial?.Element( "boxrandomuvoffset" ), "num_frames", 1 ) - 1 ) );
			float life = N( atom, "lifetime", 1 );
			var particle = new Particle
			{
				Atom = atom,
				Position = positionNode?.Attribute( "relative" ) == "world" ? WorldPosition + position : WorldTransform.PointToWorld( position ),
				Velocity = velocity + inheritedVelocity * N( atom, "inherit_velocity_multiplier", 1 ), Size = size * Coordinates.Scale,
				Age = age, Angle = angle, Spin = spin, Frame = frame, Life = life < 0 ? float.PositiveInfinity : life,
				Color = initial?.Element( "boxrandomcolor" ) is { } rgb ? RV( rgb ) : new Vector3( 255 ),
				Opacity = R( initial?.Element( "boxrandomopacity" ), fallback: 255 )
			};
			if ( initial?.Element( "boxrandomcolorgradient" ) is { } gradient )
				particle.Color = V( gradient, "color", new Vector3( 255 ) ) * R( gradient, fallback: 1 );
			foreach ( var visual in visuals )
			{
				if ( string.IsNullOrEmpty( visual.Attribute( "texture" ) ) ) continue;
				var go = new GameObject( GameObject, false, atom.Attribute( "name" ) ) { NetworkMode = NetworkMode.Never };
				var renderer = go.AddComponent<SpriteRenderer>();
				renderer.Lighting = false; renderer.Shadows = false; renderer.IsSorted = true;
				renderer.Opaque = false; renderer.Additive = visual.Attribute( "blend_mode" ) is "add" or "add_no_alpha";
				renderer.Billboard = SpriteRenderer.BillboardMode.Always; renderer.PlaybackSpeed = 0;
				var view = new View( visual, renderer );
				particle.Views.Add( view ); Render( particle, view, 0 ); go.Enabled = true;
			}
			particles.Add( particle );
		}
	}
	void Render( Particle p, View view, float atomAge )
	{
		var v = view.Visual;
		float time = v.Attribute( "per_particle_age" ) == "true" ? p.Age : atomAge;
		var size = v.Attribute( "size_input" ) switch
		{
			"constant" => V( v, "size", Vector3.One ) * Coordinates.Scale,
			"keys" => C( v, "size_keys", time, Vector3.One ) * Coordinates.Scale,
			"scaled_channel" => p.Size * C( v, "size_scale_keys", time, Vector3.One ),
			_ => p.Size
		};
		var color = v.Attribute( "color_input" ) switch
		{
			"keys" => C( v, "color_keys", time, new Vector3( 255 ) ),
			"channel" => p.Color,
			"scaled_channel" => p.Color * C( v, "color_scale_keys", time, Vector3.One ),
			_ => V( v, "color", new Vector3( 255 ) )
		};
		float alpha = v.Attribute( "opacity_input" ) switch
		{
			"keys" => C( v, "opacity_keys", time, new Vector3( 255 ) ).x,
			"channel" => p.Opacity,
			"scaled_channel" => p.Opacity * C( v, "opacity_scale_keys", time, Vector3.One ).x,
			_ => N( v, "opacity", 255 )
		};
		float angle = v.Attribute( "rotation_input" ) == "keys" ? C( v, "rotation_keys", time, default ).x
			: v.Attribute( "rotation_input" ) == "constant" ? N( v, "rotation" )
			: p.Angle * (v.Attribute( "rotation_input" ) == "scaled_channel" ? C( v, "rotation_scale_keys", time, Vector3.One ).x : 1);
		var uvSize = V( v, "uv_size", Vector3.One );
		var uv = V( v, "uv_offset" );
		if ( v.Attribute( "uv_offset_input" ) is "channel" or "keys" )
		{
			var source = v.Attribute( "uv_offset_input" ) == "channel" ? p.Atom.Element( "initializerstack" )?.Element( "boxrandomuvoffset" ) : v;
			int total = Math.Clamp( (int)N( source, "num_frames", 1 ), 1, 256 );
			int frame = v.Attribute( "uv_offset_input" ) == "channel" ? p.Frame : (int)(time * N( v, "fps", 1 ));
			frame = v.Attribute( "loop" ) == "true" ? frame % total : Math.Min( frame, total - 1 );
			uv = V( source, "frame_start" );
			int columns = Math.Max( 1, (int)MathF.Round( 1 / Math.Max( .001f, uvSize.x ) ) );
			uv += new Vector3( frame % columns * uvSize.x, frame / columns * uvSize.y, 0 );
		}
		view.Renderer.Sprite = (Sandbox.Mounting.Directory.Get( "payday" ) as PaydayMount)?.EffectSprite( v.Attribute( "texture" ), uv, uvSize );
		view.Renderer.Size = new Vector2( Math.Max( 0, size.x ), Math.Max( 0, size.y ) );
		view.Renderer.Color = new Color( color.x / 255, color.y / 255, color.z / 255, Math.Clamp( alpha / 255, 0, 1 ) );
		view.Renderer.WorldPosition = p.Position;
		view.Renderer.WorldRotation = Rotation.FromRoll( angle * 180 / MathF.PI );
	}
	/// <summary>Stop this emitter and its owned child effects. Re-enable to start a new playback.</summary>
	public void Stop() => Enabled = false;
	protected override void OnDisabled() => Clear();
	protected override void OnDestroy() => Clear();
	void Clear()
	{
		generation++; ready = false;
		foreach ( var p in particles ) foreach ( var view in p.Views )
			if ( view.Renderer.IsValid() ) view.Renderer.GameObject.Destroy();
		particles.Clear(); started.Clear();
		foreach ( var child in children ) if ( child.IsValid() ) child.Destroy();
		children.Clear(); emissionCredit.Clear();
	}
}
