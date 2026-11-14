using System;
using System.Text;

namespace Source1;

public abstract partial class BaseMount
{
	private readonly record struct HudFonts( VmtNode Scheme, Dictionary<string, string> Files );

	/// <summary>Registers <c>&lt;script&gt;.icon.vtex</c> for the script's TextureData "weapon" entry, or returns null when its font or material is not in this mount.</summary>
	private string MountWeaponIcon( MountContext context, FileIndex files, string script, SourceHudTexture icon, Lazy<HudFonts> fonts )
	{
		if ( icon is null )
			return null;
		string fontFile = null;
		if ( icon.File is null )
		{
			var family = SourceClientScheme.FontFamily( fonts.Value.Scheme, icon.Font );
			if ( family is null || !fonts.Value.Files.TryGetValue( family, out fontFile ) )
				return null;
		}
		else if ( icon.Width <= 0 || icon.Height <= 0 || !files.Files.ContainsKey( NormalizeMaterialPath( files, icon.File ) ) )
		{
			return null;
		}
		var path = $"{script}.icon.vtex";
		context.Add( ResourceType.Texture, path, new SourceWeaponIconLoader( icon, fontFile ) );
		return $"mount://{Ident}/{path}";
	}

	// Source loads scheme fonts by family name, so a family resolves to whichever resource font declares it.
	private HudFonts ReadHudFonts( FileIndex files )
	{
		VmtNode scheme = null;
		const string schemePath = "resource/clientscheme.res";
		if ( files.Files.TryGetValue( schemePath, out var schemeFile ) )
		{
			try
			{
				using var stream = schemeFile.Open();
				using var reader = new StreamReader( stream, Encoding.UTF8 );
				scheme = SourceClientScheme.Parse( reader.ReadToEnd(), schemePath );
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
			{
				Log.Trace( $"{Title}: {schemePath} was not read: {e.Message}" );
			}
		}
		var fonts = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var entry in files.Files.Where( x => x.Key.StartsWith( "resource/", StringComparison.OrdinalIgnoreCase )
			&& (x.Key.EndsWith( ".ttf", StringComparison.OrdinalIgnoreCase ) || x.Key.EndsWith( ".otf", StringComparison.OrdinalIgnoreCase )) ) )
		{
			try
			{
				using var stream = entry.Value.Open();
				var bytes = new byte[checked((int)stream.Length)];
				stream.ReadExactly( bytes );
				if ( SourceHudGlyph.FamilyName( bytes ) is { } family )
					fonts.TryAdd( family, entry.Key );
			}
			catch ( IOException e )
			{
				Log.Trace( $"{Title}: font '{entry.Key}' was not read: {e.Message}" );
			}
		}
		return new( scheme, fonts );
	}
}
