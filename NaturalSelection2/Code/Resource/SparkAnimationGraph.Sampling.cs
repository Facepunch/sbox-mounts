using Sandbox.Animation;
using System;

namespace NS2;

internal sealed partial class SparkAnimationGraph
{
	// Spark supplies absolute sequence times. AG2 samples and blends the two poses per layer.
	internal Graph CompilePlayback( GraphBuilder graph, Func<string, GraphBuilder.Node, GraphBuilder.Node, GraphBuilder.Node> sequence, string path )
	{
		var controls = layers.Select( layer => (
			Current: graph.FloatParameter( layer.Current ), Time: graph.FloatParameter( layer.Time ),
			Previous: graph.FloatParameter( layer.Previous ), PreviousTime: graph.FloatParameter( layer.PreviousTime ),
			Weight: graph.FloatParameter( layer.Weight )) ).ToArray();

		var output = graph.ReferencePose();
		foreach ( var layer in layers )
		{
			if ( layer.Start < 0 ) continue;
			var control = controls[layer.Index];
			var used = layer.Nodes.Select( n => n.Clip ).Where( c => c >= 0 ).Distinct().Order().ToArray();
			var previous = Sample( control.Previous, control.PreviousTime );
			var current = Sample( control.Current, control.Time );
			output = graph.Blend( previous, current, control.Weight );

			GraphBuilder.Node Sample( GraphBuilder.Node selection, GraphBuilder.Node seconds )
			{
				var options = graph.BlendSpace( selection ).Add( 0, output );
				foreach ( int clip in used )
					options.Add( clip + 1, sequence( clips[clip], output, seconds ) ?? output );
				return options;
			}
		}
		return graph.Create( output, path );
	}
}
