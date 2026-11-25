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

			// Spark stores animation frames vertically in a single BC3 sheet. Split its
			// block rows directly, including every mip whose frames are independently encoded.
			var bytes = Host.ReadAllBytes( path );
			if ( bytes.Length < 128 || !bytes.AsSpan( 0, 4 ).SequenceEqual( "DDS "u8 )
				|| !bytes.AsSpan( 84, 4 ).SequenceEqual( "DXT5"u8 ) ) return null;

			var width = BitConverter.ToInt32( bytes, 16 );
			var height = BitConverter.ToInt32( bytes, 12 );
			if ( width <= 0 || height <= 0 || height % count != 0 ) return null;
			height /= count;
			var levels = new List<(int Offset, int Size)>();
			int offset = 128, frameBytes = 0;
			int mipCount = Math.Max( 1, BitConverter.ToInt32( bytes, 28 ) );
			for ( int w = width, h = height; levels.Count < mipCount && h >= 4 && h % 4 == 0; w = Math.Max( 1, w / 2 ), h /= 2 )
			{
				int size = checked(((w + 3) / 4) * (h / 4) * 16);
				if ( size > (bytes.Length - offset) / count ) return null;
				levels.Add( (offset, size) );
				offset += size * count;
				frameBytes += size;
			}
			if ( levels.Count == 0 ) return null;

			var frames = new Texture[count];
			var data = new byte[frameBytes];
			for ( int i = 0; i < count; i++ )
			{
				int dest = frameBytes;
				foreach ( var level in levels )
				{
					dest -= level.Size;
					bytes.AsSpan( level.Offset + i * level.Size, level.Size ).CopyTo( data.AsSpan( dest ) );
				}
				frames[i] = Texture.Create( width, height, ImageFormat.DXT5 ).WithData( data )
					.WithMips( levels.Count ).WithStaticUsage().Finish();
			}

			return Sprite.FromTextures( frames, Find( properties, "frameRate" ).Number );
		}
	}
}
