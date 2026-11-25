namespace Diesel;

using Matrix = System.Numerics.Matrix4x4;
using Vec = System.Numerics.Vector3;

/// <summary>
/// Evaluates the sky underlay's sun rig the way the source game does. The underlay scene
/// (<c>core/environments/skies/default/default.scene</c>) declares two non-playing animation groups,
/// <c>sun_vertical</c> on <c>d_sun_vertical</c> and <c>sun_horizontal</c> on <c>d_sun_horizontal</c>.
/// The environment's <c>others/sun_anim</c> and <c>others/sun_anim_x</c> (0–1) select a time in each
/// group's embedded rotation controller. <c>d_sun</c> (child of both) is then the sun position on the
/// sky dome, and the world's <c>sky_rot</c> spins the dome about up.
/// </summary>
static class SunDirection
{
	static readonly ulong Horizontal = IdString.Hash( "d_sun_horizontal" );
	static readonly ulong Vertical = IdString.Hash( "d_sun_vertical" );

	/// <summary>Unit direction from the world origin towards the sun, in s&amp;box axes. Null if unavailable.</summary>
	public static Vector3? Evaluate( BundleDatabase database, string underlay, float vertical, float horizontal, float skyRotation )
	{
		if ( !float.IsFinite( skyRotation ) || RigDirection( database, underlay, vertical, horizontal ) is not { } sun ) return null;
		return ToSbox( sun, skyRotation );
	}

	/// <summary>
	/// Spin a Diesel-space dome direction by the world's <c>sky_rot</c> and convert to s&amp;box axes.
	/// <c>sky_rot</c> turns the dome clockwise seen from above (negative about Diesel +Z): on Counterfeit
	/// this puts the sun within ~10° of the sun painted on the level's own sky dome, while the opposite sign
	/// is ~35° off and ignoring it ~105° off (<c>Tests/SkyDump.cs</c>).
	/// </summary>
	public static Vector3 ToSbox( Vec sun, float skyRotation )
	{
		sun = Vec.Normalize( Vec.Transform( sun, Matrix.CreateRotationZ( -skyRotation * MathF.PI / 180f ) ) );
		// Coordinates.Position's axis swap, without the unit scale.
		return new Vector3( sun.Y, -sun.X, sun.Z );
	}

	/// <summary>Unit direction to <c>d_sun</c> in the underlay's own (Diesel) space, before any dome rotation.</summary>
	public static Vec? RigDirection( BundleDatabase database, string underlay, float vertical, float horizontal )
	{
		if ( string.IsNullOrEmpty( underlay ) || !float.IsFinite( vertical ) || !float.IsFinite( horizontal ) ) return null;
		if ( database.Find( "model", underlay ) is not { } bytes ) return null;
		var model = new ModelFile( bytes );
		var controllers = model.Controllers( new HashSet<ulong> { Horizontal, Vertical } );
		if ( !controllers.TryGetValue( Horizontal, out var h ) || !controllers.TryGetValue( Vertical, out var v ) ) return null;
		var frozen = new Dictionary<ulong, AnimationFile.Track>
		{
			[Horizontal] = Freeze( h, horizontal ),
			[Vertical] = Freeze( v, vertical ),
		};
		var sun = model.NodePose( model.Pose( null, 0, frozen ), "d_sun" ).Translation;
		if ( sun.LengthSquared() < 1e-6f || !float.IsFinite( sun.LengthSquared() ) ) return null;
		return Vec.Normalize( sun );
	}

	/// <summary>Collapse a controller to its pose at a normalized time, so each group gets its own time in one Pose call.</summary>
	static AnimationFile.Track Freeze( AnimationFile.Track track, float fraction )
	{
		var times = track.RotationTimes.Length > 0 ? track.RotationTimes : track.PositionTimes;
		float time = times.Length == 0 ? 0 : times[0] + Math.Clamp( fraction, 0, 1 ) * (times[^1] - times[0]);
		Matrix.Decompose( track.Sample( time, Matrix.Identity ), out _, out var rotation, out var position );
		bool moves = track.Positions.Length > 0;
		return new AnimationFile.Track( moves ? new[] { 0f } : Array.Empty<float>(), moves ? new[] { position } : Array.Empty<Vec>(),
			new[] { 0f }, new[] { rotation } );
	}
}
