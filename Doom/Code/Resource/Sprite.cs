using System;
using Doom.Wad;

namespace Doom;

/// <summary>
/// The shared frame every picture of a thing is drawn into. Doom places each sprite frame by its own
/// patch offsets, so frames of one animation differ in size and sit at different spots around the
/// thing's origin. A sprite renderer has one size and origin for every frame, so each frame is padded
/// into a canvas big enough for all of them, with the thing's origin at the same pixel in each.
/// Coordinates are pixels relative to the origin, x right and y down.
/// </summary>
public readonly record struct SpriteCanvas( int MinX, int MinY, int Width, int Height, int Lift = 0 )
{
	/// <summary>
	/// Where the thing's origin sits, as a fraction of the canvas (0,0 top left). <see cref="Lift"/>
	/// moves the origin that many pixels down the picture, which raises the sprite by as much.
	/// </summary>
	public Vector2 Origin => new( -MinX / (float)Width, (-MinY + Lift) / (float)Height );

	/// <summary>
	/// Raises the sprite so none of these frames reach below the origin. Many lumps hang a few pixels
	/// below it: vanilla never clipped sprites against the floor under them, but a depth buffer does,
	/// so source ports lift them onto the floor. Sprites drawn wholly above their origin (floating
	/// things) are left where they are.
	/// </summary>
	public SpriteCanvas OnFloor( IEnumerable<SpriteFrameInfo> frames )
		=> this with { Lift = Math.Max( 0, frames.Max( f => f.Height - f.TopOffset ) ) };

	public static SpriteCanvas Around( IEnumerable<SpriteFrameInfo> frames )
	{
		int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

		foreach ( var f in frames )
		{
			// A mirrored frame is flipped around the origin, so its left offset becomes its right.
			int left = f.Flipped ? f.LeftOffset - f.Width : -f.LeftOffset;
			minX = Math.Min( minX, left );
			maxX = Math.Max( maxX, left + f.Width );
			minY = Math.Min( minY, -f.TopOffset );
			maxY = Math.Max( maxY, f.Height - f.TopOffset );
		}

		return new SpriteCanvas( minX, minY, Math.Max( 1, maxX - minX ), Math.Max( 1, maxY - minY ) );
	}
}

/// <summary>One sprite frame drawn into its thing's <see cref="SpriteCanvas"/>, mirrored if the lump is used flipped.</summary>
public sealed class SpriteFrameTextureLoader( Palette palette, SpriteFrameInfo frame, SpriteCanvas canvas ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var picture = frame.Patch.Picture;
		var source = new byte[picture.Width * picture.Height * 4];
		picture.WriteRgba( palette, source );

		var data = new byte[canvas.Width * canvas.Height * 4];
		int left = frame.Flipped ? frame.LeftOffset - frame.Width : -frame.LeftOffset;
		int x0 = left - canvas.MinX;
		int y0 = -frame.TopOffset - canvas.MinY;

		for ( int y = 0; y < picture.Height; y++ )
		{
			for ( int x = 0; x < picture.Width; x++ )
			{
				int sx = frame.Flipped ? picture.Width - 1 - x : x;
				int from = (y * picture.Width + sx) * 4;
				int to = ((y0 + y) * canvas.Width + x0 + x) * 4;
				source.AsSpan( from, 4 ).CopyTo( data.AsSpan( to, 4 ) );
			}
		}

		return Texture.Create( canvas.Width, canvas.Height, ImageFormat.RGBA8888 )
			.WithName( Path )
			.WithData( data )
			.WithStaticUsage()
			.Finish();
	}
}

/// <summary>
/// A thing's animations as a sprite resource: its spawn animation, which plays by default, and
/// <see cref="DeathAnimation"/> if it can be killed. Every frame is a canvas texture, so one origin fits them all.
/// </summary>
public sealed class SpriteLoader( ThingType type, string[] frames, string[] death, SpriteCanvas canvas ) : ResourceLoader<GameMount>
{
	public const string DeathAnimation = "death";

	protected override object Load()
	{
		var sprite = new Sprite();
		sprite.Animations.Clear();
		sprite.Animations.Add( CreateAnimation( type.Name, frames, type.Tics, loop: true ) );

		if ( death is { Length: > 0 } && type.Death is { } d )
			sprite.Animations.Add( CreateAnimation( DeathAnimation, death, d.Tics, loop: false ) );

		return sprite;
	}

	private Sprite.Animation CreateAnimation( string name, string[] textures, int tics, bool loop ) => new()
	{
		Name = name,
		FrameRate = tics > 0 ? 35f / tics : 0f,
		LoopMode = loop && textures.Length > 1 ? Sprite.LoopMode.Loop : Sprite.LoopMode.None,
		Origin = canvas.Origin,
		Frames = textures.Select( t => new Sprite.Frame { Texture = Texture.Load( t, false ) } ).ToList(),
	};
}
