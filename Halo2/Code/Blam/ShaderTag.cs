using System.IO;

namespace Halo2.Blam;

/// <summary>
/// Parses a Halo 2 shader (shad) tag to extract bitmap references.
/// Layout from Assembly shad.xml plugin (Halo2Xbox).
/// </summary>
public class ShaderTag
{
	/// <summary>Tag index of the diffuse bitmap, or -1 if not found.</summary>
	public int DiffuseBitmapTagIndex { get; set; } = -1;

	/// <summary>Tag index of the illumination bitmap, or -1 if not found.</summary>
	public int IllumBitmapTagIndex { get; set; } = -1;

	/// <summary>Tag index of a secondary bitmap (bump/normal), or -1 if not found.</summary>
	public int BumpBitmapTagIndex { get; set; } = -1;

	/// <summary>Tag index of the change color map bitmap, or -1 if not found.</summary>
	public int ChangeColorBitmapTagIndex { get; set; } = -1;

	internal int MeterTintCurve { get; private set; }
	internal int MeterOpacityCurve { get; private set; }
	internal bool ConstantMeterOpacity { get; private set; }
	public Vector2? MeterRange { get; private set; }
	public int AmmoDigitDivisor { get; private set; }

	public int ChangeColorChannels { get; private set; }

	public enum EffectPass { None, Emblem, Plasma, Hair, Additive, Eyes, SelfIllumination, Bloom, MaskedDetail, Meter, ClampedIllumination, AmmoDigit, AdditiveEnvironment, Scope }
	public EffectPass Effect { get; private set; }
	public int[] EffectMaps { get; private set; }
	public Vector4[] EffectConstants { get; private set; }
	public int GlowBitmap { get; private set; } = -1;
	public Vector3 GlowColor { get; private set; }
	public Vector4[] EffectUV { get; private set; }
	public float? PoweredPlasmaAlpha { get; private set; }
	public Vector4 PlasmaScroll { get; private set; }
	public bool AlphaTest { get; private set; }
	public int DetailBitmapTagIndex { get; private set; } = -1;
	public int EnvironmentBitmapTagIndex { get; private set; } = -1;
	public Vector4 DetailTransform { get; private set; } = new( 1, 1, 0, 0 );
	public bool EnvironmentUsesUnsignedNormalZ { get; private set; }
	public Vector4 EnvironmentFacing { get; private set; }
	public Vector4 EnvironmentGlancing { get; private set; }

	public ShaderTag( MapFile map, TagEntry tag )
	{
		if ( tag.DataSize == 0 )
			return;

		using var reader = map.GetTagDataReader( tag );

		// RuntimeProperties at +0x0C (tagblock: count:4 + pointer:4)
		reader.BaseStream.Seek( 0x0C, SeekOrigin.Begin );
		int rpCount = reader.ReadInt32();
		int rpPtr = reader.ReadInt32();

		if ( rpCount > 0 && rpPtr != 0 )
		{
			// RuntimePropertiesBlock is 80 bytes (0x50)
			int rpOff = map.VaToOffset( rpPtr );
			using var r = map.OpenReader( rpOff, 80 );

			// DiffuseBitmapReference at +0 (TagReference: classId:4 + tagDatum:4 = 8 bytes)
			r.ReadInt32(); // classId
			int diffuseDatum = r.ReadInt32();
			if ( diffuseDatum != -1 && diffuseDatum != 0 )
				DiffuseBitmapTagIndex = diffuseDatum & 0xFFFF;

			// IllumBitmapReference at +8
			r.ReadInt32(); // classId
			int illumDatum = r.ReadInt32();
			if ( illumDatum != -1 && illumDatum != 0 )
				IllumBitmapTagIndex = illumDatum & 0xFFFF;
		}

		// PostprocessDefinition at +0x20 (tagblock: count:4 + pointer:4, element size 0x7C)
		reader.BaseStream.Seek( 0x20, SeekOrigin.Begin );
		int ppCount = reader.ReadInt32();
		int ppPtr = reader.ReadInt32();

		if ( ppCount > 0 && ppPtr != 0 )
		{
			int ppOff = map.VaToOffset( ppPtr );
			using var pp = map.OpenReader( ppOff, 0x7C );

			// +0x00: Shader Template tagRef (withGroup=false, 4 bytes = datum only)
			int templateDatum = pp.ReadInt32();
			string templateName = null;
			if ( templateDatum != -1 && templateDatum != 0 )
			{
				var templateTag = map.GetTag( templateDatum & 0xFFFF );
				templateName = templateTag?.TagPath;
			}

			// +0x04: Bitmaps tagblock (count:4 + pointer:4)
			int bmpCount = pp.ReadInt32();
			int bmpPtr = pp.ReadInt32();

			if ( bmpCount > 0 && bmpPtr != 0 && templateName != null )
			{
				int bmpOff = map.VaToOffset( bmpPtr );
				int Bitmap( int slot ) => slot >= 0 && slot < bmpCount ? map.ReadInt32( bmpOff + slot * 12 ) & 0xffff : -1;
				string name = templateName[(templateName.LastIndexOf( '\\' ) + 1)..];
				if ( name == "illum_detail" && map.ReadInt32( ppOff + 60 ) > 0 )
				{
					int overlay = map.VaToOffset( map.ReadInt32( ppOff + 64 ) );
					AmmoDigitDivisor = map.ResolveStringId( map.ReadInt32( overlay ) ) switch
					{
						"primary_ammunition_ones" => 1,
						"primary_ammunition_tens" => 10,
						_ => 0
					};
				}
				Effect = name switch
				{
					"scope" => EffectPass.Scope,
					"emblem_overlay_simple" => EffectPass.Emblem,
					"plasma_mask_offset" => EffectPass.Plasma,
					"transparent_lit_alpha_blend_two_change_color" => EffectPass.Hair,
					"illum_bloom" => EffectPass.Bloom,
					"illum" => EffectPass.SelfIllumination,
					"illum_detail" when AmmoDigitDivisor != 0 => EffectPass.AmmoDigit,
					"illum_clamped" => EffectPass.ClampedIllumination,
					"meter_active_camo" => EffectPass.Meter,
					"one_add_illum" => EffectPass.Additive,
					"two_alpha_detail_masked" => EffectPass.MaskedDetail,
					"two_add_env_illum_active_camo" => EffectPass.AdditiveEnvironment,
					"one_alpha_env_active_camo" or "one_alpha_env" or "two_alpha_env_illum" or "sky_one_alpha_env" => EffectPass.Eyes,
					_ => EffectPass.None
				};
				if ( Effect != EffectPass.None )
				{
					if ( Effect is EffectPass.Meter or EffectPass.AdditiveEnvironment ) ReadMeterBindings( map, ppOff );
					if ( Effect == EffectPass.Plasma )
					{
						PlasmaScroll = ReadPlasmaScroll( map, ppOff );
						PoweredPlasmaAlpha = ReadPoweredPlasmaAlpha( map, ppOff );
					}
					EffectMaps = new int[bmpCount];
					for ( int i = 0; i < bmpCount; i++ ) EffectMaps[i] = Bitmap( i );
					if ( Effect is EffectPass.Eyes or EffectPass.AdditiveEnvironment or EffectPass.Scope )
						EffectMaps = ReadSamplerBindings( map, map.GetTag( templateDatum & 0xffff ), EffectMaps );
					int count = pp.ReadInt32();
					int offset = map.VaToOffset( pp.ReadInt32() );
					EffectConstants = new Vector4[count];
					for ( int i = 0; i < count; i++ )
					{
						uint v = (uint)map.ReadInt32( offset + i * 4 );
						EffectConstants[i] = new Vector4( (v >> 16) & 255, (v >> 8) & 255, v & 255, v >> 24 ) / 255f;
					}
					int registerCount = pp.ReadInt32();
					int registers = map.VaToOffset( pp.ReadInt32() );
					int firstRegister = (Effect is EffectPass.Eyes or EffectPass.AdditiveEnvironment) && name != "sky_one_alpha_env" ? 4 : 0;
					int layers = Effect switch
					{
						EffectPass.Eyes or EffectPass.AdditiveEnvironment => 3,
						EffectPass.MaskedDetail or EffectPass.Plasma => 4,
						EffectPass.Hair or EffectPass.Meter => 2,
						EffectPass.Additive => 1,
						_ => 0
					};
					if ( layers > 0 && firstRegister + layers * 2 <= registerCount )
					{
						using var rows = map.OpenReader( registers + firstRegister * 16, layers * 32 );
						EffectUV = new Vector4[layers * 2];
						for ( int i = 0; i < EffectUV.Length; i++ )
							EffectUV[i] = new( rows.ReadSingle(), rows.ReadSingle(), rows.ReadSingle(), rows.ReadSingle() );
					}
					return;
				}
				AlphaTest = name.StartsWith( "tex_bump" ) && name.Contains( "alpha_test" );
				if ( name.StartsWith( "tex_bump" ) ) BumpBitmapTagIndex = Bitmap( 0 );
				ChangeColorBitmapTagIndex = Bitmap( GetChangeColorSlot( name ) );
				if ( ChangeColorBitmapTagIndex >= 0 )
					ChangeColorChannels = name.Contains( "four_change_color" ) ? 4 : name.Contains( "one_change_color" ) ? 1 : 2;

				// Compiled templates place the same maps and registers at different offsets.
				var (detail, uv, cube, tint, strength) = name switch
				{
					"tex_bump" or "tex_bump_active_camo" or "tex_bump_shiny" or "tex_bump_alpha_test_single_pass" => (3, 2, -1, -1, -1),
					"tex_bump_env" or "tex_bump_env_clamped" or "tex_bump_env_illum" => (3, 0, 4, 1, 3),
					"tex_bump_dprs_env" => (3, 2, 4, -1, -1),
					"tex_bump_env_combined" => (3, 2, 4, 0, 2),
					"tex_bump_illum" => (2, 2, -1, -1, -1),
					"tex_bump_env_no_detail" => (-1, -1, 3, 0, 2),
					"tex_bump_env_alpha_test" => (4, 2, 5, 0, 2),
					"tex_bump_env_four_change_color" or "tex_bump_env_two_change_color" => (3, 2, 5, 4, 6),
					"tex_bump_env_illum_two_change_color" => (3, 2, 5, 5, 7),
					"tex_bump_env_illum_four_change_color_no_lod" => (3, 2, 6, -1, -1),
					"tex_bump_env_illum_detail_honor_guard" => (2, 2, 5, -1, -1),
					_ => (-1, -1, -1, -1, -1)
				};
				DetailBitmapTagIndex = Bitmap( detail );
				EnvironmentBitmapTagIndex = Bitmap( cube );
				EnvironmentUsesUnsignedNormalZ = name.Contains( "four_change_color" );
				int colorCount = pp.ReadInt32();
				int colors = map.VaToOffset( pp.ReadInt32() );
				int transformCount = pp.ReadInt32();
				int transforms = map.VaToOffset( pp.ReadInt32() );
				Vector4 Register( int slot )
				{
					if ( (uint)slot >= transformCount ) return default;
					using var r = map.OpenReader( transforms + slot * 16, 16 );
					return new( r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle() );
				}
				int glowSlot = name switch
				{
					"tex_bump_env_illum" => 5,
					"tex_bump_env_illum_two_change_color" => 6,
					"tex_bump_env_illum_four_change_color_no_lod" => 5,
					"tex_bump_env_illum_detail_honor_guard" or "tex_bump_illum" => 3,
					_ => -1
				};
				if ( glowSlot >= 0 )
				{
					GlowBitmap = Bitmap( glowSlot );
					int colorSlot = name switch
					{
						"tex_bump_env_illum" => 7,
						"tex_bump_env_illum_two_change_color" => 3,
						"tex_bump_env_illum_detail_honor_guard" => 5,
						"tex_bump_illum" => 2,
						_ => 4
					};
					if ( colorSlot < colorCount )
					{
						uint rgb = (uint)map.ReadInt32( colors + colorSlot * 4 );
						GlowColor = new Vector3( (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255 ) / 255f;
					}
				}
				if ( uv >= 0 ) DetailTransform = Register( uv );
				if ( tint >= 0 && strength + 1 < colorCount && tint + 1 < colorCount )
				{
					Vector4 Reflection( int i )
					{
						uint rgb = (uint)map.ReadInt32( colors + (tint + i) * 4 );
						uint power = (uint)map.ReadInt32( colors + (strength + i) * 4 );
						return new Vector4( (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255, power >> 24 ) / 255f;
					}
					EnvironmentFacing = Reflection( 0 );
					EnvironmentGlancing = Reflection( 1 );
				}
				else if ( cube >= 0 )
				{
					int slot = name switch
					{
						"tex_bump_dprs_env" => 7,
						"tex_bump_env_illum_detail_honor_guard" => 6,
						_ => 4
					};
					EnvironmentFacing = Register( slot ).WithW( Register( slot + 2 ).w );
					EnvironmentGlancing = Register( slot + 1 ).WithW( Register( slot + 3 ).w );
				}
			}
		}
	}

	// Template parameters select bitmap slots. The pass maps those parameters to sampler stages.
	static int[] ReadSamplerBindings( MapFile map, TagEntry templateTag, int[] bitmaps )
	{
		if ( templateTag.DataSize < 96 ) return bitmaps;
		int Block( int field ) => map.VaToOffset( map.ReadInt32( field + 4 ) );
		int U16( int offset ) => System.BitConverter.ToUInt16( map.ReadSpan( offset, 2 ) );
		int root = map.VaToOffset( (int)templateTag.DataVirtualAddress ) + 88;
		if ( map.ReadInt32( root ) == 0 ) return bitmaps;
		int template = Block( root );
		if ( map.ReadInt32( template + 16 ) == 0 || map.ReadInt32( template + 24 ) == 0 ) return bitmaps;
		int pass = Block( template + 16 );
		var passTag = map.GetTag( map.ReadInt32( pass + 4 ) & 0xffff );
		if ( passTag is not { DataSize: >= 36 } ) return bitmaps;
		int postprocess = map.VaToOffset( (int)passTag.DataVirtualAddress ) + 28;
		if ( map.ReadInt32( postprocess ) == 0 ) return bitmaps;
		int definition = Block( postprocess );
		if ( map.ReadInt32( definition ) == 0 ) return bitmaps;
		int impl = U16( pass + 8 ) & 511;
		if ( impl >= map.ReadInt32( template + 24 ) ) return bitmaps;
		int mappings = U16( Block( template + 24 ) + impl * 6 );
		int bindings = U16( Block( definition ) );
		if ( (mappings & 511) + (mappings >> 9) > map.ReadInt32( template + 32 ) ||
			(bindings & 511) + (bindings >> 9) > map.ReadInt32( definition + 8 ) ) return bitmaps;
		int[] stages = [-1, -1, -1, -1];
		for ( int i = 0; i < bindings >> 9; i++ )
		{
			var binding = map.ReadSpan( Block( definition + 8 ) + ((bindings & 511) + i) * 4, 4 );
			if ( binding[1] != 0 || binding[2] >= stages.Length ) continue;
			for ( int j = 0; j < mappings >> 9; j++ )
			{
				var remap = map.ReadSpan( Block( template + 32 ) + ((mappings & 511) + j) * 4, 4 );
				if ( remap[0] == binding[0] && remap[3] < bitmaps.Length )
					stages[binding[2]] = bitmaps[remap[3]];
			}
		}
		return stages;
	}

	// The meter template binds its threshold to the alpha of pixel constant 1 (0x61).
	void ReadMeterBindings( MapFile map, int pp )
	{
		int Block( int offset ) => map.VaToOffset( map.ReadInt32( pp + offset + 4 ) );
		for ( int i = 0; i < map.ReadInt32( pp + 0x54 ); i++ )
		{
			uint binding = (uint)map.ReadInt32( Block( 0x54 ) + i * 4 );
			int destination = (int)(binding & 0xffff);
			if ( destination is not (0x61 or 0x83 or 0x64) ) continue;
			int parameter = (int)(binding >> 24);
			if ( parameter >= map.ReadInt32( pp + 0x4c ) ) continue;
			int range = System.BitConverter.ToUInt16( map.ReadSpan( Block( 0x4c ) + parameter * 2, 2 ) );
			if ( range >> 9 != 1 || (range & 511) >= map.ReadInt32( pp + 0x44 ) ) continue;
			int index = (ushort)map.ReadInt32( Block( 0x44 ) + (range & 511) * 4 );
			if ( index >= map.ReadInt32( pp + 0x3c ) ) continue;
			int overlay = Block( 0x3c ) + index * 20;
			string input = map.ResolveStringId( map.ReadInt32( overlay ) );
			if ( input is not ("ammo" or "heat" or "one") || map.ReadInt32( overlay + 12 ) < 20 ) continue;
			int curve = map.VaToOffset( map.ReadInt32( overlay + 16 ) );
			if ( destination == 0x83 ) { MeterTintCurve = curve; continue; }
			if ( destination == 0x64 )
			{
				MeterOpacityCurve = curve;
				ConstantMeterOpacity = input == "one";
				continue;
			}
			// The weapon meters use identity or a linear zero-to-one transition.
			if ( map.ReadSpan( curve, 1 )[0] is not (0 or 2) ) continue;
			using var endpoints = map.OpenReader( curve + 4, 8 );
			MeterRange = new Vector2( endpoints.ReadSingle(), endpoints.ReadSingle() );
		}
	}

	// The two noise layers use periodic sawtooth translations in the compiled overlay table.
	static float? ReadPoweredPlasmaAlpha( MapFile map, int pp )
	{
		// The sixth animated plasma parameter controls the final intensity in constant 4.
		if ( map.ReadInt32( pp + 0xc ) < 5 || map.ReadInt32( pp + 0x4c ) < 6 ) return null;
		int parameters = map.VaToOffset( map.ReadInt32( pp + 0x50 ) );
		using var parameter = map.OpenReader( parameters + 10, 2 );
		int range = parameter.ReadUInt16();
		int start = range & 0x1ff;
		if ( range >> 9 != 1 || start >= map.ReadInt32( pp + 0x44 ) ) return null;
		int references = map.VaToOffset( map.ReadInt32( pp + 0x48 ) );
		int index = (ushort)map.ReadInt32( references + 4 * start );
		if ( index >= map.ReadInt32( pp + 0x3c ) ) return null;
		int overlay = map.VaToOffset( map.ReadInt32( pp + 0x40 ) ) + 20 * index;
		if ( map.ResolveStringId( map.ReadInt32( overlay + 4 ) ) != "turned_on" || map.ReadInt32( overlay + 12 ) < 12 ) return null;
		int curve = map.VaToOffset( map.ReadInt32( overlay + 16 ) );
		if ( (map.ReadInt32( curve ) & 0xffff) != 0x101 ) return null;
		return System.BitConverter.Int32BitsToSingle( map.ReadInt32( curve + 8 ) );
	}

	static Vector4 ReadPlasmaScroll( MapFile map, int pp )
	{
		int count = map.ReadInt32( pp + 0x3c );
		int overlays = map.VaToOffset( map.ReadInt32( pp + 0x40 ) );
		int references = map.VaToOffset( map.ReadInt32( pp + 0x48 ) );
		int parameters = map.VaToOffset( map.ReadInt32( pp + 0x50 ) );
		if ( map.ReadInt32( pp + 0x4c ) < 2 ) return default;
		var rates = new Vector4();
		for ( int layer = 0; layer < 2; layer++ )
		{
			using var parameter = map.OpenReader( parameters + layer * 2, 2 );
			int range = parameter.ReadUInt16();
			int start = range & 0x1ff;
			int end = start + (range >> 9);
			if ( end > map.ReadInt32( pp + 0x44 ) ) continue;
			for ( int i = start; i < end; i++ )
			{
				using var reference = map.OpenReader( references + i * 4, 4 );
				int index = reference.ReadInt16();
				int axis = reference.ReadInt16();
				if ( (uint)index >= count || axis is not (4 or 5) ) continue;
				using var overlay = map.OpenReader( overlays + index * 20 + 8, 12 );
				float period = overlay.ReadSingle();
				int length = overlay.ReadInt32();
				if ( period <= 0 || length < 24 ) continue;
				using var function = map.OpenReader( map.VaToOffset( overlay.ReadInt32() ), length );
				if ( function.ReadByte() != 3 ) continue;
				function.BaseStream.Position = 2;
				if ( function.ReadByte() != 6 ) continue;
				function.BaseStream.Position = 4;
				float from = function.ReadSingle(), to = function.ReadSingle();
				function.BaseStream.Position = 20;
				float speed = (to - from) * function.ReadSingle() / period;
				rates[layer * 2 + axis - 4] = speed;
			}
		}
		return rates;
	}
	static int GetChangeColorSlot( string name )
	{
		// Common H2 character shader templates
		if ( name == "tex_bump_two_change_color" ) return 3;
		if ( name.Contains( "one_change_color" ) )
			return 2; // [bump, base, cc]

		if ( name.Contains( "two_change_color" ) || name.Contains( "four_change_color" ) )
		{
			// tex_bump_env variants: [bump, (camo), base, detail, cc, env]
			if ( name.Contains( "indexed" ) )
				return 3; // [bump, base, detail, cc, env]

			return 4; // [bump, (camo), base, detail, cc, env]
		}

		if ( name.Contains( "overlay" ) )
			return 2; // [base, detail, cc]

		return -1;
	}

}
