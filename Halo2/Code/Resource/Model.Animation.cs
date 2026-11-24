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
		var bindings = GetAnimationContext( context[0], context[1], context[2] );
		if ( bindings is null ) return null;
		var actions = bindings.Actions;
		var overlays = bindings.Overlays;
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
		var autoOverlay = overlay is null ? null : builder.BoolParameter( "overlay_auto" );
		var restartOverlay = overlay is null ? null : builder.BoolParameter( "overlay_restart" );
		var restartAction = action is null ? null : builder.BoolParameter( "action_restart" );
		var once = action is null ? null : builder.BoolParameter( "action_once" );
		var variation = builder.AddControlParameter( "variation", GraphBuilder.NodeKind.Float, new() { Min = -1, Max = 1, DefaultFloat = -1 } );
		var blend = builder.AddControlParameter( "action_blend", GraphBuilder.NodeKind.Float,
			new() { Min = 0, Max = 1, DefaultFloat = new GraphBuilder.TransitionSettings().Duration } );
		var strength = overlay is null ? null : builder.AddControlParameter( "overlay_weight", GraphBuilder.NodeKind.Float, new() { Min = 0, Max = 1, DefaultFloat = 1 } );
		var movement = new MovementInputs( builder, bindings );
		var screens = new ScreenInputs( this, mapFile, builder, overlayClips );
		var offsets = HasFirstPersonWeapon && overlays.TryGetValue( "overlays", out var offsetRef ) ? GetClip( offsetRef ) : null;
		bool hasOffsets = offsets is { IsAdditive: true, FrameCount: >= 9 };
		var offsetForward = hasOffsets ? builder.FloatParameter( "weapon_offset_forward", -1, 1 ) : null;
		var offsetLeft = hasOffsets ? builder.FloatParameter( "weapon_offset_left", -1, 1 ) : null;
		var previousWeight = HasFirstPersonWeapon ? builder.FloatParameter( "previous_action_weight", 0, 1 ) : null;
		var ikTargets = bindings.Ik.Select( p => p.Marker ).Distinct().Select( marker =>
			(Marker: marker, Bone: MarkerBone( marker ), Target: builder.TargetParameter( $"ik_{marker}" )) ).ToArray();
		var sample = builder.Switch( builder.Compare( variation, GraphBuilder.FloatComparison.LessThan, 0 ), builder.RandomFloat(), variation );
		var noBlendIn = options.Where( a => (GetClipInfo( a.Value ).PlaybackFlags & 1) != 0 ).Select( a => a.Key ).ToArray();
		var duration = noBlendIn.Length == 0 ? blend : builder.Switch( builder.IdCompare( action, noBlendIn ), builder.Constant( 0f ), blend );
		var ordered = options.OrderBy( a => DisablesBlendOut( a ) ).ToArray();
		GraphNode output = action is null ? builder.ReferencePose() : SelectAction( ordered );
		output = movement.Apply( builder, output, BasePose );
		if ( !HasFirstPersonWeapon ) output = screens.Apply( builder, output, this, action, options );
		if ( overlay is not null )
		{
			var layers = builder.Layers( output );
			foreach ( var group in overlayClips.GroupBy( o => o.Value ) )
			{
				int entry = group.Key;
				var enabled = builder.IdCompare( overlay, group.Select( o => o.Key ) );
				AddOverlay( builder.And( enabled, builder.Not( autoOverlay ) ),
					clip => builder.TimeControlledClip( clip, time ) );
				AddOverlay( builder.And( enabled, autoOverlay ),
					clip => builder.Clip( clip, new() { Looping = false, ResetTime = restartOverlay } ) );

				void AddOverlay( GraphNode selected, Func<Clip, GraphNode> pose )
				{
					var target = builder.Switch( selected, 1f, 0f );
					var weight = builder.Ease( target );
					int flags = GetClipInfo( entry ).PlaybackFlags;
					if ( (flags & 1) != 0 ) weight = builder.Switch( selected, target, weight );
					if ( (flags & 2) != 0 ) weight = builder.Switch( selected, weight, target );
					layers.AddLayer( Variants( entry, pose ), new()
					{
						Weight = builder.Multiply( weight, strength ),
						BlendMode = Kind( Entry( entry ) ) == 1 ? GraphBuilder.LayerBlendMode.Additive : GraphBuilder.LayerBlendMode.Overlay,
						BoneMask = clipMasks.TryGetValue( entry, out var mask ) ? builder.BoneMask( mask ) : null
					} );
				}
			}
			output = layers;
		}
		// First-person weapons apply the movement channel before the pitch-and-turn screen.
		if ( HasFirstPersonWeapon ) output = screens.Apply( builder, output, this, action, options );
		if ( hasOffsets )
		{
			var layers = builder.Layers( output );
			AddOffset( offsetForward, 0, 1 );
			AddOffset( offsetLeft, 3, 2 );
			output = layers;

			void AddOffset( GraphNode axis, int positive, int negative )
			{
				float last = offsets.FrameCount - 1;
				var time = builder.Switch( builder.Compare( axis, GraphBuilder.FloatComparison.LessThan, 0 ), negative / last, positive / last );
				layers.AddLayer( builder.TimeControlledClip( offsets, time ), new()
				{
					Weight = builder.Abs( axis ), BlendMode = GraphBuilder.LayerBlendMode.Additive
				} );
			}
		}
		foreach ( var (marker, bone, target) in ikTargets )
		{
			if ( bone is null ) continue;
			var enabled = builder.IsTargetSet( target );
			var excluded = options.Where( a => (GetClipInfo( a.Value ).PlaybackFlags & 12) != 0 ).Select( a => a.Key ).ToArray();
			if ( excluded.Length > 0 ) enabled = builder.And( enabled, builder.Not( builder.IdCompare( action, excluded ) ) );
			output = builder.TwoBoneIK( output, bone, target, new() { Enabled = enabled } );
		}
		if ( bindings.Transitions.Count > 0 || bindings.Damage.Count > 0 )
		{
			var layers = builder.Layers( output );
			foreach ( string slot in new[] { "reaction", "reaction_additive" } )
			{
				var input = builder.ExternalGraphSlot( slot );
				layers.AddLayer( input, new() { Weight = builder.Switch( builder.IsExternalGraphSlotFilled( input ), 1f, 0f ),
					BlendMode = slot == "reaction" ? GraphBuilder.LayerBlendMode.Overlay : GraphBuilder.LayerBlendMode.Additive } );
			}
			output = layers;
		}
		if ( previousWeight is not null )
		{
			var layers = builder.Layers( output );
			layers.AddLayer( builder.ExternalPose( "previous_action" ), new()
			{
				Weight = previousWeight,
				BlendMode = GraphBuilder.LayerBlendMode.NormalizedOverlay
			} );
			output = layers;
		}
		return builder.Create( output, path );

		GraphNode BasePose( int reference ) => Variants( reference, clip => builder.ClipSelector(
			(builder.Clip( clip, new() { Looping = false, ResetTime = restartAction } ), once),
			(builder.Clip( clip, new() { ResetTime = restartAction } ), builder.Not( once )) ) );

		GraphNode Variants( int reference, Func<Clip, GraphNode> pose )
		{
			var info = GetClipInfo( reference );
			if ( info.NextVariant == -1 ) return pose( GetClip( reference ) );
			var selector = builder.Selector();
			var seen = new HashSet<int>();
			while ( HasClip( reference ) && seen.Add( reference ) )
			{
				info = GetClipInfo( reference );
				if ( GetClip( reference ) is { } clip )
				{
					var condition = info.NextVariant == -1 ? builder.Constant( true )
						: builder.Compare( sample, GraphBuilder.FloatComparison.LessThan, info.Weight );
					selector.Add( pose( clip ), condition );
				}
				reference = info.NextVariant;
			}
			return selector;
		}

		bool DisablesBlendOut( KeyValuePair<string, int> a ) => (GetClipInfo( a.Value ).PlaybackFlags & 2) != 0;

		GraphNode SelectAction( ReadOnlySpan<KeyValuePair<string, int>> entries )
		{
			if ( entries.Length == 1 ) return BasePose( entries[0].Value );
			// Separate hard exits first so the outgoing branch always has one interpolation policy.
			int split = entries.Length / 2;
			if ( DisablesBlendOut( entries[0] ) != DisablesBlendOut( entries[^1] ) )
			{
				split = 1;
				while ( !DisablesBlendOut( entries[split] ) ) split++;
			}
			var left = entries[..split];
			var right = entries[split..];
			var toLeft = builder.IdCompare( action, left.ToArray().Select( a => a.Key ) );
			var toRight = builder.IdCompare( action, right.ToArray().Select( a => a.Key ) );
			var machine = builder.StateMachine();
			var a = machine.AddState( SelectAction( left ) );
			var b = machine.AddState( SelectAction( right ) );
			machine.SetEntryCondition( b, toRight );
			machine.AddTransition( a, b, toRight, Settings( left[0] ) );
			machine.AddTransition( b, a, toLeft, Settings( right[0] ) );
			return machine.Build();
		}

		GraphBuilder.TransitionSettings Settings( KeyValuePair<string, int> from ) =>
			new() { Duration = 0, DurationOverride = DisablesBlendOut( from ) ? null : duration };
	}
	float Float( int address ) => BitConverter.Int32BitsToSingle( mapFile.ReadInt32( address ) );

	int Kind( int entry ) => mapFile.ReadSpan( entry + 16, 1 )[0];

	void BuildSkeleton( int header )
	{
		var model = ReadRenderModel();
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
		var cues = GetClipInfo( reference ).Cues;
		foreach ( var cue in cues )
		{
			float cycle = (float)cue.Frame / (builder.FrameCount - 1);
			if ( cycle >= 0 && cycle <= 1 ) builder.AddEvent( cycle, new Halo2CueEvent { Cue = cue } );
		}
		bool needsOrigin = HasFirstPersonWeapon;
		foreach ( var group in cues.Where( c => c.Kind != "sound" && c.Kind != "effect" ).GroupBy( c => c.Frame ) )
		{
			float cycle = (float)group.Key / (builder.FrameCount - 1);
			if ( cycle < 0 || cycle >= 1 ) continue;
			// AG2 starts playback at the first sync marker. A weapon cue can occur mid-swing.
			if ( needsOrigin && cycle > 0 ) builder.AddSyncEvent( 0 );
			needsOrigin = false;
			builder.AddSyncEvent( cycle, group.First().Kind );
		}
		return graphClips[reference] = builder.Create( $"{Path}/clip_{reference}.anim" );
	}
}
