using System;
using Nv2 = System.Numerics.Vector2;
using Nv3 = System.Numerics.Vector3;
using NQuat = System.Numerics.Quaternion;
using NMat = System.Numerics.Matrix4x4;

namespace Magicka;

/// <summary>
/// Magicka's content is XNA: right handed, Y up, -Z forward, clockwise front faces. s&amp;box is
/// X forward, Y left, Z up with counter-clockwise front faces. The map below is a rotation, so
/// winding survives: -Z forward becomes +X, +X right becomes -Y, +Y up becomes +Z.
/// </summary>
internal static class MagickaSpace
{
	/// <summary>
	/// Magicka is metric and s&amp;box units are inches. Characters come out about 0.85 of the
	/// citizen's height, which matches Magicka's stocky proportions next to other content.
	/// </summary>
	public const float UnitScale = 30.6f;

	private static readonly NMat ToWorldMatrix = new(
		0, 0, -1, 0,
		-1, 0, 0, 0,
		0, 1, 0, 0,
		0, 0, 0, 1 );

	private static readonly NMat ToWorldTransposed = NMat.Transpose( ToWorldMatrix );

	public static Vector3 ToWorld( Nv3 v ) => new( -v.Z, -v.X, v.Y );

	public static Vector3 Position( Nv3 v ) => ToWorld( v ) * UnitScale;

	public static Vector2 ToWorld( Nv2 v ) => new( v.X, v.Y );

	/// <summary>The same axis map applied to a rotation: M * R * M^T.</summary>
	public static Rotation ToWorld( NQuat q )
	{
		var rotation = NMat.CreateFromQuaternion( q );
		var mapped = ToWorldMatrix * rotation * ToWorldTransposed;
		var result = NQuat.CreateFromRotationMatrix( mapped );
		return new Rotation( result.X, result.Y, result.Z, result.W );
	}

	public static Rotation ToWorld( NMat transform )
	{
		if ( NMat.Decompose( transform, out _, out var rotation, out _ ) )
			return ToWorld( rotation );

		return Rotation.Identity;
	}

	/// <summary>XNA stores the translation in the fourth row.</summary>
	public static Nv3 Translation( NMat transform ) => new( transform.M41, transform.M42, transform.M43 );
}
