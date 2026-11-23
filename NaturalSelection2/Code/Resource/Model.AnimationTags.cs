using Sandbox.Animation;
using System;

namespace NS2;

public partial class ModelLoader
{
	// Spark collects tags from every nonzero contributor to a blended pose.
	internal void CollectTags( int sequence, GraphParameters pose, float from, float to, List<(float Time, string Name)> tags )
	{
		if ( sequence < 0 || to <= from ) return;
		var source = AnimationSource().Source;
		Visit( source.Sequences[sequence].AnimationNodeIndex );

		void Visit( int index )
		{
			var node = source.AnimationNodes[index];
			if ( node.Type == 1 )
			{
				var anim = source.Animations[node.AnimationIndex];
				if ( anim.Tags.Length == 0 || anim.NumFrames == 0 ) return;
				float period = anim.NumFrames / anim.FrameRate;
				float cycle = anim.Looping ? MathF.Floor( from / period ) * period : 0;
				do
				{
					foreach ( var marker in anim.Tags )
					{
						float at = marker.Time + cycle;
						if ( at >= from && at < to && !Contains( at, marker.Name ) ) tags.Add( (at, marker.Name) );
					}
					cycle += period;
				} while ( anim.Looping && cycle < to );
				return;
			}
			if ( node.Type == 3 )
			{
				foreach ( int child in node.Children ) Visit( child );
				return;
			}

			if ( node.Children.Length == 0 ) return;
			if ( node.Children.Length == 1 )
			{
				Visit( node.Children[0] );
				return;
			}
			float fraction = 0.5f;
			if ( pose is not null )
			{
				pose.TryGetFloat( source.BlendParameters[node.ParameterIndex], out var value );
				fraction = (value - node.ParamMin) / (node.ParamMax - node.ParamMin);
				fraction = node.WrapParameter ? fraction - MathF.Floor( fraction ) : Math.Clamp( fraction, 0, 1 );
			}
			float position = fraction * (node.Children.Length - 1);
			int first = (int)position;
			Visit( node.Children[first] );
			if ( position > first ) Visit( node.Children[first + 1] );
		}

		bool Contains( float at, string name )
		{
			foreach ( var tag in tags )
				if ( tag.Name == name && MathF.Abs( at - tag.Time ) < 0.001f ) return true;
			return false;
		}
	}
}
