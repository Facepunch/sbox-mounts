namespace Overgrowth;

internal readonly record struct RabbitLeg( string Bone, float Length )
{
	internal static RabbitLeg[] Chain( SkeletonData rig, string label, float scale )
	{
		var chain = new RabbitLeg[6];
		int bone = rig.IKBones[label].BoneId;
		for ( int i = 0; i < chain.Length; i++ )
		{
			var (a, b) = rig.Bones[bone];
			chain[i] = new( $"bone_{bone}", rig.Points[a].Distance( rig.Points[b] ) * 40f * scale );
			bone = rig.HierParents[bone];
		}
		return chain;
	}
}

public sealed partial class RabbitGuard
{
	readonly Transform[] legPose = new Transform[6];
	bool plantedLegs;

	// movementobject.cpp CDrawLegImpl. Both the thigh and the shin have two skinned segments.
	void SolveLeg( int side, Transform foot )
	{
		var chain = boundAnimations.Legs[side];
		var bones = Renderer.Model.Bones;
		for ( int i = 0; i < chain.Length; i++ )
		{
			if ( !Renderer.TryGetBoneTransformAnimation( bones.GetBone( chain[i].Bone ), out var pose ) ) return;
			legPose[i] = Renderer.WorldTransform.ToLocal( pose );
		}
		Vector3 Start( int i ) => legPose[i].Position - legPose[i].Rotation.Forward * (chain[i].Length * 0.5f);
		var hip = Start( 5 );
		var oldEnd = Start( 0 ) - hip;
		var end = foot.Position - foot.Rotation.Forward * (chain[0].Length * 0.5f);
		if ( oldEnd.IsNearlyZero() || (end - hip).IsNearlyZero() ) return;
		var align = Rotation.FromToRotation( oldEnd.Normal, (end - hip).Normal );
		float stretch = (end - hip).Length / oldEnd.Length;
		var knee = hip + align * (Start( 3 ) - hip) * stretch;
		var ankle = hip + align * (Start( 1 ) - hip) * stretch;
		float thigh = chain[4].Length + chain[5].Length;
		float shin = chain[2].Length + chain[3].Length;
		for ( int i = 0; i < 2; i++ )
		{
			knee = hip + (knee - hip).Normal * thigh;
			ankle = end + (ankle - end).Normal * chain[1].Length;
			var half = (knee - ankle).Normal * (shin * 0.5f);
			var middle = (knee + ankle) * 0.5f;
			knee = middle + half;
			ankle = middle - half;
		}
		void Place( int index, Vector3 from, Vector3 to )
		{
			var original = legPose[index].Rotation;
			var rotation = Rotation.FromToRotation( original.Forward, (to - from).Normal ) * original;
			Renderer.SetBoneTransform( bones.GetBone( chain[index].Bone ), new Transform( (from + to) * 0.5f, rotation ) );
		}
		var midThigh = Vector3.Lerp( hip, knee, chain[5].Length / thigh );
		var midShin = Vector3.Lerp( knee, ankle, chain[3].Length / shin );
		Place( 5, hip, midThigh );
		Place( 4, midThigh, knee );
		Place( 3, knee, midShin );
		Place( 2, midShin, ankle );
		Place( 1, ankle, end );
		Renderer.SetBoneTransform( bones.GetBone( chain[0].Bone ), foot );
		plantedLegs = true;
	}

	void ReleaseLegs()
	{
		if ( !plantedLegs ) return;
		if ( Renderer.IsValid() ) Renderer.ClearPhysicsBones();
		plantedLegs = false;
	}

}
