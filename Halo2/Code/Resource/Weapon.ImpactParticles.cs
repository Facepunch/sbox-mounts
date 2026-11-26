using Halo2.Blam;
using System;
using static Halo2.PelletImpactEffect;
using static Halo2.ShotgunLoader;

namespace Halo2;

internal abstract partial class WeaponLoader
{
	protected CurveRange ReadRange( int property, float units, bool fraction = false ) => ReadCurveRange( Block( property + 8 ), units, fraction );

	protected CurveRange ReadCurveRange( int data, float units, bool fraction = false )
	{
		float min = fraction ? 0 : Float( data + 4 ) * units, span = fraction ? 1 : Float( data + 8 ) * units - min;
		int kind = map.ReadSpan( data, 1 )[0];
		bool ranged = (map.ReadSpan( data + 1, 1 )[0] & 1) != 0;
		int length = kind switch
		{
			0 => 0,
			1 => 4,
			2 when Short( data + 2 ) == 0 => 8, // Linear transition.
			3 when Short( data + 2 ) is >= 8 and <= 10 && !ranged => 16, // Periodic noise.
			4 => 24,
			5 => 80,
			7 or 10 => 48,
			_ => throw new NotSupportedException( $"Unsupported particle curve {kind}" )
		};
		var first = ReadCurve( data + 20, false );
		return new CurveRange( first, ranged ? ReadCurve( data + 20 + length, true ) : first );

		Curve ReadCurve( int start, bool upper )
		{
			Curve.Frame Key( float t, float v ) => new( t, min + span * v ) { Mode = Curve.HandleMode.Linear };
			if ( kind == 0 ) return new Curve( Key( 0, 0 ), Key( 1, 1 ) );
			if ( kind == 1 ) return new Curve( Key( 0, upper ? 1 : 0 ) );
			if ( kind == 2 ) return new Curve( Key( 0, Float( start ) ), Key( 1, Float( start + 4 ) ) );
			if ( kind == 3 )
			{
				float frequency = Float( start ) * 36.57143f, phase = Float( start + 4 ) * 36.57143f;
				float low = Float( start + 8 ), high = Float( start + 12 );
				Curve.Frame Sample( float t )
				{
					float x = phase + t * frequency;
					int index = (int)x;
					var table = Host.GetPeriodicNoise( Short( data + 2 ) );
					float noise = (table[index & 1023] + (x - index) * (table[(index + 1) & 1023] - table[index & 1023])) / 255f;
					return Key( t, (low + (high - low) * noise).Clamp( 0, 1 ) );
				}
				var keys = new List<Curve.Frame> { Sample( 0 ) };
				for ( int i = (int)MathF.Floor( phase ) + 1; i < phase + frequency; i++ )
					keys.Add( Sample( (i - phase) / frequency ) );
				keys.Add( Sample( 1 ) );
				return new Curve( keys );
			}
			if ( kind == 4 )
			{
				float slope = Float( start + 16 ), intercept = Float( start + 20 );
				return new Curve( Key( 0, intercept ), Key( 1, intercept + slope ) );
			}
			if ( kind == 5 )
				return new Curve( Enumerable.Range( 0, 4 ).Select( i => Key( Float( start + 8 * i ), Float( start + 8 * i + 4 ) ) ) );

			// The remaining four words of a cubic hold its polynomial coefficients.
			int offset = start + 32;
			float a = Float( offset ), b = Float( offset + 4 ), c = Float( offset + 8 ), d = Float( offset + 12 );
			if ( kind == 10 )
			{
				Vector2 Point( int i ) => new( Float( start + i * 8 ), Float( start + i * 8 + 4 ) );
				float head = (Point( 1 ) - Point( 0 )).LengthSquared;
				float tail = (Point( 3 ) - Point( 2 )).LengthSquared;
				float exponent = tail <= 0.0001f || MathF.Abs( head - tail ) <= 0.0001f ? 1
					: head <= 0.0001f ? 0 : BitConverter.Int32BitsToSingle( (BitConverter.SingleToInt32Bits( head ) >> 1) + 0x1fc00000 ) / MathF.Sqrt( tail );
				// Bake the power-remapped cubic into the particle system's normalized curve.
				return new Curve( Enumerable.Range( 0, 65 ).Select( i =>
				{
					float time = i / 64f, t = MathF.Pow( time, exponent );
					return Key( time, (((a * t + b) * t + c) * t + d).Clamp( 0, 1 ) );
				} ) );
			}
			return new Curve( new Curve.Frame( 0, min + span * d, 0, span * c ),
				new Curve.Frame( 1, min + span * (a + b + c + d), -span * (3 * a + 2 * b + c), 0 ) );
		}
	}

	ImpactEmitter[] ReadImpactEmitters( TagEntry contact, int row )
	{
		int count = map.ReadInt32( row + 48 );
		if ( count == 0 ) return null;
		var emitters = new ImpactEmitter[count];
		int system = Block( row + 48 );
		for ( int i = 0; i < emitters.Length; i++, system += 56 )
		{
			var particle = Reference( system );
			if ( particle is null || map.ReadInt32( system + 48 ) != 1 ) continue;
			int definition = Address( particle );
			(string TextureName, ImpactLayer? Layer) style = particle.TagPath switch
			{
				@"effects\generic\solids\dirt_burst" => (DirtBurstName, ImpactLayer.Dirt),
				@"effects\generic\smoke\dirt_puff" => (DirtPuffName, ImpactLayer.Dirt),
				@"effects\generic\solids\dirt_burst_cone" => (DirtConeName, ImpactLayer.Dirt),
				@"effects\generic\solids\rubber_impact" => (RubberName, ImpactLayer.Rubber),
				@"effects\generic\smoke\blood_mist_impact" => (MistName, ImpactLayer.Mist),
				@"effects\generic\solids\debris_shirt" => (ClothName, ImpactLayer.Cloth),
				@"effects\generic\solids\plant_leafy" => (LeafName, ImpactLayer.Plant),
				@"effects\generic\solids\plant_grassy" => (GrassName, ImpactLayer.Plant),
				@"effects\generic\solids\impact_burst" or @"effects\generic\solids\impact_burst_unlit" => (BurstName, ImpactLayer.Burst),
				@"effects\generic\solids\impact_burst_soft_unlit" => (SoftBurstName, ImpactLayer.Burst),
				@"effects\generic\solids\impact_clumps" or @"effects\generic\solids\impact_clumps_unlit" => (ClumpsName, ImpactLayer.Clumps),
				@"effects\generic\solids\impact_ripple_unlit" => (RippleName, ImpactLayer.Ripple),
				@"effects\generic\energy\impact_energy" or @"effects\generic\energy\impact_energy_flat" => (ShieldName, ImpactLayer.Shield),
				@"effects\generic\solids\plant_wood" => (WoodName, ImpactLayer.Plant),
				@"effects\generic\smoke\smoke_impact" => (DustName, ImpactLayer.Dust),
				@"effects\objects\characters\elite\elite_shield_depletion" => (null, ImpactLayer.ShieldDepletion),
				@"effects\generic\sparks\electric_arcs" => (ArcName, ImpactLayer.ElectricArcs),
				@"effects\generic\smoke\smoke_fiery_small" => (SmallFierySmokeName, ImpactLayer.FierySmoke),
				@"effects\generic\smoke\smoke_fiery" => (null, ImpactLayer.FierySmoke),
				@"effects\generic\smoke\smoke_drift" => (DriftName, ImpactLayer.Drift),
				@"effects\generic\solids\rocks" => (RocksName, ImpactLayer.Rocks),
				@"effects\generic\solids\glass_breakable" => (GlassName, ImpactLayer.GlassShards),
				@"effects\generic\solids\metal_chips" => (MetalChipsName, ImpactLayer.MetalChips),
				@"effects\generic\smoke\smoke_soft_small_lit" => (ImpactSmokeName, ImpactLayer.Smoke),
				@"effects\generic\solids\impact_spray" => (SprayName, ImpactLayer.Spray),
				@"effects\generic\solids\impact_spray_unlit" => (UnlitSprayName, ImpactLayer.Spray),
				@"effects\generic\solids\impact_spray_thick_unlit" => (ThickSprayName, ImpactLayer.Spray),
				@"effects\generic\smoke\smoke_soft_small" => (UnlitImpactSmokeName, ImpactLayer.Smoke),
				@"effects\generic\sparks\sparks_trail" => (SparkTrailName, ImpactLayer.SparkTrail),
				@"effects\generic\sparks\sparks_animated_soft" or @"effects\generic\sparks\sparks_animated_fiery" => (SparksName, ImpactLayer.Sparks),
				@"effects\generic\smoke\smoke_soft_additive" or @"effects\generic\sparks\flash_h_shield_impact" => (null, ImpactLayer.AdditiveSprite),
				@"effects\generic\flares\burst_additive" => (null, ImpactLayer.AdditiveFlare),
				@"effects\objects\weapons\pistol\needler\shard_debris" => (null, ImpactLayer.GlassShards),
				_ => (null, (ImpactLayer?)null)
			};
			if ( style.Layer is not {} layer ) continue;
			string texturePath;
			if ( style.TextureName is {} name ) texturePath = $"mount://{Halo2Mount.MountIdent}/{PrefabPath}/{name}";
			else
			{
				var bitmap = map.GetTag( map.ReadInt32( Block( Block( definition + 140 ) + 4 ) ) & 0xffff );
				if ( bitmap is null ) continue;
				texturePath = Host.AssetPath( map, bitmap.TagPath, "vtex" );
				if ( layer == ImpactLayer.AdditiveSprite && Short( Address( bitmap ) ) == 3 ) texturePath += $"/sequence_{Short( definition + 8 )}.vtex";
				else if ( map.ReadInt32( Address( bitmap ) + 60 ) > 0 ) texturePath += layer == ImpactLayer.ShieldDepletion ? "/loop.vtex" : "/sheet.vtex";
			}
			var texture = Texture.Load( texturePath );
			if ( !texture.IsValid() || texture.IsError ) continue;
			var origin = new Vector2( 0.5f, 0.5f );
			int sequence = Short( definition + 8 );
			if ( layer == ImpactLayer.Sparks ) origin = new Vector2( 1 - 48f / 56f, (sequence == 1 ? 5 : 4) / 9f );
			var geometry = new Vector2( Float( definition + 164 ), Float( definition + 168 ) );
			if ( HasSpriteRadius( layer ) || layer == ImpactLayer.AdditiveFlare )
				origin = new Vector2( 1 + Float( definition + 156 ) / geometry.x, -Float( definition + 160 ) / geometry.y );
			// Both frames of a burst share their footprint and pivot. The fourth random input blends the UVs.
			if ( layer == ImpactLayer.Burst )
			{
				var frame = ReadAnimatedFrames( definition )[0];
				var pivot = frame.Pivot * frame.InverseSize;
				origin = new Vector2( 1 - pivot.x, pivot.y );
				geometry = frame.Aspect * 2;
			}
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
			bool twoColorStops = (map.ReadSpan( color + 1, 1 )[0] >> 4) == 2;
			Color Tint( int offset )
			{
				int rgb = map.ReadInt32( color + offset );
				return Color.FromBytes( (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb );
			}
			bool animatedRadius = HasAnimatedRadius( layer ), drift = layer == ImpactLayer.Drift;
			bool dust = HasSpriteRadius( layer );
			bool spriteAngle = layer is ImpactLayer.Rubber or ImpactLayer.GlassShards;
			bool cloth = layer == ImpactLayer.Cloth;
			bool spriteCurves = drift || dust || spriteAngle || cloth || layer == ImpactLayer.SparkTrail;
			var size = Bounds( emitter + 72 ) * 120;
			if ( layer == ImpactLayer.Rubber ) size *= Bounds( definition + 60 ).x;
			bool collides = movement is not null && map.ReadInt32( Address( movement ) + 12 ) > 1 && Short( controller + 20 ) == 1;
			int mirrors = layer is ImpactLayer.Plant or ImpactLayer.Dirt || (Short( definition + 10 ) & 1) != 0 ? 4 : 2;
			int particleFlags = map.ReadInt32( definition );
			emitters[i] = new ImpactEmitter( layer, new ImpactSprite( texturePath, origin ), Bounds( emitter + 8 ), Bounds( emitter + 24 ), Bounds( emitter + 40 ) * 120, Bounds( emitter + 140 ), Bounds( emitter + 56 ),
				size, drift || dust ? ReadRange( definition + 60, 1 ) : animatedRadius || layer == ImpactLayer.Sparks ? ReadRange( emitter + 72, 120 ) : default,
				spriteCurves ? ReadRange( definition + 44, 1 ) : ReadRange( emitter + 104, 1 ),
				Tint( 4 ), Tint( twoColorStops ? 16 : 4 ), Parameter( controller, 0 ), Parameter( controller, 1 ), Parameter( controller, 2 ),
				collides ? new Vector2( Parameter( controller + 20, 0 ), Parameter( controller + 20, 1 ) ) : default )
			{
				ParallelToDirection = Short( definition + 4 ) == 1,
				VerticalSprite = (particleFlags & 0x2000) != 0,
				Collides = collides, MirrorsPerFrame = mirrors,
				PerpendicularToDirection = Short( definition + 4 ) == 2,
				Lighting = (particleFlags & 0x80) != 0,
				CountInput = Short( emitter + 8 ), LifeInput = Short( emitter + 24 ), SpeedInput = Short( emitter + 40 ), SizeInput = Short( emitter + 72 ),
				RadiusRange = Short( emitter + 74 ), ColorInput = Short( emitter + 88 ), ConeInput = Short( emitter + 140 ), Sequence = sequence,
				SpinCurve = map.ReadSpan( Block( emitter + 64 ), 1 )[0] == 4 ? ReadRange( emitter + 56, 1 ) : null,
				SpinRange = Short( emitter + 58 ), SpinInput = Short( emitter + 56 ), SpawnRadiusInput = Short( emitter + 124 ),
				DebrisFrames = layer == ImpactLayer.Dirt ? FixedFrames( texturePath, origin, geometry * 0.5f, particleFlags ) : layer is ImpactLayer.MetalChips or ImpactLayer.GlassShards or ImpactLayer.ElectricArcs or ImpactLayer.Plant or ImpactLayer.Rubber or ImpactLayer.Clumps ? ReadDebrisFrames( definition, texturePath, layer == ImpactLayer.Plant, mirrors ) : null,
				Shape = map.ReadInt32( emitter + 120 ), SpawnRadius = Bounds( emitter + 124 ) * 120, Alpha = Bounds( emitter + 104 ),
				CollidesWithWater = movement is not null && (map.ReadInt32( Address( movement ) + 8 ) & 4) != 0,
				DiesInWater = (particleFlags & 0x800) != 0, DiesOnStructure = (particleFlags & 0x400) != 0,
				AlphaScale = layer == ImpactLayer.AdditiveSprite ? ReadRange( definition + 44, 1 ) : null,
				AgeTint = Short( emitter + 88 ) == 0 && (map.ReadSpan( color + 1, 1 )[0] >> 4) == 2 && map.ReadSpan( color, 1 )[0] == 5
					? ReadRange( emitter + 88, 1, fraction: true ) : null,
				ColorRange = Short( emitter + 90 ),
				FrameCurve = layer is ImpactLayer.SparkTrail or ImpactLayer.ShieldDepletion || (layer == ImpactLayer.AdditiveSprite && map.ReadInt32( definition + 100 ) > 0) ? ReadRange( definition + 92, 1 ) : null,
				AnimatedFrames = cloth || layer is ImpactLayer.SparkTrail or ImpactLayer.ShieldDepletion ? ReadAnimatedFrames( definition ) : null, Cycles = cloth ? Bounds( definition + 92 ).y : 0,
				SpriteAspect = geometry * 0.5f, RadiusBounds = dust ? Bounds( definition + 60 ) : default,
				AngleBounds = spriteAngle ? Bounds( definition + 76 ) : default, AlphaInput = Short( emitter + 104 ),
				SpriteAngle = drift || spriteAngle ? ReadRange( definition + 76, 1 ) : default,
				Marker = map.ResolveStringId( map.ReadInt32( Block( Address( contact ) + 12 ) + 4 * Short( system + 8 ) ) )
			};
		}
		return emitters.Any( e => e is not null ) ? emitters : null;
	}

	protected AnimatedFrame[] ReadAnimatedFrames( int definition )
	{
		int shader = Block( definition + 140 );
		var bitmap = map.GetTag( map.ReadInt32( Block( shader + 4 ) ) & 0xffff );
		int sequence = Block( Address( bitmap ) + 60 ) + Short( definition + 8 ) * 60;
		int first = Block( sequence + 52 );
		return Enumerable.Range( 0, map.ReadInt32( sequence + 52 ) ).Select( i =>
		{
			int frame = first + i * 32;
			var size = Halo2TextureLoader.SpriteBounds( map, frame + 8 ).Size;
			return new AnimatedFrame( Vector2.One / size, size / MathF.Max( size.x, size.y ), new Vector2( Float( frame + 24 ), Float( frame + 28 ) ));
		} ).ToArray();
	}

	static DebrisFrame[] FixedFrames( string texture, Vector2 origin, Vector2 aspect, int flags )
	{
		var frames = new DebrisFrame[4];
		for ( int i = 0; i < frames.Length; i++ )
		{
			bool flipX = (flags & 2) != 0 && (i & 1) != 0;
			bool flipY = (flags & 4) != 0 && (i & 2) != 0;
			var pivot = new Vector2( flipX ? 1 - origin.x : origin.x, flipY ? 1 - origin.y : origin.y );
			frames[i] = new DebrisFrame( new Vector2( flipX ? -aspect.x : aspect.x, flipY ? -aspect.y : aspect.y ), new ImpactSprite( texture, pivot ) );
		}
		return frames;
	}

	DebrisFrame[] ReadDebrisFrames( int definition, string texturePath, bool singleSequence, int mirrors )
	{
		int shader = Block( definition + 140 );
		var bitmap = map.GetTag( map.ReadInt32( Block( shader + 4 ) ) & 0xffff );
		int sequences = Block( Address( bitmap ) + 60 ) + 60 * Short( definition + 8 );
		int count = singleSequence ? map.ReadInt32( sequences + 52 ) : Short( definition + 10 );
		var frames = new DebrisFrame[count * mirrors];
		for ( int i = 0; i < count; i++ )
		{
			int sprite = singleSequence ? Block( sequences + 52 ) + 32 * i : Block( sequences + 60 * i + 52 );
			var size = Halo2TextureLoader.SpriteBounds( map, sprite + 8 ).Size;
			var pivot = new Vector2( Float( sprite + 24 ), Float( sprite + 28 ) ) / size;
			var aspect = size / MathF.Max( size.x, size.y );
			for ( int mirror = 0; mirror < mirrors; mirror++ )
			{
				bool flipX = (mirror & 1) != 0, flipY = mirrors == 4 ? (mirror & 2) != 0 : (i & 1) != 0;
				var origin = new Vector2( flipX ? pivot.x : 1 - pivot.x, flipY ? 1 - pivot.y : pivot.y );
				frames[i * mirrors + mirror] = new DebrisFrame( new Vector2( flipX ? -aspect.x : aspect.x, flipY ? -aspect.y : aspect.y ), new ImpactSprite( texturePath, origin ) );
			}
		}
		return frames;
	}
}
