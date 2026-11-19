using System;

namespace Source1;

internal sealed class BspProbeTextures( string resourcePrefix, int maxTextures = MaxProbeTextures ) : IDisposable
{
	/// <summary>
	/// The engine's texture slot limit is 32 768. Reserve headroom for materials, lightmaps,
	/// render targets and other mounts. Each leaf volume needs 3 textures (ambient, indices,
	/// scalars), so this cap keeps a single map well under the global limit even when several
	/// Source 1 maps are mounted at once.
	/// </summary>
	internal const int MaxProbeTextures = 6000;

	private readonly HashSet<Texture> textures = [];
	private readonly Dictionary<(ImageFormat Format, ulong Value), Texture> constants = [];
	public int Count => textures.Count;
	public long ByteCount { get; private set; }
	public bool ReachedBudget { get; private set; }

	public Texture GetOrCreate( Vector3Int counts, int depth, ImageFormat format, ReadOnlySpan<byte> data )
	{
		if ( textures.Count >= maxTextures )
		{
			ReachedBudget = true;
			return null;
		}
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

		var size = new Vector3Int( counts.x, counts.y, depth );
		bool constant = depth == counts.z && IsConstant( data, stride );
		ulong value = 0;
		if ( constant )
		{
			size = new Vector3Int( 1, 1, 1 );
			data = data[..stride];
			value = stride == 4 ? BitConverter.ToUInt32( data ) : BitConverter.ToUInt64( data );
			if ( constants.TryGetValue( (format, value), out var existing ) )
				return existing;
		}

		var name = $"{resourcePrefix}/probes/{textures.Count}";
		var texture = Texture.CreateVolume( size.x, size.y, size.z, format )
			.WithName( name ).WithStaticUsage().WithData( data.ToArray() ).Finish();
		if ( texture is null || !texture.IsValid || texture.IsError )
			throw new InvalidOperationException( $"{resourcePrefix}: could not create {size} {format} probe texture." );
		textures.Add( texture );
		if ( constant )
			constants.Add( (format, value), texture );
		ByteCount += data.Length;
		return texture;
	}

	public bool Contains( Texture texture ) => textures.Contains( texture );

	public void Dispose()
	{
		foreach ( var texture in textures )
			texture.Dispose();
		textures.Clear();
		constants.Clear();
		ByteCount = 0;
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
