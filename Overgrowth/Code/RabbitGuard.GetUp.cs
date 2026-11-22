using Sandbox.Animation;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string GetUpSlot = "ragdoll_pose", GetUpWeight = "ragdoll_weight";
	Transform[] lastPhysicsPose;
	Graph getUpGraph;
	Clip getUpPose;
	TimeSince sinceGetUp;

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
		getUpPose = new ClipBuilder { Skeleton = rig }.AddFrame( frame ).Create();
		var graph = new GraphBuilder().WithSkeleton( rig );
		getUpGraph = graph.Create( graph.Pose( getUpPose, new GraphBuilder.PoseSettings() ) );
		Renderer.Animation.ExternalGraphs.Connect( GetUpSlot, getUpGraph );
		Renderer.Animation.Parameters.Set( GetUpWeight, 1f );
		sinceGetUp = 0;
	}

	void UpdateGetUpBlend()
	{
		if ( getUpGraph is null ) return;
		if ( sinceGetUp >= 1f )
			EndGetUpBlend();
		else
			Renderer.Animation.Parameters.Set( GetUpWeight, 1f - sinceGetUp );
	}

	void EndGetUpBlend()
	{
		lastPhysicsPose = null;
		if ( getUpGraph is null ) return;
		if ( Renderer.IsValid() )
		{
			Renderer.Animation.Parameters.Set( GetUpWeight, 0f );
			if ( Renderer.Animation.ExternalGraphs.IsConnected( GetUpSlot ) )
				Renderer.Animation.ExternalGraphs.Disconnect( GetUpSlot );
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
