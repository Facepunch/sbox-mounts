using System;
using Halo2.Blam;

namespace Halo2;

public sealed partial class PelletImpactEffect
{
	public enum ImpactLayer { Rocks, Smoke, Sparks, Drift, MetalChips, GlassShards, ElectricArcs, FierySmoke, Plant, Cloth, Dust, Rubber, Mist, Shield, Spray, Ripple, Clumps }

	internal static bool HasAnimatedRadius( ImpactLayer layer ) => layer is ImpactLayer.Smoke or ImpactLayer.Spray or ImpactLayer.ElectricArcs or ImpactLayer.FierySmoke or ImpactLayer.Shield or ImpactLayer.Ripple or ImpactLayer.Clumps;

	public sealed record ImpactEmitter( ImpactLayer Layer, ImpactSprite Sprite, Vector2 Count, Vector2 Lifetime, Vector2 Speed,
		Vector2 Cone, Vector2 Spin, Vector2 Size, CurveRange RadiusCurve, CurveRange AlphaCurve, Color Tint, Color TintEnd,
		float Gravity, float Drag, float AngularDrag, Vector2 Collision )
	{
		public int CountInput { get; set; }
		public int LifeInput { get; set; }
		public int SpeedInput { get; set; }
		public int SpinInput { get; set; }
		public int SizeInput { get; set; }
		public int RadiusRange { get; set; }
		public int ColorInput { get; set; }
		public int ConeInput { get; set; }
		public int SpawnRadiusInput { get; set; }
		public int Shape { get; set; }
		public int Sequence { get; set; }
		public DebrisFrame[] DebrisFrames { get; set; }
		public AnimatedFrame[] AnimatedFrames { get; set; }
		public float Cycles { get; set; }
		public string Marker { get; set; }
		public Vector2 SpriteAspect { get; set; }
		public Vector2 RadiusBounds { get; set; }
		public Vector2 AngleBounds { get; set; }
		public int AlphaInput { get; set; }
		public bool PerpendicularToDirection { get; set; }
		public bool Lighting { get; set; }
		public int MirrorsPerFrame { get; set; } = 2;
		public bool Collides { get; set; }
		public bool CollidesWithWater { get; set; }
		public bool DiesInWater { get; set; }
		public bool DiesOnStructure { get; set; }
		public Vector2 Alpha { get; set; }
		public Vector2 SpawnRadius { get; set; }
		public CurveRange SpriteAngle { get; set; }
		public CurveRange? SpinCurve { get; set; }
		public int SpinRange { get; set; }
	}

	public sealed record AnimatedFrame( Vector2 InverseSize, Vector2 Aspect, Vector2 Pivot );
	public sealed record DebrisFrame( Vector2 Aspect, ImpactSprite Sprite );
	public sealed record ImpactSprite( string TexturePath, Vector2 Origin )
	{
		Sprite sprite;
		internal Sprite Get()
		{
			if ( sprite is null )
			{
				sprite = Sprite.FromTexture( Texture.Load( TexturePath ) );
				sprite.Animations[0].Origin = Origin;
			}
			return sprite;
		}
	}

	static uint impactParticleSeed = (uint)Random.Shared.Next();

	float ParticleFraction() => BlamRandom.Next( ref impactParticleSeed ) * (1f / 65535f);
	static float Sample( Vector2 bounds, float t ) => bounds.x + (bounds.y - bounds.x) * t;

	void EmitImpactParticles( ImpactEmitter[] emitters, GameObject parent, Transform contact, Vector3 incoming )
	{
		// World effects initialize every system and location before allocating the first particle.
		Span<Vector2> systemRandom = stackalloc Vector2[emitters.Length];
		for ( int i = 0; i < emitters.Length; i++ )
		{
			systemRandom[i] = new Vector2( ParticleFraction(), ParticleFraction() );
			BlamRandom.Next( ref impactParticleSeed ); // Location random byte
		}
		for ( int i = 0; i < emitters.Length; i++ )
		{
			if ( emitters[i] is not {} emitter ) continue;
			var random = systemRandom[i];
			// Particle random inputs are zero until a particle has been allocated.
			float input = emitter.CountInput switch { 5 => random.x, 6 => random.y, _ => 0 };
			int births = (int)(Sample( emitter.Count, input ) + 0.0001f);
			EmitImpactLayer( emitter, births, parent, contact, incoming, random );
		}
	}

	void EmitImpactLayer( ImpactEmitter data, int births, GameObject parent, Transform contact, Vector3 incoming, Vector2 random )
	{
		if ( births == 0 ) return;
		if ( data.AnimatedFrames is not null )
		{
			// A cloth fragment's atlas pivot changes with its own age.
			for ( int i = 0; i < births; i++ )
			{
				var sprite = Sprite.FromTexture( Texture.Load( data.Sprite.TexturePath ) );
				CreateImpactLayer( data, sprite, parent, contact, incoming, random ).Emit( contact.Position, 0 );
			}
			return;
		}
		if ( data.DebrisFrames is not {} frames )
		{
			var effect = CreateImpactLayer( data, data.Sprite.Get(), parent, contact, incoming, random );
			for ( int i = 0; i < births; i++ ) effect.Emit( contact.Position, 0 );
			return;
		}

		// The renderer shares one pivot across its particles. Batch births using the same atlas variant.
		var effects = new ParticleEffect[frames.Length];
		for ( int i = 0; i < births; i++ )
		{
			uint seed = impactParticleSeed;
			BlamRandom.Next( ref seed );
			int horizontal = (int)(BlamRandom.Next( ref seed ) & 1);
			uint second = BlamRandom.Next( ref seed );
			int mirrors = data.MirrorsPerFrame;
			int count = frames.Length / mirrors;
			int frame = (int)(second % count);
			if ( data.Layer == ImpactLayer.Plant )
			{
				BlamRandom.Next( ref seed );
				frame = (int)((1 + BlamRandom.Next( ref seed ) * (1f / 65535f)) * count) % count;
			}
			int variant = frame * mirrors + (mirrors == 4 ? (int)(second & 1) * 2 : 0) + horizontal;
			var effect = effects[variant] ??= CreateImpactLayer( data, frames[variant].Sprite.Get(), parent, contact, incoming, random, variant );
			effect.Emit( contact.Position, 0 );
		}
	}

	ParticleEffect CreateImpactLayer( ImpactEmitter data, Sprite sprite, GameObject parent, Transform contact, Vector3 incoming, Vector2 random, int variant = 0 )
	{
		bool sparks = data.Layer == ImpactLayer.Sparks;
		bool animatedRadius = HasAnimatedRadius( data.Layer );
		var axis = data.Marker switch { "up" => Vector3.Up, "forward" => contact.Rotation * incoming, _ => -contact.Rotation.Forward };
		if ( data.Marker == "reflection" )
		{
			var ray = contact.Rotation * incoming;
			axis = ray - 2 * Vector3.Dot( ray, axis ) * axis;
		}
		var go = new GameObject( parent, false, $"Pellet impact {data.Layer}" );
		go.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		var up = Vector3.Cross( Vector3.Up, axis ).LengthSquared < 1e-8f ? Vector3.Left : Vector3.Up;
		go.WorldTransform = new Transform( contact.Position, Rotation.LookAt( axis, up ) );
		var effect = go.AddComponent<ParticleEffect>();
		effect.Lifetime = MathF.Max( data.Lifetime.x, data.Lifetime.y );
		if ( data.Layer == ImpactLayer.Rubber ) effect.SequenceTime = 0;
		effect.SnapToFrame = data.Layer is not (ImpactLayer.Drift or ImpactLayer.Cloth);
		var renderer = go.AddComponent<ParticleSpriteRenderer>();
		renderer.Sprite = sprite;
		renderer.Scale = sparks ? 1 : 2;
		renderer.Lighting = data.Lighting;
		renderer.Additive = sparks || data.Layer is ImpactLayer.ElectricArcs or ImpactLayer.FierySmoke or ImpactLayer.Shield;
		if ( sparks || data.PerpendicularToDirection ) renderer.Alignment = ParticleSpriteRenderer.BillboardAlignment.Particle;
		go.AddComponent<TemporaryEffect>().BecomeOrphan = true;

		void Appearance( Particle p )
		{
			float t = p.LifeDelta;
			float life = p.Get<float>( "life_fraction" );
			if ( data.AnimatedFrames is {} frames )
			{
				float cycle = t * data.Cycles;
				float frame = cycle * frames.Length;
				int index = (int)frame;
				float blend = frame - index;
				var a = frames[index % frames.Length];
				var b = frames[(index + 1) % frames.Length];
				var pivot = Vector2.Lerp( a.Pivot, b.Pivot, blend ) * Vector2.Lerp( a.InverseSize, b.InverseSize, blend );
				sprite.Animations[0].Origin = new Vector2( 1 - pivot.x, pivot.y );
				var aspect = a.Aspect;
				if ( b.InverseSize.x < b.InverseSize.y ) aspect.y = MathX.Lerp( aspect.y, b.Aspect.y, blend );
				else aspect.x = MathX.Lerp( aspect.x, b.Aspect.x, blend );
				p.Size = aspect * Sample( data.Size, life );
				p.Alpha = data.AlphaCurve.Evaluate( t, life ).Clamp( 0, 1 );
				p.SequenceTime = new Vector3( cycle, 0, 0 );
				return;
			}
			if ( data.Layer is ImpactLayer.Dust or ImpactLayer.Mist )
			{
				p.Size = data.SpriteAspect * (Sample( data.Size, life ) * data.RadiusCurve.Evaluate( t, life ).Clamp( data.RadiusBounds.x, data.RadiusBounds.y ));
				p.Alpha = p.Get<float>( "base_alpha" ) * data.AlphaCurve.Evaluate( t, life ).Clamp( 0, 1 );
				return;
			}
			if ( data.Layer == ImpactLayer.Drift )
			{
				float variation = p.Get<float>( "variation" );
				p.Size = Sample( data.Size, life ) * (data.RadiusCurve.Evaluate( t, life ).Clamp( 1, 2.5f ) + variation);
				p.Alpha = Sample( data.Alpha, life ) * data.AlphaCurve.Evaluate( t, life ).Clamp( 0, 1 );
				p.Angles.roll = (data.SpriteAngle.Evaluate( t, life ).Clamp( -0.2f, 0.2f ) + variation) * 360;
				p.SequenceTime = new Vector3( t * (0.8f * 17 / 15), 0, 0 );
				return;
			}
			if ( data.Layer is ImpactLayer.GlassShards or ImpactLayer.Rubber )
			{
				p.Alpha = data.AlphaCurve.Evaluate( t, life ).Clamp( 0, 1 );
				p.Angles.roll = data.SpriteAngle.Evaluate( t, life ).Clamp( data.AngleBounds.x, data.AngleBounds.y ) * 360 + p.Get<float>( "mirror_angle" );
			}
			else p.Alpha = data.AlphaCurve.Evaluate( t, life ).Clamp( data.Alpha.x, data.Alpha.y );
			if ( animatedRadius )
			{
				float input = data.SizeInput == 0 ? t : p.Get<float>( "size" );
				p.Size = data.RadiusCurve.Evaluate( input, p.Get<float>( "radius_range" ) ).Clamp( data.Size.x, data.Size.y );
				if ( data.Layer is ImpactLayer.Shield or ImpactLayer.Spray or ImpactLayer.Ripple ) p.Size = new Vector2( p.Size.x, p.Size.x * p.Get<float>( "mirror" ) );
				if ( data.DebrisFrames is not null ) p.Size = p.Get<Vector2>( "debris_aspect" ) * p.Size.x;
				return;
			}
			if ( !sparks )
			{
				p.Size = Sample( data.Size, p.Get<float>( "size" ) );
				if ( data.DebrisFrames is not null ) p.Size = p.Get<Vector2>( "debris_aspect" ) * p.Size.x;
				return;
			}
			float radius = data.RadiusCurve.A.Evaluate( t ).Clamp( data.Size.x, data.Size.y ) * (data.Sequence == 1 ? 1 : ShotgunModel.SparkScale( t ));
			ShotgunModel.OrientSpark( p, radius, go.Scene.Camera, go.WorldRotation.Up, data.Sequence == 1 );
		}

		effect.OnParticleCreated += p =>
		{
			p.LifeDelta = 0;
			uint bucket = BlamRandom.Next( ref impactParticleSeed ) * 5 >> 16;
			p.Set( "collision_tick", (int)bucket + 1 );
			uint r0 = BlamRandom.Next( ref impactParticleSeed ), r1 = BlamRandom.Next( ref impactParticleSeed );
			float life = r0 * (1f / 65535f), fraction = r1 * (1f / 65535f), size = ParticleFraction();
			float fourth = ParticleFraction();
			float RandomInput( int input ) => input switch { 2 => life, 3 => fraction, 14 => size, 15 => fourth, 4 => 1, 5 => random.x, 6 => random.y, _ => 0 };
			p.Set( "life_fraction", life );
			float lifetime = Sample( data.Lifetime, RandomInput( data.LifeInput ) );
			p.Set( "lifetime", lifetime );
			p.DeathTime = p.BornTime + lifetime;
			if ( animatedRadius ) p.Set( "radius_range", RandomInput( data.RadiusRange ) );
			p.Set( "size", RandomInput( data.SizeInput ) );
			if ( data.Layer is ImpactLayer.Dust or ImpactLayer.Mist ) p.Set( "base_alpha", Sample( data.Alpha, RandomInput( data.AlphaInput ) ) );
			if ( data.Layer == ImpactLayer.Drift ) p.Set( "variation", fraction );
			float cone = Sample( data.Cone, RandomInput( data.ConeInput ) ).DegreeToRadian();
			Vector3 direction;
			if ( data.Shape == 2 )
			{
				direction = Spread.RandomDirection( ref impactParticleSeed );
				p.Position += go.WorldRotation * direction * Sample( data.SpawnRadius, RandomInput( data.SpawnRadiusInput ) );
			}
			else
			{
				float azimuth = ParticleFraction() * MathF.Tau;
				var radial = new Vector3( 0, MathF.Sin( azimuth ), MathF.Cos( azimuth ) );
				// Ring emitters use the same azimuth for the spawn point and outward velocity.
				float polar = data.Shape == 4 ? cone : ParticleFraction() * cone;
				if ( data.Shape == 4 )
					p.Position += go.WorldRotation * radial * Sample( data.SpawnRadius, RandomInput( data.SpawnRadiusInput ) );
				direction = Vector3.Forward * MathF.Cos( polar ) + radial * MathF.Sin( polar );
				if ( data.Shape == 5 )
					p.Position += go.WorldRotation * Spread.RandomDirection( ref impactParticleSeed ) * Sample( data.SpawnRadius, RandomInput( data.SpawnRadiusInput ) );
			}
			p.Velocity = go.WorldRotation * direction.Normal * Sample( data.Speed, RandomInput( data.SpeedInput ) );
			if ( data.PerpendicularToDirection ) p.Angles = (go.WorldRotation * direction.Normal).EulerAngles;
			p.Color = Color.Lerp( data.Tint, data.TintEnd, RandomInput( data.ColorInput ) );
			p.Angles.roll = (data.Layer switch { ImpactLayer.Dust or ImpactLayer.Mist => fraction, ImpactLayer.Plant => size, ImpactLayer.Cloth => 0, _ => life }) * 360;
			if ( data.Layer is ImpactLayer.Shield or ImpactLayer.Spray or ImpactLayer.Ripple )
			{
				bool flipX = (r0 & 1) != 0, flipY = (r1 & 1) != 0;
				p.Set( "mirror", flipX == flipY ? 1f : -1f );
				if ( flipX ) p.Angles.roll += 180;
			}
			float spinInput = RandomInput( data.SpinInput );
			float spin = data.SpinCurve is {} curve ? curve.Evaluate( spinInput, RandomInput( data.SpinRange ) ).Clamp( data.Spin.x, data.Spin.y ) : Sample( data.Spin, spinInput );
			p.Set( "spin", spin );
			if ( data.Layer == ImpactLayer.Rocks ) p.Sequence = (int)(r1 % 4);
			if ( data.DebrisFrames is {} frames )
			{
				p.Sequence = variant / data.MirrorsPerFrame;
				var aspect = frames[variant].Aspect;
				// A half turn preserves the mirror while keeping the longest extent positive for sprite culling.
				if ( (MathF.Abs( aspect.x ) >= MathF.Abs( aspect.y ) ? aspect.x : aspect.y) < 0 )
				{
					aspect = -aspect;
					p.Angles.roll += 180;
					if ( data.Layer is ImpactLayer.GlassShards or ImpactLayer.Rubber ) p.Set( "mirror_angle", 180f );
				}
				p.Set( "debris_aspect", aspect );
			}
			if ( sparks ) p.Sequence = data.Sequence;
			Appearance( p );
		};
		effect.OnPostStep += dt =>
		{
			float drag = 1 - (data.Drag * dt).Clamp( 0, 1 );
			float gravity = 4.1712594f * 120 * data.Gravity * dt;
			// The engine reevaluates its shared lifetime every step. Retire each particle at its own authored age.
			for ( int i = effect.Particles.Count - 1; i >= 0; i-- )
			{
				var p = effect.Particles[i];
				float lifetime = p.Get<float>( "lifetime" );
				if ( p.Age >= lifetime )
				{
					effect.Terminate( p );
					continue;
				}
				p.DeathTime = p.BornTime + lifetime;
				p.LifeDelta = p.Age / lifetime;
				p.Velocity = (p.Velocity - Vector3.Up * gravity) * drag;
				float spin = p.Get<float>( "spin" );
				p.Angles.roll += spin * dt;
				p.Set( "spin", spin * (1 - (data.AngularDrag * dt).Clamp( 0, 1 )) );
				if ( data.Collides && dt > 0 && Collide( p, data, go.Scene, dt ) )
				{
					effect.Terminate( p );
					continue;
				}
				Appearance( p );
			}
		};
		go.Enabled = true;
		return effect;
	}

	internal static SceneTraceResult TraceParticleStructure( SceneTrace ray )
	{
		var structure = ray.IgnoreDynamic().WithoutTags( "water" );
		var hit = structure.Run();
		// Standalone level colliders are keyframed too. Skip frozen props without hiding geometry behind them.
		return hit.Hit && hit.Body?.Component is Rigidbody
			? structure.RunAll().FirstOrDefault( h => h.Body?.Component is not Rigidbody )
			: hit;
	}

	static bool Collide( Particle p, ImpactEmitter data, Scene scene, float dt )
	{
		if ( p.Frame < p.Get<int>( "collision_tick" ) ) return false;
		var step = p.Velocity * dt;
		var from = p.Position - step;
		var ray = scene.Trace.Ray( from, from + step * 12 ).UseHitPosition();
		// Both terrain debris controllers query structure. Only some also query media.
		var hit = TraceParticleStructure( ray );
		bool water = false;
		if ( data.CollidesWithWater )
		{
			var surface = ray.WithTag( "water" ).HitTriggers().Run();
			if ( surface.Hit && !surface.StartedSolid && (!hit.Hit || surface.Fraction < hit.Fraction) )
			{
				hit = surface;
				water = true;
			}
		}
		// The collider looks twelve steps ahead and accepts contacts within the first two.
		p.Set( "collision_tick", p.Frame + (!hit.Hit || hit.StartedSolid ? 6 : 1) );
		if ( !hit.Hit || hit.StartedSolid || hit.Fraction >= 1f / 6 ) return false;
		float projection = Vector3.Dot( p.Velocity, hit.Normal );
		if ( projection >= 0 ) return false;
		var normal = hit.Normal * projection;
		p.Velocity = (p.Velocity - normal) * (1 - data.Collision.x) - normal * data.Collision.y;
		p.Position = hit.HitPosition + hit.Normal * 0.6f;
		p.Set( "spin", p.Get<float>( "spin" ) * (1 - data.Collision.x) );
		return water ? data.DiesInWater : data.DiesOnStructure;
	}
}
