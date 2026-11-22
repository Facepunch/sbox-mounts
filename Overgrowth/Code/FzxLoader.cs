
namespace Overgrowth;

public struct FzxObject
{
	public string Label;
	public Rotation Rotation;
	public Vector3 Position;
	public Vector3 Scale;
}

static class FzxLoader
{
	static readonly byte[] Header = [211, (byte)'F', (byte)'Z', (byte)'X', 13, 10, 32, 10];

	public static List<FzxObject> Load( BaseFileSystem fs, string path )
	{
		var data = fs.ReadAllBytes( path ).ToArray();
		int pos = Header.Length;

		for ( int i = 0; i < Header.Length; i++ )
			if ( data[i] != Header[i] )
				throw new System.Exception( "Invalid FZX header" );

		int version = System.BitConverter.ToInt32( data, pos ); pos += 4;
		if ( version != 1 )
			throw new System.Exception( $"Unsupported FZX version {version}" );

		int count = System.BitConverter.ToInt32( data, pos ); pos += 4;
		var objects = new List<FzxObject>( count );

		for ( int i = 0; i < count; i++ )
		{
			int len = System.BitConverter.ToInt32( data, pos ); pos += 4;
			string label = System.Text.Encoding.ASCII.GetString( data, pos, len ); pos += len;

			float qx = ReadFloat(); float qy = ReadFloat(); float qz = ReadFloat(); float qw = ReadFloat();
			float px = ReadFloat(); float py = ReadFloat(); float pz = ReadFloat();
			float sx = ReadFloat(); float sy = ReadFloat(); float sz = ReadFloat();

			objects.Add( new FzxObject
			{
				Label = label,
				Rotation = new Rotation( qy, qw, -qz, qx ),
				Position = new Vector3( px, pz, -py ),
				Scale = new Vector3( sx, sy, sz )
			} );
		}

		return objects;

		float ReadFloat()
		{
			var v = System.BitConverter.ToSingle( data, pos );
			pos += 4;
			return v;
		}
	}
}
