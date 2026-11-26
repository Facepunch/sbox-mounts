using System;

namespace Source1;

static partial class WeaponEffects
{
	/// <summary>
	/// Play the surface sound and place its decal alongside the Source impact effect.
	/// </summary>
	public static bool Impact( in BaseCombatWeapon.ShotEffect shot, Vector3 direction, string mount )
	{
		if ( Application.IsDedicatedServer || !shot.Hit || !shot.Surface.IsValid() ) return false;
		var surface = shot.Surface;
		var blood = BloodParticle( surface );
		var debris = surface.AudioSurface is AudioSurface.Wood or AudioSurface.Concrete or AudioSurface.Ceramic;
		var soil = surface.AudioSurface is AudioSurface.Soil or AudioSurface.Sand;
		if ( blood is not null && !MountedEffects.Prefab( blood, mount ).IsValid() ) return false;
		if ( blood is null && !debris && !soil && surface.AudioSurface != AudioSurface.Metal ) return false;
		var baseSurface = surface.GetBaseSurface();
		var sound = surface.SoundCollection.Bullet ?? baseSurface?.SoundCollection.Bullet;
		if ( sound.IsValid() ) Sound.Play( sound, shot.HitPosition );
		var decal = surface.PrefabCollection.BulletImpactDecal ?? baseSurface?.PrefabCollection.BulletImpactDecal;
		PlaceDecal( decal, shot.HitObject, shot.HitPosition, shot.Normal );
		if ( blood is not null )
			MountedEffects.Spawn( blood, new Transform( shot.HitPosition, Rotation.LookAt( shot.Normal ) * Rotation.FromPitch( 90 ) ), mount );
		else if ( soil )
			SoilImpact( shot.HitPosition + shot.Normal, shot.Normal, mount );
		else if ( debris )
			DebrisImpact( shot.HitPosition, shot.Normal, mount, surface.AudioSurface == AudioSurface.Wood );
		else if ( !surface.HasTag( "grate" ) )
			MetalSparks( shot.HitPosition + shot.Normal, direction, shot.Normal, mount );
		return true;
	}

	static void DebrisImpact( Vector3 position, Vector3 normal, string mount, bool wood )
	{
		var origin = position + normal;
		var count = Game.Random.Int( 4, 16 );
		var first = 0;
		for ( var i = 0; i < count; i++ ) first += Game.Random.Int( 0, 1 );
		for ( var variant = 1; variant <= 2; variant++ )
		{
			var pieces = variant == 1 ? first : count - first;
			if ( pieces == 0 ) continue;
			var name = wood ? "wood" : "cement";
			var effect = Particles.Emitter( Game.ActiveScene, $"effects/fleck_{name}{variant}", mount, false, true, pieces );
			if ( !effect.IsValid() ) continue;
			effect.WorldPosition = origin;
			effect.Lifetime = 3;
			effect.Force = true;
			effect.ForceDirection = Vector3.Down * 800;
			effect.Collision = true;
			effect.Bounce = 0.3f;
			effect.ApplyAlpha = true;
			effect.Alpha = SmokeFade;
			effect.ApplyRotation = true;
			effect.InitialRoll = new ParticleFloat( 0, 360 );
			effect.AngularVelocity = new ParticleVector3 { Z = new( 0, 360 ) };
			for ( var i = 0; i < pieces; i++ )
			{
				if ( effect.Emit( origin, 0 ) is not { } chip ) continue;
				var size = Game.Random.Int( 1, 2 );
				chip.Size = size * 2;
				chip.Velocity = (normal + Particles.InCube( 0.4f )) * (Game.Random.Float( 64, 128 ) * (3 - size));
				chip.Color = DebrisTint( Game.Random.Float( 0.75f, 1.25f ) );
			}
			effect.Invoke( 3, effect.DestroyGameObject );
		}

		var dustOrigin = origin + normal;
		var smoke = ImpactDust( dustOrigin, mount, "particle/particle_smokegrenade", 2, 8, 1 );
		if ( smoke.IsValid() )
		{
			for ( var i = 1; i <= 2; i++ )
			{
				var velocity = (normal + Particles.InCube( 0.8f )) * Game.Random.Float( 2, 24 );
				velocity.z -= Game.Random.Float( 8, 32 );
				AddDust( smoke, velocity * i, Game.Random.Int( 2, 4 ), 1, Game.Random.Int( 100, 200 ) );
			}
		}

		var haze = ImpactDust( dustOrigin, mount, "particle/particle_smokegrenade", 1, 4, 1.5f );
		if ( haze.IsValid() )
		{
			var cap = (normal + Particles.InCube( 0.8f )) * Game.Random.Float( 2, 24 );
			AddDust( haze, cap.WithZ( Game.Random.Float( -2, 2 ) ), Game.Random.Int( 4, 8 ), Game.Random.Float( 1, 1.5f ), Game.Random.Int( 100, 200 ) );
		}

		var dust = ImpactDust( dustOrigin, mount, "effects/blood", 4, 4, 0.5f );
		if ( !dust.IsValid() ) return;
		for ( var i = 0; i < 4; i++ )
		{
			var velocity = (normal + Particles.InCube( 0.8f )) * Game.Random.Float( 8, 32 );
			velocity.z -= Game.Random.Float( 8, 64 );
			AddDust( dust, velocity, Game.Random.Int( 1, 4 ), Game.Random.Float( 0.25f, 0.5f ), 255 );
		}
	}

	static void SoilImpact( Vector3 origin, Vector3 normal, string mount )
	{
		var smoke = ImpactDust( origin, mount, "particle/particle_smokegrenade", 8, 4, 1 );
		var grit = ImpactDust( origin, mount, "effects/blood", 4, 2, 0.75f );
		if ( smoke.IsValid() ) smoke.Damping = 18.42f;
		if ( grit.IsValid() ) grit.Damping = 18.42f;

		for ( int i = 0; i < 4; i++ )
		{
			Vector3 Burst() => (normal * Game.Random.Float( 1, 6 ) + Particles.InCube( 0.2f )).Normal * Game.Random.Float( 250, 500 ) * i;
			if ( smoke.IsValid() )
			{
				AddDust( smoke, Burst(), Game.Random.Int( 3, 4 ) * (i + 1), Game.Random.Float( 0.5f, 1 ), Game.Random.Int( 32, 255 ) );
				AddDust( smoke, (normal + Particles.InCube( 1 )).Normal * Game.Random.Float( 0, 50 ), Game.Random.Int( 1, 4 ), Game.Random.Float( 0.5f, 1 ), Game.Random.Int( 32, 64 ) );
			}
			if ( grit.IsValid() )
				AddDust( grit, Burst(), Game.Random.Int( 2, 4 ) * (i + 1), Game.Random.Float( 0.25f, 0.75f ), 255 );
		}
	}

	static Color DebrisTint( float shade )
	{
		var linear = MathF.Pow( 0.5f * shade, 2.2f );
		return new Color( linear, linear, linear );
	}

	static ParticleEffect ImpactDust( Vector3 position, string mount, string material, int count, float growth, float lifetime )
	{
		var effect = Particles.Emitter( Game.ActiveScene, material, mount, false, true, count );
		if ( !effect.IsValid() ) return null;
		effect.WorldPosition = position;
		effect.ApplyShape = true;
		effect.Scale = LinearOverLife( 1, growth );
		effect.ApplyAlpha = true;
		effect.Alpha = SmokeFade;
		effect.ApplyRotation = true;
		effect.InitialRoll = new ParticleFloat( 0, 360 );
		effect.AngularVelocity = new ParticleVector3 { Z = new( -115, 115 ) };
		var renderer = effect.GetComponent<ParticleSpriteRenderer>();
		renderer.DepthFeather = MathF.Max( renderer.DepthFeather, 4 );
		renderer.CameraFadeNear = 16;
		renderer.CameraFadeFar = 64;
		effect.Invoke( lifetime, effect.DestroyGameObject );
		return effect;
	}

	static void AddDust( ParticleEffect effect, Vector3 velocity, float radius, float life, int alpha )
	{
		effect.Lifetime = life;
		if ( effect.Emit( effect.WorldPosition, 0 ) is not { } puff ) return;
		puff.Velocity = velocity;
		puff.StartScale = radius * 2;
		puff.Size = puff.StartScale;
		puff.Color = DebrisTint( Game.Random.Float( 0.5f, 1.25f ) ).WithAlpha( alpha / 255f );
	}

	static void MetalSparks( Vector3 origin, Vector3 incoming, Vector3 normal, string mount )
	{
		const float maxSpeed = 512;
		const float maxLifetime = 0.1f;
		var reflected = incoming - normal * (2 * incoming.Dot( normal )) + Particles.InCube( 0.2f );
		var count = 2 * Game.Random.Int( 4, 8 );
		var effect = ParticleStreaks.Create( origin, mount, "effects/spark", count, new( 0.05f, maxLifetime ),
			new( 1, 4 ), ImpactTrailStretch, 4 + 2 * maxSpeed * (maxLifetime + 0.1f) );
		if ( effect.IsValid() )
		{
			effect.Force = true;
			effect.ForceDirection = Vector3.Down * 400;
			effect.Damping = 8;

			for ( var i = 0; i < count; i++ )
			{
				var spread = Game.Random.Float( 0, 2 );
				var dir = (reflected + Particles.InCube( 0.5f * spread )).Normal;
				var spark = effect.Emit( origin, 0 );
				if ( spark is not null ) spark.Velocity = dir * Game.Random.Float( 128, maxSpeed ) * (2 - spread);
			}
		}
		Hl2.FlatQuad.Create( "effects/yellowflare", origin, normal, Game.Random.Int( 24, 28 ), 0, 0.1f, mount, linear: true );
	}

	internal static Transform DecalTransform( GameObject target, Vector3 position, Vector3 normal )
		=> target.WorldTransform.ToLocal( new Transform( position, Rotation.LookAt( -normal, Vector3.Random ) ) );

	internal static void PlaceDecal( GameObject prefab, GameObject target, Vector3 position, Vector3 normal )
	{
		if ( target.IsValid() ) PlaceDecal( prefab, target, DecalTransform( target, position, normal ) );
	}

	internal static void PlaceDecal( GameObject prefab, GameObject target, Transform local )
	{
		if ( !prefab.IsValid() || !target.IsValid() ) return;
		var mark = prefab.Clone( local, target );
		mark.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
	}

	/// <summary>Source surfaceprops by name: alien, antlion and zombie flesh bleed yellow, other flesh red.</summary>
	static string BloodParticle( Surface surface )
	{
		var name = surface.ResourceName ?? "";
		if ( name.Contains( "alienflesh" ) || name.Contains( "antlion" ) || name.Contains( "zombieflesh" ) ) return "blood_impact_yellow_01";
		return name.Contains( "flesh" ) ? "blood_impact_red_01" : null;
	}
}
