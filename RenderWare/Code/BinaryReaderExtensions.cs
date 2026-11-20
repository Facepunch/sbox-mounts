using System.Text;

namespace Renderware;

public static class Units
{
	/// <summary>
	/// GTA units (metres) to sbox units (inches), rounded for nicer numbers.
	/// </summary>
	public const float Scale = 40;
}

public static class BinaryReaderExtensions
{
	public static Vector2 ReadVector2( this BinaryReader br ) => new( br.ReadSingle(), br.ReadSingle() );
	public static Vector3 ReadVector3( this BinaryReader br ) => new( br.ReadSingle(), br.ReadSingle(), br.ReadSingle() );

	/// <summary>
	/// A null padded ASCII string of a fixed length, minus the padding and any control characters.
	/// </summary>
	public static string ReadFixedString( this BinaryReader br, int length )
	{
		var bytes = br.ReadBytes( length );
		var sb = new StringBuilder( length );
		foreach ( var b in bytes )
		{
			if ( b == 0 ) break;
			if ( b >= 32 ) sb.Append( (char)b );
		}
		return sb.ToString();
	}
}
