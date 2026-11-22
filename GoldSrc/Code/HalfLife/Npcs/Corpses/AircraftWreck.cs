namespace GoldSrc.HalfLife;

[Hide]
public sealed class AircraftWreck : Component
{
	[Property] public string DebrisModel { get; set; }
	Vector3 _velocity;
	Angles _spin;
	TimeUntil _timeout, _nextFire;

	internal static GameObject Spawn( Aircraft source )
	{
		var go = new GameObject( false, $"{source.GameObject.Name} Wreck" );
		go.Tags.Add( "ragdoll", "removable" );
		go.WorldTransform = source.WorldTransform;
		var model = go.AddComponent<ModelRenderer>();
		model.Model = source.Renderer.Model;
		model.BodyGroups = source.Renderer.BodyGroups;
		var wreck = go.AddComponent<AircraftWreck>();
		if ( MountUtility.TryParse( source.ModelPath, out var ident ) && Sandbox.Mounting.Directory.Get( ident ) is HalfLifeMount mount )
			wreck.DebrisModel = mount.GameDirs.Select( dir => $"mount://{ident}/{dir}/models/{source.DebrisModel}.mdl.vmdl" )
				.FirstOrDefault( path => mount.GetByPath( path ) is ModelLoader );
		wreck._velocity = source.Animation.MoveVelocity;
		wreck._spin = new Angles( Game.Random.Float( -20, 20 ), 0, Game.Random.Float( -50, 50 ) );
		wreck._timeout = 4;
		go.Enabled = true;
		go.NetworkSpawn( true, null );
		return go;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		_velocity += Scene.PhysicsWorld.Gravity * (0.3f * Time.Delta);
		var end = WorldPosition + _velocity * Time.Delta;
		var trace = Scene.Trace.Ray( WorldPosition, end ).Size( 40 ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "ragdoll", "npc", "player", "playercontroller", "trigger" ).Run();
		WorldPosition = trace.EndPosition;
		WorldRotation *= Rotation.From( _spin * Time.Delta );
		if ( trace.Hit || _timeout <= 0 )
		{
			Explode();
			return;
		}
		if ( _nextFire <= 0 )
		{
			_nextFire = 0.2f;
			Fireball( WorldPosition + Vector3.Random * 60, 48 );
		}
	}

	void Explode()
	{
		var origin = WorldPosition + Vector3.Up * 8;
		Fireball( origin, 192 );
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount ) mount.NpcAudio.Emit( "weapons/mortarhit.wav", origin, attenuation: 0.3f );
		Blast.DamageInRadius( GameObject, GameObject, origin, 300, 750, "blast" );
		if ( DebrisModel is not null ) ScatterDebris( origin, DebrisModel );
		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Fireball( Vector3 position, float size )
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount || mount.GetIndexedSprite( "fexplo" ) is not { } sprite ) return;
		var go = new GameObject( false, "Aircraft explosion" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;
		var renderer = go.AddComponent<SpriteRenderer>();
		renderer.Sprite = sprite;
		renderer.Size = size;
		renderer.Additive = true;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0.6f;
		go.Enabled = true;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ScatterDebris( Vector3 position, string modelPath )
	{
		var model = Model.Load( modelPath );
		if ( !model.IsValid() || model.IsError ) return;
		for ( int i = 0; i < 8; ++i )
		{
			var go = new GameObject( false, "Aircraft debris" );
			go.NetworkMode = NetworkMode.Never;
			go.Tags.Add( "debris" );
			go.WorldPosition = position + Vector3.Random * 48 + Vector3.Up * 32;
			var renderer = go.AddComponent<ModelRenderer>();
			renderer.Model = model;
			foreach ( var part in model.Parts.All )
				if ( part.Choices.Count > 1 ) renderer.SetBodyGroup( part.Name, Game.Random.Int( 0, part.Choices.Count - 1 ) );
			go.AddComponent<BoxCollider>().Scale = new Vector3( 8, 8, 8 );
			var body = go.AddComponent<Rigidbody>();
			go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 5;
			go.Enabled = true;
			body.Velocity = Vector3.Random * 180 + Vector3.Up * 250;
			body.AngularVelocity = Vector3.Random * 4;
		}
	}
}
