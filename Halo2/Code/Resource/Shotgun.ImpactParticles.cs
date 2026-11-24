using Halo2.Blam;
using System;

namespace Halo2;

internal sealed partial class ShotgunLoader
{
	enum ImpactLayer { Rocks, Smoke, Sparks, Drift, MetalChips, GlassShards, ElectricArcs, FierySmoke, Plant, Cloth, Dust, Rubber, Mist, Shield }
	sealed record ImpactEmitter( ImpactLayer Layer, Sprite Sprite, Vector2 Count, Vector2 Lifetime, Vector2 Speed,
		Vector2 Cone, Vector2 Spin, Vector2 Size, CurveRange RadiusCurve, CurveRange AlphaCurve, Color Tint, Color TintEnd,
		float Gravity, float Drag, float AngularDrag, Vector2 Collision )
	{
		public int CountInput, LifeInput, SpeedInput, SpinInput, SizeInput, RadiusRange, ColorInput, ConeInput, SpawnRadiusInput, Shape, Sequence;
		public (Vector2 Aspect, Sprite Sprite)[] DebrisFrames;
		public (Vector2 InverseSize, Vector2 Aspect, Vector2 Pivot)[] AnimatedFrames;
		public float Cycles;
		public string Marker;
		public Vector2 SpriteAspect, RadiusBounds, AngleBounds;
		public int AlphaInput;
		public bool CollidesWithWater, DiesInWater, DiesOnStructure;
		public Vector2 Alpha, SpawnRadius;
		public CurveRange SpriteAngle;
		public CurveRange? SpinCurve;
		public int SpinRange;
	}

	uint impactParticleSeed = (uint)System.Random.Shared.Next();

	ImpactEmitter[] ReadImpactEmitters( TagEntry contact, int row )
	{
		int count = map.ReadInt32( row + 48 );
		if ( count == 0 ) return null;
		var material = contact.TagPath[(contact.TagPath.LastIndexOf( '\\' ) + 1)..];
		if ( !material.StartsWith( "hard_terrain", StringComparison.Ordinal ) &&
			material is not ("default" or "brittle" or "brittle_elec" or "brittle_elec_cov" or "brittle_explosive" or "brittle_mech_cov" or "brittle_mech"
				or "energy_shield_thin" or "energy_shield_thick" or "energy_shield_thick_cov"
				or "soft" or "soft_inorganic" or "soft_organic" or "soft_organic_plant" or "soft_organic_plant_grassy"
				or "tough_inorganic" or "tough_inorganic_plastic" or "tough_inorganic_rubber" or "tough_organic_wood" or "brittle_glass" or "brittle_glass_for_unbreakable" or "hard_metal_solid" or "hard_metal_thin" or "hard_metal_thick") ) return null;
		var emitters = new ImpactEmitter[count];
		int system = Block( row + 48 );
		for ( int i = 0; i < emitters.Length; i++, system += 56 )
		{
			var particle = Reference( system );
			var name = particle?.TagPath switch
			{
				@"effects\generic\solids\rubber_impact" => RubberName,
				@"effects\generic\smoke\blood_mist_impact" => MistName,
				@"effects\generic\solids\debris_shirt" => ClothName,
				@"effects\generic\solids\plant_leafy" => LeafName,
				@"effects\generic\solids\plant_grassy" => GrassName,
				@"effects\generic\energy\impact_energy" => ShieldName,
				@"effects\generic\solids\plant_wood" => WoodName,
				@"effects\generic\smoke\smoke_impact" => DustName,
				@"effects\generic\sparks\electric_arcs" => ArcName,
				@"effects\generic\smoke\smoke_fiery_small" => SmallFierySmokeName,
				@"effects\generic\smoke\smoke_drift" => DriftName,
				@"effects\generic\solids\rocks" => RocksName,
				@"effects\generic\solids\glass_breakable" => GlassName,
				@"effects\generic\solids\metal_chips" => MetalChipsName,
				@"effects\generic\smoke\smoke_soft_small_lit" => ImpactSmokeName,
				@"effects\generic\sparks\sparks_animated_soft" or @"effects\generic\sparks\sparks_animated_fiery" => SparksName,
				_ => null
			};
			if ( name is null || map.ReadInt32( system + 48 ) != 1 ) return null;
			var texture = Texture.Load( $"mount://{Halo2Mount.MountIdent}/{PrefabPath}/{name}" );
			if ( !texture.IsValid() || texture.IsError ) return null;
			var layer = name switch
			{
				RubberName => ImpactLayer.Rubber,
				MistName => ImpactLayer.Mist,
				ShieldName => ImpactLayer.Shield,
				WoodName or LeafName or GrassName => ImpactLayer.Plant,
				ClothName => ImpactLayer.Cloth,
				DustName => ImpactLayer.Dust,
				ArcName => ImpactLayer.ElectricArcs,
				SmallFierySmokeName => ImpactLayer.FierySmoke,
				RocksName => ImpactLayer.Rocks,
				MetalChipsName => ImpactLayer.MetalChips,
				GlassName => ImpactLayer.GlassShards,
				SparksName => ImpactLayer.Sparks,
				DriftName => ImpactLayer.Drift,
				_ => ImpactLayer.Smoke
			};
			var sprite = Sprite.FromTexture( texture );
			int definition = Address( particle );
			int sequence = Short( definition + 8 );
			if ( layer == ImpactLayer.Sparks ) sprite.Animations[0].Origin = new Vector2( 1 - 48f / 56f, (sequence == 1 ? 5 : 4) / 9f );
			var geometry = new Vector2( Float( definition + 164 ), Float( definition + 168 ) );
			if ( layer is ImpactLayer.Dust or ImpactLayer.Mist )
				sprite.Animations[0].Origin = new Vector2( 1 + Float( definition + 156 ) / geometry.x, -Float( definition + 160 ) / geometry.y );
			int emitter = Block( system + 48 );
			Vector2 Bounds( int property )
			{
				int data = Block( property + 8 );
				return new Vector2( Float( data + 4 ), Float( data + 8 ) );
			}
			var movement = Reference( emitter );
			int controller = movement is null ? 0 : Block( Address( movement ) + 12 );
			float Parameter( int control, int number ) => control == 0 ? 0 : Float( Block( Block( control + 4 ) + number * 20 + 12 ) + 4 );
			int color = Block( emitter + 96 );
			bool twoColorStops = (map.ReadInt32( color ) & 0xff00) == 0x2000;
			Color Tint( int offset )
			{
				int rgb = map.ReadInt32( color + offset );
				return Color.FromBytes( (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb );
			}
			bool animatedRadius = layer is ImpactLayer.Smoke or ImpactLayer.FierySmoke or ImpactLayer.ElectricArcs or ImpactLayer.Shield, drift = layer == ImpactLayer.Drift;
			bool dust = layer is ImpactLayer.Dust or ImpactLayer.Mist;
			bool spriteAngle = layer is ImpactLayer.Rubber or ImpactLayer.GlassShards;
			bool cloth = layer == ImpactLayer.Cloth;
			bool spriteCurves = drift || dust || spriteAngle || cloth;
			var size = Bounds( emitter + 72 ) * 120;
			if ( layer == ImpactLayer.Rubber ) size *= Bounds( definition + 60 ).x;
			int particleFlags = map.ReadInt32( definition );
			emitters[i] = new ImpactEmitter( layer, sprite, Bounds( emitter + 8 ), Bounds( emitter + 24 ), Bounds( emitter + 40 ) * 120, Bounds( emitter + 140 ), Bounds( emitter + 56 ),
				size, drift || dust ? ReadRange( definition + 60, 1 ) : animatedRadius || layer == ImpactLayer.Sparks ? ReadRange( emitter + 72, 120 ) : default,
				spriteCurves ? ReadRange( definition + 44, 1 ) : ReadRange( emitter + 104, 1 ),
				Tint( 4 ), Tint( twoColorStops ? 16 : 4 ), Parameter( controller, 0 ), Parameter( controller, 1 ), Parameter( controller, 2 ),
				controller == 0 || animatedRadius || drift || dust ? default : new Vector2( Parameter( controller + 20, 0 ), Parameter( controller + 20, 1 ) ) )
			{
				CountInput = Short( emitter + 8 ), LifeInput = Short( emitter + 24 ), SpeedInput = Short( emitter + 40 ), SizeInput = Short( emitter + 72 ),
				RadiusRange = Short( emitter + 74 ), ColorInput = Short( emitter + 88 ), ConeInput = Short( emitter + 140 ), Sequence = sequence,
				SpinCurve = map.ReadSpan( Block( emitter + 64 ), 1 )[0] == 4 ? ReadRange( emitter + 56, 1 ) : null,
				SpinRange = Short( emitter + 58 ), SpinInput = Short( emitter + 56 ), SpawnRadiusInput = Short( emitter + 124 ),
				DebrisFrames = layer is ImpactLayer.MetalChips or ImpactLayer.GlassShards or ImpactLayer.ElectricArcs or ImpactLayer.Plant or ImpactLayer.Rubber ? ReadDebrisFrames( definition, texture, layer == ImpactLayer.Plant ) : null,
				Shape = map.ReadInt32( emitter + 120 ), SpawnRadius = Bounds( emitter + 124 ) * 120, Alpha = Bounds( emitter + 104 ),
				CollidesWithWater = movement is not null && (map.ReadInt32( Address( movement ) + 8 ) & 4) != 0,
				DiesInWater = (particleFlags & 0x800) != 0, DiesOnStructure = (particleFlags & 0x400) != 0,
				AnimatedFrames = cloth ? ReadAnimatedFrames( definition ) : null, Cycles = cloth ? Bounds( definition + 92 ).y : 0,
				SpriteAspect = geometry * 0.5f, RadiusBounds = dust ? Bounds( definition + 60 ) : default,
				AngleBounds = spriteAngle ? Bounds( definition + 76 ) : default, AlphaInput = Short( emitter + 104 ),
				SpriteAngle = drift || spriteAngle ? ReadRange( definition + 76, 1 ) : default,
				Marker = map.ResolveStringId( map.ReadInt32( Block( Address( contact ) + 12 ) + 4 * Short( system + 8 ) ) )
			};
		}
		return emitters;
	}

	(Vector2 InverseSize, Vector2 Aspect, Vector2 Pivot)[] ReadAnimatedFrames( int definition )
	{
		int shader = Block( definition + 140 );
		var bitmap = map.GetTag( map.ReadInt32( Block( shader + 4 ) ) & 0xffff );
		int sequence = Block( Address( bitmap ) + 60 ) + Short( definition + 8 ) * 60;
		int first = Block( sequence + 52 );
		return Enumerable.Range( 0, map.ReadInt32( sequence + 52 ) ).Select( i =>
		{
			int frame = first + i * 32;
			var size = Halo2TextureLoader.SpriteBounds( map, frame + 8 ).Size;
			return (Vector2.One / size, size / MathF.Max( size.x, size.y ), new Vector2( Float( frame + 24 ), Float( frame + 28 ) ));
		} ).ToArray();
	}

	(Vector2 Aspect, Sprite Sprite)[] ReadDebrisFrames( int definition, Texture texture, bool singleSequence )
	{
		int shader = Block( definition + 140 );
		var bitmap = map.GetTag( map.ReadInt32( Block( shader + 4 ) ) & 0xffff );
		int sequences = Block( Address( bitmap ) + 60 ) + 60 * Short( definition + 8 );
		int count = singleSequence ? map.ReadInt32( sequences + 52 ) : Short( definition + 10 );
		int mirrors = singleSequence ? 4 : 2;
		var frames = new (Vector2, Sprite)[count * mirrors];
		for ( int i = 0; i < count; i++ )
		{
			int sprite = singleSequence ? Block( sequences + 52 ) + 32 * i : Block( sequences + 60 * i + 52 );
			var size = Halo2TextureLoader.SpriteBounds( map, sprite + 8 ).Size;
			var pivot = new Vector2( Float( sprite + 24 ), Float( sprite + 28 ) ) / size;
			var aspect = size / MathF.Max( size.x, size.y );
			for ( int mirror = 0; mirror < mirrors; mirror++ )
			{
				bool flipX = (mirror & 1) != 0, flipY = singleSequence ? (mirror & 2) != 0 : (i & 1) != 0;
				var variant = Sprite.FromTexture( texture );
				variant.Animations[0].Origin = new Vector2( flipX ? pivot.x : 1 - pivot.x, flipY ? 1 - pivot.y : pivot.y );
				frames[i * mirrors + mirror] = (new Vector2( flipX ? -aspect.x : aspect.x, flipY ? -aspect.y : aspect.y ), variant);
			}
		}
		return frames;
	}

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
			var random = systemRandom[i];
			// Particle random inputs are zero until a particle has been allocated.
			float input = emitters[i].CountInput switch { 5 => random.x, 6 => random.y, _ => 0 };
			int births = (int)(Sample( emitters[i].Count, input ) + 0.0001f);
			EmitImpactLayer( emitters[i], births, parent, contact, incoming, random );
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
				var sprite = Sprite.FromTexture( data.Sprite.Animations[0].Frames[0].Texture );
				CreateImpactLayer( data, sprite, parent, contact, incoming, random ).Emit( contact.Position, 0 );
			}
			return;
		}
		if ( data.DebrisFrames is not {} frames )
		{
			var effect = CreateImpactLayer( data, data.Sprite, parent, contact, incoming, random );
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
			int variant = (int)(second % (frames.Length / 2)) * 2 + horizontal;
			if ( data.Layer == ImpactLayer.Plant )
			{
				BlamRandom.Next( ref seed );
				int count = frames.Length / 4;
				int frame = (int)((1 + BlamRandom.Next( ref seed ) * (1f / 65535f)) * count) % count;
				variant = frame * 4 + (int)(second & 1) * 2 + horizontal;
			}
			var effect = effects[variant] ??= CreateImpactLayer( data, frames[variant].Sprite, parent, contact, incoming, random, variant );
			effect.Emit( contact.Position, 0 );
		}
	}

	ParticleEffect CreateImpactLayer( ImpactEmitter data, Sprite sprite, GameObject parent, Transform contact, Vector3 incoming, Vector2 random, int variant = 0 )
	{
		bool smoke = data.Layer == ImpactLayer.Smoke, sparks = data.Layer == ImpactLayer.Sparks;
		bool animatedRadius = smoke || data.Layer is ImpactLayer.ElectricArcs or ImpactLayer.FierySmoke or ImpactLayer.Shield;
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
		renderer.Lighting = smoke || data.Layer is ImpactLayer.Plant or ImpactLayer.Dust or ImpactLayer.Mist or ImpactLayer.Rubber or ImpactLayer.Rocks or ImpactLayer.MetalChips or ImpactLayer.GlassShards;
		renderer.Additive = sparks || data.Layer is ImpactLayer.ElectricArcs or ImpactLayer.FierySmoke or ImpactLayer.Shield;
		if ( sparks ) renderer.Alignment = ParticleSpriteRenderer.BillboardAlignment.Particle;
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
				if ( data.Layer == ImpactLayer.Shield ) p.Size = new Vector2( p.Size.x, p.Size.x * p.Get<float>( "mirror" ) );
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
			p.Color = Color.Lerp( data.Tint, data.TintEnd, RandomInput( data.ColorInput ) );
			p.Angles.roll = (data.Layer switch { ImpactLayer.Dust or ImpactLayer.Mist => fraction, ImpactLayer.Plant => size, ImpactLayer.Cloth => 0, _ => life }) * 360;
			if ( data.Layer == ImpactLayer.Shield )
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
				p.Sequence = variant / (data.Layer == ImpactLayer.Plant ? 4 : 2);
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
				if ( data.Layer is ImpactLayer.Rocks or ImpactLayer.Sparks or ImpactLayer.MetalChips or ImpactLayer.GlassShards or ImpactLayer.Plant or ImpactLayer.Rubber or ImpactLayer.Cloth && dt > 0 && Collide( p, data, go.Scene, dt ) )
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
