using System;
using System.Buffers.Binary;
using System.Text;

namespace NS2;

/// <summary>The base layer of a Spark .animation_graph, as used by prop_dynamic.</summary>
internal sealed class PropAnimationGraph
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

	record Node( int Clip, bool WaitOnExit, bool WaitOnEnter, int[] Links, Condition[] Conditions );
	Node[] nodes = [];
	string[] clips = [];
	string[] inputs = [];
	readonly Dictionary<int, string[]> values = [];
	int start;

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
		int index = start;
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

	public static PropAnimationGraph Read( byte[] bytes )
	{
		var r = new LevelReader( bytes );
		if ( !r.ReadBytes( 3 ).Span.SequenceEqual( "ANG"u8 ) )
			throw new InvalidDataException( "Invalid Spark animation graph header." );
		var version = r.ReadByte();
		if ( version is < 4 or > 9 )
			throw new InvalidDataException( $"Unsupported Spark animation graph version {version}." );

		var graph = new PropAnimationGraph();
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
					for ( int i = 0; i < graph.inputs.Length; i++ )
					{
						r.Skip( 4 ); // input type
						graph.inputs[i] = ReadString( r );
					}
					break;
				case 2:
					if ( r.ReadCount( 4 ) == 0 ) break;
					graph.nodes = new Node[r.ReadCount( 4 )];
					if ( graph.nodes.Length == 0 ) break;
					graph.start = r.ReadInt32();
					for ( int i = 0; i < graph.nodes.Length; i++ )
						graph.nodes[i] = ReadNode( r, version );
					break;
				case 4:
					while ( r.Position < r.End )
						graph.values[r.ReadInt32()] = ReadStrings( r );
					break;
				case 5:
					graph.clips = ReadStrings( r );
					break;
			}
			r.Position = r.End;
		}
		return graph;
	}

	static Node ReadNode( LevelReader r, int version )
	{
		r.Skip( r.ReadCount( 1 ) ); // editor label
		r.Skip( 8 ); // editor position
		bool waitOnExit = r.ReadInt32() != 0;
		bool waitOnEnter = r.ReadInt32() != 0;
		if ( version >= 9 ) r.Skip( 4 ); // disallow label entry
		if ( version >= 8 ) r.Skip( 4 ); // exit tag
		if ( version >= 5 ) r.Skip( 4 ); // enter tag
		if ( r.ReadInt32() != 1 ) throw new InvalidDataException( "Invalid Spark graph action count." );
		int action = r.ReadInt32();
		int clip = -1;
		if ( action == 1 )
		{
			clip = r.ReadInt32();
			r.Skip( 8 ); // transition and label blend times
			if ( version >= 7 ) r.Skip( 12 ); // rate input and constant (1 for shipped props)
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
		return new( clip, waitOnExit, waitOnEnter, links, conditions );
	}

	static string ReadString( LevelReader r ) => Encoding.UTF8.GetString( r.ReadBytes( r.ReadCount( 1 ) ).Span );

	static string[] ReadStrings( LevelReader r )
	{
		var strings = new string[r.ReadCount( 4 )];
		for ( int i = 0; i < strings.Length; i++ ) strings[i] = ReadString( r );
		return strings;
	}
}
