namespace Overgrowth;

public sealed partial class RabbitGuard
{
	Model poseModel;
	Transform[] animationPose, solvedPose;
	bool[] overridden;
	int[] poseParents, recoveryParents;

	Transform[] gaitPose, originalGaitPose;
	int[] rigIndices;
	Model gaitModel;
	bool hasGaitPose;
	readonly List<RabbitMovement.PoseInput> gaitInputs = [];
	(Rotation Unit, Rotation Raw)[] gaitBlend = [];

	// Apply only the rotation difference so AG2 transitions and additive layers survive.
	Rotation GaitBlendCorrection( float phase, int bone, int bones )
	{
		int count = 0;
		foreach ( var input in gaitInputs )
		{
			if ( input.Clip is { } clip )
			{
				var unit = clip.Animation.GetLocalTransform( phase, bone ).Rotation;
				gaitBlend[count++] = (unit, clip.RawRotation( unit, phase, bone, bones ));
				continue;
			}
			var b = gaitBlend[--count];
			var a = gaitBlend[count - 1];
			var raw = RabbitClip.MixRotation( a.Raw, b.Raw, input.Weight );
			gaitBlend[count - 1] = (Rotation.Slerp( a.Unit, b.Unit, input.Weight ), raw);
		}
		return gaitBlend[0].Unit.Inverse * gaitBlend[0].Raw.Normal;
	}

	// SyncedAnimationGroup blends clips authored for the same rig before Retarget enforces each
	// joint length. AG2 blends translations as well as rotations, so restore the joints here.
	void ReadGaitPose()
	{
		var model = Renderer.Model;
		var rig = boundAnimations.MovementRig;
		if ( rig is null ) return;
		if ( gaitModel != model || rigIndices is null )
		{
			gaitModel = model;
			rigIndices = new int[model.BoneCount];
			gaitPose = new Transform[model.BoneCount];
			originalGaitPose = new Transform[model.BoneCount];
			foreach ( var bone in model.Bones.AllBones )
				rigIndices[bone.Index] = int.Parse( bone.Name[5..] );
		}

		var basis = Renderer.WorldTransform;
		var parameters = Renderer.Animation.Parameters;
		parameters.TryGetFloat( "stride_phase", out float phase );
		parameters.TryGetFloat( "ground_speed", out float speed );
		parameters.TryGetFloat( "tall_coord", out float height );
		parameters.TryGetFloat( "threat_coord", out float threat );
		gaitInputs.Clear();
		boundAnimations.Movement.CollectPoseInputs( new( speed, height, threat ), MirroredStance, gaitInputs );
		if ( gaitBlend.Length < gaitInputs.Count ) gaitBlend = new (Rotation, Rotation)[gaitInputs.Count];
		foreach ( var bone in model.Bones.AllBones )
		{
			if ( !Renderer.TryGetBoneTransformAnimation( bone, out var world ) ) return;
			var pose = basis.ToLocal( world );
			originalGaitPose[bone.Index] = pose;
			var parent = bone.Parent;
			var local = parent is null ? pose : originalGaitPose[parent.Index].ToLocal( pose );
			if ( gaitInputs.Count > 1 ) local.Rotation *= GaitBlendCorrection( phase, rigIndices[bone.Index], model.BoneCount );
			if ( parent is not null )
			{
				local.Position = rig.GaitJointPosition( rigIndices[bone.Index], local.Rotation, MirroredStance );
				local = gaitPose[parent.Index].ToWorld( local );
			}
			gaitPose[bone.Index] = local;
		}
		hasGaitPose = true;
	}

	bool TryGetAnimatedBone( BoneCollection.Bone bone, out Transform world )
	{
		if ( !hasGaitPose || bone is null ) return Renderer.TryGetBoneTransformAnimation( bone, out world );
		world = Renderer.WorldTransform.ToWorld( gaitPose[bone.Index] );
		return true;
	}

	void EnsurePoseBuffers()
	{
		if ( poseModel != Renderer.Model || recoveryParents is null )
		{
			poseModel = Renderer.Model;
			int count = poseModel.BoneCount;
			animationPose = new Transform[count];
			solvedPose = new Transform[count];
			overridden = new bool[count];
			poseParents = new int[count];
			recoveryParents = new int[count];
			foreach ( var modelBone in poseModel.Bones.AllBones )
			{
				var parent = modelBone.Parent;
				while ( parent is not null && boundAnimations.UnweightedBones.Contains( parent.Name ) )
					parent = parent.Parent;
				recoveryParents[modelBone.Index] = parent?.Index ?? -1;
				// DrawArms and DrawHead also carry their unweighted child joints.
				bool followsEnd = parent is not null && (parent.Name == boundAnimations.HeadBone || boundAnimations.Arms.Any( arm => arm[0].Bone == parent.Name ));
				poseParents[modelBone.Index] = followsEnd || !boundAnimations.UnweightedBones.Contains( modelBone.Name )
					? parent?.Index ?? -1 : -1;
			}
		}
	}

	void SetPoseBone( BoneCollection.Bone bone, Transform modelSpace )
	{
		EnsurePoseBuffers();
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
			if ( !TryGetAnimatedBone( bone, out var world ) ) continue;
			animationPose[i] = model.ToLocal( world );
			if ( !overridden[i] )
			{
				int parent = poseParents[i];
				solvedPose[i] = parent >= 0
					? solvedPose[parent].ToWorld( animationPose[parent].ToLocal( animationPose[i] ) )
					: animationPose[i];
			}
			overridden[i] = false;
		}
		if ( recoveryRate > 0f ) BlendRecoveryPose();
		foreach ( var bone in poseModel.Bones.AllBones )
			Renderer.SetBoneTransform( bone, solvedPose[bone.Index] );
	}
}
