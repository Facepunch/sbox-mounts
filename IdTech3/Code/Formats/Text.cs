using System.Globalization;

namespace IdTech3.Formats;

/// <summary>
/// Port of the idTech3 COM_ParseExt tokenizer: whitespace separated tokens, quoted strings,
/// // and /* */ comments. Tracks line breaks so callers can parse "rest of line" arguments.
/// </summary>
public sealed class Tokenizer
{
	readonly string _text;
	int _pos;

	public Tokenizer( string text ) { _text = text ?? string.Empty; }

	public bool AtEnd { get { SkipWhitespace( true, out _ ); return _pos >= _text.Length; } }

	/// <summary>
	/// Get the next token. When <paramref name="allowLineBreaks"/> is false, returns empty string
	/// instead of crossing a newline.
	/// </summary>
	public string Next( bool allowLineBreaks = true )
	{
		if ( !SkipWhitespace( allowLineBreaks, out bool hitNewline ) )
			return string.Empty;
		if ( hitNewline && !allowLineBreaks )
			return string.Empty;

		if ( _pos >= _text.Length ) return string.Empty;

		char c = _text[_pos];
		if ( c == '"' )
		{
			_pos++;
			int start = _pos;
			while ( _pos < _text.Length && _text[_pos] != '"' )
			{
				if ( _text[_pos] == '\n' ) break;
				_pos++;
			}
			var s = _text[start.._pos];
			if ( _pos < _text.Length && _text[_pos] == '"' ) _pos++;
			return s;
		}

		int b = _pos;
		while ( _pos < _text.Length && _text[_pos] > ' ' )
		{
			// a comment may start directly after a token
			if ( _text[_pos] == '/' && _pos + 1 < _text.Length && (_text[_pos + 1] == '/' || _text[_pos + 1] == '*') )
				break;
			_pos++;
		}
		return _text[b.._pos];
	}

	/// <summary>Peek the next token without consuming it.</summary>
	public string Peek( bool allowLineBreaks = true )
	{
		int save = _pos;
		var t = Next( allowLineBreaks );
		_pos = save;
		return t;
	}

	/// <summary>Skip the rest of the current line.</summary>
	public void SkipRestOfLine()
	{
		while ( _pos < _text.Length && _text[_pos] != '\n' ) _pos++;
	}

	/// <summary>Skip a braced block, assuming the opening brace has already been consumed.</summary>
	public void SkipBracedSection( int depth = 1 )
	{
		while ( depth > 0 )
		{
			var t = Next();
			if ( t.Length == 0 && AtEnd ) return;
			if ( t == "{" ) depth++;
			else if ( t == "}" ) depth--;
		}
	}

	/// <summary>Tokens remaining on the current line.</summary>
	public List<string> RestOfLine()
	{
		var list = new List<string>();
		while ( true )
		{
			var t = Next( false );
			if ( t.Length == 0 ) break;
			list.Add( t );
		}
		return list;
	}

	bool SkipWhitespace( bool allowLineBreaks, out bool hitNewline )
	{
		hitNewline = false;
		while ( _pos < _text.Length )
		{
			char c = _text[_pos];
			if ( c == '\n' )
			{
				hitNewline = true;
				if ( !allowLineBreaks ) return true;
				_pos++;
				continue;
			}
			if ( c <= ' ' ) { _pos++; continue; }

			if ( c == '/' && _pos + 1 < _text.Length )
			{
				if ( _text[_pos + 1] == '/' )
				{
					while ( _pos < _text.Length && _text[_pos] != '\n' ) _pos++;
					continue;
				}
				if ( _text[_pos + 1] == '*' )
				{
					_pos += 2;
					while ( _pos + 1 < _text.Length && !(_text[_pos] == '*' && _text[_pos + 1] == '/') )
					{
						if ( _text[_pos] == '\n' ) hitNewline = true;
						_pos++;
					}
					_pos = Math.Min( _text.Length, _pos + 2 );
					if ( hitNewline && !allowLineBreaks ) return true;
					continue;
				}
			}
			return true;
		}
		return true;
	}
}

public static class Num
{
	public static float F( string s, float def = 0 )
	{
		if ( string.IsNullOrEmpty( s ) ) return def;
		// atof semantics: parse the longest valid prefix
		s = s.Trim();
		if ( float.TryParse( s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v ) ) return v;
		int end = 0;
		while ( end < s.Length && (char.IsDigit( s[end] ) || s[end] == '.' || s[end] == '-' || s[end] == '+' || s[end] == 'e' || s[end] == 'E') ) end++;
		while ( end > 0 )
		{
			if ( float.TryParse( s[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out v ) ) return v;
			end--;
		}
		return def;
	}

	public static int I( string s, int def = 0 )
	{
		if ( string.IsNullOrEmpty( s ) ) return def;
		if ( int.TryParse( s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v ) ) return v;
		var f = F( s, float.NaN );
		return float.IsNaN( f ) ? def : (int)f;
	}
}

/// <summary>
/// An entity from the BSP entity lump.
/// </summary>
public sealed class Entity
{
	public readonly List<KeyValuePair<string, string>> Pairs = new();
	readonly Dictionary<string, string> _map = new( StringComparer.OrdinalIgnoreCase );

	public int Index { get; internal set; }

	public void Set( string key, string value )
	{
		Pairs.Add( new( key, value ) );
		_map[key] = value; // last one wins, like the game spawn code
	}

	public string ClassName => Get( "classname" );
	public string TargetName => Get( "targetname" );
	public string Target => Get( "target" );

	public bool Has( string key ) => _map.ContainsKey( key );
	public string Get( string key, string def = null ) => _map.TryGetValue( key, out var v ) ? v : def;
	public float Float( string key, float def = 0 ) => _map.TryGetValue( key, out var v ) ? Num.F( v, def ) : def;
	public int Int( string key, int def = 0 ) => _map.TryGetValue( key, out var v ) ? Num.I( v, def ) : def;

	public Vector3 Vector( string key, Vector3 def = default )
	{
		if ( !_map.TryGetValue( key, out var v ) ) return def;
		return ParseVector( v, def );
	}

	public static Vector3 ParseVector( string v, Vector3 def = default )
	{
		if ( string.IsNullOrWhiteSpace( v ) ) return def;
		var parts = v.Split( (char[])null, StringSplitOptions.RemoveEmptyEntries );
		if ( parts.Length == 1 ) { var f = Num.F( parts[0] ); return new Vector3( f, f, f ); }
		if ( parts.Length < 3 ) return def;
		return new Vector3( Num.F( parts[0] ), Num.F( parts[1] ), Num.F( parts[2] ) );
	}

	public Vector3 Origin => Vector( "origin" );

	/// <summary>pitch/yaw/roll, honoring "angles" then "angle" (yaw; -1 up, -2 down).</summary>
	public Vector3 Angles
	{
		get
		{
			if ( Has( "angles" ) ) return Vector( "angles" );
			var a = Float( "angle" );
			if ( a == -1 ) return new Vector3( -90, 0, 0 );
			if ( a == -2 ) return new Vector3( 90, 0, 0 );
			return new Vector3( 0, a, 0 );
		}
	}

	/// <summary>Q3 G_SetMovedir: movement direction from "angle"/"angles".</summary>
	public Vector3 MoveDir
	{
		get
		{
			var a = Angles;
			if ( !Has( "angles" ) )
			{
				var yaw = Float( "angle" );
				if ( yaw == -1 ) return new Vector3( 0, 0, 1 );
				if ( yaw == -2 ) return new Vector3( 0, 0, -1 );
			}
			return Axis.FromAngles( a.x, a.y, a.z ).Forward;
		}
	}

	/// <summary>"*N" brush model reference, or -1.</summary>
	public int BrushModel
	{
		get
		{
			var m = Get( "model" );
			if ( string.IsNullOrEmpty( m ) || m[0] != '*' ) return -1;
			return int.TryParse( m.AsSpan( 1 ), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i ) ? i : -1;
		}
	}

	public override string ToString() => $"{ClassName} #{Index}";

	public static List<Entity> ParseAll( string text )
	{
		var list = new List<Entity>();
		if ( string.IsNullOrEmpty( text ) ) return list;

		var tok = new Tokenizer( text );
		while ( !tok.AtEnd )
		{
			var t = tok.Next();
			if ( t != "{" ) { if ( t.Length == 0 ) break; continue; }

			var e = new Entity { Index = list.Count };
			while ( true )
			{
				var key = tok.Next();
				if ( key == "}" || key.Length == 0 ) break;
				var value = tok.Next();
				if ( value == "}" ) break;
				e.Set( key, value );
			}
			list.Add( e );
		}
		return list;
	}
}
