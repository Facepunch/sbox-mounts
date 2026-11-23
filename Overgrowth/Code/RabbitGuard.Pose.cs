namespace Overgrowth;

public sealed partial class RabbitGuard
{
	Model poseModel;
	Transform[] animationPose, solvedPose;
	Transform[] previousMotionPose, currentMotionPose;
	Transform previousMotionRoot, currentMotionRoot;
	float motionStep;
	int motionSamples;
	internal Transform? SolvedBodyTransform( int bone ) => motionSamples > 0 ? currentMotionRoot.ToWorld( currentMotionPose[bone] ) : null;
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
		var parameters = Renderer.Animation.Parameters;
		parameters.TryGetFloat( "stride_phase", out float phase );
		parameters.TryGetFloat( "ground_speed", out float speed );
		parameters.TryGetFloat( "tall_coord", out float height );
		parameters.TryGetFloat( "threat_coord", out float threat );
		parameters.TryGetFloat( "close_coord", out float close );
		ReadMovementPose( boundAnimations.Movement, boundAnimations.MovementRig, new( speed, height, threat, close ), phase, MirroredStance );
	}

	void ReadMovementPose( RabbitMovement movement, RabbitRetarget rig, RabbitMovement.Controls controls, float phase, bool mirrored )
	{
		var model = Renderer.Model;
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
		gaitInputs.Clear();
		movement.CollectPoseInputs( controls, mirrored, gaitInputs );
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
				local.Position = rig.GaitJointPosition( rigIndices[bone.Index], local.Rotation, mirrored );
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
			previousMotionPose = new Transform[count];
			currentMotionPose = new Transform[count];
			motionSamples = 0;
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
		// Binding a graph leaves the bind pose visible until its first evaluation.
		if ( Networking.IsHost && playing is not null && Time.Delta > 0f )
		{
			(previousMotionPose, currentMotionPose) = (currentMotionPose, previousMotionPose);
			System.Array.Copy( solvedPose, currentMotionPose, solvedPose.Length );
			previousMotionRoot = currentMotionRoot;
			currentMotionRoot = model;
			motionStep = Time.Delta;
			motionSamples = System.Math.Min( 2, motionSamples + 1 );
		}
		foreach ( var bone in poseModel.Bones.AllBones )
			Renderer.SetBoneTransform( bone, solvedPose[bone.Index] );
	}

	// RiggedObject gives the physics bodies the velocity between its last two solved
	// animation frames. Display-only breathing and changes of renderer graph are excluded.
	void InheritPoseMotion( ModelPhysics target )
	{
		foreach ( var part in target.Bodies )
		{
			var body = part.Component;
			if ( !body.IsValid() ) continue;
			if ( motionSamples < 2 || poseModel != target.Model )
			{
				body.Velocity = Animation.MoveVelocity;
				body.AngularVelocity = Vector3.Zero;
				continue;
			}
			var from = previousMotionRoot.ToWorld( previousMotionPose[part.Bone] );
			var to = currentMotionRoot.ToWorld( currentMotionPose[part.Bone] );
			var center = body.PhysicsBody.LocalMassCenter;
			// Physics local mass centers are already scaled when the collider is built.
			body.Velocity = (to.Position - from.Position + to.Rotation * center - from.Rotation * center) / motionStep;
			var turn = to.Rotation * from.Rotation.Inverse;
			var axis = new Vector3( turn.x, turn.y, turn.z ).Normal;
			body.AngularVelocity = axis * ((turn.w < 0f ? -1f : 1f) * turn.Angle().DegreeToRadian() / motionStep);
		}
	}
}
