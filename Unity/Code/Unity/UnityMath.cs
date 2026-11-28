namespace UnityMount;

/// <summary>
/// Coordinate-system and unit conversions between Unity (left-handed, Y-up, meters)
/// and s&amp;box / Source 2 (right-handed, Z-up, inches).
///
/// Basis mapping:
///   sbox.X = +unity.Z (Forward)
///   sbox.Y = -unity.X (Left = -Right)
///   sbox.Z = +unity.Y (Up)
/// </summary>
public static class UnityMath
{
	/// <summary>1 Unity meter = 39.3700787 Source inches.</summary>
	public const float MeterToInch = 39.3700787f;

	public static Vector3 Position( float ux, float uy, float uz )
		=> new( uz * MeterToInch, -ux * MeterToInch, uy * MeterToInch );

	public static Vector3 Position( Vector3 u )
		=> new( u.z * MeterToInch, -u.x * MeterToInch, u.y * MeterToInch );

	/// <summary>Direction or normal vector without unit scaling.</summary>
	public static Vector3 Direction( float ux, float uy, float uz )
		=> new( uz, -ux, uy );

	public static Vector3 Direction( Vector3 u )
		=> new( u.z, -u.x, u.y );

	/// <summary>
	/// Tangent vector (xyz + handedness sign w). Because the basis change has det = -1,
	/// the bitangent handedness sign flips.
	/// </summary>
	public static Vector4 Tangent( float tx, float ty, float tz, float tw )
		=> new( tz, -tx, ty, tw < 0f ? 1f : -1f );

	/// <summary>
	/// Scale permutation (positive components; does not negate Y).
	/// </summary>
	public static Vector3 Scale( float sx, float sy, float sz )
		=> new( sz, sx, sy );

	public static Vector3 Scale( Vector3 s )
		=> new( s.z, s.x, s.y );

	/// <summary>
	/// Converts a Unity quaternion (qx, qy, qz, qw) to s&amp;box Rotation.
	/// Derived from M * R(q) * M^-1 where M maps (x,y,z) -> (z, -x, y).
	/// Preserves quaternion multiplication order (group homomorphism).
	/// </summary>
	public static Rotation ConvertRotation( float qx, float qy, float qz, float qw )
	{
		var rot = new Rotation( -qz, qx, -qy, qw );
		var lenSq = rot.x * rot.x + rot.y * rot.y + rot.z * rot.z + rot.w * rot.w;
		if ( lenSq < 1e-12f ) return global::Rotation.Identity;
		return rot.Normal;
	}

	/// <summary>
	/// Converts Unity UV (bottom-left origin) to s&amp;box / D3D UV (top-left origin).
	/// Paired with vertically flipped textures so Asset Browser previews and UVs both align.
	/// </summary>
	public static Vector2 Uv( float u, float v ) => new( u, 1f - v );

	/// <summary>
	/// Converts Unity texture ST (scaleX, scaleY, offsetX, offsetY) for flipped V textures.
	/// </summary>
	public static Vector4 ConvertTextureST( Vector2 scale, Vector2 offset )
		=> new( scale.x, scale.y, offset.x, 1f - scale.y - offset.y );

	/// <summary>Decodes an IEEE 754 binary16 (half-precision float) to float32 using pure bit ops.</summary>
	public static float HalfToFloat( ushort h )
	{
		uint sign = (uint)(h >> 15) << 31;
		uint exp = (uint)((h >> 10) & 0x1F);
		uint mant = (uint)(h & 0x3FF);

		uint bits;
		if ( exp == 0 )
		{
			if ( mant == 0 )
			{
				bits = sign;
			}
			else
			{
				// Subnormal half -> normalized float32
				exp = 127 - 14;
				while ( (mant & 0x400) == 0 )
				{
					mant <<= 1;
					exp--;
				}
				mant &= 0x3FF;
				bits = sign | (exp << 23) | (mant << 13);
			}
		}
		else if ( exp == 31 )
		{
			bits = sign | 0x7F800000u | (mant << 13);
		}
		else
		{
			bits = sign | ((exp + (127 - 15)) << 23) | (mant << 13);
		}

		return BitConverter.UInt32BitsToSingle( bits );
	}
}
