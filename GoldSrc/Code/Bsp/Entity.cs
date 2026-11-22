using System;
using System.Globalization;

namespace GoldSrc.Bsp;

/// <summary>
/// One entity block from the BSP entity lump, with the key lookups GoldSrc maps rely on.
/// </summary>
internal sealed class Entity( Dictionary<string, string> keyValues )
{
	public IReadOnlyDictionary<string, string> KeyValues => keyValues;

	public string ClassName => GetString( "classname" );
	public string TargetName => GetString( "targetname" );
	public string Target => GetString( "target" );
	public int SpawnFlags => GetInt( "spawnflags" );

	public Vector3 Origin => GetVector3( "origin" );

	/// <summary>
	/// kRenderNormal / kRenderTransColor / kRenderTransTexture / kRenderGlow / kRenderTransAlpha /
	/// kRenderTransAdd.
	/// </summary>
	public int RenderMode => GetInt( "rendermode" );

	/// <summary>
	/// 0-255 blend amount. GoldSrc only reads this for the translucent and additive rendermodes.
	/// </summary>
	public int RenderAmt => GetInt( "renderamt" );

	/// <summary>
	/// 0-255 colour, only used by kRenderTransColor and kRenderGlow.
	/// </summary>
	public Vector3 RenderColor => GetVector3( "rendercolor" );

	/// <summary>
	/// GoldSrc writes either "angles" (pitch yaw roll) or the older yaw-only "angle", where -1
	/// means straight up and -2 straight down.
	/// </summary>
	public Angles Angles
	{
		get
		{
			var angles = new Angles();
			foreach ( var (key, _) in keyValues )
			{
				if ( key == "angles" )
				{
					var value = GetVector3( key );
					angles = new Angles( value.x, value.y, value.z );
				}
				else if ( key == "angle" )
					angles = new Angles( 0f, GetFloat( key ), 0f );
			}
			return angles;
		}
	}

	public Rotation Rotation => Rotation.From( Angles );

	/// <summary>
	/// The movement direction used by doors and buttons, honouring the up/down "angle" specials.
	/// </summary>
	public Vector3 MoveDirection
	{
		get
		{
			var angles = Angles;
			if ( angles == new Angles( 0f, -1f, 0f ) ) return Vector3.Up;
			if ( angles == new Angles( 0f, -2f, 0f ) ) return Vector3.Down;
			return Rotation.Forward;
		}
	}

	/// <summary>
	/// Index into the BSP model array for brush entities ("*3"), or -1 for point entities.
	/// </summary>
	public int BrushModelIndex
	{
		get
		{
			var model = GetString( "model" );
			if ( string.IsNullOrEmpty( model ) || model[0] != '*' )
				return -1;

			return int.TryParse( model.AsSpan( 1 ), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index ) ? index : -1;
		}
	}

	public bool Has( string key ) => keyValues.ContainsKey( key );

	public string GetString( string key, string defaultValue = null )
		=> keyValues.TryGetValue( key, out var value ) && !string.IsNullOrWhiteSpace( value ) ? value : defaultValue;

	public float GetFloat( string key, float defaultValue = 0f )
		=> float.TryParse( GetString( key ), NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) ? value : defaultValue;

	public int GetInt( string key, int defaultValue = 0 )
		=> int.TryParse( GetString( key ), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value ) ? value : defaultValue;

	public Vector3 GetVector3( string key, Vector3 defaultValue = default )
	{
		var value = GetString( key ).AsSpan();
		Span<Range> parts = stackalloc Range[4];
		if ( value.SplitAny( parts, " \t", StringSplitOptions.RemoveEmptyEntries ) < 3 )
			return defaultValue;

		return new Vector3( Parse( value[parts[0]] ), Parse( value[parts[1]] ), Parse( value[parts[2]] ) );
	}

	/// <summary>
	/// "light"/"_light" is "r g b brightness", where brightness is optional.
	/// </summary>
	public bool TryGetLight( string key, out Color color, out float brightness )
	{
		color = Color.White;
		brightness = 0f;

		var value = GetString( key ).AsSpan();
		Span<Range> parts = stackalloc Range[5];
		int count = value.SplitAny( parts, " \t", StringSplitOptions.RemoveEmptyEntries );
		if ( count == 0 )
			return false;

		if ( count < 3 )
		{
			brightness = Parse( value[parts[0]] );
			return true;
		}

		color = new Color( Parse( value[parts[0]] ) / 255f, Parse( value[parts[1]] ) / 255f, Parse( value[parts[2]] ) / 255f );
		brightness = count > 3 ? Parse( value[parts[3]] ) : 200f;
		return true;
	}

	private static float Parse( ReadOnlySpan<char> value )
		=> float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result ) ? result : 0f;
}
