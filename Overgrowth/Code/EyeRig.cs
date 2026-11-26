using System;

namespace Overgrowth;

internal sealed class EyeRig
{
	readonly record struct Axis( Vector3 Origin, Vector3 Normal, Vector3 Tangent, float Positive, float Negative )
	{
		internal float Weight( Vector3 target )
		{
			var projected = target - Normal * target.Dot( Normal );
			float angle = Angle( (projected - Origin).Normal, Tangent );
			var limit = angle > 0f ? Positive : Negative;
			return limit == 0f ? 0f : MathF.CopySign( Math.Clamp( angle / limit, 0f, 1f ), angle );
		}
	}

	readonly record struct Eye( Axis Vertical, Axis Horizontal );
	readonly Eye right, left;
	internal bool SideFacing { get; }
	EyeRig( Eye right, Eye left, bool sideFacing ) => (this.right, this.left, SideFacing) = (right, left, sideFacing);

	static float Angle( Vector3 direction, Vector3 tangent ) => MathF.Asin( Math.Clamp( direction.Dot( tangent ), -1f, 1f ) );

	internal static EyeRig Load( XmlAsset character, Vector3 center, Transform head )
	{
		var morphs = character.Element( "morphs" );
		bool sideFacing = character.Element( "tags" )?.Attribute( "species" ) is "rabbit" or "rat";
		var neutral = morphs?.Element( "look_c" );
		var up = morphs?.Element( "look_u" );
		var down = morphs?.Element( "look_d" );
		var positive = morphs?.Element( sideFacing ? "look_f" : "look_l" );
		var negative = morphs?.Element( sideFacing ? "look_b" : "look_r" );
		if ( new[] { neutral, up, down, positive, negative }.Any( m => m?.Number( "start_x" ) is null ) ) return null;

		Eye Build( float side )
		{
			Vector3 Point( XmlAsset morph, string key )
			{
				var point = new Vector3( -morph.Number( key + "_y" ).Value, morph.Number( key + "_x" ).Value * side, morph.Number( key + "_z" ).Value );
				return head.PointToLocal( point * 40f - center ) / 40f;
			}

			var start = Point( neutral, "start" );
			var forward = (Point( neutral, "end" ) - start).Normal;
			Axis Plane( XmlAsset high, XmlAsset low )
			{
				var a = Point( high, "end" );
				var b = Point( low, "end" );
				var normal = (b - start).Cross( a - start ).Normal;
				var tangent = normal.Cross( forward ).Normal;
				return new( start, normal, tangent, Angle( (a - Point( high, "start" )).Normal, tangent ), Angle( (b - Point( low, "start" )).Normal, tangent ) );
			}
			return new( Plane( up, down ), Plane( positive, negative ) );
		}
		var right = Build( 1f );
		return new( right, sideFacing ? Build( -1f ) : right, sideFacing );
	}

	internal Vector3 Evaluate( Vector3 target, bool useLeft )
	{
		var eye = useLeft ? left : right;
		var horizontal = eye.Horizontal.Weight( target.Normal );
		return new( SideFacing ? horizontal : 0f, SideFacing ? 0f : -horizontal, eye.Vertical.Weight( target.Normal ) );
	}
}
