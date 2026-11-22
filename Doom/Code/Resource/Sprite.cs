namespace Doom;

/// <summary>
/// A thing's animations as a sprite resource: its spawn animation, which plays by default, and
/// <see cref="DeathAnimation"/> if it can be killed.
/// </summary>
public sealed class SpriteLoader( ThingType type, SpriteFrameInfo[] frames, SpriteFrameInfo[] death = null ) : ResourceLoader<GameMount>
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

	private Sprite.Animation CreateAnimation( string name, SpriteFrameInfo[] animationFrames, int tics, bool loop ) => new()
	{
		Name = name,
		FrameRate = tics > 0 ? 35f / tics : 0f,
		LoopMode = loop && animationFrames.Length > 1 ? Sprite.LoopMode.Loop : Sprite.LoopMode.None,
		Origin = new Vector2( 0.5f, 1f ),
		Frames = animationFrames.Select( f => new Sprite.Frame { Texture = Texture.Load( Host.Url( f.TexturePath, "vtex" ), false ) } ).ToList(),
	};
}
