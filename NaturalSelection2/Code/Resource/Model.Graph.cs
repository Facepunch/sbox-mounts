using Sandbox.Animation;
using System;

namespace NS2;

public partial class ModelLoader
{
	internal Graph BuildGraph( SparkAnimationGraph definition, string graphPath, bool npcPlayback )
	{
		var model = Source;
		var animSource = AnimationSource();
		var animations = animSource.Source;
		using var source = Host.FileSystem.OpenRead( animSource.FullPath );
		var bones = new SkeletonBuilder();
		foreach ( var bone in model.Bones )
		{
			// Match the model builder, which binds bones by world position and rotation.
			var bind = bone.Transform.WithScale( 1 );
			if ( bone.Parent >= 0 ) bind = model.Bones[bone.Parent].Transform.WithScale( 1 ).ToLocal( bind );
			bones.AddBone( bone.Name, bind, bone.Parent >= 0 ? model.Bones[bone.Parent].Name : null );
		}
		// Sparse absolute clips only replace the bones the source actually animated.
		var masks = new Dictionary<int, string>();
		for ( int i = 0; i < animations.Animations.Length; i++ )
		{
			var anim = animations.Animations[i];
			if ( !npcPlayback || anim.Additive || anim.BoneCurves.Length >= model.Bones.Length ) continue;
			var weights = model.Bones.ToDictionary( bone => bone.Name, _ => 0f );
			foreach ( var (index, _) in anim.BoneCurves ) weights[model.Bones[index].Name] = 1f;
			var name = $"tracks_{i}";
			bones.AddBoneMask( name, weights );
			masks.Add( i, name );
		}
		var skeleton = bones.Create( $"{Path}/skeleton.skel" );
		var graph = new GraphBuilder().WithSkeleton( skeleton );
		var clips = new Dictionary<int, Clip>();
		var parameters = new Dictionary<string, GraphBuilder.Node>();
		foreach ( var node in animations.AnimationNodes )
		{
			if ( node.Type != 2 ) continue;
			var name = animations.BlendParameters[node.ParameterIndex];
			if ( !parameters.ContainsKey( name ) ) parameters.Add( name, graph.FloatParameter( npcPlayback ? name : definition.PoseInputName( name ), node.ParamMin, node.ParamMax ) );
		}
		var reader = new Reader( source );
		var frame = new Transform[model.Bones.Length];
		return npcPlayback ? definition.CompilePlayback( graph, SequencePose, graphPath )
			: definition.Compile( graph, (name, incoming) => SequencePose( name, incoming, null ), graphPath );

		GraphBuilder.Node SequencePose( string name, GraphBuilder.Node incoming, GraphBuilder.Node seconds )
		{
			int i = SequenceIndex( name );
			// Spark enters the state and clears the active sequence when the model lacks that name.
			if ( i < 0 ) return null;
			return Apply( Pose( animations.Sequences[i].AnimationNodeIndex, incoming, seconds ), incoming );
		}

		GraphBuilder.Node Apply( (GraphBuilder.Node Node, bool Additive) pose, GraphBuilder.Node incoming ) =>
			pose.Additive ? graph.Layers( incoming ?? graph.ReferencePose() ).AddLayer( pose.Node, new() { BlendMode = GraphBuilder.LayerBlendMode.Additive } ) : pose.Node;

		(GraphBuilder.Node Node, bool Additive) Pose( int index, GraphBuilder.Node incoming, GraphBuilder.Node seconds )
		{
			var node = animations.AnimationNodes[index];
			if ( node.Type == 1 )
			{
				var anim = animations.Animations[node.AnimationIndex];
				if ( anim.NumFrames == 0 ) return (incoming ?? graph.ReferencePose(), false);
				if ( !clips.TryGetValue( node.AnimationIndex, out var clip ) )
				{
					// Spark's compressor subdivides the full clip duration on a binary grid.
					int intervals = anim.NumFrames - 1;
					if ( npcPlayback && anim.Compressed && intervals > 0 )
						intervals = (int)System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)intervals * 2 );
					float rate = intervals > 0 ? anim.FrameRate * intervals / (anim.NumFrames - 1) : anim.FrameRate;
					var builder = new ClipBuilder { Skeleton = skeleton, FrameRate = rate, IsAdditive = anim.Additive };
					DecodeFrames( pose =>
					{
						for ( int i = 0; i < pose.Length; i++ )
						{
							// The base pose already includes inherited stretch. Apply it to the layer's displacement too.
							int parent = model.Bones[i].Parent;
							if ( npcPlayback && anim.Additive && parent >= 0 )
								pose[i].Position *= model.Bones[parent].Transform.Scale;
							pose[i] = pose[i].WithScale( anim.Additive ? 0 : 1 );
						}
						builder.AddFrame( pose );
					}, anim, model, ref reader, frame, intervals, bakeScale: npcPlayback );
					if ( builder.FrameCount == 1 ) builder.AddFrame( frame );
					clips[node.AnimationIndex] = clip = builder.Create( $"{Path}/clip_{node.AnimationIndex}.anim" );
				}
				if ( seconds is null )
					return (graph.Clip( clip, new() { Looping = anim.Looping, SampleRootMotion = false } ), anim.Additive);
				var time = anim.NumFrames <= 1 ? graph.Constant( 0f ) : graph.Multiply( seconds, anim.FrameRate / (anim.NumFrames - 1) );
				time = anim.Looping ? graph.Subtract( time, graph.Floor( time ) ) : graph.Clamp( time, 0, 1 );
				var sampled = graph.TimeControlledClip( clip, time, new() { SampleRootMotion = false } );
				if ( incoming is not null && masks.TryGetValue( node.AnimationIndex, out var mask ) )
					return (graph.Layers( incoming ).AddLayer( sampled, new() { BoneMask = graph.BoneMask( mask ), BlendMode = GraphBuilder.LayerBlendMode.Overlay } ), false);
				return (sampled, anim.Additive);
			}

			if ( node.Children.Length == 0 ) return (incoming ?? graph.ReferencePose(), false);
			if ( node.Children.Length == 1 ) return Pose( node.Children[0], incoming, seconds );
			if ( node.Type == 2 )
			{
				var sources = Array.ConvertAll( node.Children, child => Pose( child, incoming, seconds ) );
				bool additive = sources.All( s => s.Additive );
				var input = parameters[animations.BlendParameters[node.ParameterIndex]];
				if ( node.WrapParameter && node.ParamMax > node.ParamMin )
				{
					float width = node.ParamMax - node.ParamMin;
					var turns = graph.Floor( graph.Divide( graph.Subtract( input, node.ParamMin ), width ) );
					input = graph.Subtract( input, graph.Multiply( turns, width ) );
				}
				var blend = graph.BlendSpace( input );
				for ( int i = 0; i < sources.Length; i++ )
				{
					var sample = additive ? sources[i].Node : Apply( sources[i], incoming );
					blend.Add( node.ParamMin + (node.ParamMax - node.ParamMin) * i / (sources.Length - 1), sample );
				}
				return (blend, additive);
			}

			foreach ( var child in node.Children )
				incoming = Apply( Pose( child, incoming, seconds ), incoming );
			return (incoming, false);
		}
	}
}
