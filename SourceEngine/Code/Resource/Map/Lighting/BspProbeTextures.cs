using System;

namespace Source1;

/// <summary>
/// Accumulates probe texture data during BSP loading and packs all leaves into shared 3D atlas
/// textures, grouped by (width, height, format). Call <see cref="GetOrCreate"/> for each leaf,
/// then <see cref="Finish"/> once all leaves are done. Each leaf gets an atlas handle that
/// identifies the shared texture and the leaf's Z region within it.
/// </summary>
internal sealed class BspProbeTextures( string resourcePrefix ) : IDisposable
{
	/// <summary>
	/// Vulkan guarantees maxImageDimension3D >= 2048 on desktop. Split an atlas when the
	/// accumulated depth would exceed this, so each atlas texture stays within the limit.
	/// </summary>
	private const int MaxAtlasDepth = 2048;

	internal readonly record struct AtlasHandle( int GroupIndex, int SliceOffset, int SliceCount );

	private record struct PendingLeaf( int Width, int Height, int Depth, ImageFormat Format, byte[] Data );

	private sealed class AtlasGroup( int width, int height, ImageFormat format )
	{
		public int Width => width;
		public int Height => height;
		public ImageFormat Format => format;
		public int TotalDepth;
		public readonly List<byte[]> Slabs = [];
		public Texture Texture;
	}

	private readonly List<PendingLeaf> pending = [];
	private readonly List<AtlasGroup> groups = [];
	private readonly Dictionary<(ImageFormat Format, ulong Value), AtlasHandle> constants = [];
	private readonly HashSet<Texture> textures = [];
	private bool finished;

	public int TextureCount => textures.Count;
	public long ByteCount { get; private set; }

	/// <summary>
	/// Accumulate a leaf's probe data. Returns a handle used after <see cref="Finish"/> to
	/// retrieve the shared texture and the leaf's Z offset/scale within it.
	/// </summary>
	public AtlasHandle GetOrCreate( Vector3Int counts, int depth, ImageFormat format, ReadOnlySpan<byte> data )
	{
		if ( finished )
			throw new InvalidOperationException( "Cannot add probes after Finish()." );

		int stride = format switch
		{
			ImageFormat.RGBA8888 => 4,
			ImageFormat.RGBA16161616F => 8,
			_ => throw new NotSupportedException( $"Unsupported BSP probe texture format {format}." )
		};
		if ( counts.x is < 1 or > 32 || counts.y is < 1 or > 32 || counts.z is < 1 or > 32
			|| depth != counts.z && depth != counts.z * 6
			|| data.Length != checked(counts.x * counts.y * depth * stride) )
			throw new InvalidDataException( "BSP probe texture dimensions do not match its data." );

		bool constant = depth == counts.z && IsConstant( data, stride );
		ulong value = 0;
		if ( constant )
		{
			depth = 1;
			data = data[..stride];
			value = stride == 4 ? BitConverter.ToUInt32( data ) : BitConverter.ToUInt64( data );
			if ( constants.TryGetValue( (format, value), out var existing ) )
				return existing;
		}

		// Find or create an atlas group with matching (width, height, format) that has room.
		int width = constant ? 1 : counts.x;
		int height = constant ? 1 : counts.y;
		var group = FindOrCreateGroup( width, height, format, depth );
		int groupIndex = groups.IndexOf( group );
		int sliceOffset = group.TotalDepth;
		group.Slabs.Add( data.ToArray() );
		group.TotalDepth += depth;
		ByteCount += data.Length;

		var handle = new AtlasHandle( groupIndex, sliceOffset, depth );
		if ( constant )
			constants.Add( (format, value), handle );
		return handle;
	}

	/// <summary>
	/// Build the atlas textures from the accumulated data. Call once after all leaves are added.
	/// </summary>
	public void Finish()
	{
		if ( finished )
			return;
		finished = true;

		int textureIndex = 0;
		foreach ( var group in groups )
		{
			int totalBytes = 0;
			foreach ( var slab in group.Slabs )
				totalBytes += slab.Length;

			var combined = new byte[totalBytes];
			int offset = 0;
			foreach ( var slab in group.Slabs )
			{
				slab.CopyTo( combined, offset );
				offset += slab.Length;
			}

			var name = $"{resourcePrefix}/probes/atlas_{textureIndex++}";
			var texture = Texture.CreateVolume( group.Width, group.Height, group.TotalDepth, group.Format )
				.WithName( name ).WithStaticUsage().WithData( combined ).Finish();
			if ( texture is null || !texture.IsValid || texture.IsError )
				throw new InvalidOperationException( $"{resourcePrefix}: could not create {group.Width}x{group.Height}x{group.TotalDepth} {group.Format} probe atlas." );
			group.Texture = texture;
			textures.Add( texture );

			// Free the slab data now that it's uploaded
			group.Slabs.Clear();
		}
	}

	public Texture GetTexture( AtlasHandle handle ) => groups[handle.GroupIndex].Texture;
	public float GetAtlasZScale( AtlasHandle handle ) => (float)handle.SliceCount / groups[handle.GroupIndex].TotalDepth;
	public float GetAtlasZOffset( AtlasHandle handle ) => (float)handle.SliceOffset / groups[handle.GroupIndex].TotalDepth;

	public bool Contains( Texture texture ) => textures.Contains( texture );

	public void Dispose()
	{
		foreach ( var texture in textures )
			texture.Dispose();
		textures.Clear();
		groups.Clear();
		constants.Clear();
		pending.Clear();
		ByteCount = 0;
	}

	private AtlasGroup FindOrCreateGroup( int width, int height, ImageFormat format, int depthNeeded )
	{
		// Find an existing group with matching dimensions that has room
		foreach ( var g in groups )
		{
			if ( g.Width == width && g.Height == height && g.Format == format && g.TotalDepth + depthNeeded <= MaxAtlasDepth )
				return g;
		}
		var group = new AtlasGroup( width, height, format );
		groups.Add( group );
		return group;
	}

	private static bool IsConstant( ReadOnlySpan<byte> data, int stride )
	{
		var first = data[..stride];
		for ( int offset = stride; offset < data.Length; offset += stride )
			if ( !data.Slice( offset, stride ).SequenceEqual( first ) )
				return false;
		return true;
	}
}
