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
	public static string ThingSpritePath( string wad, ThingType type ) => $"{wad}/things/{type.Name}.sprite";
	public static string ThingPrefabPath( string wad, ThingType type ) => $"{wad}/things/{type.Name}.prefab";
	public static string ThingFramePath( string wad, ThingType type, string lump, bool flipped ) => $"{wad}/things/{type.Name}/{ResourceName( lump )}{(flipped ? "_flip" : "")}";
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

		// Each thing type gets a prefab, registered under the WAD that provides its first sprite frame.
		// Things that aren't drawn (starts, teleport destinations) belong to the IWAD.
		foreach ( var type in ThingTable.Values )
		{
			SpriteFrameInfo[] frames = null;
			if ( type.Sprite is null ? stack.Wads.Count > 1 : !TryGetThingSprite( stack, type, out var owner, out frames ) || owner != wad )
				continue;

			SpriteFrameInfo[] death = null;
			if ( type.Death is { } d && !TryGetSpriteFrames( stack, d.Sprite, d.Frames, out _, out death ) )
				Log.Warning( $"{Title}: {wad.Name} has no {d.Sprite} frames for the {type.Name}'s death" );

			SpriteCanvas? canvas = null;
			if ( frames is not null )
			{
				// Every picture of the thing shares one canvas, so the sprite's size and origin fit all of them.
				var all = death is null ? frames : [.. frames, .. death];
				var shared = SpriteCanvas.Around( all );

				// Standing things rest on the floor; the idle frames decide how far to lift, so a bigger
				// death animation doesn't make them float.
				if ( (type.Flags & ThingTypeFlags.Hanging) == 0 )
					shared = shared.OnFloor( frames );
				canvas = shared;

				// One texture per distinct lump (and mirroring), however many times the animations repeat it.
				var textures = new Dictionary<(string Lump, bool Flipped), string>();
				foreach ( var f in all )
				{
					var key = (f.Patch.Name, f.Flipped);
					if ( textures.ContainsKey( key ) ) continue;

					var framePath = ThingFramePath( wad.Name, type, f.Patch.Name, f.Flipped );
					context.Add( ResourceType.Texture, framePath, new SpriteFrameTextureLoader( palette, f, shared ) );
					textures[key] = Url( framePath, "vtex" );
				}

				var spawnTextures = frames.Select( f => textures[(f.Patch.Name, f.Flipped)] ).ToArray();
				var deathTextures = death?.Select( f => textures[(f.Patch.Name, f.Flipped)] ).ToArray();
				context.Add( ResourceType.GameResource, ThingSpritePath( wad.Name, type ), new SpriteLoader( type, spawnTextures, deathTextures, shared ) );
			}

			context.Add( ResourceType.PrefabFile, ThingPrefabPath( wad.Name, type ),
				new ThingPrefabLoader( stack, type, frames, canvas, frames is null ? null : Url( ThingSpritePath( wad.Name, type ) ) ) );
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

	/// <summary>The WAD a thing type's prefab is registered under, or null if the stack can't draw it.</summary>
	public static WadFile GetThingOwner( WadStack stack, ThingType type )
	{
		if ( type.Sprite is null ) return stack.Wads[^1];
		return TryGetThingSprite( stack, type, out var owner, out _ ) ? owner : null;
	}

	/// <summary>
	/// The sprite frames of a thing's spawn animation, and the WAD that provides its first frame
	/// (which is the WAD its sprite resource is registered under).
	/// </summary>
	public static bool TryGetThingSprite( WadStack stack, ThingType type, out WadFile owner, out SpriteFrameInfo[] frames )
	{
		owner = null;
		frames = null;
		return type.Sprite is not null && TryGetSpriteFrames( stack, type.Sprite, type.Frames, out owner, out frames );
	}

	/// <summary>
	/// Resolves a run of frame letters of a sprite through the stack. Each frame can come from a
	/// different WAD; <paramref name="owner"/> is the one providing the first.
	/// </summary>
	public static bool TryGetSpriteFrames( WadStack stack, string sprite, string letters, out WadFile owner, out SpriteFrameInfo[] frames )
	{
		owner = null;
		frames = null;

		var result = new List<SpriteFrameInfo>();
		foreach ( char letter in letters )
		{
			if ( !stack.TryFindSpriteFrame( sprite, letter, out var frame, out var frameOwner ) )
				return false;

			owner ??= frameOwner;
			result.Add( new SpriteFrameInfo( frame.Patch, SpritePath( frameOwner.Name, frame.Patch.Name ), frame.Patch.Width, frame.Patch.Height,
				frame.Patch.LeftOffset, frame.Patch.TopOffset, frame.Flipped ) );
		}

		frames = [.. result];
		return true;
	}
}

/// <summary>One frame of a thing's sprite: its patch, where the raw lump texture is mounted, and how the picture sits on the thing's origin.</summary>
public readonly record struct SpriteFrameInfo( Patch Patch, string TexturePath, int Width, int Height, int LeftOffset, int TopOffset, bool Flipped );
