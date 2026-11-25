using System;

namespace NS2;

public partial class MaterialLoader
{
	sealed class ParticleSpriteLoader : ResourceLoader<GameMount>
	{
		protected override object Load()
		{
			var material = (MaterialLoader)Parent;
			var properties = Parse( Host.ReadAllBytes( material.FilePath ) );
			var source = Find( properties, "emissiveMap" ).String ?? Find( properties, "albedoMap" ).String;
			if ( !GameMount.TryResolve( Host.FileSystem, source, out var path ) ) return null;

			var count = (int)Find( properties, "numFrames" ).Number;
			if ( count <= 1 ) return Sprite.FromTexture( Texture.Load( $"mount://{Host.Ident}/{path}.vtex", false ) );

			// Spark stores the animation in a vertical BC3 atlas.
			var bytes = Host.ReadAllBytes( path );
			if ( bytes.Length < 128 || !bytes.AsSpan( 0, 4 ).SequenceEqual( "DDS "u8 )
				|| !bytes.AsSpan( 84, 4 ).SequenceEqual( "DXT5"u8 ) ) return null;

			var width = BitConverter.ToInt32( bytes, 16 );
			var height = BitConverter.ToInt32( bytes, 12 );
			if ( width <= 0 || height <= 0 || count > 1024 || height % count != 0 ) return null;
			var levels = new List<(int Offset, int Size)>();
			int offset = 128;
			int mipCount = Math.Max( 1, BitConverter.ToInt32( bytes, 28 ) );
			for ( int w = width, h = height; levels.Count < mipCount && h / count >= 4; w = Math.Max( 1, w / 2 ), h /= 2 )
			{
				int size = checked(((w + 3) / 4) * ((h + 3) / 4) * 16);
				if ( size > bytes.Length - offset ) return null;
				levels.Add( (offset, size) );
				offset += size;
			}
			if ( levels.Count == 0 ) return null;

			var data = new byte[offset - 128];
			int dest = data.Length;
			foreach ( var level in levels )
			{
				dest -= level.Size;
				bytes.AsSpan( level.Offset, level.Size ).CopyTo( data.AsSpan( dest ) );
			}

			var frames = new Rect[count];
			for ( int i = 0; i < count; i++ )
				frames[i] = new Rect( 0, (float)i / count, 1, 1f / count );

			float frameRate = Find( properties, "frameRate" ).Number;
			var texture = Texture.Create( width, height, ImageFormat.DXT5 ).WithData( data )
				.WithMips( levels.Count ).WithStaticUsage().AddSequence( frames, frameRate > 0 ? frameRate : 1 ).Finish();
			var sprite = Sprite.FromTexture( texture );
			sprite.Animations[0].FrameRate = frameRate;
			return sprite;
		}
	}
}
