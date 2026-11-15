using System;

namespace Source1;

/// <summary>
/// Code-driven Source particles: one emitter per material, whose particles the owning effect places itself
/// each frame, as Source's CSimpleEmitter and C_ParticleSmokeGrenade do.
/// </summary>
static class CssParticles
{
	public static ParticleEffect Emitter( GameObject parent, string material, string mountName, bool additive, bool lit, int count )
	{
		var texture = SourceMountedEffects.SpriteTexture( material, mountName );
		if ( texture is null || !parent.IsValid() ) return null;
		var go = new GameObject( parent, false, material );
		go.NetworkMode = NetworkMode.Never;
		var effect = go.AddComponent<ParticleEffect>();
		effect.MaxParticles = count;
		effect.Lifetime = 1000;
		var renderer = go.AddComponent<ParticleSpriteRenderer>();
		renderer.Sprite = Sprite.FromTexture( texture );
		renderer.Additive = additive;
		renderer.Lighting = lit;
		renderer.SortMode = ParticleSpriteRenderer.ParticleSortMode.ByDistance;
		go.Enabled = true;
		return effect;
	}

	/// <summary>A uniformly random vector in a cube, Source's Vector::Random.</summary>
	public static Vector3 InCube( float extent ) => new( Game.Random.Float( -extent, extent ), Game.Random.Float( -extent, extent ), Game.Random.Float( -extent, extent ) );
}

/// <summary>
/// The HE grenade's blast as CBaseGrenade::Explode draws it: TE_Explosion's C_TEExplosion, a 0.1 second glow
/// (CExplosionOverlay) and C_BaseExplosionEffect's fireballs, embers, dust and sparks, then a scorch decal.
/// Approximations: lit sprites stand in for the dust's sampled world light, a point light for the flash,
/// sandbox's scorch for the Scorch decal, and the cement flecks are left out.
/// </summary>
[Title( "CS:S Explosion Effect" )]
[Category( "Counter-Strike: Source" )]
public sealed class CssExplosionEffect : Component
{
	// EXPLOSION_FORCE_MIN and _MAX are both 2, so every blast spreads alike.
	const float Force = 2;
	const float Spread = 1 - 0.15f * Force;
	const float Magnitude = 100;
	const float GlowTime = 0.1f;

	[Property] public string MountName { get; set; } = "css";
	[Property] public string Sound { get; set; } = "BaseGrenade.Explode";

	sealed class Puff
	{
		public Particle Particle;
		public Vector3 Velocity;
		public float Life;
		public float StartSize;
		public float EndSize;
		public float StartAlpha;
		public Color Color;
		public float RollSpeed;
		public bool Spark;
	}

	readonly List<(ParticleEffect Effect, List<Puff> Puffs)> _groups = new();
	Vector3 _direction;
	SpriteRenderer _glow;
	PointLight _light;
	TimeSince _age;

	protected override async void OnStart()
	{
		if ( Application.IsDedicatedServer ) return;
		await Sandbox.Mounting.Directory.Mount( MountName );
		if ( !IsValid ) return;
		_age = 0;
		if ( SourceSounds.Sound( Sound, MountName ) is { } sound ) GameObject.PlaySound( sound );
		_direction = ForceDirection();
		Glow();
		Core();
		Sparks();
		Scorch();
	}

	/// <summary>C_BaseExplosionEffect::GetForceDirection: away from whatever the six axis probes hit.</summary>
	Vector3 ForceDirection()
	{
		var origin = WorldPosition;
		var direction = Vector3.Zero;
		foreach ( var axis in new[] { Vector3.Forward, Vector3.Backward, Vector3.Left, Vector3.Right, Vector3.Up, Vector3.Down } )
		{
			var tr = Scene.Trace.Ray( origin, origin + axis * Magnitude ).IgnoreDynamic().WithoutTags( "player", "projectile", "trigger", "debris" ).Run();
			direction -= axis * (1 - tr.Fraction);
		}
		return direction.IsNearlyZero() ? Vector3.Up : direction.Normal;
	}

	void Glow()
	{
		_glow = SourceMountedEffects.AddSprite( GameObject, "sprites/light_glow02", MountName );
		var go = new GameObject( GameObject, true, "Flash" );
		go.NetworkMode = NetworkMode.Never;
		_light = go.AddComponent<PointLight>();
		_light.Radius = 255;
		_light.LightColor = new Color( 1, 220 / 255f, 128 / 255f ) * 4;
		_light.Shadows = false;
	}

	List<Puff> Group( string material, bool additive, bool lit, int count )
	{
		var effect = CssParticles.Emitter( GameObject, material, MountName, additive, lit, count );
		if ( effect is null ) return null;
		var puffs = new List<Puff>();
		_groups.Add( (effect, puffs) );
		return puffs;
	}

	static float Deviation( Vector3 direction, Vector3 source, float spread ) => spread * MathF.Abs( source.Dot( direction ) );

	Puff Add( List<Puff> group, Vector3 position, Vector3 velocity, float life, float startSize, float endSize, float alpha, Color color, float rollSpeed )
	{
		var effect = _groups.First( x => x.Puffs == group ).Effect;
		var p = effect.Emit( position, 0 );
		p.Velocity = Vector3.Zero;
		p.Angles = new Angles( 0, 0, Game.Random.Float( 0, 360 ) );
		var puff = new Puff { Particle = p, Velocity = velocity, Life = life, StartSize = startSize, EndSize = endSize, StartAlpha = alpha, Color = color, RollSpeed = rollSpeed };
		group.Add( puff );
		return puff;
	}

	/// <summary>C_BaseExplosionEffect::CreateCore.</summary>
	void Core()
	{
		var origin = WorldPosition;
		var right = _direction.Cross( MathF.Abs( _direction.z ) > 0.9f ? Vector3.Forward : Vector3.Up ).Normal;
		var up = right.Cross( _direction ).Normal;
		if ( Group( "particle/particle_noisesphere", false, true, 44 ) is { } smoke )
		{
			// Halved like the smoke grenade's puffs, for taking the full light of the scene.
			Color Grey() => Color.White * Game.Random.Float( 0.5f, 1 ) * 0.5f;
			for ( var i = 0; i < 4; i++ )
			{
				var dir = (CssParticles.InCube( Spread ) + _direction * Game.Random.Float( 1, 6 )).Normal;
				var speed = Game.Random.Float( 1, 750 ) * Force * Deviation( dir, _direction, Spread );
				Add( smoke, origin, dir * speed, Game.Random.Float( 2, 3 ), 72, 144, 1, Grey(), Game.Random.Float( -2, 2 ) );
			}
			for ( var i = 0; i < 8; i++ )
			{
				var dir = (CssParticles.InCube( Spread ) + _direction * Game.Random.Float( 1, 6 )).Normal;
				var speed = Game.Random.Float( 1, 2000 ) * Force * Deviation( dir, _direction, Spread );
				var size = Game.Random.Int( 32, 64 );
				Add( smoke, origin + CssParticles.InCube( 16 ), dir * speed, Game.Random.Float( 0.5f, 1 ), size, size * 2, Game.Random.Float( 128, 255 ) / 255, Grey(), Game.Random.Float( -8, 8 ) );
			}
			for ( var i = 0; i < 32; i++ )
			{
				var yaw = (i + 1) * MathF.PI * 2 / 32;
				var forward = right * MathF.Cos( yaw ) + up * MathF.Sin( yaw );
				var speed = Game.Random.Float( 500, 2000 ) * Force * Spread;
				var size = Game.Random.Int( 16, 32 );
				Add( smoke, origin + CssParticles.InCube( 4 ) + forward * Game.Random.Float( 8, 16 ), forward * speed, Game.Random.Float( 0.5f, 1.5f ), size, size * 4, Game.Random.Float( 16, 32 ) / 255, Grey(), Game.Random.Float( -8, 8 ) );
			}
		}
		var embers = new[] { Group( "effects/fire_embers1", true, false, 16 ), Group( "effects/fire_embers2", true, false, 16 ) };
		for ( var i = 0; i < 16; i++ )
		{
			var group = embers[Game.Random.Int( 0, 1 )];
			if ( group is null ) continue;
			var dir = (CssParticles.InCube( Spread * 2 ) + _direction).Normal;
			var deviation = Deviation( dir, _direction, Spread );
			var speed = Game.Random.Float( 1, 400 ) * 16 * deviation * deviation * 0.5f;
			var size = Math.Clamp( (int)(Game.Random.Int( 8, 16 ) * deviation), 4, 32 );
			Add( group, origin + CssParticles.InCube( 32 ), dir * speed, Game.Random.Float( 2, 3 ), size, size, 1, Color.White * (Game.Random.Int( 192, 255 ) / 255f), Game.Random.Float( -8, 8 ) );
		}
		if ( Group( "effects/fire_cloud2", true, false, 32 ) is { } fire )
		{
			for ( var i = 0; i < 32; i++ )
			{
				var dir = (CssParticles.InCube( Spread * 0.75f ) + _direction).Normal;
				var deviation = Deviation( dir, _direction, Spread );
				var speed = Game.Random.Float( 400, 800 ) * 16 * deviation * deviation * 0.5f;
				var size = Math.Clamp( (int)(Game.Random.Int( 32, 85 ) * deviation), 32, 85 );
				Add( fire, origin + CssParticles.InCube( 48 ), dir * speed, Game.Random.Float( 0.2f, 0.4f ), size, size * 1.5f, 1, Color.White * (Game.Random.Int( 128, 255 ) / 255f), Game.Random.Float( -16, 16 ) );
			}
		}
	}

	/// <summary>C_BaseExplosionEffect::CreateDebris' sparks: fast streaks under 200 gravity, damped 8 per second.</summary>
	void Sparks()
	{
		var sparks = Group( "effects/fire_cloud2", true, false, 16 );
		if ( sparks is null ) return;
		var renderer = _groups[^1].Effect.GetComponent<ParticleSpriteRenderer>();
		renderer.VelocityStretch = true;
		renderer.StretchTime = 0.075f;
		var count = Game.Random.Int( 8, 16 );
		for ( var i = 0; i < count; i++ )
		{
			var dir = (CssParticles.InCube( 1 ) + _direction).Normal;
			var width = Game.Random.Float( 2, 16 );
			var spark = Add( sparks, WorldPosition, dir * Game.Random.Float( 1500, 2500 ), Game.Random.Float( 0.1f, 0.15f ), width, width, 1, Color.White, 0 );
			spark.Spark = true;
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
		var glow = 1 - _age / GlowTime;
		if ( _glow.IsValid() && Scene.Camera is { } camera )
		{
			var toCamera = camera.WorldPosition - WorldPosition;
			var distance = toCamera.Length;
			// CExplosionOverlay: a sun-style glow 5% of the view wide, growing 16 view widths a second. Brought halfway
			// to the eye so the floor doesn't cut it, as the overlay draws without depth.
			_glow.Size = (0.05f + 16 * _age) * distance * 0.5f;
			_glow.WorldPosition = WorldPosition + toCamera * 0.5f;
			_glow.Color = new Color( 1, 0.9f, 0.7f ) * MathF.Max( glow, 0 );
			_glow.Enabled = glow > 0;
		}
		if ( _light.IsValid() ) _light.Enabled = glow > 0;
		// CExplosionParticle velocity: 1/10000 left after half a second, never slower than 32.
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
				if ( puff.Spark )
				{
					puff.Velocity += Vector3.Down * 200 * dt;
					puff.Velocity *= MathF.Max( 0, 1 - 8 * dt );
					p.Velocity = puff.Velocity;
					p.Size = puff.StartSize;
					p.Color = puff.Color;
					p.Alpha = 1 - t;
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
				p.Color = puff.Color * ramp;
				p.Alpha = ramp * puff.StartAlpha;
			}
		}
	}
}
