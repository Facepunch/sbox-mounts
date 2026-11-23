using Sandbox.Animation;
using System;
using State = Sandbox.Animation.GraphBuilder.StateMachineBuilder.State;

namespace NS2;

internal sealed partial class SparkAnimationGraph
{
	internal string PoseInputName( string name ) => inputs.Contains( name, StringComparer.OrdinalIgnoreCase ) ? $"pose_{name}" : name;

	// Spark tries the explicit links first, then other states bearing the same label.
	internal Graph Compile( GraphBuilder graph, Func<string, GraphBuilder.Node, GraphBuilder.Node> sequence, string path )
	{
		var parameters = new GraphBuilder.Node[inputs.Length];
		for ( int i = 0; i < inputs.Length; i++ )
			parameters[i] = inputTypes[i] switch
			{
				1 => graph.BoolParameter( inputs[i] ),
				2 => graph.AddControlParameter( inputs[i], GraphBuilder.NodeKind.Id, new() { Options = values[i], DefaultID = values[i].FirstOrDefault() ?? "" } ),
				_ => graph.FloatParameter( inputs[i] )
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
			if ( layer.Start < 0 ) continue;
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
				GraphBuilder.Node explicitMatch = null;
				foreach ( var dest in from.Links )
				{
					Link( dest, false );
					var target = layer.Nodes[dest];
					if ( from.Clip >= 0 && (from.WaitOnExit || target.WaitOnEnter) )
						explicitMatch = explicitMatch is null ? conditions[dest] : graph.Or( explicitMatch, conditions[dest] );
				}
				if ( from.Label.Length == 0 ) continue;
				for ( int j = 0; j < states.Length; j++ )
					if ( j != i && !layer.Nodes[j].ExplicitEntryOnly && layer.Nodes[j].Label == from.Label && !from.Links.Contains( j ) ) Link( j, true );

				void Link( int dest, bool byLabel )
				{
					if ( i == dest ) return;
					var to = layer.Nodes[dest];
					float blend = byLabel ? to.LabelBlend : to.Blend;
					float duration = to.Rate != 0 ? blend / to.Rate : 0;
					var rate = to.RateInput < 0 ? null : parameters[to.RateInput];
					var durationOverride = rate is null ? null : graph.Divide( graph.Constant( blend ), rate );
					var test = conditions[dest];
					if ( from.Clip >= 0 && (from.WaitOnExit || to.WaitOnEnter) )
						test = graph.And( test, machine.Completed( states[i], to.Blend * to.Rate, rate is null ? null : graph.Multiply( rate, to.Blend ) ) );
					if ( byLabel && explicitMatch is not null ) test = graph.And( test, graph.Not( explicitMatch ) );
					machine.AddTransition( states[i], states[dest], test, new GraphBuilder.TransitionSettings { Duration = duration, DurationOverride = durationOverride } );
				}
			}
			output = output is null ? machine.Build() : graph.Layers( output ).AddLayer( machine );
		}
		return graph.Create( output ?? graph.ReferencePose(), path );
	}
}
