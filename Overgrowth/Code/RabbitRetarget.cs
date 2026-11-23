namespace Overgrowth;

// The unmirrored, shared-hierarchy path of Animation::Retarget for the rabbit clips.
internal sealed class RabbitRetarget
{
	readonly Transform[] sourceBind;
	readonly int[] eventBones;
	readonly SkeletonData target;
	readonly float scale;
	readonly float legRatio;
	readonly Vector3 hipOffset;
	readonly Vector3[] parentAnchor;
	readonly Vector3[] childHalf;
	Vector3[] mirroredParentAnchor, mirroredChildHalf;
	readonly List<int> flippedToes = [];

	internal RabbitRetarget( BaseFileSystem fs, string rigPath, SkeletonData target, float scale, out SkeletonData source )
	{
		this.target = target;
		this.scale = scale;
		var rig = XmlAsset.Read( fs, rigPath, "rig" );
		var mesh = ObjLoader.Load( fs, rig.Attribute( "model_path" ) );
		using var stream = fs.OpenRead( rig.Attribute( "bone_path" ) );
		source = PhxbnLoader.Load( stream, mesh.FaceVertexCount );
		sourceBind = source.BindPose;
		eventBones = RabbitMirror.MatchBones( source );
		legRatio = LegLength( target ) / LegLength( source );
		int torso = source.IKBones["torso"].BoneId;
		int hip = source.BoneParents[source.BoneParents[torso]];
		hipOffset = ToUnits( source.Points[source.Bones[hip].A] ) + mesh.Center;

		foreach ( var label in new[] { "right_leg", "left_leg" } )
		{
			int toe = target.IKBones[label].BoneId;
			var comparison = source.BindPose[toe].Rotation * target.BindPose[toe].Rotation.Inverse;
			if ( (comparison * Vector3.Up).z < 0 )
				flippedToes.Add( toe );
		}

		parentAnchor = new Vector3[target.Bones.Count];
		childHalf = new Vector3[target.Bones.Count];
		for ( int i = 0; i < target.Bones.Count; i++ )
		{
			int parent = target.AnimationParents[i];
			if ( parent < 0 ) continue;
			var (a, b) = target.Bones[i];
			var parentCenter = target.BindPose[parent].Position;
			parentAnchor[i] = target.BindPose[parent].Rotation.Inverse * (ToUnits( target.Points[a] ) * scale - parentCenter);
			childHalf[i] = target.BindPose[i].Rotation.Inverse * ToUnits( target.Points[b] - target.Points[a] ) * (0.5f * scale);
			if ( flippedToes.Contains( i ) ) childHalf[i] = ToeFlip.Inverse * childHalf[i];
		}
	}

	static Vector3 ToUnits( Vector3 point ) => new Vector3( point.z, point.x, point.y ) * 40f;

	static float LegLength( SkeletonData rig )
	{
		var chain = rig.IKBones["left_leg"];
		float length = 0;
		int bone = chain.BoneId;
		for ( int i = 0; i < chain.ChainLength - 1; i++ )
		{
			var (a, b) = rig.Bones[bone];
			length += rig.Points[a].Distance( rig.Points[b] );
			bone = rig.HierParents[bone];
		}
		return length;
	}

	static readonly Rotation ToeFlip = Rotation.FromAxis( Vector3.Forward, 3.14f.RadianToDegree() );

	internal Vector3 JointPosition( int bone, Rotation rotation ) => parentAnchor[bone] + rotation * childHalf[bone];

	internal void PrepareMirroredJoints( RabbitMirror mirror )
	{
		mirroredParentAnchor = new Vector3[parentAnchor.Length];
		mirroredChildHalf = new Vector3[childHalf.Length];
		for ( int i = 0; i < childHalf.Length; i++ )
		{
			int parent = target.AnimationParents[i];
			if ( parent < 0 ) continue;
			int other = mirror.Opposite( i );
			// Mirroring a model-space bone also changes its reference bind frame.
			// Express each end of the mirrored joint in its new parent and child frames.
			var parentBasis = mirror.BindChange( parent );
			var childBasis = mirror.BindChange( i );
			var anchor = parentAnchor[i];
			mirroredParentAnchor[other] = parentBasis.PointToLocal( anchor.WithY( -anchor.y ) );
			var half = flippedToes.Contains( i ) ? ToeFlip * childHalf[i] : childHalf[i];
			half = childBasis.Rotation.Inverse * (childBasis.Position + half.WithY( -half.y ));
			mirroredChildHalf[other] = flippedToes.Contains( other ) ? ToeFlip.Inverse * half : half;
		}
	}

	internal Vector3 GaitJointPosition( int bone, Rotation rotation, bool mirrored ) => mirrored
		? mirroredParentAnchor[bone] + rotation * mirroredChildHalf[bone]
		: JointPosition( bone, rotation );

	internal int EventBone( int bone ) => (uint)bone < eventBones.Length ? eventBones[bone] : bone;

	internal void Mirror( RabbitMirror mirror, System.Span<Transform> frame, Transform[] scratch )
	{
		foreach ( int toe in flippedToes ) frame[toe].Rotation *= ToeFlip.Inverse;
		mirror.Apply( frame, scratch, target.AnimationParents );
		foreach ( int toe in flippedToes ) frame[toe].Rotation *= ToeFlip;
	}

	internal Transform LimbTarget( int side, Transform pose, RabbitMirror mirror, int anchor, bool foot )
	{
		pose.Position = ScaleOffset( pose.Position + hipOffset );
		if ( mirror is not null )
		{
			pose = mirror.LimbTarget( anchor, pose );
			if ( foot ) pose.Rotation *= ToeFlip;
		}
		// Retarget applies each flipped-toe correction to the left IK target.
		if ( foot && side == 0 )
			foreach ( int _ in flippedToes ) pose.Rotation *= ToeFlip;
		return pose;
	}

	internal Vector3 ScaleOffset( Vector3 offset ) => offset * (legRatio * scale);

	internal Transform[] ToLocalPose( Transform[] pose )
	{
		var result = new Transform[target.Bones.Count];
		for ( int i = 0; i < result.Length; i++ )
		{
			int parent = target.AnimationParents[i];
			if ( parent < 0 )
			{
				result[i] = pose[i].WithPosition( (pose[i].Position + hipOffset) * (legRatio * scale) );
				continue;
			}
			// Overgrowth retains authored rotations on shared bones and attaches additional joints in bind pose.
			var rotation = i < pose.Length && parent < pose.Length ? pose[parent].Rotation.Inverse * pose[i].Rotation
				: (parent < sourceBind.Length ? sourceBind[parent] : target.BindPose[parent]).Rotation.Inverse * target.BindPose[i].Rotation;
			result[i] = new Transform( Vector3.Zero, rotation );
		}
		// Retarget's flipped-toe check rotates about the source rig's Z (s&box forward).
		foreach ( int toe in flippedToes )
			result[toe] = result[toe].WithRotation( result[toe].Rotation * ToeFlip );
		return result;
	}
}
