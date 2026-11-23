using Sandbox.Animation;
using System;
using State = Sandbox.Animation.GraphBuilder.StateMachineBuilder.State;

namespace NS2;

internal sealed partial class SparkAnimationGraph
{
	// Spark tries the explicit links first, then other states bearing the same label.
	internal Graph Compile( GraphBuilder graph, Dictionary<string, GraphBuilder.Node> poseParameters, Func<string, GraphBuilder.Node, GraphBuilder.Node> sequence, string path )
	{
		var parameters = new GraphBuilder.Node[inputs.Length];
		for ( int i = 0; i < inputs.Length; i++ )
			parameters[i] = inputTypes[i] switch
			{
				1 => graph.BoolParameter( inputs[i] ),
				2 => graph.IdParameter( inputs[i], values[i] ),
				_ => poseParameters.TryGetValue( inputs[i], out var input ) ? input : graph.FloatParameter( inputs[i] )
			};

		GraphBuilder.Node ConditionFor( Condition c )
		{
			var input = parameters[c.Input];
			if ( inputTypes[c.Input] == 2 )
				return graph.IdCompare( input, values[c.Input][(int)c.Value], c.Comparison == 1 );
			if ( inputTypes[c.Input] == 1 )
				return (c.Value != 0) == (c.Comparison == 1) ? input : graph.Not( input );
			var op = c.Comparison switch
			{
				1 or 2 => GraphBuilder.FloatComparison.NearEqual,
				3 => GraphBuilder.FloatComparison.LessThan,
				4 => GraphBuilder.FloatComparison.LessThanEqual,
				5 => GraphBuilder.FloatComparison.GreaterThan,
				6 => GraphBuilder.FloatComparison.GreaterThanEqual,
				_ => throw new InvalidDataException( $"Unknown Spark comparison {c.Comparison}" )
			};
			var test = graph.Compare( input, op, (float)c.Value );
			return c.Comparison == 2 ? graph.Not( test ) : test;
		}

		GraphBuilder.Node output = null;
		foreach ( var layer in layers )
		{
			if ( layer.Nodes.Length == 0 ) continue;
			var machine = graph.StateMachine();
			var states = new State[layer.Nodes.Length];
			var conditions = new GraphBuilder.Node[states.Length];
			for ( int i = 0; i < states.Length; i++ )
			{
				var node = layer.Nodes[i];
				var pose = node.Clip >= 0 ? sequence( clips[node.Clip], output ) : null;
				if ( pose is not null )
				{
					if ( node.RateInput >= 0 ) pose = graph.SpeedScale( pose, parameters[node.RateInput] );
					else if ( node.Rate != 1f ) pose = graph.SpeedScale( pose, node.Rate );
				}
				states[i] = machine.AddState( pose, new GraphBuilder.StateMachineBuilder.StateSettings
				{
					IsOffState = pose is null,
					EntryEvents = (uint)node.EnterTag < tags.Length ? [tags[node.EnterTag]] : [],
					ExitEvents = (uint)node.ExitTag < tags.Length ? [tags[node.ExitTag]] : []
				} );
				conditions[i] = node.Conditions.Length == 0 ? graph.Constant( true ) : graph.And( node.Conditions.Select( ConditionFor ).ToArray() );
			}
			machine.SetDefaultState( states[layer.Start] );
			for ( int i = 0; i < states.Length; i++ )
			{
				var from = layer.Nodes[i];
				foreach ( var dest in from.Links ) Link( dest, false );
				if ( from.Label.Length == 0 ) continue;
				for ( int j = 0; j < states.Length; j++ )
					if ( j != i && !layer.Nodes[j].ExplicitEntryOnly && layer.Nodes[j].Label == from.Label && !from.Links.Contains( j ) ) Link( j, true );

				void Link( int dest, bool byLabel )
				{
					if ( i == dest ) return;
					var to = layer.Nodes[dest];
					var duration = byLabel ? to.LabelBlend : to.Blend;
					if ( to.RateInput < 0 ) duration = to.Rate > 0 ? duration / to.Rate : 0;
					var test = conditions[dest];
					if ( from.Clip >= 0 && (from.WaitOnExit || to.WaitOnEnter) )
						test = graph.And( test, machine.Completed( states[i], duration ) );
					machine.AddTransition( states[i], states[dest], test, new GraphBuilder.TransitionSettings { Duration = duration } );
				}
			}
			output = output is null ? machine.Build() : graph.Layers( output ).AddLayer( machine );
		}
		return graph.Create( output ?? graph.ReferencePose(), path );
	}
}
