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

	public bool IsEmblem { get; private set; }
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
				if ( name == "emblem_overlay_simple" )
				{
					IsEmblem = true;
					DiffuseBitmapTagIndex = Bitmap( 0 );
				}
				if ( name.StartsWith( "tex_bump" ) ) BumpBitmapTagIndex = Bitmap( 0 );
				ChangeColorBitmapTagIndex = Bitmap( GetChangeColorSlot( name ) );

				// Compiled MCC registers differ between these opaque template families.
				bool fourColors = name == "tex_bump_env_four_change_color";
				bool twoColors = name == "tex_bump_env_two_change_color";
				if ( name == "tex_bump_env" || fourColors || twoColors )
				{
					EnvironmentUsesUnsignedNormalZ = fourColors;
					DetailBitmapTagIndex = Bitmap( 3 );
					EnvironmentBitmapTagIndex = Bitmap( fourColors || twoColors ? 5 : 4 );
					int colorCount = pp.ReadInt32();
					int colors = map.VaToOffset( pp.ReadInt32() );
					int transformCount = pp.ReadInt32();
					int transforms = map.VaToOffset( pp.ReadInt32() );
					int uv = fourColors || twoColors ? 2 : 0;
					if ( uv < transformCount )
					{
						using var transform = map.OpenReader( transforms + uv * 16, 16 );
						DetailTransform = new( transform.ReadSingle(), transform.ReadSingle(), transform.ReadSingle(), transform.ReadSingle() );
					}

					int tint = fourColors || twoColors ? 4 : 1;
					int brightness = fourColors || twoColors ? 6 : 3;
					Vector4 Reflection( int color, int intensity )
					{
						if ( color >= colorCount || intensity >= colorCount ) return default;
						uint rgb = (uint)map.ReadInt32( colors + color * 4 );
						uint power = (uint)map.ReadInt32( colors + intensity * 4 );
						return new Vector4( (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255, power >> 24 ) / 255f;
					}
					EnvironmentFacing = Reflection( tint, brightness );
					EnvironmentGlancing = Reflection( tint + 1, brightness + 1 );
				}
			}
		}
	}

	/// <summary>
	/// Returns the bitmap slot index for change_color_map based on the shader template name.
	/// Slot positions from TagTool/Reclaimer shader template mappings.
	/// </summary>
	static int GetChangeColorSlot( string name )
	{
		// Common H2 character shader templates
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
