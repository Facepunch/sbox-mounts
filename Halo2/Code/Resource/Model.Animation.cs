using System;
using Halo2.Blam;
using Sandbox.Animation;
using Sandbox.Mounting;
using FilePath = System.IO.Path;
using GraphNode = Sandbox.Animation.GraphBuilder.Node;

namespace Halo2;

public partial class Halo2ModelLoader
{
	// A graph is a loadable child of its model: model.vmdl/combat/rifle/br.ag.
	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = default;
		if ( graph is null ) return null;
		var parts = name.Split( '/' );
		string leaf = FilePath.GetFileNameWithoutExtension( parts[^1] );
		switch ( FilePath.GetExtension( parts[^1] ).ToLowerInvariant() )
		{
			case ".anim" when parts is ["clips", _] && int.TryParse( leaf, out int index )
				&& (uint)index < mapFile.ReadInt32( GraphHeader + 44 ):
				type = ResourceType.AnimationClip;
				return new ClipLoader( index );
			case ".anim" when parts is ["clips", _, _] && short.TryParse( parts[1], out var slot ) && slot >= 0
				&& int.TryParse( leaf, out var inheritedIndex ) && (uint)inheritedIndex <= short.MaxValue:
				type = ResourceType.AnimationClip;
				return new ClipLoader( inheritedIndex, slot );
			case ".ag" when parts.Length == 3:
				parts[2] = leaf;
				type = ResourceType.AnimationGraph;
				return new GraphLoader( parts );
			default:
				return null;
		}
	}

	int GraphHeader => mapFile.VaToOffset( (int)graph.DataVirtualAddress );

	sealed record AnimationSource( int Header, int[] Mapping, float TranslationScale, bool ScaleRootOnly );
	readonly Dictionary<int, AnimationSource> sources = new();
	int Entry( int reference ) => mapFile.VaToOffset( mapFile.ReadInt32( sources[(short)reference].Header + 48 ) ) + (reference >> 16) * 96;

	bool HasClip( int reference ) => sources.TryGetValue( (short)reference, out var source )
		&& (uint)(reference >> 16) < mapFile.ReadInt32( source.Header + 44 );

	sealed class ClipLoader( int index, short slot = -1 ) : ResourceLoader<Halo2Mount>
	{
		protected override object Load()
		{
			var model = (Halo2ModelLoader)Parent;
			if ( model.skeleton is null ) model.BuildSkeleton( model.GraphHeader );
			if ( model.skeleton is null ) return null;
			var reference = (index << 16) | (ushort)slot;
			return model.HasClip( reference ) ? model.GetClip( reference ) : null;
		}
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
		int header = GraphHeader;
		if ( skeleton is null ) BuildSkeleton( header );
		if ( skeleton is null ) return null;
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
					BlendMode = Kind( Entry( entry ) ) == 1 ? GraphBuilder.LayerBlendMode.Additive : GraphBuilder.LayerBlendMode.Overlay,
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
			foreach ( int row in Rows( block, 8 ) )
			{
				int reference = mapFile.ReadInt32( row + 4 );
				if ( HasClip( reference ) && Label( row ) is { Length: > 0 } label )
					destination.TryAdd( label, reference );
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

	float Float( int address ) => BitConverter.Int32BitsToSingle( mapFile.ReadInt32( address ) );

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

		sources[-1] = new( header, nodeMap, 1, false );
		int slot = 0;
		foreach ( int parent in Rows( header + 76, 32 ) )
		{
			int index = slot++;
			var inherited = mapFile.GetTag( mapFile.ReadInt32( parent + 4 ) & 0xffff );
			if ( inherited is not { PrimaryClass: "damj", DataSize: >= 172 } ) continue;
			int parentHeader = mapFile.VaToOffset( (int)inherited.DataVirtualAddress );
			int[] remap = Enumerable.Repeat( -1, mapFile.ReadInt32( parentHeader + 12 ) ).ToArray();
			int flags = mapFile.VaToOffset( mapFile.ReadInt32( parent + 20 ) );
			int flagsCount = mapFile.ReadInt32( parent + 16 );
			int n = 0;
			foreach ( int row in Rows( parent + 8, 2 ).Take( remap.Length ) )
			{
				int local = (short)mapFile.ReadInt32( row );
				if ( (uint)local < nodeMap.Length && n / 32 < flagsCount && (mapFile.ReadInt32( flags + n / 32 * 4 ) & (1 << (n % 32))) != 0 )
					remap[n] = nodeMap[local];
				n++;
			}
			sources[index] = new( parentHeader, remap, Float( parent + 24 ), (mapFile.ReadInt32( parent + 28 ) & 1) != 0 );
		}

		var masks = new Dictionary<string, string>();
		foreach ( var (sourceIndex, source) in sources )
		foreach ( var (index, entry) in Rows( source.Header + 44, 96 ).Index() )
		{
			if ( Kind( entry ) != 2 ) continue;
			var data = mapFile.ReadSpan( entry, 96 );
			if ( !Blam.AnimationGraph.CanDecode( mapFile, data, source.Mapping.Length ) ) continue;
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
					if ( source.Mapping[n] >= 0 && ((flags[i] | flags[i + stride] | flags[i + 2 * stride]) & (1 << (n % 8))) != 0 )
						tracked[source.Mapping[n]] = true;
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
			clipMasks[(index << 16) | (ushort)sourceIndex] = mask;
		}
		skeleton = builder.Create( $"{Path}/skeleton.skel" );
	}

	Clip GetClip( int reference )
	{
		if ( graphClips.TryGetValue( reference, out var clip ) ) return clip;
		int address = Entry( reference );
		var source = sources[(short)reference];
		var entry = mapFile.ReadSpan( address, 96 );
		if ( !Blam.AnimationGraph.CanDecode( mapFile, entry, source.Mapping.Length ) ) return null;
		bool additive = Kind( address ) == 1;
		int frames = (ushort)mapFile.ReadInt32( address + 20 ), bones = bindPose.Length;
		var poses = new Transform[checked(frames * bones)];
		var defaults = additive ? Enumerable.Repeat( Transform.Zero, bones ).ToArray() : bindPose;
		Blam.AnimationGraph.DecodeFrames( mapFile, entry, source.Mapping, defaults, new Transform[bones], poses, ScaleFactor );
		if ( source.TranslationScale != 1 )
		{
			for ( int i = 0; i < poses.Length; i++ )
			{
				int bone = i % bones;
				if ( source.ScaleRootOnly && bone != source.Mapping[0] ) continue;
				poses[i].Position *= source.TranslationScale;
			}
		}
		if ( additive )
			for ( int i = 0; i < poses.Length; i++ ) poses[i].Scale -= Vector3.One;

		var builder = new ClipBuilder { Skeleton = skeleton, IsAdditive = additive, FrameRate = 30 };
		int movementType = entry[17];
		int stride = movementType is >= 1 and <= 3 ? (movementType + 1) * 4 : 0;
		int movementSize = (ushort)mapFile.ReadInt32( address + 50 );
		if ( stride * frames != movementSize ) return null;
		int movement = mapFile.VaToOffset( mapFile.ReadInt32( address + 44 ) )
			+ (ushort)mapFile.ReadInt32( address + 54 ) + mapFile.ReadInt32( address + 56 ) + mapFile.ReadInt32( address + 60 )
			+ entry[48] + entry[49];
		var motion = Transform.Zero;
		for ( int i = 0; i < Math.Max( frames, 2 ); i++ )
		{
			builder.AddFrame( poses.AsSpan( Math.Min( i, frames - 1 ) * bones, bones ), motion );
			if ( stride == 0 || i >= frames ) continue;
			int p = movement + i * stride;
			var translation = new Vector3( Float( p ), Float( p + 4 ), movementType == 3 ? Float( p + 8 ) : 0 ) * (ScaleFactor * source.TranslationScale);
			float yaw = movementType == 1 ? 0 : Float( p + stride - 4 ).RadianToDegree();
			motion = motion.ToWorld( new Transform( translation, Rotation.FromYaw( yaw ) ) );
		}
		// Halo stores a displacement for every frame, including the interval
		// from the last pose to the next cycle. AG2 samples both endpoints.
		if ( movementSize > 0 && frames > 1 )
			builder.AddFrame( poses.AsSpan( bones * Math.Clamp( (short)mapFile.ReadInt32( address + 32 ), 0, frames - 1 ), bones ), motion );
		return graphClips[reference] = builder.Create( $"{Path}/clip_{reference}.anim" );
	}
}
