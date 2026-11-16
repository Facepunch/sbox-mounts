namespace Doom;

/// <summary>
/// A thing's spawn animation as a sprite resource. The origin comes from the first frame's patch
/// offsets, which is where Doom puts the thing's position: centred, standing on the floor.
/// </summary>
public sealed class SpriteLoader( ThingType type, SpriteFrameInfo[] frames ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var first = frames[0];

		var sprite = new Sprite();
		sprite.Animations.Clear();
		sprite.Animations.Add( new Sprite.Animation
		{
			Name = type.Name,
			FrameRate = type.Tics > 0 ? 35f / type.Tics : 0f,
			LoopMode = frames.Length > 1 ? Sprite.LoopMode.Loop : Sprite.LoopMode.None,
			//Origin = new Vector2( first.LeftOffset / (float)first.Width, first.TopOffset / (float)first.Height ),
			Origin = new Vector2( 0.5f, 1f ),
			Frames = frames.Select( f => new Sprite.Frame { Texture = Texture.Load( Host.Url( f.TexturePath, "vtex" ), false ) } ).ToList(),
		} );

		return sprite;
	}
}
