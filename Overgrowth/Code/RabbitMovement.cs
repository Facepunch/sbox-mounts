using System;
using System.IO;
using Sandbox.Animation;

namespace Overgrowth;

// The pose, contact weights and footstep events all follow the same authored blend tree.
internal sealed class RabbitMovement
{
	internal readonly record struct Controls( float Speed, float Height, float Threat, float Close = 0f );
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

	internal GraphBuilder.Node Build( GraphBuilder graph, Func<string, GraphBuilder.Node> parameter, GraphBuilder.Node phase, bool mirrored )
	{
		if ( clip is not null ) return graph.TimeControlledClip( clip.ForSide( mirrored ).Animation, phase, new() { SampleRootMotion = false } );
		var blend = graph.BlendSpace( parameter( label ), looping: false );
		foreach ( var child in children ) blend.Add( child.Coordinate, child.Motion.Build( graph, parameter, phase, mirrored ) );
		return blend;
	}

	(Child First, Child Second, float Weight) Inputs( Controls controls )
	{
		float coord = label switch { "speed_coord" => controls.Speed, "tall_coord" => controls.Height, "threat_coord" => controls.Threat, "close_coord" => controls.Close, _ => throw new InvalidDataException( label ) };
		var hi = 0;
		while ( hi < children.Length - 1 && children[hi].Coordinate < coord ) hi++;
		var a = children[Math.Max( 0, hi - 1 )];
		var b = children[hi];
		if ( coord <= a.Coordinate ) return (a, null, 0f);
		if ( coord >= b.Coordinate ) return (b, null, 0f);
		return (a, b, (coord - a.Coordinate) / (b.Coordinate - a.Coordinate));
	}

	internal readonly record struct PoseInput( RabbitClip Clip, float Weight );

	// Emit the active leaves followed by their parent blends, once per pose.
	internal void CollectPoseInputs( Controls controls, bool mirrored, List<PoseInput> inputs )
	{
		if ( clip is not null )
		{
			inputs.Add( new( clip.ForSide( mirrored ), 0f ) );
			return;
		}
		var (a, b, weight) = Inputs( controls );
		a.Motion.CollectPoseInputs( controls, mirrored, inputs );
		if ( b is null ) return;
		b.Motion.CollectPoseInputs( controls, mirrored, inputs );
		inputs.Add( new( null, weight ) );
	}

	internal Sample Evaluate( Controls controls, float phase, bool mirrored )
	{
		if ( clip?.ForSide( mirrored ) is { } pose )
			return new( 1f / pose.Duration, 0f, FootPose.Sample( pose.Feet?[0], phase ), FootPose.Sample( pose.Feet?[1], phase ), pose );
		var (a, b, w) = Inputs( controls );
		Sample EvaluateChild( Child child )
		{
			var sample = child.Motion.Evaluate( controls, phase, mirrored );
			float frequency = sample.Frequency * (child.GroundSpeed.HasValue ? controls.Speed / child.GroundSpeed.Value : 1f);
			float rate = Math.Max( 2f, frequency );
			float t = phase % 0.5f / rate;
			return sample with { Frequency = frequency, Lift = sample.Lift + child.Bounce * 392f * t * (0.5f / rate - t) };
		}
		var first = EvaluateChild( a );
		if ( b is null ) return first;
		var second = EvaluateChild( b );
		return new( first.Frequency + (second.Frequency - first.Frequency) * w, first.Lift + (second.Lift - first.Lift) * w,
			FootPose.Blend( first.Left, second.Left, w ), FootPose.Blend( first.Right, second.Right, w ), w <= 0.5f ? first.Events : second.Events );
	}
}
