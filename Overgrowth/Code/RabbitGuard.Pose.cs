namespace Overgrowth;

public sealed partial class RabbitGuard
{
	Model poseModel;
	Transform[] animationPose, solvedPose;
	bool[] overridden;

	void SetPoseBone( BoneCollection.Bone bone, Transform modelSpace )
	{
		if ( poseModel != Renderer.Model || solvedPose is null )
		{
			poseModel = Renderer.Model;
			int count = poseModel.BoneCount;
			animationPose = new Transform[count];
			solvedPose = new Transform[count];
			overridden = new bool[count];
		}
		solvedPose[bone.Index] = modelSpace;
		overridden[bone.Index] = true;
	}

	void PublishPose()
	{
		if ( solvedPose is null ) return;
		// Physics overrides can replace animation on uninvolved chains. Compose the solved bones with
		// the current animation explicitly so fingers, feet and other descendants retain their pose.
		var model = Renderer.WorldTransform;
		foreach ( var bone in poseModel.Bones.AllBones )
		{
			int i = bone.Index;
			if ( !Renderer.TryGetBoneTransformAnimation( bone, out var world ) ) continue;
			animationPose[i] = model.ToLocal( world );
			if ( !overridden[i] )
			{
				solvedPose[i] = bone.Parent is { } parent
					? solvedPose[parent.Index].ToWorld( animationPose[parent.Index].ToLocal( animationPose[i] ) )
					: animationPose[i];
			}
			Renderer.SetBoneTransform( bone, solvedPose[i] );
			overridden[i] = false;
		}
	}
}
