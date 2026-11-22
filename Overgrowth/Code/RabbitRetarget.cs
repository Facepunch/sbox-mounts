namespace Overgrowth;

// The unmirrored, shared-hierarchy path of Animation::Retarget for the rabbit clips.
internal sealed class RabbitRetarget
{
	readonly SkeletonData target;
	readonly float scale;
	readonly float legRatio;
	readonly Vector3 hipOffset;
	readonly Vector3[] parentAnchor;
	readonly Vector3[] childHalf;
	readonly List<int> flippedToes = [];

	internal RabbitRetarget( BaseFileSystem fs, string rigPath, SkeletonData target, float scale )
	{
		this.target = target;
		this.scale = scale;
		var rig = XmlAsset.Read( fs, rigPath, "rig" );
		var mesh = ObjLoader.Load( fs, rig.Attribute( "model_path" ) );
		using var stream = fs.OpenRead( rig.Attribute( "bone_path" ) );
		var source = PhxbnLoader.Load( stream, mesh.FaceVertexCount );
		if ( !source.HierParents.SequenceEqual( target.HierParents ) )
			throw new InvalidDataException( $"Rabbit animation hierarchy differs from {rigPath}" );
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
			int parent = target.HierParents[i];
			if ( parent < 0 ) continue;
			var (a, b) = target.Bones[i];
			var parentCenter = target.BindPose[parent].Position;
			parentAnchor[i] = target.BindPose[parent].Rotation.Inverse * (ToUnits( target.Points[a] ) * scale - parentCenter);
			childHalf[i] = target.BindPose[i].Rotation.Inverse * ToUnits( target.Points[b] - target.Points[a] ) * (0.5f * scale);
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

	internal Vector3 ScaleOffset( Vector3 offset ) => offset * (legRatio * scale);

	internal Transform[] ToLocalPose( Transform[] pose )
	{
		var result = new Transform[pose.Length];
		for ( int i = 0; i < pose.Length; i++ )
		{
			int parent = target.HierParents[i];
			if ( parent < 0 )
			{
				result[i] = pose[i].WithPosition( (pose[i].Position + hipOffset) * (legRatio * scale) );
				continue;
			}
			var rotation = pose[parent].Rotation.Inverse * pose[i].Rotation;
			result[i] = new Transform( parentAnchor[i] + rotation * childHalf[i], rotation );
		}
		// Retarget's flipped-toe check rotates about the source rig's Z (s&box forward).
		foreach ( int toe in flippedToes )
			result[toe] = result[toe].WithRotation( result[toe].Rotation * Rotation.FromAxis( Vector3.Forward, 3.14f.RadianToDegree() ) );
		return result;
	}
}
