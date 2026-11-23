using System;
using System.Buffers.Binary;
using System.Text;

namespace NS2;

/// <summary>Authored states, inputs and layers in a Spark .animation_graph.</summary>
internal sealed partial class SparkAnimationGraph
{
	record Condition( int Input, int Comparison, double Value )
	{
		public bool Matches( double[] inputs ) => (uint)Input < inputs.Length && Comparison switch
		{
			1 => inputs[Input] == Value,
			2 => inputs[Input] != Value,
			3 => inputs[Input] < Value,
			4 => inputs[Input] <= Value,
			5 => inputs[Input] > Value,
			6 => inputs[Input] >= Value,
			_ => false
		};
	}

	enum ActionType { Keep, Play, Clear }

	record Node( string Label, ActionType Action, int Clip, bool WaitOnExit, bool WaitOnEnter, bool ExplicitEntryOnly,
		int EnterTag, int ExitTag, float Blend, float LabelBlend, int RateInput, float Rate, int[] Links, Condition[] Conditions );
	record Layer( Node[] Nodes, int Start, int Index )
	{
		public PoseParameters Parameters { get; } = new( Index );
	}

	internal record PoseParameters( int Index )
	{
		public string Current { get; } = $"spark_{Index}_clip";
		public string Time { get; } = $"spark_{Index}_time";
		public string Previous { get; } = $"spark_{Index}_previous";
		public string PreviousTime { get; } = $"spark_{Index}_previous_time";
		public string Weight { get; } = $"spark_{Index}_weight";
	}
	Layer[] layers = [];
	int[] inputTypes = [];
	string[] tags = [];
	string[] clips = [];
	string[] inputs = [];
	readonly Dictionary<int, string[]> values = [];

	// PropDynamic.lua sets "animation" once. Sandbox has no power grid, so use the powered state.
	// Resolve transitions at import time. Only props that actually change clips need a component.
	public (string[] Clips, int Loop) Resolve( string animation )
	{
		var input = new double[inputs.Length];
		for ( int i = 0; i < inputs.Length; i++ )
		{
			if ( inputs[i] == "powered" ) input[i] = 1;
			if ( inputs[i] == "animation" && values.TryGetValue( i, out var names ) )
				input[i] = Math.Max( 0, Array.IndexOf( names, animation ) );
		}

		var sequence = new List<string>();
		var visited = new Dictionary<int, int>();
		if ( layers.Length == 0 ) return ([], -1);
		var nodes = layers[0].Nodes;
		int index = layers[0].Start;
		while ( (uint)index < nodes.Length )
		{
			if ( !visited.TryAdd( index, sequence.Count ) )
				return (sequence.ToArray(), Math.Min( visited[index], sequence.Count - 1 ));

			var node = nodes[index];
			int next = node.Links.FirstOrDefault( i => (uint)i < nodes.Length && nodes[i].Conditions.All( c => c.Matches( input ) ), -1 );
			bool linked = next >= 0;
			if ( (uint)node.Clip < clips.Length && (!linked || node.WaitOnExit || nodes[next].WaitOnEnter) )
				sequence.Add( clips[node.Clip] );
			if ( !linked ) break;
			index = next;
		}
		return (sequence.ToArray(), sequence.Count - 1);
	}

	public static SparkAnimationGraph Read( byte[] bytes )
	{
		var r = new LevelReader( bytes );
		if ( !r.ReadBytes( 3 ).Span.SequenceEqual( "ANG"u8 ) )
			throw new InvalidDataException( "Invalid Spark animation graph header." );
		var version = r.ReadByte();
		if ( version is < 4 or > 9 )
			throw new InvalidDataException( $"Unsupported Spark animation graph version {version}." );

		var graph = new SparkAnimationGraph();
		while ( r.Position < bytes.Length )
		{
			r.End = bytes.Length;
			int chunk = r.ReadInt32();
			int length = r.ReadInt32();
			r.SeekChunk( r.Position, length );
			switch ( chunk )
			{
				case 1:
					graph.inputs = new string[r.ReadCount( 8 )];
					graph.inputTypes = new int[graph.inputs.Length];
					for ( int i = 0; i < graph.inputs.Length; i++ )
					{
						graph.inputTypes[i] = r.ReadInt32();
						graph.inputs[i] = ReadString( r );
					}
					break;
				case 2:
					graph.layers = new Layer[r.ReadCount( 4 )];
					for ( int layer = 0; layer < graph.layers.Length; layer++ )
					{
						var nodes = new Node[r.ReadCount( 4 )];
						int start = nodes.Length == 0 ? -1 : r.ReadInt32();
						for ( int i = 0; i < nodes.Length; i++ ) nodes[i] = ReadNode( r, version );
						graph.layers[layer] = new( nodes, start, layer );
					}
					break;
				case 4:
					while ( r.Position < r.End )
						graph.values[r.ReadInt32()] = ReadStrings( r );
					break;
				case 5:
					graph.clips = ReadStrings( r );
					break;
				case 6:
					graph.tags = ReadStrings( r );
					break;
			}
			r.Position = r.End;
		}
		return graph;
	}

	static Node ReadNode( LevelReader r, int version )
	{
		var label = ReadString( r );
		r.Skip( 8 ); // editor position
		bool waitOnExit = r.ReadInt32() != 0;
		bool waitOnEnter = r.ReadInt32() != 0;
		bool explicitOnly = version >= 9 && r.ReadInt32() != 0;
		int enterTag = version >= 8 ? r.ReadInt32() : -1;
		int exitTag = version >= 5 ? r.ReadInt32() : -1;
		if ( r.ReadInt32() != 1 ) throw new InvalidDataException( "Invalid Spark graph action count." );
		int action = r.ReadInt32();
		int clip = -1, rateInput = -1;
		float blend = 0, labelBlend = 0, rate = 1;
		if ( action == 1 )
		{
			clip = r.ReadInt32();
			blend = r.ReadSingle();
			labelBlend = r.ReadSingle();
			if ( version >= 7 )
			{
				rateInput = r.ReadInt32();
				rate = (float)BinaryPrimitives.ReadDoubleLittleEndian( r.ReadBytes( 8 ).Span );
			}
		}
		else if ( action is not (0 or 2) ) throw new InvalidDataException( $"Unknown Spark graph action {action}." );

		var links = new int[r.ReadCount( 4 )];
		for ( int i = 0; i < links.Length; i++ ) links[i] = r.ReadInt32();
		var conditions = new Condition[r.ReadCount( version < 6 ? 12 : 16 )];
		for ( int i = 0; i < conditions.Length; i++ )
		{
			int input = r.ReadInt32();
			int comparison = r.ReadInt32();
			double value = version < 6 ? r.ReadInt32() : BinaryPrimitives.ReadDoubleLittleEndian( r.ReadBytes( 8 ).Span );
			conditions[i] = new( input, comparison, value );
		}
		return new( label, (ActionType)action, clip, waitOnExit, waitOnEnter, explicitOnly, enterTag, exitTag, blend, labelBlend, rateInput, rate, links, conditions );
	}

	static string ReadString( LevelReader r ) => Encoding.UTF8.GetString( r.ReadBytes( r.ReadCount( 1 ) ).Span );

	static string[] ReadStrings( LevelReader r )
	{
		var strings = new string[r.ReadCount( 4 )];
		for ( int i = 0; i < strings.Length; i++ ) strings[i] = ReadString( r );
		return strings;
	}
}
