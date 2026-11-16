using System;
using Doom.Wad;

namespace Doom;

partial class GameMount
{
	// Every resource lives in its WAD's folder, so a PWAD's replacement never collides with the IWAD's.
	public static string FlatPath( string wad, string name ) => $"{wad}/flats/{ResourceName( name )}";
	public static string TexturePath( string wad, string name ) => $"{wad}/textures/{ResourceName( name )}";
	public static string SkyPath( string wad, string name ) => $"{wad}/skies/{ResourceName( name )}";
	public static string SpritePath( string wad, string lump ) => $"{wad}/sprites/{ResourceName( lump )}";
	public static string ThingPath( string wad, ThingType type ) => $"{wad}/things/{type.Name}.sprite";
	public static string SoundPath( string wad, string name ) => $"{wad}/sounds/{ResourceName( name )}";
	public static string SoundEventPath( string wad, string name ) => $"{SoundPath( wad, name )}.sound";
	public static string MapPath( string wad, string name ) => $"{wad}/maps/{ResourceName( name )}";

	/// <summary>Lump names can hold characters that don't belong in a path (sprites like VILE\0).</summary>
	public static string ResourceName( string name ) => name.ToLowerInvariant().Replace( '\\', '_' );

	/// <summary>The full mount URL for a relative resource path, with the engine's extension for its type.</summary>
	public string Url( string path, string extension = null ) => extension is null ? $"mount://{Ident}/{path}" : $"mount://{Ident}/{path}.{extension}";

	private void MountWad( MountContext context, WadStack stack )
	{
		var wad = stack.Primary;
		var palette = stack.Palette ?? throw new InvalidDataException( $"{wad.Name} has no PLAYPAL and no base WAD to take it from." );

		foreach ( var flat in wad.Flats )
		{
			var cycle = stack.GetAnimation( false, flat.Name );
			var frames = cycle is null
				? [() => flat.Picture]
				: cycle.Frames.Select( name => (Func<Picture>)(() => stack.FindFlat( name, out _ )?.Picture) ).ToArray();

			AddSurface( context, FlatPath( wad.Name, flat.Name ), palette, frames, cycle?.Tics ?? 0 );
		}

		foreach ( var texture in wad.Textures )
		{
			var cycle = stack.GetAnimation( true, texture.Name );
			var frames = cycle is null
				? [() => texture.Compose( stack.FindPatch )]
				: cycle.Frames.Select( name => (Func<Picture>)(() => stack.FindTexture( name, out _ )?.Compose( stack.FindPatch )) ).ToArray();

			var loader = AddSurface( context, TexturePath( wad.Name, texture.Name ), palette, frames, cycle?.Tics ?? 0 );

			if ( texture.Name.StartsWith( "SKY", StringComparison.Ordinal ) )
				context.Add( ResourceType.Material, SkyPath( wad.Name, texture.Name ), new SkyMaterialLoader( loader.Path ) );
		}

		foreach ( var sprite in wad.Sprites )
			context.Add( ResourceType.Texture, SpritePath( wad.Name, sprite.Name ), new PictureTextureLoader( palette, [() => sprite.Picture], array: false ) );

		foreach ( var sound in wad.Sounds )
		{
			var path = SoundPath( wad.Name, sound.Name );
			context.Add( ResourceType.Sound, path, new SoundLoader( sound ) );
			context.Add( ResourceType.GameResource, SoundEventPath( wad.Name, sound.Name ), new SoundEventLoader( Url( path, "vsnd" ) ) );
		}

		foreach ( var type in ThingTable.Values )
		{
			if ( TryGetThingSprite( stack, type, out var owner, out var frames ) && owner == wad )
				context.Add( ResourceType.GameResource, ThingPath( wad.Name, type ), new SpriteLoader( type, frames ) );

			// prefabs?
		}

		foreach ( var map in wad.Maps )
		{
			if ( map.Format != MapFormat.Doom )
			{
				Log.Info( $"{Title}: {wad.Name} {map.Name} is a {map.Format} format map, which isn't supported" );
				continue;
			}

			context.Add( ResourceType.Scene, MapPath( wad.Name, map.Name ), new MapLoader( wad.Name, map ) );
		}
	}

	private PictureTextureLoader AddSurface( MountContext context, string path, Palette palette, Func<Picture>[] frames, int tics )
	{
		var texture = new PictureTextureLoader( palette, frames, array: true );
		context.Add( ResourceType.Texture, path, texture );
		context.Add( ResourceType.Material, path, new SurfaceMaterialLoader( texture.Path, tics ) );
		return texture;
	}

	/// <summary>
	/// The sprite frames of a thing's spawn animation, and the WAD that provides its first frame
	/// (which is the WAD its sprite resource is registered under).
	/// </summary>
	public static bool TryGetThingSprite( WadStack stack, ThingType type, out WadFile owner, out SpriteFrameInfo[] frames )
	{
		owner = null;
		frames = null;
		if ( type.Sprite is null ) return false;

		var result = new List<SpriteFrameInfo>();
		foreach ( char letter in type.Frames )
		{
			if ( !stack.TryFindSpriteFrame( type.Sprite, letter, out var frame, out var frameOwner ) )
				return false;

			owner ??= frameOwner;
			result.Add( new SpriteFrameInfo( SpritePath( frameOwner.Name, frame.Patch.Name ), frame.Patch.Width, frame.Patch.Height,
				frame.Patch.LeftOffset, frame.Patch.TopOffset, frame.Flipped ) );
		}

		frames = [.. result];
		return true;
	}
}

/// <summary>One frame of a thing's sprite: where its texture is mounted, and how the picture sits on the thing's origin.</summary>
public readonly record struct SpriteFrameInfo( string TexturePath, int Width, int Height, int LeftOffset, int TopOffset, bool Flipped );
