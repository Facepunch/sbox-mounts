using System;
using Halo2.Blam;
using Sandbox.Animation;
using Sandbox.Mounting;
using GraphNode = Sandbox.Animation.GraphBuilder.Node;

namespace Halo2;

public partial class Halo2ModelLoader
{
	// A graph is a loadable child of its model: model.vmdl/combat/rifle/br.ag.
	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = default;
		if ( graph is null || !name.EndsWith( ".ag", StringComparison.OrdinalIgnoreCase ) ) return null;
		var context = name[..^3].Split( '/' );
		if ( context.Length != 3 ) return null;
		type = ResourceType.AnimationGraph;
		return new GraphLoader( context );
	}

	sealed class GraphLoader( string[] context ) : ResourceLoader<Halo2Mount>
	{
		protected override object Load() => ((Halo2ModelLoader)Parent).BuildGraph( Path, context );
	}

	Skeleton skeleton;
	Transform[] bindPose;
	int[] nodeMap;
	readonly Dictionary<int, Clip> graphClips = new();
	readonly Dictionary<int, string> clipMasks = new();

	IEnumerable<int> Rows( int block, int stride )
	{
		int count = mapFile.ReadInt32( block );
		int address = mapFile.VaToOffset( mapFile.ReadInt32( block + 4 ) );
		for ( int i = 0; i < count; i++ ) yield return address + i * stride;
	}

	string Label( int address ) => mapFile.ResolveStringId( mapFile.ReadInt32( address ) );

	Graph BuildGraph( string path, string[] context )
	{
		int header = mapFile.VaToOffset( (int)graph.DataVirtualAddress );
		var actions = new Dictionary<string, int>();
		var overlays = new Dictionary<string, int>();
		bool found = false;
		foreach ( int mode in Matches( header + 52, 20, context[0] ) )
		foreach ( int category in Matches( mode + 4, 20, context[1] ) )
		foreach ( int weapon in Matches( category + 4, 52, context[2] ) )
		{
			found |= Label( mode ) == context[0] && Label( category ) == context[1] && Label( weapon ) == context[2];
			ReadBindings( weapon + 4, actions );
			ReadBindings( weapon + 12, overlays );
		}
		if ( !found || (actions.Count == 0 && overlays.Count == 0) ) return null;

		if ( skeleton is null ) BuildSkeleton( header );
		if ( skeleton is null ) return null;
		var builder = Graph.Builder.WithSkeleton( skeleton );
		var baseClips = actions.Where( a => GetClip( a.Value ) is not null ).ToArray();
		var overlayClips = overlays.Where( o => GetClip( o.Value ) is not null ).ToArray();
		if ( baseClips.Length == 0 && overlayClips.Length == 0 ) return null;

		var options = baseClips.OrderByDescending( a => a.Key == "idle" ).ToArray();
		var action = options.Length == 0 ? null : builder.AddControlParameter( "action", GraphBuilder.NodeKind.Id, new()
		{
			DefaultID = options[0].Key, Options = options.Select( a => a.Key ).ToArray()
		} );
		var overlay = overlayClips.Length == 0 ? null : builder.IdParameter( "overlay", overlayClips.Select( o => o.Key ).ToArray() );
		var time = overlay is null ? null : builder.FloatParameter( "overlay_time", 0, 1 );
		GraphNode output = action is null ? builder.ReferencePose() : SelectAction( options, action, builder );
		if ( overlay is not null )
		{
			var layers = builder.Layers( output );
			foreach ( var group in overlayClips.GroupBy( o => o.Value ) )
			{
				int entry = group.Key;
				var enabled = builder.IdCompare( overlay, group.Select( o => o.Key ) );
				var pose = builder.TimeControlledClip( GetClip( entry ), time );
				layers.AddLayer( pose, new()
				{
					Weight = builder.Switch( enabled, 1f, 0f ),
					BlendMode = Kind( entry ) == 1 ? GraphBuilder.LayerBlendMode.Additive : GraphBuilder.LayerBlendMode.Overlay,
					BoneMask = clipMasks.TryGetValue( entry, out var mask ) ? builder.BoneMask( mask ) : null
				} );
			}
			output = layers;
		}
		return builder.Create( output, path );

		IEnumerable<int> Matches( int block, int stride, string requested ) =>
			Rows( block, stride ).Where( p => Label( p ) == requested || Label( p ) == "any" )
				.OrderByDescending( p => Label( p ) == requested );

		void ReadBindings( int block, Dictionary<string, int> destination )
		{
			int count = mapFile.ReadInt32( header + 44 ), table = mapFile.VaToOffset( mapFile.ReadInt32( header + 48 ) );
			foreach ( int row in Rows( block, 8 ) )
			{
				int reference = mapFile.ReadInt32( row + 4 ), index = reference >> 16;
				if ( (short)reference == -1 && (uint)index < count && Label( row ) is { Length: > 0 } label )
					destination.TryAdd( label, table + index * 96 );
			}
		}
	}

	// Native ID selectors choose on entry. State machines reevaluate the requested
	// action during playback. Grouping limits each machine to two states.
	GraphNode SelectAction( ReadOnlySpan<KeyValuePair<string, int>> options, GraphNode action, GraphBuilder builder )
	{
		if ( options.Length == 1 ) return builder.Clip( GetClip( options[0].Value ) );
		int split = options.Length / 2;
		var right = options[split..];
		var match = builder.IdCompare( action, right.ToArray().Select( a => a.Key ) );
		var machine = builder.StateMachine();
		var a = machine.AddState( SelectAction( options[..split], action, builder ) );
		var b = machine.AddState( SelectAction( right, action, builder ) );
		machine.SetEntryCondition( b, match );
		var cut = new GraphBuilder.TransitionSettings { Duration = 0 };
		machine.AddTransition( a, b, match, cut );
		machine.AddTransition( b, a, builder.Not( match ), cut );
		return machine.Build();
	}

	int Kind( int entry ) => mapFile.ReadSpan( entry + 16, 1 )[0];

	void BuildSkeleton( int header )
	{
		var model = new RenderModelTag( mapFile, tag );
		if ( model.Nodes.Count == 0 ) return;
		Blam.AnimationGraph.ReadRig( mapFile, graph, model, ScaleFactor, out bindPose, out nodeMap );
		var builder = new SkeletonBuilder();
		var names = model.Nodes.Select( (n, i) => mapFile.ResolveStringId( n.StringId ) ?? $"bone_{i}" ).ToArray();
		for ( int i = 0; i < names.Length; i++ )
		{
			int parent = model.Nodes[i].ParentIndex;
			builder.AddBone( names[i], bindPose[i], parent >= 0 ? names[parent] : null );
		}

		var masks = new Dictionary<string, string>();
		foreach ( int entry in Rows( header + 44, 96 ) )
		{
			if ( Kind( entry ) != 2 ) continue;
			var data = mapFile.ReadSpan( entry, 96 );
			if ( !Blam.AnimationGraph.CanDecode( mapFile, data, nodeMap.Length ) ) continue;
			int start = mapFile.VaToOffset( mapFile.ReadInt32( entry + 44 ) ) + (ushort)mapFile.ReadInt32( entry + 54 )
				+ mapFile.ReadInt32( entry + 56 ) + mapFile.ReadInt32( entry + 60 );
			var tracked = new bool[names.Length];
			foreach ( int size in new[] { (int)data[48], data[49] } )
			{
				int stride = size / 3;
				var flags = mapFile.ReadSpan( start, size );
				for ( int n = 0; n < data[19] && n / 8 < stride; n++ )
				{
					int i = n / 8;
					if ( nodeMap[n] >= 0 && ((flags[i] | flags[i + stride] | flags[i + 2 * stride]) & (1 << (n % 8))) != 0 )
						tracked[nodeMap[n]] = true;
				}
				start += size;
			}
			var key = string.Concat( tracked.Select( used => used ? '1' : '0' ) );
			if ( !masks.TryGetValue( key, out var mask ) )
			{
				mask = $"tracks_{masks.Count}";
				masks.Add( key, mask );
				builder.AddBoneMask( mask, names.Select( (name, i) => (name, weight: tracked[i] ? 1f : 0f) )
					.ToDictionary( b => b.name, b => b.weight ) );
			}
			clipMasks[entry] = mask;
		}
		skeleton = builder.Create( $"{Path}/skeleton.skel" );
	}

	Clip GetClip( int address )
	{
		if ( graphClips.TryGetValue( address, out var clip ) ) return clip;
		var entry = mapFile.ReadSpan( address, 96 );
		if ( !Blam.AnimationGraph.CanDecode( mapFile, entry, nodeMap.Length ) ) return null;
		bool additive = Kind( address ) == 1;
		int frames = (ushort)mapFile.ReadInt32( address + 20 ), bones = bindPose.Length;
		var poses = new Transform[checked(frames * bones)];
		var defaults = additive ? Enumerable.Repeat( Transform.Zero, bones ).ToArray() : bindPose;
		Blam.AnimationGraph.DecodeFrames( mapFile, entry, nodeMap, defaults, new Transform[bones], poses, ScaleFactor );
		if ( additive )
			for ( int i = 0; i < poses.Length; i++ ) poses[i].Scale -= Vector3.One;

		var builder = new ClipBuilder { Skeleton = skeleton, IsAdditive = additive, FrameRate = 30 };
		for ( int i = 0; i < Math.Max( frames, 2 ); i++ )
			builder.AddFrame( poses.AsSpan( Math.Min( i, frames - 1 ) * bones, bones ) );
		return graphClips[address] = builder.Create( $"{Path}/clip_{address}.anim" );
	}
}
