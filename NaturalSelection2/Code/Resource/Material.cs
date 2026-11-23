using Sandbox.Mounting;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NS2;

public partial class MaterialLoader( string filePath ) : ResourceLoader<GameMount>
{
	/// <summary>
	/// Path relative to the game's install root
	/// </summary>
	private string FilePath { get; init; } = filePath;

	private static Material ModelMaterial => field ??= Material.Load( "shaders/ns2/shaders/model.vmat" );

	string surfaceName;
	internal string SurfaceName => surfaceName ??= SurfaceLoader.Normalize( Find( Parse( Host.ReadAllBytes( FilePath ) ), "surface" ).String );

	protected override object Load()
	{
		var properties = Parse( Host.ReadAllBytes( FilePath ) );
		var shader = Find( properties, "shader" ).String;
		var material = SelectShader( shader ).CreateCopy( Path );

		surfaceName = SurfaceLoader.Normalize( Find( properties, "surface" ).String );
		material.Attributes.Set( "PhysicsSurfaceProperties", SurfaceLoader.PathFor( surfaceName ) );
		ApplyShaderDefaults( material, shader, properties );

		foreach ( var property in properties )
		{
			switch ( property.Type )
			{
				case ValueType.String:
					ApplyTexture( material, property );
					break;

				case ValueType.Number:
					material.Set( $"g_fl{Pascal( property.Key )}", property.Number );
					break;

				case ValueType.Bool:
					material.Set( $"g_fl{Pascal( property.Key )}", property.Bool ? 1f : 0f );
					break;

				case ValueType.Vector:
					material.Set( $"g_v{Pascal( property.Key )}", property.Vector );
					break;
			}
		}

		return material;
	}

	private static Material SelectShader( string shader )
	{
		if ( string.IsNullOrEmpty( shader ) || !shader.EndsWith( ".surface_shader", StringComparison.OrdinalIgnoreCase ) )
			return ModelMaterial;

		var path = $"shaders/ns2/{shader[..^".surface_shader".Length].ToLowerInvariant()}.vmat";

		return FileSystem.Mounted.FileExists( path ) || FileSystem.Mounted.FileExists( $"{path}_c" )
			? Material.Load( path ) : ModelMaterial;
	}

	private void ApplyTexture( Material material, Property property )
	{
		if ( property.Key == "shader" || !property.String.EndsWith( ".dds", StringComparison.OrdinalIgnoreCase ) )
			return;

		SetTexture( material, property.Key, property.String );
	}

	private void SetTexture( Material material, string key, string relative )
	{
		relative = relative.Replace( '\\', '/' );
		var path = $"mount://{Host.Ident}/ns2/{relative}.vtex";
		if ( Host.GetByPath( path ) is null )
		{
			path = $"mount://{Host.Ident}/core/{relative}.vtex";
			if ( Host.GetByPath( path ) is null )
				return;
		}

		var texture = Texture.Load( path, false );
		if ( texture is not null )
			material.Set( $"g_t{Pascal( key )}", texture );
	}

	// Spark materials inherit unspecified inputs from their surface shader.
	private void ApplyShaderDefaults( Material material, string shader, Property[] properties )
	{
		foreach ( var (key, path) in ShaderDefaults( shader ) )
		{
			if ( Find( properties, key ).Key is null )
				SetTexture( material, key, path );
		}
	}

	private static readonly Regex TextureInput = new(
		"""<input\s+name="([^"]+)"\s+type="texture"[^>]*>\s*([^<\s][^<]*\.dds)\s*</input>""",
		RegexOptions.Compiled | RegexOptions.IgnoreCase );

	private (string Key, string Path)[] ShaderDefaults( string shader )
	{
		if ( string.IsNullOrEmpty( shader ) )
			return [];

		return Host.ShaderDefaults.GetOrAdd( shader, key => ReadShaderDefaults( Host.FileSystem, key ) );
	}

	private static (string Key, string Path)[] ReadShaderDefaults( BaseFileSystem fs, string shader )
	{
		if ( !GameMount.TryResolve( fs, shader, out var source ) )
			return [];

		var defaults = new List<(string, string)>();

		foreach ( Match match in TextureInput.Matches( fs.ReadAllText( source ) ) )
		{
			defaults.Add( (match.Groups[1].Value, match.Groups[2].Value.Trim()) );
		}

		return [.. defaults];
	}

	private static string Pascal( string key ) => char.ToUpperInvariant( key[0] ) + key[1..];

	private static Property Find( Property[] properties, string key ) =>
		properties.FirstOrDefault( x => x.Key == key );

	private enum ValueType
	{
		String,
		Number,
		Bool,
		Vector,
	}

	private readonly struct Property
	{
		public string Key { get; init; }
		public ValueType Type { get; init; }
		public string String { get; init; }
		public Vector4 Vector { get; init; }
		public bool Bool { get; init; }

		public float Number => Vector.x;
	}

	[ThreadStatic] private static Property[] Scratch;

	private static Property[] Parse( ReadOnlySpan<byte> data )
	{
		var lexer = new Lexer( data );

		var properties = Scratch ??= new Property[64];
		var count = 0;

		while ( count < properties.Length && lexer.Read( out var key ) && lexer.Read( out _ ) && lexer.Read( out var value ) )
		{
			properties[count++] = Read( key, value, ref lexer );
			lexer.SkipComma();
		}

		return properties[..count];
	}

	private static Property Read( Token key, Token value, ref Lexer lexer )
	{
		var name = key.ToString();

		if ( !value.Quoted )
		{
			if ( value.Is( '[' ) )
				return new Property { Key = name, Type = ValueType.Vector, Vector = lexer.ReadVector() };

			if ( value.Is( "true"u8 ) || value.Is( "false"u8 ) )
				return new Property { Key = name, Type = ValueType.Bool, Bool = value.Is( "true"u8 ) };

			if ( value.IsNumber( out var number ) )
				return new Property { Key = name, Type = ValueType.Number, Vector = new Vector4( number, 0, 0, 0 ) };
		}

		return new Property { Key = name, Type = ValueType.String, String = value.ToString() };
	}

	private readonly ref struct Token( ReadOnlySpan<byte> text, bool quoted )
	{
		public readonly ReadOnlySpan<byte> Text = text;
		public readonly bool Quoted = quoted;

		public bool Is( char c ) => !Quoted && Text.Length == 1 && Text[0] == c;
		public bool Is( ReadOnlySpan<byte> word ) => !Quoted && Text.SequenceEqual( word );

		public bool IsNumber( out float value )
		{
			value = 0;
			return !Text.IsEmpty
				&& (char.IsAsciiDigit( (char)Text[0] ) || Text[0] is (byte)'-' or (byte)'+' or (byte)'.')
				&& float.TryParse( Text, null, out value );
		}

		public override string ToString() => Encoding.UTF8.GetString( Text );
	}

	private ref struct Lexer( ReadOnlySpan<byte> data )
	{
		private readonly ReadOnlySpan<byte> _data = data;
		private int _at = 0;

		public bool Read( out Token token )
		{
			SkipTrivia();
			token = default;

			if ( _at >= _data.Length )
				return false;

			if ( _data[_at] == '"' )
			{
				var start = ++_at;
				Seek( _data[start..].IndexOf( (byte)'"' ) );
				token = new Token( _data[start.._at], quoted: true );
				if ( _at < _data.Length ) _at++;
				return true;
			}

			if ( IsWordChar( _data[_at] ) )
			{
				var start = _at++;
				while ( _at < _data.Length && IsWordChar( _data[_at] ) )
					_at++;
				token = new Token( _data[start.._at], quoted: false );
				return true;
			}

			token = new Token( _data.Slice( _at++, 1 ), quoted: false );
			return true;
		}

		private void Seek( int offset ) => _at = offset < 0 ? _data.Length : _at + offset;

		public Vector4 ReadVector()
		{
			Span<float> elements = [1f, 1f, 1f, 1f];
			var index = 0;

			while ( Read( out var token ) && !token.Is( ']' ) )
			{
				if ( index < 4 && token.IsNumber( out var number ) )
					elements[index] = number;

				index++;
				SkipComma();
			}

			return new Vector4( elements[0], elements[1], elements[2], elements[3] );
		}

		public void SkipComma()
		{
			SkipTrivia();
			if ( _at < _data.Length && _data[_at] == ',' ) _at++;
		}

		private static bool IsWordChar( byte b ) =>
			b is (byte)'-' or (byte)'.' or (byte)'_' ||
			b is not ((byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r'
				or >= 0x21 and <= 0x2F or >= 0x3A and <= 0x40 or >= 0x5B and <= 0x60 or >= 0x7B and <= 0x7E);

		private void SkipTrivia()
		{
			while ( _at < _data.Length )
			{
				if ( _data[_at] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' )
				{
					_at++;
					continue;
				}

				if ( _data[_at] != '/' )
					return;

				var rest = _data[_at..];

				if ( rest.StartsWith( "//"u8 ) )
				{
					var end = rest.IndexOf( (byte)'\n' );
					_at = end < 0 ? _data.Length : _at + end;
				}
				else if ( rest.StartsWith( "/*"u8 ) )
				{
					var end = rest[2..].IndexOf( "*/"u8 );
					_at = end < 0 ? _data.Length : _at + end + 4;
				}
				else
				{
					return;
				}
			}
		}
	}
}
