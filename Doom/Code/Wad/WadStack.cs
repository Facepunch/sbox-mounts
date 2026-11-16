using System;

namespace Doom.Wad;

/// <summary>The frames an animated surface shows, starting with its own, and how long each is shown.</summary>
public sealed record TextureCycle( string[] Frames, int Tics );

/// <summary>
/// The WADs a level or resource is resolved against, highest priority first: a PWAD, then the IWAD
/// it was made for. Doom itself resolves every name to the last loaded lump, so a PWAD's lumps
/// override the IWAD's. This mirrors that without merging the WADs.
/// </summary>
public sealed class WadStack
{
	private Dictionary<(bool IsTexture, string Name), TextureCycle> _animations;

	public WadStack( IReadOnlyList<WadFile> wads, IReadOnlyList<TextureAnimation> defaultAnimations )
	{
		if ( wads.Count == 0 ) throw new ArgumentException( "A WAD stack needs at least one WAD.", nameof( wads ) );

		Wads = wads;
		DefaultAnimations = defaultAnimations;
	}

	/// <summary>Highest priority first.</summary>
	public IReadOnlyList<WadFile> Wads { get; }

	/// <summary>The WAD this stack belongs to.</summary>
	public WadFile Primary => Wads[0];

	/// <summary>Used when no WAD in the stack has an ANIMATED lump.</summary>
	public IReadOnlyList<TextureAnimation> DefaultAnimations { get; }

	/// <summary>PLAYPAL palette 0 from the highest priority WAD that has one.</summary>
	public Palette Palette => Wads.Select( w => w.Palettes.FirstOrDefault() ).FirstOrDefault( p => p is not null );

	public Flat FindFlat( string name, out WadFile owner ) => Find( w => w.Flats.Get( name ), out owner );

	public WallTexture FindTexture( string name, out WadFile owner ) => Find( w => w.Textures.Get( name ), out owner );

	public DmxSound FindSound( string name, out WadFile owner ) => Find( w => w.Sounds.Get( name ), out owner );

	/// <summary>
	/// A patch for a wall texture. Doom looks patch names up with W_CheckNumForName, so a lump outside
	/// the P_START/P_END markers counts too.
	/// </summary>
	public Patch FindPatch( string name )
	{
		if ( name is null ) return null;

		foreach ( var wad in Wads )
		{
			if ( wad.Patches.Get( name ) is { } patch ) return patch;
			if ( wad.FindLump( name ) is { Kind: LumpKind.Unknown or LumpKind.Sprite, Size: > 8 } lump ) return new Patch( lump );
		}

		return null;
	}

	/// <summary>Finds the lump drawing a sprite frame, preferring the all-angles rotation, then the front.</summary>
	public bool TryFindSpriteFrame( string sprite, char frame, out SpriteFrame result, out WadFile owner )
	{
		foreach ( var wad in Wads )
		{
			if ( wad.TryGetSpriteFrame( sprite, frame, 0, out result ) || wad.TryGetSpriteFrame( sprite, frame, 1, out result ) )
			{
				owner = wad;
				return true;
			}
		}

		result = default;
		owner = null;
		return false;
	}

	/// <summary>
	/// The frames a flat or wall texture cycles through, starting at itself, or null if it doesn't
	/// animate. Doom advances every animated surface on one shared clock, so a surface using the
	/// second frame of an animation is always one frame ahead of one using the first.
	/// </summary>
	public TextureCycle GetAnimation( bool isTexture, string name )
	{
		if ( _animations is null )
		{
			var animations = new Dictionary<(bool, string), TextureCycle>();
			var definitions = Wads.Select( TextureAnimations.ReadAnimated ).FirstOrDefault( a => a is not null ) ?? DefaultAnimations;

			foreach ( var definition in definitions )
			{
				var frames = ResolveRange( definition );
				if ( frames is null ) continue;

				for ( int i = 0; i < frames.Length; i++ )
					animations[(definition.IsTexture, frames[i])] = new TextureCycle( [.. frames[i..], .. frames[..i]], definition.Tics );
			}

			_animations = animations;
		}

		return _animations.GetValueOrDefault( (isTexture, name) );
	}

	/// <summary>
	/// Vanilla defines an animation by its first and last names; the frames are whatever lies between
	/// them in lump order (flats) or texture number order (wall textures).
	/// </summary>
	private string[] ResolveRange( TextureAnimation animation )
	{
		foreach ( var wad in Wads )
		{
			int first, last;
			Func<int, string> nameAt;

			if ( animation.IsTexture )
			{
				first = wad.Textures.IndexOf( animation.First );
				last = wad.Textures.IndexOf( animation.Last );
				nameAt = i => wad.Textures[i].Name;
			}
			else
			{
				first = wad.Flats.IndexOf( animation.First );
				last = wad.Flats.IndexOf( animation.Last );
				nameAt = i => wad.Flats[i].Name;
			}

			if ( first < 0 || last < 0 ) continue;
			if ( last <= first ) return null; // vanilla errors with "bad cycle"

			return Enumerable.Range( first, last - first + 1 ).Select( nameAt ).ToArray();
		}

		return null;
	}

	private T Find<T>( Func<WadFile, T> lookup, out WadFile owner ) where T : class
	{
		foreach ( var wad in Wads )
		{
			if ( lookup( wad ) is { } found )
			{
				owner = wad;
				return found;
			}
		}

		owner = null;
		return null;
	}
}
