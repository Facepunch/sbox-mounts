// Synthetic face/mip colors are checked by GPU readback, then real mounted cubes are loaded.
var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
await mount.RefreshInternal();
var load = mount.GetType().Assembly.GetType( "Diesel.DieselTextureLoader" ).GetMethod( "LoadDds",
	System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static );
ushort[] palette = { 0xf800, 0x07e0, 0x001f, 0xffe0, 0xf81f, 0x07ff };
int cases = 0, checkedPixels = 0;
foreach ( var (fourcc, bgra) in new[] { (0x31545844u, false), (0x35545844u, false), (0u, true), (0u, false) } )
{
	var header = new byte[128];
	void Set( int offset, uint value ) => BitConverter.GetBytes( value ).CopyTo( header, offset );
	Set( 0, 0x20534444 ); Set( 4, 124 ); Set( 8, 0x20000 ); Set( 12, 8 ); Set( 16, 8 );
	Set( 28, 4 ); Set( 76, 32 ); Set( 80, fourcc == 0 ? 0x41u : 4u ); Set( 84, fourcc ); Set( 112, 0xfe00 );
	if ( fourcc == 0 )
	{
		Set( 88, 32 ); Set( 92, bgra ? 0xff0000u : 0xffu ); Set( 96, 0xff00 );
		Set( 100, bgra ? 0xffu : 0xff0000u ); Set( 104, 0xff000000 );
	}
	using var stream = new System.IO.MemoryStream();
	stream.Write( header );
	using var writer = new System.IO.BinaryWriter( stream );
	for ( int face = 0; face < 6; face++ )
		for ( int mip = 0; mip < 4; mip++ )
		{
			int dimension = 8 >> mip;
			ushort color = palette[(face + mip) % 6];
			int blocks = Math.Max( 1, (dimension + 3) / 4 );
			if ( fourcc == 0 )
			{
				for ( int pixel = 0; pixel < dimension * dimension; pixel++ )
					writer.Write( new byte[] { (byte)((bgra ? color & 31 : color >> 11) * 255 / 31),
						(byte)(((color >> 5) & 63) * 255 / 63), (byte)((bgra ? color >> 11 : color & 31) * 255 / 31), 255 } );
			}
			else for ( int block = 0; block < blocks * blocks; block++ )
			{
				if ( fourcc == 0x35545844 ) writer.Write( new byte[] { 255, 255, 0, 0, 0, 0, 0, 0 } );
				writer.Write( color ); writer.Write( (ushort)0 ); writer.Write( 0u );
			}
		}
	using var texture = (Texture)load.Invoke( null, new object[] { stream.ToArray() } );
	int mips = fourcc == 0 ? 4 : 2;
	if ( texture.Depth != 6 || texture.Mips != mips || texture.Width != 8 ) throw new Exception( "Wrong cube descriptor." );
	for ( int face = 0; face < 6; face++ )
		for ( int mip = 0; mip < mips; mip++ )
		{
			int dimension = 8 >> mip;
			var pixels = new Color32[dimension * dimension];
			texture.GetPixels( (0, 0, dimension, dimension), face, mip, pixels.AsSpan(), ImageFormat.RGBA8888 );
			ushort color = palette[(face + mip) % 6];
			foreach ( var pixel in pixels )
				if ( pixel.r != (color >> 11) * 255 / 31 || pixel.g != ((color >> 5) & 63) * 255 / 63
					|| pixel.b != (color & 31) * 255 / 31 || pixel.a != 255 )
					throw new Exception( $"Cube upload pixel mismatch: {fourcc:x}, face {face}, mip {mip}: {pixel.r},{pixel.g},{pixel.b},{pixel.a}, expected 565 {color:x}." );
			checkedPixels += pixels.Length;
		}
	cases++;
}
var shipped = new System.Collections.Generic.List<object>();
foreach ( var path in new[]
{
	"environments/cubemaps/cubemap_bank_01",
	"levels/bank/cube_lights/100134",
	"levels/diamondheist/cube_lights/600000",
	"core/textures/cubemap_template_day"
} )
{
	var loader = Sandbox.Mounting.MountUtility.FindLoader( $"mount://payday/{path}.texture.vtex" );
	var texture = await loader.GetOrCreate() as Texture;
	if ( texture is null || texture.IsError || texture.Depth != 6 || !ReferenceEquals( texture, await loader.GetOrCreate() ) )
		throw new Exception( "Mounted cubemap load/cache failed: " + path );
	shipped.Add( new { Path = path, texture.Width, texture.Mips, texture.Depth } );
}
return new { Formats = cases, CheckedPixels = checkedPixels, Shipped = shipped };
