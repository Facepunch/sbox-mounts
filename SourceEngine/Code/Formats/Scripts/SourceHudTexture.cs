using System;

namespace Source1;

/// <summary>
/// A weapon script <c>TextureData</c> entry: a character of a scheme font, or a pixel region of a material's texture.
/// </summary>
internal sealed record SourceHudTexture( string Font, string Character, string File, int X, int Y, int Width, int Height )
{
	internal static SourceHudTexture Read( VmtNode weaponData, string name )
	{
		if ( weaponData?.GetBlock( "TextureData" )?.GetBlock( name ) is not { } entry )
			return null;
		var p = new VmtParameters( entry.Values, name );
		var file = p.String( "file" );
		if ( !string.IsNullOrWhiteSpace( file ) )
			return new( null, null, file, p.Integer( "x" ), p.Integer( "y" ), p.Integer( "width" ), p.Integer( "height" ) );
		var font = p.String( "font" );
		var character = p.String( "character" );
		return string.IsNullOrWhiteSpace( font ) || string.IsNullOrEmpty( character ) ? null : new( font, character, null, 0, 0, 0, 0 );
	}
}

/// <summary>Reads font definitions from a VGUI scheme such as <c>resource/clientscheme.res</c>.</summary>
internal static class SourceClientScheme
{
	internal static VmtNode Parse( string text, string path ) =>
		VmtFile.ParseKeyValues( text, path, diagnostic => Log.Trace( $"{diagnostic.Path}:{diagnostic.Line}: {diagnostic.Message}" ) )
			.FirstOrDefault( x => x.IsBlock && x.Name.Equals( "Scheme", StringComparison.OrdinalIgnoreCase ) );

	/// <summary>The family of a logical font: the first of its numbered definitions that names one.</summary>
	internal static string FontFamily( VmtNode scheme, string font ) => scheme?.GetBlock( "Fonts" )?.GetBlock( font )?.Children
		.Where( x => x.IsBlock )
		.Select( x => x.Values.GetValueOrDefault( "name" ) )
		.FirstOrDefault( x => !string.IsNullOrWhiteSpace( x ) );
}
