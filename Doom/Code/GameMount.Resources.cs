using Doom.Wad;
using System;

namespace Doom;

partial class GameMount
{
	// Level resources live in their WAD's folder, so a PWAD's replacement never collides with the IWAD's.
	public static string FlatPath( string wad, string name ) => $"{wad}/flats/{ResourceName( name )}";
	public static string TexturePath( string wad, string name ) => $"{wad}/textures/{ResourceName( name )}";
	public static string SkyPath( string wad, string name ) => $"{wad}/skies/{ResourceName( name )}";
	public static string SpritePath( string wad, string lump ) => $"{wad}/sprites/{ResourceName( lump )}";

	// Things exist once for the whole mount, drawn from the mount-wide stack (ThingStack).
	public static string ThingSpritePath( ThingType type ) => $"things/{type.Name}.sprite";
	public static string ThingPrefabPath( ThingType type ) => $"things/{type.Name}.prefab";
	public static string ThingFramePath( ThingType type, string lump, bool flipped ) => $"things/{type.Name}/{ResourceName( lump )}{(flipped ? "_flip" : "")}";
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

	/// <summary>
	/// Every thing type, once for the whole mount: each drawn from whichever WAD in the mount-wide
	/// stack provides its first sprite frame, so a WAD that redraws a monster redraws it everywhere.
	/// Things that aren't drawn (starts, teleport destinations) get a prefab too. Also the missiles
	/// monsters fire and the puffs and blood shots leave, which are drawn around their origin.
	/// </summary>
	private void MountThings( MountContext context, WadStack stack )
	{
		var palette = stack.Palette ?? throw new InvalidDataException( "No mounted WAD has a PLAYPAL to draw things with." );

		foreach ( var type in ThingTable.Values )
		{
			SpriteFrameInfo[] frames = null;
			if ( type.Sprite is not null && !TryGetThingSprite( stack, type, out _, out frames ) )
				continue;

			if ( MonsterTable.TryGetValue( type.Id, out var monster ) && TryAddMonster( context, stack, palette, type, monster ) )
				continue;

			var death = DeathFrames( stack, type );

			// Standing things rest on the floor.
			SpriteCanvas? canvas = frames is null ? null
				: AddThingSprite( context, palette, type, frames, death, onFloor: (type.Flags & ThingTypeFlags.Hanging) == 0 );

			AddThingPrefab( context, type, new ThingPrefabLoader( stack, type, frames, canvas, frames is null ? null : Url( ThingSpritePath( type ) ) ) );
		}

		foreach ( var projectile in ProjectileTable.Values )
		{
			var type = projectile.AsThing;
			if ( !TryGetThingSprite( stack, type, out _, out var frames ) ) continue;

			var canvas = AddThingSprite( context, palette, type, frames, DeathFrames( stack, type ), onFloor: false );
			AddThingPrefab( context, type, new ProjectilePrefabLoader( stack, projectile, canvas, Url( ThingSpritePath( type ) ) ) );
		}

		foreach ( var type in HitEffects.All )
		{
			if ( !TryGetThingSprite( stack, type, out _, out var frames ) ) continue;

			var canvas = AddThingSprite( context, palette, type, frames, null, onFloor: false );
			AddThingPrefab( context, type, new EffectPrefabLoader( type, canvas, Url( ThingSpritePath( type ) ) ) );
		}

		// The Icon of Sin's spawn cube.
		if ( TryGetThingSprite( stack, BrainCube.Type, out _, out var cubeFrames ) )
		{
			var canvas = AddThingSprite( context, palette, BrainCube.Type, cubeFrames, null, onFloor: false );
			AddThingPrefab( context, BrainCube.Type, new BrainCubePrefabLoader( stack, canvas, Url( ThingSpritePath( BrainCube.Type ) ) ) );
		}
	}

	private readonly HashSet<string> _thingPrefabs = new( StringComparer.OrdinalIgnoreCase );

	private ResourceLoader AddThingPrefab( MountContext context, ThingType type, ResourceLoader<GameMount> loader )
	{
		context.Add( ResourceType.PrefabFile, ThingPrefabPath( type ), loader );
		_thingPrefabs.Add( type.Name );
		return loader;
	}

	/// <summary>The mount URL of a thing's prefab, or null if the mounted WADs can't draw it.</summary>
	public string ThingPrefabUrl( ThingType type )
		=> _thingPrefabs.Contains( type.Name ) ? Url( ThingPrefabPath( type ) ) : null;

	/// <summary>A thing's death frames, if it has a death animation the stack can draw.</summary>
	private SpriteFrameInfo[] DeathFrames( WadStack stack, ThingType type )
	{
		if ( type.Death is not { } d ) return null;
		if ( TryGetSpriteFrames( stack, d.Sprite, d.Frames, out _, out var death ) ) return death;

		Log.Warning( $"{Title}: no mounted WAD has {d.Sprite} frames for the {type.Name}'s death" );
		return null;
	}

	private PictureTextureLoader AddSurface( MountContext context, string path, Palette palette, Func<Picture>[] frames, int tics )
	{
		var texture = new PictureTextureLoader( palette, frames, array: true );
		context.Add( ResourceType.Texture, path, texture );
		context.Add( ResourceType.Material, path, new SurfaceMaterialLoader( texture.Path, tics ) );
		return texture;
	}

	/// <summary>
	/// Registers a thing's frame textures and its sprite (spawn animation, and death if it has one),
	/// all drawn into one canvas so the sprite's size and origin fit every frame. Things standing on the
	/// floor are lifted onto it by their spawn frames, so a bigger death animation doesn't make them float.
	/// </summary>
	private SpriteCanvas AddThingSprite( MountContext context, Palette palette, ThingType type, SpriteFrameInfo[] frames, SpriteFrameInfo[] death, bool onFloor )
	{
		var all = death is null ? frames : [.. frames, .. death];
		var canvas = SpriteCanvas.Around( all );
		if ( onFloor )
			canvas = canvas.OnFloor( frames );

		// One texture per distinct lump (and mirroring), however many times the animations repeat it.
		var textures = new Dictionary<(string Lump, bool Flipped), string>();
		foreach ( var f in all )
		{
			var key = (f.Patch.Name, f.Flipped);
			if ( textures.ContainsKey( key ) ) continue;

			var framePath = ThingFramePath( type, f.Patch.Name, f.Flipped );
			context.Add( ResourceType.Texture, framePath, new SpriteFrameTextureLoader( palette, f, canvas ) );
			textures[key] = Url( framePath, "vtex" );
		}

		var spawnTextures = frames.Select( f => textures[(f.Patch.Name, f.Flipped)] ).ToArray();
		var deathTextures = death?.Select( f => textures[(f.Patch.Name, f.Flipped)] ).ToArray();
		context.Add( ResourceType.GameResource, ThingSpritePath( type ), new SpriteLoader( type, spawnTextures, deathTextures, canvas ) );

		return canvas;
	}

	/// <summary>The sprite frames of a thing's spawn animation, and the WAD that provides its first frame.</summary>
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
		=> TryGetSpriteFrames( stack, sprite, letters, 0, out owner, out frames );

	/// <summary>
	/// Resolves a run of frame letters as seen from one rotation (1 front to 8), or the front or
	/// all-angles lumps for rotation 0. A frame drawn for all angles is used for every rotation.
	/// </summary>
	public static bool TryGetSpriteFrames( WadStack stack, string sprite, string letters, int rotation, out WadFile owner, out SpriteFrameInfo[] frames )
	{
		owner = null;
		frames = null;

		var result = new List<SpriteFrameInfo>();
		foreach ( char letter in letters )
		{
			SpriteFrame frame;
			WadFile frameOwner;
			bool found = rotation == 0
				? stack.TryFindSpriteFrame( sprite, letter, out frame, out frameOwner )
				: stack.TryFindSpriteFrame( sprite, letter, rotation, out frame, out frameOwner );

			if ( !found )
				return false;

			owner ??= frameOwner;
			result.Add( new SpriteFrameInfo( frame.Patch, SpritePath( frameOwner.Name, frame.Patch.Name ), frame.Patch.Width, frame.Patch.Height,
				frame.Patch.LeftOffset, frame.Patch.TopOffset, frame.Flipped ) );
		}

		frames = [.. result];
		return true;
	}

	/// <summary>
	/// A monster's sprite and prefab: every animation it has, once per rotation, all drawn into one
	/// canvas. False if the stack is missing any of its frames, leaving it to be mounted as a plain thing.
	/// </summary>
	private bool TryAddMonster( MountContext context, WadStack stack, Palette palette, ThingType type, MonsterInfo monster )
	{
		var states = new List<(string Name, ThingAnimation Animation, bool Loop)>
		{
			(MonsterAnimations.Spawn, new ThingAnimation( type.Sprite, type.Frames, type.Tics ), true)
		};
		states.AddRange( monster.Animations.Select( a => (a.Name, a.Animation, a.Name == MonsterAnimations.See) ) );

		var rotated = new Dictionary<(string State, int Rotation), SpriteFrameInfo[]>();
		foreach ( var (name, animation, _) in states )
		{
			for ( int rotation = 1; rotation <= 8; rotation++ )
			{
				if ( !TryGetSpriteFrames( stack, animation.Sprite, animation.Frames, rotation, out _, out var frames ) )
				{
					Log.Warning( $"{Title}: no mounted WAD has {animation.Sprite} {name} frames for the {type.Name}" );
					return false;
				}

				rotated[(name, rotation)] = frames;
			}
		}

		// Stand on the floor by the frames it stands and walks in; dying ones can sit lower.
		var canvas = SpriteCanvas.Around( rotated.Values.SelectMany( f => f ) )
			.OnFloor( rotated.Where( r => r.Key.State is MonsterAnimations.Spawn or MonsterAnimations.See ).SelectMany( r => r.Value ) );

		var textures = new Dictionary<(string Lump, bool Flipped), string>();
		var animations = new List<SpriteAnimationDefinition>();

		foreach ( var (name, animation, loop) in states )
		{
			for ( int rotation = 1; rotation <= 8; rotation++ )
			{
				var urls = new string[animation.Frames.Length];
				var frames = rotated[(name, rotation)];

				for ( int i = 0; i < frames.Length; i++ )
				{
					var f = frames[i];
					var key = (f.Patch.Name, f.Flipped);
					if ( !textures.TryGetValue( key, out var url ) )
					{
						var framePath = ThingFramePath( type, f.Patch.Name, f.Flipped );
						context.Add( ResourceType.Texture, framePath, new SpriteFrameTextureLoader( palette, f, canvas ) );
						textures[key] = url = Url( framePath, "vtex" );
					}

					urls[i] = url;
				}

				animations.Add( new SpriteAnimationDefinition( MonsterAnimations.Name( name, rotation ), urls, animation.Tics, loop ) );
			}
		}

		context.Add( ResourceType.GameResource, ThingSpritePath( type ), new AnimatedSpriteLoader( animations, canvas ) );

		var front = rotated[(MonsterAnimations.Spawn, 1)];
		var loader = AddThingPrefab( context, type, new ThingPrefabLoader( stack, type, front, canvas, Url( ThingSpritePath( type ) ), monster ) );

		switch ( type.Category )
		{
			case ThingCategory.Monster:
				loader.Tags.Add( "npc" );
				break;
			default:
				break;
		}

		return true;
	}
}

/// <summary>One frame of a thing's sprite: its patch, where the raw lump texture is mounted, and how the picture sits on the thing's origin.</summary>
public readonly record struct SpriteFrameInfo( Patch Patch, string TexturePath, int Width, int Height, int LeftOffset, int TopOffset, bool Flipped );
