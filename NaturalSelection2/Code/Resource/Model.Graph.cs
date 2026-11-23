using Sandbox.Animation;
using System;

namespace NS2;

public partial class ModelLoader
{
	internal Graph BuildGraph( SparkAnimationGraph definition, string graphPath )
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
		var skeleton = bones.Create( $"{Path}/skeleton.skel" );
		var graph = new GraphBuilder().WithSkeleton( skeleton );
		var clips = new Dictionary<int, Clip>();
		var parameters = new Dictionary<string, GraphBuilder.Node>();
		foreach ( var node in animations.AnimationNodes )
		{
			if ( node.Type != 2 ) continue;
			var name = animations.BlendParameters[node.ParameterIndex];
			if ( !parameters.ContainsKey( name ) ) parameters.Add( name, graph.FloatParameter( name, node.ParamMin, node.ParamMax ) );
		}
		var reader = new Reader( source );
		var frame = new Transform[model.Bones.Length];
		return definition.Compile( graph, parameters, SequencePose, graphPath );

		GraphBuilder.Node SequencePose( string name, GraphBuilder.Node incoming )
		{
			int i = Array.FindIndex( animations.Sequences, s => s.Name == name );
			if ( i < 0 ) throw new InvalidDataException( $"Graph references missing sequence '{name}' in {FullPath}" );
			return Apply( Pose( animations.Sequences[i].AnimationNodeIndex, incoming ), incoming );
		}

		GraphBuilder.Node Apply( (GraphBuilder.Node Node, bool Additive) pose, GraphBuilder.Node incoming ) =>
			pose.Additive ? graph.Layers( incoming ?? graph.ReferencePose() ).AddLayer( pose.Node, new() { BlendMode = GraphBuilder.LayerBlendMode.Additive } ) : pose.Node;

		(GraphBuilder.Node Node, bool Additive) Pose( int index, GraphBuilder.Node incoming )
		{
			var node = animations.AnimationNodes[index];
			if ( node.Type == 1 )
			{
				var anim = animations.Animations[node.AnimationIndex];
				if ( !clips.TryGetValue( node.AnimationIndex, out var clip ) )
				{
					var builder = new ClipBuilder { Skeleton = skeleton, FrameRate = anim.FrameRate, IsAdditive = anim.Additive };
					DecodeFrames( pose =>
					{
						for ( int i = 0; i < pose.Length; i++ ) pose[i] = pose[i].WithScale( anim.Additive ? 0 : 1 );
						builder.AddFrame( pose );
					}, anim, model, ref reader, frame );
					if ( builder.FrameCount == 1 ) builder.AddFrame( frame );
					clips[node.AnimationIndex] = clip = builder.Create( $"{Path}/clip_{node.AnimationIndex}.anim" );
				}
				var sampled = graph.Clip( clip, new() { Looping = anim.Looping, SampleRootMotion = false } );
				return (sampled, anim.Additive);
			}

			if ( node.Children.Length == 0 ) return (incoming ?? graph.ReferencePose(), false);
			if ( node.Children.Length == 1 ) return Pose( node.Children[0], incoming );
			if ( node.Type == 2 )
			{
				var sources = Array.ConvertAll( node.Children, child => Pose( child, incoming ) );
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
				incoming = Apply( Pose( child, incoming ), incoming );
			return (incoming, false);
		}
	}
}
