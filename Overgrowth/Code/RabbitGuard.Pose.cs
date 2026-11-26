namespace Overgrowth;

public sealed partial class RabbitGuard
{
	Model poseModel;
	Transform[] animationPose, solvedPose;
	Transform[] previousMotionPose, currentMotionPose;
	Transform previousMotionRoot, currentMotionRoot;
	float motionStep;
	int motionSamples;
	internal Transform? SolvedBodyTransform( int bone ) => motionSamples > 0 && currentMotionPose is not null ? currentMotionRoot.ToWorld( currentMotionPose[bone] ) : null;
	bool[] overridden;
	int[] poseParents, recoveryParents;
	(int Hip, int Abdomen, int Chest) torsoIndices;
	int[] tailIndices;

	Transform[] sampledPose, graphPose;
	int[] rigIndices;
	Model sampledModel;
	bool hasSampledPose;
	Vector3 getUpPoseOffset;
	readonly List<RabbitMovement.PoseInput> gaitInputs = [], nextStrideInputs = [];
	(Transform Unit, Rotation Raw)[] gaitBlend = [];

	// AG2 blends unit quaternions. Overgrowth retains the authored magnitudes until retargeting.
	(Transform Unit, Rotation Raw) SampleMovementBone( List<RabbitMovement.PoseInput> inputs, int bone, int bones )
	{
		int count = 0;
		foreach ( var input in inputs )
		{
			if ( input.Clip is { } clip )
			{
				var unit = clip.Animation.GetLocalTransform( input.Phase, bone );
				gaitBlend[count++] = (unit, clip.RawRotation( unit.Rotation, input.Phase, bone, bones ));
				continue;
			}
			var b = gaitBlend[--count];
			var a = gaitBlend[count - 1];
			var raw = RabbitClip.MixRotation( a.Raw, b.Raw, input.Weight );
			gaitBlend[count - 1] = (global::Transform.Lerp( a.Unit, b.Unit, input.Weight, false ), raw);
		}
		return gaitBlend[0];
	}

	// SyncedAnimationGroup blends clips authored for the same rig before Retarget enforces each
	// joint length. AG2 blends translations as well as rotations, so restore the joints here.
	void ReadGaitPose( RabbitMovement.Controls controls, float nextPhase )
	{
		var parameters = Renderer.Animation.Parameters;
		parameters.TryGetFloat( "stride_phase", out float phase );
		parameters.TryGetFloat( "ground_speed", out float speed );
		parameters.TryGetFloat( "tall_coord", out float height );
		parameters.TryGetFloat( "threat_coord", out float threat );
		parameters.TryGetFloat( "close_coord", out float close );
		parameters.TryGetBool( "mirrored_pose", out bool mirrored );
		nextStrideInputs.Clear();
		boundAnimations.Movement.CollectPoseInputs( controls, nextPhase, MirroredStance, nextStrideInputs );
		ReadMovementPose( boundAnimations.Movement, boundAnimations.MovementRig, new( speed, height, threat, close ), phase, mirrored, nextStrideInputs );
	}

	void ReadMovementPose( RabbitMovement movement, RabbitRetarget rig, RabbitMovement.Controls controls, float phase, bool mirrored, List<RabbitMovement.PoseInput> stride = null )
	{
		if ( rig is null ) return;
		gaitInputs.Clear();
		movement.CollectPoseInputs( controls, phase, mirrored, gaitInputs );
		ReadRetargetedPose( rig, mirrored, gaitInputs, stride );
	}

	void ReadRetargetedPose( RabbitRetarget rig, bool mirrored, List<RabbitMovement.PoseInput> inputs = null, List<RabbitMovement.PoseInput> stride = null )
	{
		var model = PrepareSampledPose();
		// The graph exposes world-space bones from the last animation pass. Navigation can
		// already have moved the component before the renderer receives its new transform.
		var basis = Renderer.SceneModel.Transform;
		int count = System.Math.Max( inputs?.Count ?? 0, stride?.Count ?? 0 );
		if ( gaitBlend.Length < count ) gaitBlend = new (Transform, Rotation)[count];
		bool advance = stride is not null;
		foreach ( var bone in model.Bones.AllBones )
		{
			if ( !Renderer.TryGetBoneTransformAnimation( bone, out var world ) ) return;
			var pose = basis.ToLocal( world );
			graphPose[bone.Index] = pose;
			var parent = bone.Parent;
			var local = parent is null ? pose : graphPose[parent.Index].ToLocal( pose );
			// Advance the stride under the graph's layers so all procedural joints read the same frame.
			if ( advance || inputs?.Count > 1 )
			{
				int joint = rigIndices[bone.Index];
				var from = SampleMovementBone( inputs, joint, model.BoneCount );
				var to = advance ? SampleMovementBone( stride, joint, model.BoneCount ) : from;
				local.Rotation *= from.Unit.Rotation.Inverse * to.Raw.Normal;
				if ( parent is null ) local.Position += to.Unit.Position - from.Unit.Position;
			}
			if ( parent is not null )
			{
				local.Position = rig.GaitJointPosition( rigIndices[bone.Index], local.Rotation, advance ? MirroredStance : mirrored );
				local = sampledPose[parent.Index].ToWorld( local );
			}
			sampledPose[bone.Index] = local;
		}
		hasSampledPose = true;
	}

	// The ledge solver moves the renderer around the grip after the graph was evaluated.
	// Keep the evaluated joints in model space throughout the lean and limb passes.
	void ReadLedgePose()
	{
		var model = PrepareSampledPose();
		var basis = Renderer.SceneModel.Transform;
		foreach ( var bone in model.Bones.AllBones )
		{
			if ( !Renderer.TryGetBoneTransformAnimation( bone, out var world ) ) return;
			sampledPose[bone.Index] = basis.ToLocal( world );
		}
		hasSampledPose = true;
	}

	Model PrepareSampledPose()
	{
		EnsurePoseBuffers();
		var model = Renderer.Model;
		if ( sampledModel != model || sampledPose is null )
		{
			sampledModel = model;
			sampledPose = new Transform[model.BoneCount];
			graphPose = new Transform[model.BoneCount];
		}
		return model;
	}

	// The renderer still exposes the previous evaluation here. Advance only the
	// throw clip's contribution so the solver follows the same phase as root motion.
	void ReadThrowPose()
	{
		var parameters = Renderer.Animation.Parameters;
		if ( !parameters.TryGetFloat( "reaction_phase", out var from ) || from == ReactionPhase ) return;
		var clip = boundAnimations.Clips[Stance].ForSide( MirroredAction );
		float elapsed = Networking.IsHost ? System.Math.Max( 0f, fadeElapsed - Time.Delta ) : from * clip.Duration / (Stance == "throw" ? AttackSpeed : 1f);
		float weight = System.Math.Min( 1f, elapsed * ThrowFadeRate );
		var model = PrepareSampledPose();
		var basis = Renderer.WorldTransform;
		foreach ( var bone in model.Bones.AllBones )
		{
			if ( !Renderer.TryGetBoneTransformAnimation( bone, out var world ) ) return;
			var pose = basis.ToLocal( world );
			var parent = bone.Parent;
			var local = parent is null ? pose : graphPose[parent.Index].ToLocal( pose );
			graphPose[bone.Index] = pose;
			int joint = rigIndices[bone.Index];
			var previous = clip.Animation.GetLocalTransform( from, joint );
			var current = clip.Animation.GetLocalTransform( ReactionPhase, joint );
			local.Position += (current.Position - previous.Position) * weight;
			local.Rotation *= Rotation.Slerp( Rotation.Identity, previous.Rotation.Inverse * current.Rotation, weight );
			sampledPose[bone.Index] = parent is null ? local : sampledPose[parent.Index].ToWorld( local );
		}
		hasSampledPose = true;
		parameters.Set( "reaction_phase", ReactionPhase );
	}

	bool TryGetAnimatedBone( BoneCollection.Bone bone, out Transform world )
	{
		if ( hasSampledPose && bone is not null )
			world = Renderer.WorldTransform.ToWorld( sampledPose[bone.Index] );
		else if ( !Renderer.TryGetBoneTransformAnimation( bone, out world ) )
			return false;
		world.Position += getUpPoseOffset;
		return true;
	}

	void EnsurePoseBuffers()
	{
		if ( poseModel != Renderer.Model || recoveryParents is null || hipSockets is null || rigIndices is null || tailIndices is null )
		{
			poseModel = Renderer.Model;
			var torso = boundAnimations.TorsoBones;
			int BoneIndex( string name ) => poseModel.Bones.GetBone( name ).Index;
			torsoIndices = (BoneIndex( torso[^1] ), BoneIndex( torso[1] ), BoneIndex( torso[0] ));
			tailIndices = boundAnimations.TailBones.Select( part => BoneIndex( part.Bone ) ).ToArray();
			tailMotion = tailIndices.Length == 0 ? null : new TailMotion( tailIndices.Length );
			pelvisBind = poseModel.GetBoneTransform( torsoIndices.Hip );
			hipSockets = new Vector3[2];
			for ( int i = 0; i < hipSockets.Length; i++ )
			{
				var thigh = boundAnimations.Legs[i][5];
				var bind = poseModel.GetBoneTransform( poseModel.Bones.GetBone( thigh.Bone ).Index );
				hipSockets[i] = bind.Position - bind.Rotation.Forward * (thigh.Length * 0.5f);
			}
			int count = poseModel.BoneCount;
			rigIndices = new int[count];
			animationPose = new Transform[count];
			solvedPose = new Transform[count];
			motionSamples = 0;
			overridden = new bool[count];
			poseParents = new int[count];
			recoveryParents = new int[count];
			foreach ( var modelBone in poseModel.Bones.AllBones )
			{
				rigIndices[modelBone.Index] = int.Parse( modelBone.Name[5..] );
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
		var (hip, abdomen, chest) = torsoIndices;
		solvedPose[abdomen] = boundAnimations.Torso.Abdomen( solvedPose[hip], solvedPose[chest] );
		solvedPose[hip] = RabbitTorso.Hip( animationPose[hip], solvedPose[hip] );
		foreach ( int tail in tailIndices )
			solvedPose[tail] = solvedPose[hip].ToWorld( animationPose[hip].ToLocal( animationPose[tail] ) );
		DrawTail( model );
		if ( recoveryRate > 0f )
			BlendRecoveryPose( Networking.IsHost && RecoveringRoll && RollProgress < 1f ? rollVelocity : Animation.MoveVelocity );
		// Binding a graph leaves the bind pose visible until its first evaluation.
		if ( Networking.IsHost && playing is not null && Time.Delta > 0f )
		{
			// Live instances can retain their skeleton cache across a reload while lacking motion history.
			if ( currentMotionPose?.Length != solvedPose.Length || previousMotionPose?.Length != solvedPose.Length )
			{
				previousMotionPose = new Transform[solvedPose.Length];
				currentMotionPose = new Transform[solvedPose.Length];
				motionSamples = 0;
			}
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
			if ( motionSamples < 2 || poseModel != target.Model || previousMotionPose is null || currentMotionPose is null )
			{
				body.Velocity = Animation.MoveVelocity;
				body.AngularVelocity = Vector3.Zero;
				continue;
			}
			var from = previousMotionRoot.ToWorld( previousMotionPose[part.Bone] );
			var to = currentMotionRoot.ToWorld( currentMotionPose[part.Bone] );
			// BulletObject.SetPositionAndVel samples the bone midpoint, including on offset shapes.
			body.Velocity = (to.Position - from.Position) / motionStep;
			var turn = to.Rotation * from.Rotation.Inverse;
			var axis = new Vector3( turn.x, turn.y, turn.z ).Normal;
			body.AngularVelocity = axis * ((turn.w < 0f ? -1f : 1f) * turn.Angle().DegreeToRadian() / motionStep);
		}
	}
}
