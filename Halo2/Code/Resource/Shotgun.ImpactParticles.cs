using Halo2.Blam;
using System;
using static Halo2.PelletImpactEffect;

namespace Halo2;

internal sealed partial class ShotgunLoader
{
	ImpactEmitter[] ReadImpactEmitters( TagEntry contact, int row )
	{
		int count = map.ReadInt32( row + 48 );
		if ( count == 0 ) return null;
		var emitters = new ImpactEmitter[count];
		int system = Block( row + 48 );
		for ( int i = 0; i < emitters.Length; i++, system += 56 )
		{
			var particle = Reference( system );
			var name = particle?.TagPath switch
			{
				@"effects\generic\solids\dirt_burst" => DirtBurstName,
				@"effects\generic\smoke\dirt_puff" => DirtPuffName,
				@"effects\generic\solids\dirt_burst_cone" => DirtConeName,
				@"effects\generic\solids\rubber_impact" => RubberName,
				@"effects\generic\smoke\blood_mist_impact" => MistName,
				@"effects\generic\solids\debris_shirt" => ClothName,
				@"effects\generic\solids\plant_leafy" => LeafName,
				@"effects\generic\solids\plant_grassy" => GrassName,
				@"effects\generic\solids\impact_burst" or @"effects\generic\solids\impact_burst_unlit" => BurstName,
				@"effects\generic\solids\impact_burst_soft_unlit" => SoftBurstName,
				@"effects\generic\solids\impact_clumps" or @"effects\generic\solids\impact_clumps_unlit" => ClumpsName,
				@"effects\generic\solids\impact_ripple_unlit" => RippleName,
				@"effects\generic\energy\impact_energy" or @"effects\generic\energy\impact_energy_flat" => ShieldName,
				@"effects\generic\solids\plant_wood" => WoodName,
				@"effects\generic\smoke\smoke_impact" => DustName,
				@"effects\generic\sparks\electric_arcs" => ArcName,
				@"effects\generic\smoke\smoke_fiery_small" => SmallFierySmokeName,
				@"effects\generic\smoke\smoke_drift" => DriftName,
				@"effects\generic\solids\rocks" => RocksName,
				@"effects\generic\solids\glass_breakable" => GlassName,
				@"effects\generic\solids\metal_chips" => MetalChipsName,
				@"effects\generic\smoke\smoke_soft_small_lit" => ImpactSmokeName,
				@"effects\generic\solids\impact_spray" => SprayName,
				@"effects\generic\solids\impact_spray_unlit" => UnlitSprayName,
				@"effects\generic\solids\impact_spray_thick_unlit" => ThickSprayName,
				@"effects\generic\smoke\smoke_soft_small" => UnlitImpactSmokeName,
				@"effects\generic\sparks\sparks_animated_soft" or @"effects\generic\sparks\sparks_animated_fiery" => SparksName,
				_ => null
			};
			if ( name is null || map.ReadInt32( system + 48 ) != 1 ) continue;
			var texturePath = $"mount://{Halo2Mount.MountIdent}/{PrefabPath}/{name}";
			var texture = Texture.Load( texturePath );
			if ( !texture.IsValid() || texture.IsError ) continue;
			var layer = name switch
			{
				SprayName or UnlitSprayName or ThickSprayName => ImpactLayer.Spray,
				RubberName => ImpactLayer.Rubber,
				MistName => ImpactLayer.Mist,
				ShieldName => ImpactLayer.Shield,
				RippleName => ImpactLayer.Ripple,
				ClumpsName => ImpactLayer.Clumps,
				BurstName or SoftBurstName => ImpactLayer.Burst,
				WoodName or LeafName or GrassName => ImpactLayer.Plant,
				ClothName => ImpactLayer.Cloth,
				DirtBurstName or DirtPuffName or DirtConeName => ImpactLayer.Dirt,
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
			var origin = new Vector2( 0.5f, 0.5f );
			int definition = Address( particle );
			int sequence = Short( definition + 8 );
			if ( layer == ImpactLayer.Sparks ) origin = new Vector2( 1 - 48f / 56f, (sequence == 1 ? 5 : 4) / 9f );
			var geometry = new Vector2( Float( definition + 164 ), Float( definition + 168 ) );
			if ( HasSpriteRadius( layer ) )
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
			bool twoColorStops = (map.ReadInt32( color ) & 0xff00) == 0x2000;
			Color Tint( int offset )
			{
				int rgb = map.ReadInt32( color + offset );
				return Color.FromBytes( (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb );
			}
			bool animatedRadius = HasAnimatedRadius( layer ), drift = layer == ImpactLayer.Drift;
			bool dust = HasSpriteRadius( layer );
			bool spriteAngle = layer is ImpactLayer.Rubber or ImpactLayer.GlassShards;
			bool cloth = layer == ImpactLayer.Cloth;
			bool spriteCurves = drift || dust || spriteAngle || cloth;
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
				AnimatedFrames = cloth ? ReadAnimatedFrames( definition ) : null, Cycles = cloth ? Bounds( definition + 92 ).y : 0,
				SpriteAspect = geometry * 0.5f, RadiusBounds = dust ? Bounds( definition + 60 ) : default,
				AngleBounds = spriteAngle ? Bounds( definition + 76 ) : default, AlphaInput = Short( emitter + 104 ),
				SpriteAngle = drift || spriteAngle ? ReadRange( definition + 76, 1 ) : default,
				Marker = map.ResolveStringId( map.ReadInt32( Block( Address( contact ) + 12 ) + 4 * Short( system + 8 ) ) )
			};
		}
		return emitters.Any( e => e is not null ) ? emitters : null;
	}

	AnimatedFrame[] ReadAnimatedFrames( int definition )
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
