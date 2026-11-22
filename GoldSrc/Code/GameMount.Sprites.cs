using System;
using Sandbox.Mounting;
using GoldSrc.Bsp;

namespace GoldSrc;

partial class GameMount
{
	private readonly Dictionary<string, SpriteFile> _sprites = new( StringComparer.OrdinalIgnoreCase );

	internal SpriteFile GetSprite( string model )
	{
		var path = FindFile( model );
		return path is not null && _sprites.TryGetValue( path, out var sprite ) ? sprite : null;
	}

	private void MountSprite( MountContext context, string path )
	{
		try
		{
			using var stream = FileSystem.OpenRead( path );
			var sprite = new SpriteFile( stream );
			_sprites[path] = sprite;
			for ( var frame = 0; frame < sprite.Frames.Count; frame++ )
				context.Add( ResourceType.Texture, $"{path}/frame{frame}", new SpriteTextureLoader( sprite, sprite.Frames[frame] ) );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Cannot mount sprite '{path}': {e.Message}" );
		}
	}
}

internal sealed class SpriteTextureLoader( SpriteFile sprite, SpriteFile.Frame frame ) : ResourceLoader<GameMount>
{
	protected override object Load() => Texture.Create( frame.Width, frame.Height, ImageFormat.RGBA8888_LINEAR )
		.WithName( Path ).WithData( sprite.DecodePixels( frame ) ).WithMips().WithStaticUsage().Finish();
}
