using System;

namespace Doom.Wad;

/// <summary>Where one patch is placed inside a wall texture (its top-left corner).</summary>
public readonly record struct TexturePatch( short OriginX, short OriginY, string PatchName );

/// <summary>
/// A wall texture from TEXTURE1/TEXTURE2: a size and a list of patches stamped onto it in order,
/// later ones on top. It isn't stored as pixels; <see cref="Compose"/> builds them.
/// </summary>
public sealed class WallTexture
{
	internal WallTexture( string name, int width, int height, IReadOnlyList<TexturePatch> patches )
	{
		Name = name;
		Width = width;
		Height = height;
		Patches = patches;
	}

	public string Name { get; }

	public int Width { get; }

	public int Height { get; }

	public IReadOnlyList<TexturePatch> Patches { get; }

	public override string ToString() => $"{Name} ({Width}x{Height})";

	/// <summary>
	/// Builds the texture the way vanilla's r_data.c does. A column covered by a single patch is
	/// drawn straight from that patch, so the patch's Y origin is ignored. A column covered by
	/// several is composited, and a post that starts above the top is clipped by its length
	/// only, not its source, so it slides down (the classic composite bug some textures show).
	/// Anything no patch covers stays transparent.
	/// </summary>
	/// <param name="resolvePatch">Finds a patch by name, the way W_CheckNumForName would. Null if missing.</param>
	public Picture Compose( Func<string, Patch> resolvePatch )
	{
		var pixels = new byte[Width * Height];
		var opaque = new bool[Width * Height];

		var patches = new Patch[Patches.Count];
		var coverage = new int[Width];

		for ( int i = 0; i < patches.Length; i++ )
		{
			var patch = patches[i] = resolvePatch( Patches[i].PatchName );
			if ( patch is null )
			{
				Log.Warning( $"Doom: texture {Name} uses missing patch {Patches[i].PatchName}" );
				continue;
			}

			int x1 = Math.Max( 0, (int)Patches[i].OriginX );
			int x2 = Math.Min( Width, Patches[i].OriginX + patch.Width );
			for ( int x = x1; x < x2; x++ )
				coverage[x]++;
		}

		for ( int i = 0; i < patches.Length; i++ )
		{
			var patch = patches[i];
			if ( patch is null ) continue;

			var place = Patches[i];
			int x1 = Math.Max( 0, (int)place.OriginX );
			int x2 = Math.Min( Width, place.OriginX + patch.Width );

			for ( int x = x1; x < x2; x++ )
			{
				bool single = coverage[x] == 1;

				foreach ( var post in patch.Column( x - place.OriginX ) )
				{
					var source = patch.PostPixels( post );
					int position = single ? post.Top : place.OriginY + post.Top;
					int count = source.Length;

					// Clipped by length only: the source pointer isn't advanced (vanilla behaviour).
					if ( position < 0 )
					{
						count += position;
						position = 0;
					}

					count = Math.Min( count, Height - position );

					for ( int k = 0; k < count; k++ )
					{
						int at = (position + k) * Width + x;
						pixels[at] = source[k];
						opaque[at] = true;
					}
				}
			}
		}

		return new Picture( Width, Height, pixels, opaque );
	}
}
