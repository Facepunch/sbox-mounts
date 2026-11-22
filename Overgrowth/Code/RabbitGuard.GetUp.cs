using System;
using Sandbox.Animation;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	string getUpSlot;
	Transform[] lastPhysicsPose;
	Graph getUpGraph;
	Clip getUpPose;


	void CapturePhysicsPose()
	{
		var rig = boundAnimations.Rig;
		lastPhysicsPose ??= new Transform[rig.BoneCount];
		foreach ( var bone in rig.Bones )
		{
			if ( Renderer.TryGetBoneTransform( bone.Name, out var world ) )
				lastPhysicsPose[bone.Index] = world;
			else
				lastPhysicsPose[bone.Index] = Renderer.WorldTransform.ToWorld( bone.Transform );
		}
	}

	void BeginGetUpBlend()
	{
		var rig = boundAnimations.Rig;
		var frame = new Transform[rig.BoneCount];
		foreach ( var bone in rig.Bones )
		{
			var parent = bone.ParentIndex < 0 ? Renderer.WorldTransform : lastPhysicsPose[bone.ParentIndex];
			frame[bone.Index] = parent.ToLocal( lastPhysicsPose[bone.Index] );
		}

		lastPhysicsPose = null;
		getUpSlot = Stance;
		var stand = boundAnimations.Clips[getUpSlot];
		var duration = Math.Min( 1f, stand.Duration );
		int steps = (int)MathF.Ceiling( duration * 120f );
		var clip = new ClipBuilder { Skeleton = rig, FrameRate = steps / duration };
		clip.AddFrame( frame );
		// aschar.as blends from the previous result towards the moving stand pose in parent space.
		for ( int step = 1; step <= steps; step++ )
		{
			float time = step * duration / steps;
			for ( int i = 0; i < frame.Length; i++ )
				frame[i] = global::Transform.Lerp( frame[i], stand.Animation.GetLocalTransform( time / stand.Duration, i ), time, false );
			clip.AddFrame( frame );
		}
		getUpPose = clip.Create();
		var graph = new GraphBuilder().WithSkeleton( rig );
		getUpGraph = graph.Create( graph.Clip( getUpPose, new() { Looping = false, SampleRootMotion = false } ) );
		Renderer.Animation.ExternalGraphs.Connect( getUpSlot, getUpGraph );
	}

	void EndGetUpBlend()
	{
		lastPhysicsPose = null;
		if ( getUpGraph is null ) return;
		if ( Renderer.IsValid() )
		{
			if ( Renderer.Animation.ExternalGraphs.IsConnected( getUpSlot ) )
				Renderer.Animation.ExternalGraphs.Disconnect( getUpSlot );
		}
		getUpGraph.Dispose();
		getUpPose.Dispose();
		getUpGraph = null;
		getUpPose = null;
	}

	protected override void OnDisabled()
	{
		EndGetUpBlend();
		base.OnDisabled();
	}
}
