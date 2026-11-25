using Sandbox.Animation;
using System;

namespace NS2;

public partial class ModelLoader
{
	// Spark collects tags from every nonzero contributor to a blended pose.
	internal void EmitSequenceTags( int sequence, GraphParameters pose, float from, float to, List<(float Time, string Name)> tags, Action<string> emit )
	{
		if ( sequence < 0 || to <= from ) return;
		var source = AnimationSource().Source;
		ref var seq = ref source.Sequences[sequence];
		bool loop = seq.Looping ??= Loops( seq.AnimationNodeIndex );
		// A zero-length non-looping clip crosses both boundaries on its first update.
		if ( !loop || seq.Duration > 0 )
		{
			float start = loop ? from % seq.Duration : from;
			float end = loop ? to % seq.Duration : to;
			if ( start <= seq.Duration && (end > seq.Duration || (end <= start && end > 0)) ) emit( "end" );
			if ( end > 0 && (start <= 0 || end <= start) ) emit( "start" );
		}

		tags.Clear();
		Visit( seq.AnimationNodeIndex );
		foreach ( var marker in tags ) emit( marker.Name );

		bool Loops( int index )
		{
			var node = source.AnimationNodes[index];
			if ( node.Type == 1 ) return source.Animations[node.AnimationIndex].Looping;
			foreach ( var child in node.Children )
				if ( Loops( child ) ) return true;
			return false;
		}

		void Visit( int index )
		{
			var node = source.AnimationNodes[index];
			if ( node.Type == 1 )
			{
				var anim = source.Animations[node.AnimationIndex];
				if ( anim.Tags.Length == 0 || anim.NumFrames == 0 ) return;
				float period = anim.NumFrames / anim.FrameRate;
				float cycleStart = anim.Looping ? MathF.Floor( from / period ) * period : 0;
				float start = from - cycleStart, end = to - cycleStart;
				float cycle = 0;
				do
				{
					foreach ( var marker in anim.Tags )
					{
						float at = marker.Time + cycle;
						if ( marker.Time >= start && marker.Time < end && !Contains( at, marker.Name ) ) tags.Add( (at, marker.Name) );
					}
					start -= period;
					end -= period;
					cycle += period;
				} while ( anim.Looping && end >= 0 );
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
			float value = 0;
			pose?.TryGetFloat( source.BlendParameters[node.ParameterIndex], out value );
			float fraction = (value - node.ParamMin) / (node.ParamMax - node.ParamMin);
			fraction = node.WrapParameter ? fraction - MathF.Floor( fraction ) : Math.Clamp( fraction, 0, 1 );
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
