using Sandbox.Animation;
using System;

namespace NS2;

internal sealed partial class SparkAnimationGraph
{
	// Spark supplies absolute sequence times. AG2 samples and blends the two poses per layer.
	internal Graph CompilePlayback( GraphBuilder graph, Func<string, GraphBuilder.Node, GraphBuilder.Node, GraphBuilder.Node> sequence, string path )
	{
		var controls = layers.Select( layer => (
			Current: graph.FloatParameter( layer.Parameters.Current ), Time: graph.FloatParameter( layer.Parameters.Time ),
			Previous: graph.FloatParameter( layer.Parameters.Previous ), PreviousTime: graph.FloatParameter( layer.Parameters.PreviousTime ),
			Weight: graph.FloatParameter( layer.Parameters.Weight )) ).ToArray();

		var output = graph.ReferencePose();
		foreach ( var layer in layers )
		{
			if ( layer.Start < 0 ) continue;
			var control = controls[layer.Index];
			var used = layer.Nodes.Select( n => n.Clip ).Where( c => c >= 0 ).Distinct().Order().ToArray();
			var current = Sample( control.Current, control.Time, output );
			if ( layer.Index == 0 )
				output = graph.Blend( Sample( control.Previous, control.PreviousTime, output ), current, control.Weight );
			else
				output = graph.Blend( output, Sample( control.Previous, control.PreviousTime, current ), control.Weight );

			GraphBuilder.Node Sample( GraphBuilder.Node selection, GraphBuilder.Node seconds, GraphBuilder.Node incoming )
			{
				var options = graph.BlendSpace( selection ).Add( 0, incoming );
				foreach ( int clip in used )
					options.Add( clip + 1, sequence( clips[clip], incoming, seconds ) ?? incoming );
				return options;
			}
		}
		return graph.Create( output, path );
	}
}
