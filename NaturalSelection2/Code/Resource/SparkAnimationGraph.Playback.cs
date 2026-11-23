using Sandbox.Animation;
using System;

namespace NS2;

internal sealed partial class SparkAnimationGraph
{
	internal sealed class Playback
	{
		readonly SparkAnimationGraph graph;
		readonly double[] inputs;
		readonly ModelLoader model;
		readonly int[] sequences;
		readonly float[] durations;
		readonly List<(float Time, string Name)> markers = [];
		readonly Track[] tracks;
		float time;

		internal readonly record struct Sample( int Clip, float Start, float Rate )
		{
			public readonly float Seconds( float time ) => (time - Start) * Rate;
		}

		internal readonly record struct LayerPose( Sample Current, Sample Previous, float Blend )
		{
			internal void Write( PoseParameters layer, GraphParameters output, float time )
			{
				float weight = Blend > 0 ? Math.Clamp( (time - Current.Start) / Blend, 0, 1 ) : 1;
				var previous = Previous;
				if ( layer.Index == 0 )
				{
					if ( previous.Clip < 0 ) weight = 1;
				}
				else if ( weight < 1 && Current.Clip >= 0 )
				{
					// Spark accumulates both action clips onto a copy of the base, then blends back toward the base.
					weight = 1 - weight;
				}
				else
				{
					previous = previous with { Clip = -1 };
					weight = 1;
				}
				output.Set( layer.Current, (float)(Current.Clip + 1) );
				output.Set( layer.Time, Current.Seconds( time ) );
				output.Set( layer.Weight, weight );
				output.Set( layer.Previous, (float)(previous.Clip + 1) );
				if ( previous.Clip >= 0 ) output.Set( layer.PreviousTime, previous.Seconds( time ) );
			}
		}

		internal LayerPose GetPose( int layer )
		{
			ref var track = ref tracks[layer];
			return new( track.Current, track.Previous, track.Blend );
		}

		struct Track
		{
			public int State;
			public Sample Current, Previous;
			public float Blend, Remaining;
		}

		internal Playback( SparkAnimationGraph graph, ModelLoader model )
		{
			this.graph = graph;
			inputs = new double[graph.inputs.Length];
			this.model = model;
			sequences = Array.ConvertAll( graph.clips, model.SequenceIndex );
			durations = Array.ConvertAll( sequences, model.SequenceDuration );
			tracks = new Track[graph.layers.Length];
			foreach ( var layer in graph.layers )
			{
				ref var track = ref tracks[layer.Index];
				track.Current = track.Previous = new( -1, 0, 1 );
				track.State = -1;
				if ( layer.Start >= 0 ) Enter( layer, ref track, layer.Start, false, null );
			}
		}

		internal void Set( string input, double value )
		{
			int i = Array.IndexOf( graph.inputs, input );
			if ( i >= 0 ) inputs[i] = value;
		}

		internal void Set( string input, string value )
		{
			int i = Array.IndexOf( graph.inputs, input );
			if ( i >= 0 ) inputs[i] = Math.Max( 0, Array.IndexOf( graph.values[i], value ) );
		}

		internal void Update( float elapsed, GraphParameters output = null, Action<string> tag = null )
		{
			float before = time;
			time = elapsed;
			Span<int> visited = stackalloc int[32];
			foreach ( var layer in graph.layers )
			{
				ref var track = ref tracks[layer.Index];
				if ( track.State < 0 ) continue;
				if ( tag is not null )
				{
					EmitMarkers( track.Current, before, output, tag );
					if ( time - track.Current.Start <= track.Blend ) EmitMarkers( track.Previous, before, output, tag );
				}
				track.Remaining = 0;
				if ( track.Current.Clip >= 0 )
				{
					float duration = durations[track.Current.Clip];
					float start = track.Current.Seconds( before );
					float end = track.Current.Seconds( time );
					float wrapped = duration > 0 ? start % duration : 0;
					track.Remaining = track.Current.Rate == 0 ? float.PositiveInfinity :
						(duration - end - (start - wrapped)) / track.Current.Rate;
				}
			}

			foreach ( var layer in graph.layers )
			{
				ref var track = ref tracks[layer.Index];
				if ( track.State < 0 ) continue;
				int count = 0;
				while ( Next( layer, track, out int dest, out bool byLabel ) )
				{
					Enter( layer, ref track, dest, byLabel, tag );
					if ( count == visited.Length || visited[..count].Contains( dest ) ) break;
					visited[count++] = dest;
				}

				if ( output is not null ) GetPose( layer.Index ).Write( layer.Parameters, output, time );
			}
		}

		void EmitMarkers( Sample sample, float before, GraphParameters pose, Action<string> emit )
		{
			if ( sample.Clip < 0 ) return;
			model.EmitSequenceTags( sequences[sample.Clip], pose, sample.Seconds( before ), sample.Seconds( time ), markers, emit );
		}

		bool Next( Layer layer, Track track, out int dest, out bool byLabel )
		{
			var from = layer.Nodes[track.State];
			bool waiting = false;
			byLabel = false;
			foreach ( int i in from.Links )
			{
				if ( !Eligible( layer.Nodes[i] ) ) continue;
				dest = i;
				return true;
			}

			if ( !waiting && from.Label.Length > 0 )
			{
				byLabel = true;
				for ( int i = 0; i < layer.Nodes.Length; i++ )
				{
					var to = layer.Nodes[i];
					if ( i != track.State && !to.ExplicitEntryOnly && to.Label == from.Label && Eligible( to ) )
					{
						dest = i;
						return true;
					}
				}
			}
			dest = -1;
			return false;

			bool Eligible( Node to )
			{
				foreach ( var condition in to.Conditions )
					if ( !condition.Matches( inputs ) ) return false;
				if ( (from.WaitOnExit || to.WaitOnEnter) && to.Blend * Rate( to ) < track.Remaining )
				{
					waiting = true;
					return false;
				}
				return true;
			}
		}

		float Rate( Node node ) => node.RateInput >= 0 ? (float)inputs[node.RateInput] : node.Rate;

		void Enter( Layer layer, ref Track track, int dest, bool byLabel, Action<string> tag )
		{
			var node = layer.Nodes[dest];
			if ( track.State >= 0 ) Emit( layer.Nodes[track.State].ExitTag );
			track.State = dest;
			Emit( node.EnterTag );
			if ( node.Action == ActionType.Clear ) track.Current = track.Current with { Clip = -1, Rate = 1 };
			if ( node.Action != ActionType.Play ) return;
			if ( track.Current.Start != time && track.Current.Clip >= 0 ) track.Previous = track.Current;
			float rate = Rate( node );
			track.Current = new( sequences[node.Clip] >= 0 ? node.Clip : -1, time, rate );
			track.Blend = rate == 0 ? 0 : (byLabel ? node.LabelBlend : node.Blend) / rate;
			track.Remaining = durations[node.Clip] / rate;

			void Emit( int index )
			{
				if ( (uint)index < graph.tags.Length ) tag?.Invoke( graph.tags[index] );
			}
		}
	}
}
