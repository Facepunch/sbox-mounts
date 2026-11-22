namespace Overgrowth;

public sealed partial class RabbitGuard
{
	Model poseModel;
	Transform[] animationPose, solvedPose;
	bool[] overridden;
	int[] poseParents;

	void SetPoseBone( BoneCollection.Bone bone, Transform modelSpace )
	{
		if ( poseModel != Renderer.Model || poseParents is null )
		{
			poseModel = Renderer.Model;
			int count = poseModel.BoneCount;
			animationPose = new Transform[count];
			solvedPose = new Transform[count];
			overridden = new bool[count];
			poseParents = new int[count];
			foreach ( var modelBone in poseModel.Bones.AllBones )
			{
				var parent = modelBone.Parent;
				while ( parent is not null && boundAnimations.UnweightedBones.Contains( parent.Name ) )
					parent = parent.Parent;
				// DrawArms carries every finger with the hand, including unweighted finger joints.
				bool followsHand = parent is not null && boundAnimations.Arms.Any( arm => arm[0].Bone == parent.Name );
				poseParents[modelBone.Index] = followsHand || !boundAnimations.UnweightedBones.Contains( modelBone.Name )
					? parent?.Index ?? -1 : -1;
			}
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
				int parent = poseParents[i];
				solvedPose[i] = parent >= 0
					? solvedPose[parent].ToWorld( animationPose[parent].ToLocal( animationPose[i] ) )
					: animationPose[i];
			}
			Renderer.SetBoneTransform( bone, solvedPose[i] );
			overridden[i] = false;
		}
	}
}
