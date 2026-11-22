using System;
using System.IO;
using Sandbox.Animation;

namespace Overgrowth;

// The pose, contact weights and footstep events all follow the same authored blend tree.
internal sealed class RabbitMovement
{
	internal readonly record struct Controls( float Speed, float Height, float Threat );
	internal readonly record struct Sample( float Frequency, float Lift, FootPose Left, FootPose Right, RabbitClip Events );
	sealed record Child( float Coordinate, float? GroundSpeed, float Bounce, RabbitMovement Motion );

	readonly RabbitClip clip;
	readonly string label;
	readonly Child[] children;
	RabbitMovement( RabbitClip clip ) => this.clip = clip;
	RabbitMovement( string label, Child[] children ) => (this.label, this.children) = (label, children);

	internal static RabbitMovement Load( BaseFileSystem fs, string path, Func<string, RabbitClip> load )
	{
		if ( path.EndsWith( ".anm" ) ) return new( load( Path.GetFileNameWithoutExtension( path ) ) );
		var group = XmlAsset.Read( fs, path, "SyncedAnimationGroup" );
		var children = group.Element( "Animations" ).Elements( "Animation" ).Select( x => new Child(
			x.Number( "coord" ).Value, x.Number( "ground_speed" ), x.Number( "run_bounce" ) ?? 0f,
			Load( fs, x.Attribute( "path" ), load ) ) ).OrderBy( x => x.Coordinate ).ToArray();
		return new( group.Element( "CoordLabel" ).Value, children );
	}

	internal GraphBuilder.Node Build( GraphBuilder graph, Func<string, GraphBuilder.Node> parameter, GraphBuilder.Node phase )
	{
		if ( clip is not null ) return graph.TimeControlledClip( clip.Animation, phase, new() { SampleRootMotion = false } );
		var blend = graph.BlendSpace( parameter( label ), looping: false );
		foreach ( var child in children ) blend.Add( child.Coordinate, child.Motion.Build( graph, parameter, phase ) );
		return blend;
	}

	internal Sample Evaluate( Controls controls, float phase )
	{
		if ( clip is not null )
			return new( 1f / clip.Duration, 0f, FootPose.Sample( clip.Feet[0], phase ), FootPose.Sample( clip.Feet[1], phase ), clip );
		float coord = label switch { "speed_coord" => controls.Speed, "tall_coord" => controls.Height, "threat_coord" => controls.Threat, _ => throw new InvalidDataException( label ) };
		var hi = 0;
		while ( hi < children.Length - 1 && children[hi].Coordinate < coord ) hi++;
		var a = children[Math.Max( 0, hi - 1 )];
		var b = children[hi];
		Sample EvaluateChild( Child child )
		{
			var sample = child.Motion.Evaluate( controls, phase );
			float frequency = sample.Frequency * (child.GroundSpeed.HasValue ? controls.Speed / child.GroundSpeed.Value : 1f);
			float rate = Math.Max( 2f, frequency );
			float t = phase % 0.5f / rate;
			return sample with { Frequency = frequency, Lift = sample.Lift + child.Bounce * 392f * t * (0.5f / rate - t) };
		}
		if ( coord <= a.Coordinate ) return EvaluateChild( a );
		if ( coord >= b.Coordinate ) return EvaluateChild( b );
		float w = (coord - a.Coordinate) / (b.Coordinate - a.Coordinate);
		var first = EvaluateChild( a );
		var second = EvaluateChild( b );
		return new( first.Frequency + (second.Frequency - first.Frequency) * w, first.Lift + (second.Lift - first.Lift) * w,
			FootPose.Blend( first.Left, second.Left, w ), FootPose.Blend( first.Right, second.Right, w ), w <= 0.5f ? first.Events : second.Events );
	}
}
