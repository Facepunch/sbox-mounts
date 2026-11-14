using System;
using System.IO;
using GoldSrc.Bsp;

namespace GoldSrc;

partial class MapLoader
{
	private const int LightmapPageSize = 2048;

	/// <summary>
	/// GoldSrc lightmaps sample one luxel every 16 world units.
	/// </summary>
	private const int LuxelSize = 16;

	private readonly List<Texture[]> _lightmapPages = [];
	private FaceSurface[] _surfaces;

	private struct FaceSurface
	{
		public float UMin;
		public float VMin;
		public Vector2 Offset;
		public Vector2 Size;
		public int Page;
	}

	/// <summary>
	/// Measures every face's lightmap extents, packs them into atlas pages and copies the
	/// RGB samples (one block per active lightstyle) into the three style-channel textures.
	/// </summary>
	private void CreateSurfaces( BspFile bsp )
	{
		const int size = LightmapPageSize;

		var faceCount = bsp.Faces.Length;
		_surfaces = new FaceSurface[faceCount];

		Sandbox.Utility.Parallel.For( 0, faceCount, faceIndex =>
		{
			var face = bsp.Faces[faceIndex];
			var texInfo = bsp.TexInfos[face.TextureInfo];

			var umin = float.MaxValue;
			var vmin = float.MaxValue;
			var umax = float.MinValue;
			var vmax = float.MinValue;

			for ( var edge = 0; edge < face.EdgeCount; ++edge )
			{
				var vertex = bsp.VertexFromSurfEdge( face.FirstEdge + edge );

				var u = Vector3.Dot( vertex, texInfo.S ) + texInfo.OffsetS;
				var v = Vector3.Dot( vertex, texInfo.T ) + texInfo.OffsetT;

				umin = MathF.Min( u, umin );
				vmin = MathF.Min( v, vmin );
				umax = MathF.Max( u, umax );
				vmax = MathF.Max( v, vmax );
			}

			var minu = MathF.Floor( umin / LuxelSize );
			var maxu = MathF.Ceiling( umax / LuxelSize );
			var minv = MathF.Floor( vmin / LuxelSize );
			var maxv = MathF.Ceiling( vmax / LuxelSize );

			var width = (int)(maxu - minu + 1);
			var height = (int)(maxv - minv + 1);

			_surfaces[faceIndex] = new FaceSurface
			{
				UMin = minu * LuxelSize,
				VMin = minv * LuxelSize,
				Size = new Vector2( width, height ),
			};
		} );

		var root = NewLightmapRoot( size );
		byte[] red = [], green = [], blue = [];
		var page = 0;

		void FinishPage()
		{
			if ( red.Length == 0 ) return;
			_lightmapPages.Add(
			[
				Texture.Create( size, size, ImageFormat.RGBA8888 ).WithData( red ).Finish(),
				Texture.Create( size, size, ImageFormat.RGBA8888 ).WithData( green ).Finish(),
				Texture.Create( size, size, ImageFormat.RGBA8888 ).WithData( blue ).Finish(),
			] );
		}

		for ( var faceIndex = 0; faceIndex < faceCount; ++faceIndex )
		{
			var face = bsp.Faces[faceIndex];

			// Sky, liquid and other special surfaces carry no samples, so don't waste atlas space.
			if ( face.LightmapOffset < 0 )
				continue;

			var width = (int)_surfaces[faceIndex].Size.x;
			var height = (int)_surfaces[faceIndex].Size.y;

			if ( width <= 0 || height <= 0 || width > size || height > size )
				throw new InvalidDataException( $"{FilePath}: face {faceIndex} has invalid lightmap dimensions {width}x{height}." );

			var node = AllocateLightmapRect( root, width, height );

			// Current page is full: finalise it and start a fresh one.
			if ( node is null )
			{
				FinishPage();
				page++;
				root = NewLightmapRoot( size );
				red = new byte[size * size * 4];
				green = new byte[size * size * 4];
				blue = new byte[size * size * 4];
				node = AllocateLightmapRect( root, width, height );
			}

			_surfaces[faceIndex].Page = page;

			if ( node is null )
				throw new InvalidDataException( $"{FilePath}: face {faceIndex} does not fit in an empty lightmap page." );

			_surfaces[faceIndex].Offset = new Vector2( node.X, node.Y );

			if ( red.Length == 0 )
			{
				red = new byte[size * size * 4];
				green = new byte[size * size * 4];
				blue = new byte[size * size * 4];
			}
			ReadOnlySpan<byte> styles = [face.Style0, face.Style1, face.Style2, face.Style3];
			var luxels = width * height;
			var block = 0;

			for ( var s = 0; s < styles.Length; ++s )
			{
				if ( styles[s] == 255 )
					break;
				if ( styles[s] >= LightStyles.StyleCount )
					throw new InvalidDataException( $"{FilePath}: face {faceIndex} has invalid lightstyle {styles[s]}." );

				int source = checked(face.LightmapOffset + block++ * luxels * 3);
				if ( source < 0 || source > bsp.Lighting.Length - luxels * 3 )
					throw new InvalidDataException( $"{FilePath}: face {faceIndex} has invalid lightmap samples." );
				for ( var y = 0; y < height; ++y )
				{
					int src = source + y * width * 3;
					int dst = (((node.Y + y) * size) + node.X) * 4 + s;
					for ( var x = 0; x < width; ++x, src += 3, dst += 4 )
					{
						red[dst] = bsp.Lighting[src + 0];
						green[dst] = bsp.Lighting[src + 1];
						blue[dst] = bsp.Lighting[src + 2];
					}
				}
			}
		}

		FinishPage();
	}

	/// <summary>
	/// Maps a face's texture space coordinate into its slot in the lightmap atlas, offsetting by
	/// half a luxel so we sample texel centres.
	/// </summary>
	private static Vector2 LightmapCoord( FaceSurface surface, float u, float v )
	{
		return new Vector2(
			(u - surface.UMin + LuxelSize * 0.5f) / LuxelSize + surface.Offset.x,
			(v - surface.VMin + LuxelSize * 0.5f) / LuxelSize + surface.Offset.y ) / LightmapPageSize;
	}

	private static LightmapNode NewLightmapRoot( int size ) => new( 0, 0, size, size );

	private sealed class LightmapNode( int x, int y, int width, int height )
	{
		public readonly int X = x;
		public readonly int Y = y;
		public readonly int Width = width;
		public readonly int Height = height;
		public LightmapNode Left;
		public LightmapNode Right;
		public int MaxW = width;
		public int MaxH = height;
	}

	private static LightmapNode AllocateLightmapRect( LightmapNode node, int width, int height )
	{
		if ( node.MaxW < width || node.MaxH < height )
			return null;

		if ( node.Left is not null )
		{
			var ret = AllocateLightmapRect( node.Left, width, height )
				?? AllocateLightmapRect( node.Right, width, height );

			node.MaxW = Math.Max( node.Left.MaxW, node.Right.MaxW );
			node.MaxH = Math.Max( node.Left.MaxH, node.Right.MaxH );
			return ret;
		}

		if ( node.Width < width || node.Height < height ) return null;

		if ( node.Width == width && node.Height == height )
		{
			node.MaxW = 0;
			node.MaxH = 0;
			return node;
		}

		if ( (node.Width - width) > (node.Height - height) )
		{
			node.Left = new( node.X, node.Y, width, node.Height );
			node.Right = new( node.X + width, node.Y, node.Width - width, node.Height );
		}
		else
		{
			node.Left = new( node.X, node.Y, node.Width, height );
			node.Right = new( node.X, node.Y + height, node.Width, node.Height - height );
		}

		var result = AllocateLightmapRect( node.Left, width, height );

		node.MaxW = Math.Max( node.Left.MaxW, node.Right.MaxW );
		node.MaxH = Math.Max( node.Left.MaxH, node.Right.MaxH );
		return result;
	}
}
