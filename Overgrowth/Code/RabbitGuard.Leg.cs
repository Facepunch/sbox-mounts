using System;

namespace Overgrowth;

internal readonly record struct RabbitLimbBone( string Bone, float Length )
{
	internal static RabbitLimbBone[] Chain( SkeletonData rig, int[] parents, string label, float scale )
	{
		var chain = new RabbitLimbBone[6];
		int bone = rig.IKBones[label].BoneId;
		for ( int i = 0; i < chain.Length; i++ )
		{
			var (a, b) = rig.Bones[bone];
			chain[i] = new( $"bone_{bone}", rig.Points[a].Distance( rig.Points[b] ) * 40f * scale );
			bone = parents[bone];
		}
		return chain;
	}
}

public sealed partial class RabbitGuard
{
	readonly Transform[] legPose = new Transform[6];
	readonly Transform[] stanceFoot = new Transform[2];
	Transform pelvisBind;
	Vector3[] hipSockets;
	Vector3 hipOffset, solvedHipOffset;
	Rotation balanceRotation = Rotation.Identity;
	bool plantedLegs;

	// CDoHipIK keeps the distance from each hip to its foot from the authored pose.
	void SolveStance()
	{
		var bones = Renderer.Model.Bones;
		Transform Animated( string name )
		{
			TryGetAnimatedBone( bones.GetBone( name ), out var world );
			return Renderer.WorldTransform.ToLocal( world );
		}
		Span<Vector3> hips = stackalloc Vector3[2];
		Span<Vector3> feet = stackalloc Vector3[2];
		Span<float> lengths = stackalloc float[2];
		var rootBone = bones.GetBone( boundAnimations.TorsoBones[^1] );
		var pelvis = Animated( rootBone.Name );
		EnsurePoseBuffers();
		var hipKey = pelvis.ToWorld( pelvisBind.ToLocal( global::Transform.Zero ) );
		for ( int i = 0; i < 2; i++ )
		{
			var chain = boundAnimations.Legs[i];
			var toe = Animated( chain[0].Bone );
			hips[i] = hipKey.PointToWorld( hipSockets[i] );
			lengths[i] = hips[i].Distance( toe.Position - toe.Rotation.Forward * (chain[0].Length * 0.5f) );
			feet[i] = stanceFoot[i].Position - stanceFoot[i].Rotation.Forward * (chain[0].Length * 0.5f);
		}
		var middle = (hips[0] + hips[1]) * 0.5f;
		var axis = hips[1] - hips[0];
		for ( int pass = 0; pass < 2; pass++ )
		{
			for ( int i = 0; i < 2; i++ )
				hips[i] = feet[i] + (hips[i] - feet[i]).Normal * lengths[i];
			var center = middle.WithZ( (hips[0].z + hips[1].z) * 0.5f );
			var half = (hips[1] - hips[0] + axis * 3f).Normal * (axis.Length * 0.5f);
			hips[0] = center - half;
			hips[1] = center + half;
		}
		var rotation = Rotation.FromToRotation( axis.Normal, (hips[1] - hips[0]).Normal );
		float maxTilt = Math.Max( 0f, 0.2f - Animation.MoveVelocity.WithZ( 0 ).Length / 40f * 0.2f );
		float tilt = Rotation.Identity.Distance( rotation );
		if ( tilt > maxTilt.RadianToDegree() )
			rotation = Rotation.Slerp( Rotation.Identity, rotation, maxTilt.RadianToDegree() / tilt );
		var hipRoot = pelvis.Position + pelvis.Rotation.Forward * boundAnimations.HipHalfLength;
		var pivot = middle - hipRoot;
		var offset = (hips[0] + hips[1]) * 0.5f - middle + pivot - rotation * pivot;
		solvedHipOffset = plantedLegs ? Vector3.Lerp( offset, solvedHipOffset, MathF.Pow( 0.95f, Time.Delta * 120f ) ) : offset;
		var across = feet[1] - feet[0];
		var leanAxis = Vector3.Up.Cross( across ).Normal;
		balanceRotation = Rotation.FromAxis( leanAxis, (balanceOffset.x * 0.25f * idleStance).RadianToDegree() );
		var shift = (across * balanceOffset.x + Vector3.Forward * balanceOffset.y).WithZ( balanceOffset.z ) * idleStance;
		hipOffset = solvedHipOffset + shift;
		hipKey.Position += hipOffset;
		hipKey.Rotation = rotation * balanceRotation * hipKey.Rotation;
		SetPoseBone( rootBone, hipKey.ToWorld( pelvisBind ) );
		// Overgrowth translates the torso keys with the hip, then rolls the chest about its own origin.
		for ( int i = 0; i < 2; i++ )
		{
			var name = boundAnimations.TorsoBones[i];
			var torso = Animated( name );
			torso.Position += hipOffset;
			if ( i == 0 )
			{
				var raw = Renderer.WorldTransform.ToWorld( Animated( name ) );
				torso.Rotation = balanceRotation * torso.Rotation;
				headMotion = chestMotion = (raw, Renderer.WorldTransform.ToWorld( torso ));
			}
			SetPoseBone( bones.GetBone( name ), torso );
		}
		for ( int i = 0; i < 2; i++ )
			SolveLeg( i, stanceFoot[i], hipKey.PointToWorld( hipSockets[i] ) );
	}

	// movementobject.cpp CDrawLegImpl. Both the thigh and the shin have two skinned segments.
	void SolveLeg( int side, Transform foot, Vector3 hip )
	{
		var chain = boundAnimations.Legs[side];
		var bones = Renderer.Model.Bones;
		for ( int i = 0; i < chain.Length; i++ )
		{
			if ( !TryGetAnimatedBone( bones.GetBone( chain[i].Bone ), out var pose ) ) return;
			legPose[i] = Renderer.WorldTransform.ToLocal( pose );
		}
		Vector3 Start( int i ) => legPose[i].Position - legPose[i].Rotation.Forward * (chain[i].Length * 0.5f);
		var oldHip = Start( 5 );
		var oldEnd = Start( 0 ) - oldHip;
		var end = foot.Position - foot.Rotation.Forward * (chain[0].Length * 0.5f);
		if ( oldEnd.IsNearlyZero() || (end - hip).IsNearlyZero() ) return;
		var align = Rotation.FromToRotation( oldEnd.Normal, (end - hip).Normal );
		float stretch = (end - hip).Length / oldEnd.Length;
		var knee = hip + align * (Start( 3 ) - oldHip) * stretch;
		var ankle = hip + align * (Start( 1 ) - oldHip) * stretch;
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
		void Place( int index, Vector3 from, Vector3 to, Rotation turn )
		{
			SetPoseBone( bones.GetBone( chain[index].Bone ), new Transform( (from + to) * 0.5f, turn * legPose[index].Rotation ) );
		}
		// RotateBonesToMatchVec turns both segments together, preserving their authored relative rotation.
		void PlacePair( int index, Vector3 from, Vector3 to, float length )
		{
			var middle = Vector3.Lerp( from, to, chain[index].Length / length );
			var turn = Rotation.FromToRotation( legPose[index].Rotation.Forward, (to - from).Normal );
			Place( index, from, middle, turn );
			Place( index - 1, middle, to, turn );
		}
		PlacePair( 5, hip, knee, thigh );
		PlacePair( 3, knee, ankle, shin );
		Place( 1, ankle, end, Rotation.FromToRotation( legPose[1].Rotation.Forward, (end - ankle).Normal ) );
		SetPoseBone( bones.GetBone( chain[0].Bone ), foot );
		plantedLegs = true;
	}

}
