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
			var data = map.ReadTagMetadata( rpOff, 80 );
			using var r = new BinaryReader( new MemoryStream( data ) );

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
			var ppData = map.ReadTagMetadata( ppOff, 0x7C );
			using var pp = new BinaryReader( new MemoryStream( ppData ) );

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

				// Bump/normal map is always slot 0 for tex_bump_ templates
				if ( templateName.Contains( "tex_bump" ) && bmpCount > 0 )
				{
					ReadBitmapSlot( map, bmpOff, 0, out int bumpDatum );
					if ( bumpDatum != -1 && bumpDatum != 0 )
						BumpBitmapTagIndex = bumpDatum & 0xFFFF;
				}

				int ccSlot = GetChangeColorSlot( templateName );
				if ( ccSlot >= 0 && ccSlot < bmpCount )
				{
					ReadBitmapSlot( map, bmpOff, ccSlot, out int ccDatum );
					if ( ccDatum != -1 && ccDatum != 0 )
						ChangeColorBitmapTagIndex = ccDatum & 0xFFFF;
				}
			}
		}
	}

	/// <summary>
	/// Returns the bitmap slot index for change_color_map based on the shader template name.
	/// Slot positions from TagTool/Reclaimer shader template mappings.
	/// </summary>
	static int GetChangeColorSlot( string templatePath )
	{
		string name = Path.GetFileNameWithoutExtension( templatePath ) ?? templatePath;

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

	static void ReadBitmapSlot( MapFile map, int bmpOff, int slot, out int datum )
	{
		var data = map.ReadTagMetadata( bmpOff + slot * 0x0C, 0x0C );
		using var r = new BinaryReader( new MemoryStream( data ) );
		datum = r.ReadInt32();
	}
}
