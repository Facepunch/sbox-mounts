using Halo2.Blam;
using System;

namespace Halo2;

internal sealed partial class ShotgunLoader
{
	enum ImpactLayer { Rocks, Smoke, Sparks, Drift, MetalChips }
	sealed record ImpactEmitter( ImpactLayer Layer, Sprite Sprite, Vector2 Count, Vector2 Lifetime, Vector2 Speed,
		Vector2 Cone, Vector2 Spin, Vector2 Size, CurveRange RadiusCurve, CurveRange AlphaCurve, Color Tint, Color TintEnd,
		float Gravity, float Drag, float AngularDrag, Vector2 Collision )
	{
		public int CountInput, LifeInput, SpeedInput, SpinInput, SizeInput, RadiusRange, ColorInput, ConeInput, Shape, Sequence;
		public (Vector2 Aspect, Sprite Sprite)[] ChipFrames;
		public bool Reflected;
		public bool CollidesWithWater, DiesInWater, DiesOnStructure;
		public Vector2 Alpha;
		public CurveRange DriftAngle;
	}

	uint impactParticleSeed = (uint)System.Random.Shared.Next();

	ImpactEmitter[] ReadImpactEmitters( TagEntry contact, int row )
	{
		int count = map.ReadInt32( row + 48 );
		if ( count == 0 ) return null;
		var material = contact.TagPath[(contact.TagPath.LastIndexOf( '\\' ) + 1)..];
		if ( !material.StartsWith( "hard_terrain", StringComparison.Ordinal ) &&
			material is not ("default" or "brittle_glass_for_unbreakable" or "hard_metal_solid" or "hard_metal_thin" or "hard_metal_thick") ) return null;
		var emitters = new ImpactEmitter[count];
		int system = Block( row + 48 );
		for ( int i = 0; i < emitters.Length; i++, system += 56 )
		{
			var particle = Reference( system );
			var name = particle?.TagPath switch
			{
				@"effects\generic\smoke\smoke_drift" => DriftName,
				@"effects\generic\solids\rocks" => RocksName,
				@"effects\generic\solids\metal_chips" => MetalChipsName,
				@"effects\generic\smoke\smoke_soft_small_lit" => ImpactSmokeName,
				@"effects\generic\sparks\sparks_animated_soft" or @"effects\generic\sparks\sparks_animated_fiery" => SparksName,
				_ => null
			};
			if ( name is null || map.ReadInt32( system + 48 ) != 1 ) return null;
			var texture = Texture.Load( $"mount://{Halo2Mount.MountIdent}/{PrefabPath}/{name}" );
			if ( !texture.IsValid() || texture.IsError ) return null;
			var layer = name switch { RocksName => ImpactLayer.Rocks, MetalChipsName => ImpactLayer.MetalChips, SparksName => ImpactLayer.Sparks, DriftName => ImpactLayer.Drift, _ => ImpactLayer.Smoke };
			var sprite = Sprite.FromTexture( texture );
			int definition = Address( particle );
			int sequence = Short( definition + 8 );
			if ( layer == ImpactLayer.Sparks ) sprite.Animations[0].Origin = new Vector2( 1 - 48f / 56f, (sequence == 1 ? 5 : 4) / 9f );
			int emitter = Block( system + 48 );
			Vector2 Bounds( int property )
			{
				int data = Block( emitter + property + 8 );
				return new Vector2( Float( data + 4 ), Float( data + 8 ) );
			}
			var movement = Reference( emitter );
			int controller = movement is null ? 0 : Block( Address( movement ) + 12 );
			float Parameter( int control, int number ) => control == 0 ? 0 : Float( Block( Block( control + 4 ) + number * 20 + 12 ) + 4 );
			int color = Block( emitter + 96 );
			Color Tint( int offset )
			{
				int rgb = map.ReadInt32( color + offset );
				return Color.FromBytes( (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb );
			}
			bool smoke = layer == ImpactLayer.Smoke, drift = layer == ImpactLayer.Drift;
			int particleFlags = map.ReadInt32( definition );
			emitters[i] = new ImpactEmitter( layer, sprite, Bounds( 8 ), Bounds( 24 ), Bounds( 40 ) * 120, Bounds( 140 ), Bounds( 56 ),
				Bounds( 72 ) * 120, drift ? ReadRange( definition + 60, 1 ) : smoke || layer == ImpactLayer.Sparks ? ReadRange( emitter + 72, 120 ) : default,
				drift ? ReadRange( definition + 44, 1 ) : ReadRange( emitter + 104, 1 ),
				Tint( 4 ), Tint( smoke || drift ? 16 : 4 ), Parameter( controller, 0 ), Parameter( controller, 1 ), Parameter( controller, 2 ),
				smoke || drift ? default : new Vector2( Parameter( controller + 20, 0 ), Parameter( controller + 20, 1 ) ) )
			{
				CountInput = Short( emitter + 8 ), LifeInput = Short( emitter + 24 ), SpeedInput = Short( emitter + 40 ), SizeInput = Short( emitter + 72 ),
				RadiusRange = Short( emitter + 74 ), ColorInput = Short( emitter + 88 ), ConeInput = Short( emitter + 140 ), Sequence = sequence,
				SpinInput = Short( emitter + 56 ),
				ChipFrames = layer == ImpactLayer.MetalChips ? ReadChipFrames( definition, texture ) : null,
				Shape = map.ReadInt32( emitter + 120 ), Alpha = Bounds( 104 ),
				CollidesWithWater = movement is not null && (map.ReadInt32( Address( movement ) + 8 ) & 4) != 0,
				DiesInWater = (particleFlags & 0x800) != 0, DiesOnStructure = (particleFlags & 0x400) != 0,
				DriftAngle = drift ? ReadRange( definition + 76, 1 ) : default,
				Reflected = map.ResolveStringId( map.ReadInt32( Block( Address( contact ) + 12 ) + 4 * Short( system + 8 ) ) ) == "reflection"
			};
		}
		return emitters;
	}

	(Vector2 Aspect, Sprite Sprite)[] ReadChipFrames( int definition, Texture texture )
	{
		int shader = Block( definition + 140 );
		var bitmap = map.GetTag( map.ReadInt32( Block( shader + 4 ) ) & 0xffff );
		int sequences = Block( Address( bitmap ) + 60 );
		int count = Short( definition + 10 );
		var frames = new (Vector2, Sprite)[count * 2];
		for ( int i = 0; i < count; i++ )
		{
			int sprite = Block( sequences + 60 * i + 52 );
			var size = Halo2TextureLoader.SpriteBounds( map, sprite + 8 ).Size;
			var pivot = new Vector2( Float( sprite + 24 ), Float( sprite + 28 ) ) / size;
			var aspect = size / MathF.Max( size.x, size.y );
			// The same random word selects one of four sequences and its vertical mirror.
			bool flipY = (i & 1) != 0;
			for ( int flipX = 0; flipX < 2; flipX++ )
			{
				var variant = Sprite.FromTexture( texture );
				variant.Animations[0].Origin = new Vector2( flipX == 0 ? 1 - pivot.x : pivot.x, flipY ? 1 - pivot.y : pivot.y );
				frames[i * 2 + flipX] = (new Vector2( flipX == 0 ? aspect.x : -aspect.x, flipY ? -aspect.y : aspect.y ), variant);
			}
		}
		return frames;
	}

	float ParticleFraction() => BlamRandom.Next( ref impactParticleSeed ) * (1f / 65535f);
	static float Sample( Vector2 bounds, float t ) => bounds.x + (bounds.y - bounds.x) * t;

	void EmitImpactParticles( ImpactEmitter[] emitters, GameObject parent, Transform contact, Vector3 incoming )
	{
		// World effects initialize every system and location before allocating the first particle.
		Span<int> births = stackalloc int[emitters.Length];
		for ( int i = 0; i < emitters.Length; i++ )
		{
			float a = ParticleFraction(), b = ParticleFraction();
			BlamRandom.Next( ref impactParticleSeed ); // Location random byte
			// Particle random inputs are zero until a particle has been allocated.
			float input = emitters[i].CountInput switch { 5 => a, 6 => b, _ => 0 };
			births[i] = (int)(Sample( emitters[i].Count, input ) + 0.0001f);
		}
		for ( int i = 0; i < emitters.Length; i++ )
			EmitImpactLayer( emitters[i], births[i], parent, contact, incoming );
	}

	void EmitImpactLayer( ImpactEmitter data, int births, GameObject parent, Transform contact, Vector3 incoming )
	{
		if ( births == 0 ) return;
		bool smoke = data.Layer == ImpactLayer.Smoke, sparks = data.Layer == ImpactLayer.Sparks;
		var axis = -contact.Rotation.Forward;
		if ( data.Reflected )
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
		effect.SnapToFrame = data.Layer != ImpactLayer.Drift;
		var renderer = go.AddComponent<ParticleSpriteRenderer>();
		renderer.Sprite = data.Sprite;
		renderer.Scale = sparks ? 1 : 2;
		renderer.Lighting = smoke || data.Layer is ImpactLayer.Rocks or ImpactLayer.MetalChips;
		renderer.Additive = sparks;
		if ( sparks ) renderer.Alignment = ParticleSpriteRenderer.BillboardAlignment.Particle;
		go.AddComponent<TemporaryEffect>().BecomeOrphan = true;

		void Appearance( Particle p )
		{
			float t = p.LifeDelta;
			float life = p.Get<float>( "life_fraction" );
			if ( data.Layer == ImpactLayer.Drift )
			{
				float variation = p.Get<float>( "variation" );
				p.Size = Sample( data.Size, life ) * (data.RadiusCurve.Evaluate( t, life ).Clamp( 1, 2.5f ) + variation);
				p.Alpha = Sample( data.Alpha, life ) * data.AlphaCurve.Evaluate( t, life ).Clamp( 0, 1 );
				p.Angles.roll = (data.DriftAngle.Evaluate( t, life ).Clamp( -0.2f, 0.2f ) + variation) * 360;
				p.SequenceTime = new Vector3( t * (0.8f * 17 / 15), 0, 0 );
				return;
			}
			p.Alpha = data.AlphaCurve.Evaluate( t, life ).Clamp( data.Alpha.x, data.Alpha.y );
			if ( smoke )
			{
				p.Size = data.RadiusCurve.Evaluate( t, p.Get<float>( "radius_range" ) ).Clamp( data.Size.x, data.Size.y );
				return;
			}
			if ( !sparks )
			{
				p.Size = Sample( data.Size, p.Get<float>( "size" ) );
				if ( data.ChipFrames is not null ) p.Size = p.Get<Vector2>( "chip_aspect" ) * p.Size.x;
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
			float RandomInput( int input ) => input switch { 2 => life, 3 => fraction, 14 => size, 15 => fourth, _ => 0 };
			p.Set( "life_fraction", life );
			float lifetime = Sample( data.Lifetime, RandomInput( data.LifeInput ) );
			p.Set( "lifetime", lifetime );
			p.DeathTime = p.BornTime + lifetime;
			if ( smoke ) p.Set( "radius_range", RandomInput( data.RadiusRange ) );
			p.Set( "size", data.SizeInput == 2 ? life : size );
			if ( data.Layer == ImpactLayer.Drift ) p.Set( "variation", fraction );
			float cone = Sample( data.Cone, RandomInput( data.ConeInput ) ).DegreeToRadian();
			float azimuth = ParticleFraction() * MathF.Tau, polar = ParticleFraction() * cone;
			var direction = new Vector3( MathF.Cos( polar ), MathF.Sin( azimuth ) * MathF.Sin( polar ), MathF.Cos( azimuth ) * MathF.Sin( polar ) );
			// The terrain rock emitter uses shape 5 with a zero radius, consuming a direction draw at the origin.
			if ( data.Shape == 5 ) BlamRandom.Next( ref impactParticleSeed );
			p.Velocity = go.WorldRotation * direction.Normal * Sample( data.Speed, RandomInput( data.SpeedInput ) );
			p.Color = Color.Lerp( data.Tint, data.TintEnd, RandomInput( data.ColorInput ) );
			p.Angles.roll = life * 360;
			p.Set( "spin", Sample( data.Spin, RandomInput( data.SpinInput ) ) );
			if ( data.Layer == ImpactLayer.Rocks ) p.Sequence = (int)(r1 % 4);
			if ( data.ChipFrames is {} frames )
			{
				p.Sequence = (int)(r1 % (frames.Length / 2));
				var (aspect, sprite) = frames[p.Sequence * 2 + (int)(r0 & 1)];
				// A half turn preserves the mirror while keeping the longest extent positive for sprite culling.
				if ( (MathF.Abs( aspect.x ) >= MathF.Abs( aspect.y ) ? aspect.x : aspect.y) < 0 )
				{
					aspect = -aspect;
					p.Angles.roll += 180;
				}
				p.Set( "chip_aspect", aspect );
				// Sheet metal emits one chip per impact. Its chosen atlas frame owns the pivot.
				renderer.Sprite = sprite;
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
				if ( data.Layer is ImpactLayer.Rocks or ImpactLayer.Sparks or ImpactLayer.MetalChips && dt > 0 && Collide( p, data, go.Scene, dt ) )
				{
					effect.Terminate( p );
					continue;
				}
				Appearance( p );
			}
		};
		go.Enabled = true;
		for ( int i = 0; i < births; i++ ) effect.Emit( contact.Position, 0 );
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
