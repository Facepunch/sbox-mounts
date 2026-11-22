namespace GoldSrc.Maps;

public sealed class SparkBurst : Component
{
	private sealed class Tracer
	{
		public LineRenderer Renderer;
		public Vector3 Position;
		public Vector3 Velocity;
		public float Lifetime;
	}

	private readonly List<Tracer> _tracers = [];
	private TimeSince _age;

	public static void Spawn( GameObject parent, Vector3 position )
	{
		var go = new GameObject( true, "GoldSrc spark tracers" ) { Parent = parent };
		var burst = go.AddComponent<SparkBurst>();
		burst._age = 0;
		for ( var i = 0; i < 8; i++ )
		{
			var line = go.AddComponent<LineRenderer>();
			line.UseVectorPoints = true;
			line.VectorPoints = [position, position];
			line.Face = SceneLineObject.FaceMode.Camera;
			line.Width = 0.35f;
			line.Color = new Color( 1, 0.65f, 0.15f );
			line.Additive = true;
			line.Opaque = false;
			line.Lighting = false;
			line.CastShadows = false;
			line.FogStrength = 0;
			burst._tracers.Add( new()
			{
				Renderer = line,
				Position = position,
				Velocity = new Vector3( Game.Random.Float( -200, 200 ), Game.Random.Float( -200, 200 ), Game.Random.Float( -200, 200 ) ),
				Lifetime = Game.Random.Float( 0.1f, 0.5f )
			} );
		}
	}

	protected override void OnUpdate()
	{
		var alive = false;
		foreach ( var tracer in _tracers )
		{
			if ( _age >= tracer.Lifetime )
			{
				tracer.Renderer.Enabled = false;
				continue;
			}
			alive = true;
			tracer.Velocity += Scene.PhysicsWorld.Gravity * (0.5f * Time.Delta);
			tracer.Position += tracer.Velocity * Time.Delta;
			tracer.Renderer.VectorPoints[0] = tracer.Position;
			tracer.Renderer.VectorPoints[1] = tracer.Position - tracer.Velocity * 0.025f;
			tracer.Renderer.Color = new Color( 1, 0.65f, 0.15f, 1f - _age / tracer.Lifetime );
		}
		if ( !alive ) GameObject.Destroy();
	}
}

public sealed class SparkShower : Component
{
	private Vector3 _velocity;
	private float _life;
	private TimeUntil _nextBurst;
	private SparkEffect _spark;

	public static void Spawn( MapState map, Vector3 origin, Vector3 direction )
	{
		var go = new GameObject( true, "GoldSrc spark shower" );
		if ( map.IsValid() ) go.Parent = map.GameObject;
		go.WorldPosition = origin;
		var shower = go.AddComponent<SparkShower>();
		shower._velocity = direction * Game.Random.Float( 200, 300 );
		shower._velocity.x += Game.Random.Float( -100, 100 );
		shower._velocity.y += Game.Random.Float( -100, 100 );
		shower._velocity.z += shower._velocity.z >= 0 ? 200 : -200;
		shower._life = Game.Random.Float( 0.5f, 1.5f );
		shower._nextBurst = 0.1f;
		shower._spark = go.AddComponent<SparkEffect>();
		shower._spark.Map = map;
		shower._spark.Toggleable = true;
		shower._spark.StartOn = false;
	}

	protected override void OnUpdate()
	{
		_velocity += Scene.PhysicsWorld.Gravity * (Time.Delta * 0.5f);
		var trace = Scene.Trace.Ray( WorldPosition, WorldPosition + _velocity * Time.Delta )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "player", "npc" ).Run();
		WorldPosition = trace.EndPosition;
		if ( trace.Hit )
		{
			_velocity -= trace.Normal * (2f * Vector3.Dot( _velocity, trace.Normal ));
			var grounded = trace.Normal.z > 0.7f && _velocity.z < 60;
			_velocity *= grounded ? 0.1f : 0.6f;
			if ( _velocity.x * _velocity.x + _velocity.y * _velocity.y < 10 ) _life = 0;
		}
		if ( _nextBurst > 0 ) return;
		_spark.EmitVisuals();
		_life -= 0.1f;
		if ( _life <= 0 )
		{
			GameObject.Destroy();
			return;
		}
		_nextBurst = 0.1f;
	}
}
