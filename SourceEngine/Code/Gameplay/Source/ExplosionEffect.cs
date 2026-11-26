using System;

namespace Source1;

/// <summary>Fire, embers, dust, sparks and the initial flash of a dry-land explosion.</summary>
[Title( "Source Explosion Effect" )]
[Category( "Source 1" )]
[Alias( "Source1.Css.ExplosionEffect", "Source1.CssExplosionEffect", "CssExplosionEffect" )]
public sealed class ExplosionEffect : Component
{
	const float Force = 2;
	const float Spread = 1 - 0.15f * Force;
	[Property] public float Magnitude { get; set; } = 100;
	[Property] public bool CreateScorch { get; set; } = true;

	[Property] public string MountName { get; set; } = "css";
	[Property] public string Sound { get; set; } = "BaseGrenade.Explode";

	sealed class Puff
	{
		public Particle Particle;
		public Vector3 Velocity;
		public float Life;
		public float StartSize;
		public float EndSize;
		public float Brightness;
		public float RollSpeed;
	}

	readonly List<(ParticleEffect Effect, List<Puff> Puffs)> _groups = new();
	Vector3 _direction;
	PointLight _light;
	TimeSince _age;

	protected override async void OnStart()
	{
		if ( Application.IsDedicatedServer ) return;
		await Sandbox.Mounting.Directory.Mount( MountName );
		if ( !IsValid ) return;
		_age = 0;
		if ( !string.IsNullOrEmpty( Sound ) && Sounds.Sound( Sound, MountName ) is { } sound ) GameObject.PlaySound( sound );
		_direction = Explosion.ForceDirection( Scene, WorldPosition, Magnitude );
		Glow();
		Core();
		Debris();
		if ( CreateScorch ) Scorch();
	}

	void Glow()
	{
		ExplosionOverlay.Show( Scene, WorldPosition, MountName, 0.05f );
		var go = new GameObject( GameObject, true, "Flash" );
		go.NetworkMode = NetworkMode.Never;
		_light = go.AddComponent<PointLight>();
		_light.Radius = 255;
		_light.LightColor = new Color( 1, 220 / 255f, 128 / 255f ) * 4;
		_light.Shadows = false;
	}

	List<Puff> Group( string material, bool additive, bool lit, int count )
	{
		var effect = Particles.Emitter( GameObject, material, MountName, additive, lit, count );
		if ( effect is null ) return null;
		var renderer = effect.GetComponent<ParticleSpriteRenderer>();
		renderer.CameraFadeNear = 64;
		renderer.CameraFadeFar = 128;
		var puffs = new List<Puff>( count );
		_groups.Add( (effect, puffs) );
		return puffs;
	}

	static float Deviation( Vector3 direction, Vector3 source, float spread ) => spread * MathF.Abs( source.Dot( direction ) );

	static Color PuffColor( float brightness )
	{
		var linear = MathF.Pow( brightness, 2.2f );
		return new Color( linear, linear, linear );
	}

	void Add( List<Puff> group, Vector3 position, Vector3 velocity, float life, float startSize, float endSize, float brightness, float rollSpeed )
	{
		var effect = _groups.First( x => x.Puffs == group ).Effect;
		if ( effect.Emit( position, 0 ) is not { } p ) return;
		p.Size = startSize * 2;
		p.Color = PuffColor( brightness );
		p.Angles = new Angles( 0, 0, Game.Random.Float( 0, 360 ) );
		var puff = new Puff { Particle = p, Velocity = velocity, Life = life, StartSize = startSize, EndSize = endSize, Brightness = brightness, RollSpeed = rollSpeed };
		group.Add( puff );
	}

	void Core()
	{
		var origin = WorldPosition;
		var right = _direction.Cross( MathF.Abs( _direction.z ) > 0.9f ? Vector3.Forward : Vector3.Up ).Normal;
		var up = right.Cross( _direction ).Normal;
		if ( Group( "particle/particle_noisesphere", false, true, 44 ) is { } smoke )
		{
			float Grey() => Game.Random.Float( 0.25f, 0.5f );
			for ( var i = 0; i < 4; i++ )
			{
				var dir = (Particles.InCube( Spread ) + _direction * Game.Random.Float( 1, 6 )).Normal;
				var speed = Game.Random.Float( 1, 750 ) * Force * Deviation( dir, _direction, Spread );
				Add( smoke, origin, dir * speed, Game.Random.Float( 2, 3 ), 72, 144, Grey(), Game.Random.Float( -2, 2 ) );
			}
			for ( var i = 0; i < 8; i++ )
			{
				var dir = (Particles.InCube( Spread ) + _direction * Game.Random.Float( 1, 6 )).Normal;
				var speed = Game.Random.Float( 1, 2000 ) * Force * Deviation( dir, _direction, Spread );
				var size = Game.Random.Int( 32, 64 );
				Add( smoke, origin + Particles.InCube( 16 ), dir * speed, Game.Random.Float( 0.5f, 1 ), size, size * 2, Grey(), Game.Random.Float( -8, 8 ) );
			}
			for ( var i = 0; i < 32; i++ )
			{
				var yaw = (i + 1) * MathF.PI * 2 / 32;
				var forward = right * MathF.Cos( yaw ) + up * MathF.Sin( yaw );
				var speed = Game.Random.Float( 500, 2000 ) * Force * Spread;
				var size = Game.Random.Int( 16, 32 );
				Add( smoke, origin + Particles.InCube( 4 ) + forward * Game.Random.Float( 8, 16 ), forward * speed, Game.Random.Float( 0.5f, 1.5f ), size, size * 4, Grey(), Game.Random.Float( -8, 8 ) );
			}
		}
		var embers = new[] { Group( "effects/fire_embers1", true, false, 16 ), Group( "effects/fire_embers2", true, false, 16 ) };
		for ( var i = 0; i < 16; i++ )
		{
			var group = embers[Game.Random.Int( 0, 1 )];
			if ( group is null ) continue;
			var dir = (Particles.InCube( Spread * 2 ) + _direction).Normal;
			var deviation = Deviation( dir, _direction, Spread );
			var speed = Game.Random.Float( 1, 400 ) * 16 * deviation * deviation * 0.5f;
			var size = Math.Clamp( (int)(Game.Random.Int( 8, 16 ) * deviation), 4, 32 );
			Add( group, origin + Particles.InCube( 32 ), dir * speed, Game.Random.Float( 2, 3 ), size, size, Game.Random.Int( 192, 255 ) / 255f, Game.Random.Float( -8, 8 ) );
		}
		if ( Group( "effects/fire_cloud2", true, false, 32 ) is { } fire )
		{
			for ( var i = 0; i < 32; i++ )
			{
				var dir = (Particles.InCube( Spread * 0.75f ) + _direction).Normal;
				var deviation = Deviation( dir, _direction, Spread );
				var speed = Game.Random.Float( 400, 800 ) * 16 * deviation * deviation * 0.5f;
				var size = Math.Clamp( (int)(Game.Random.Int( 32, 85 ) * deviation), 32, 85 );
				Add( fire, origin + Particles.InCube( 48 ), dir * speed, Game.Random.Float( 0.2f, 0.4f ), size, size * 1.5f, Game.Random.Int( 128, 255 ) / 255f, Game.Random.Float( -16, 16 ) );
			}
		}
	}

	record struct Spark( LineRenderer Line, Vector3 Position, Vector3 Velocity, float Life, float Length );
	readonly List<Spark> _sparks = new();

	void Debris()
	{
		var count = Game.Random.Int( 8, 16 );
		for ( var i = 0; i < count; i++ )
		{
			var line = MountedEffects.AddBeam( GameObject, "effects/fire_cloud2", MountName );
			if ( !line.IsValid() ) break;
			var direction = (Particles.InCube( 1 ) + _direction).Normal;
			line.Color = Color.White;
			line.Width = Game.Random.Float( 2, 16 ) * 0.5f;
			var spark = new Spark( line, WorldPosition, direction * Game.Random.Float( 1500, 2500 ),
				Game.Random.Float( 0.1f, 0.15f ), Game.Random.Float( 0.05f, 0.1f ) );
			DrawSpark( spark, 0 );
			_sparks.Add( spark );
		}

		var flecks = new ParticleEffect[2];
		count = Game.Random.Int( 16, 32 );
		for ( var i = 0; i < flecks.Length; i++ )
		{
			var effect = Particles.Emitter( GameObject, $"effects/fleck_cement{i + 1}", MountName, false, false, count );
			flecks[i] = effect;
			if ( !effect.IsValid() ) continue;
			effect.Lifetime = 3;
			effect.Force = true;
			effect.ForceDirection = Vector3.Down * 800;
			effect.Collision = true;
			effect.Bounce = 0.5f;
			effect.ApplyAlpha = true;
			effect.Alpha = WeaponEffects.LinearOverLife( 1, 0 );
			effect.ApplyRotation = true;
			effect.InitialRoll = new ParticleFloat( 0, 360 );
			effect.AngularVelocity = new ParticleVector3 { Z = new( 0, 360 ) };
		}
		for ( var i = 0; i < count; i++ )
		{
			if ( flecks[Game.Random.Int( 0, 1 )]?.Emit( WorldPosition + _direction * 16 + Particles.InCube( 8 ), 0 ) is not { } p ) continue;
			var size = Game.Random.Int( 1, 3 );
			var direction = (_direction + Particles.InCube( 1 )).Normal;
			var deviation = Deviation( direction, _direction, 0.8f );
			p.Velocity = direction * (Game.Random.Float( 64, 256 ) * (4 - size) * 8 * deviation * deviation);
			p.Size = size * 2;
			p.Color = (Color.White * Game.Random.Float( 0.125f, 0.375f )).WithAlpha( 1 );
		}
	}

	static void DrawSpark( Spark spark, float age )
	{
		spark.Line.VectorPoints[0] = spark.Position;
		spark.Line.VectorPoints[1] = spark.Position + spark.Velocity * MathF.Max( 0.01f, spark.Length * (1 - age / spark.Life) );
	}

	void UpdateSparks( float dt )
	{
		for ( var i = _sparks.Count - 1; i >= 0; i-- )
		{
			var spark = _sparks[i];
			if ( _age >= spark.Life )
			{
				spark.Line.GameObject.Destroy();
				_sparks.RemoveAt( i );
				continue;
			}
			var velocity = (spark.Velocity + Vector3.Down * (200 * dt)) * MathF.Max( 0, 1 - 8 * dt );
			spark = spark with { Position = spark.Position + spark.Velocity * dt, Velocity = velocity };
			_sparks[i] = spark;
			DrawSpark( spark, _age );
		}
	}

	void Scorch()
	{
		var tr = Scene.Trace.Ray( WorldPosition + _direction * 8, WorldPosition - _direction * 32 ).IgnoreDynamic().WithoutTags( "player", "projectile", "trigger", "debris" ).Run();
		if ( !tr.Hit ) return;
		var prefab = GameObject.GetPrefab( "/prefabs/engine/scorch.prefab" );
		if ( prefab is null ) return;
		var go = prefab.Clone( tr.HitPosition + tr.Normal, Rotation.LookAt( -tr.Normal, Vector3.Up ) );
		go.NetworkMode = NetworkMode.Never;
		go.SetParent( tr.GameObject, true );
		go.Enabled = true;
	}

	protected override void OnUpdate()
	{
		var dt = Time.Delta;
		UpdateSparks( dt );
		if ( _light.IsValid() ) _light.Enabled = _age < ExplosionOverlay.Lifetime;
		// The expanding cloud loses nearly all of its initial speed within half a second.
		var decay = MathF.Exp( MathF.Log( 0.0001f ) * dt / 0.5f );
		foreach ( var (effect, puffs) in _groups )
		{
			for ( var i = puffs.Count - 1; i >= 0; i-- )
			{
				var puff = puffs[i];
				var p = puff.Particle;
				var t = p.Age / puff.Life;
				if ( t >= 1 )
				{
					effect.Terminate( p );
					puffs.RemoveAt( i );
					continue;
				}
				var direction = puff.Velocity.Normal;
				puff.Velocity *= decay;
				if ( puff.Velocity.Length < 32 ) puff.Velocity = direction * 32;
				p.Position += puff.Velocity * dt;
				p.Angles = p.Angles.WithRoll( p.Angles.roll + puff.RollSpeed.RadianToDegree() * dt );
				puff.RollSpeed -= puff.RollSpeed * dt * 8;
				if ( MathF.Abs( puff.RollSpeed ) < 0.5f ) puff.RollSpeed = puff.RollSpeed > 0 ? 0.5f : -0.5f;
				// Bias( 1 - t, 0.25 ) on both colour and alpha.
				var ramp = (1 - t) * (1 - t);
				// Source sizes are half widths.
				p.Size = MathX.Lerp( puff.StartSize, puff.EndSize, t ) * 2;
				p.Color = PuffColor( puff.Brightness * ramp );
				p.Alpha = ramp;
			}
		}
	}
}
