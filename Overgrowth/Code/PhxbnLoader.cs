using System;
using System.IO;
using System.Text;

namespace Overgrowth;

public class SkeletonData
{
	public List<Vector3> Points = new();
	public List<int> PointParents = new();
	public List<(int A, int B)> Bones = new();
	public List<int> BoneParents = new();
	public List<float> BoneMass = new();
	public List<Vector3> BoneCom = new();
	public List<int> HierParents = new();
	public List<JointData> Joints = new();
	public Dictionary<string, SimpleIKBone> IKBones = new();
	public Transform[] BindPose;
	public Vector4[] BoneWeights;
	public Vector4[] BoneIds;

	internal bool[] GetWeightedBones()
	{
		var assigned = new bool[Bones.Count];
		for ( int i = 0; i < BoneWeights.Length; i++ )
			for ( int j = 0; j < 4; j++ )
				if ( BoneWeights[i][j] > 0 )
					assigned[(int)BoneIds[i][j]] = true;
		return assigned;
	}
}

public struct JointData
{
	public int Type;
	public int BoneA, BoneB;
	public float[] StopAngles;
	public Vector3 Axis;
}

public struct SimpleIKBone
{
	public int BoneId;
	public int ChainLength;
}

static class PhxbnLoader
{
	public static SkeletonData Load( System.IO.Stream stream, int faceVertexCount = 0 )
	{
		using var r = new System.IO.BinaryReader( stream );
		var data = new SkeletonData();

		int version = 5;
		int temp = r.ReadInt32();
		if ( temp >= 6 )
		{
			version = temp;
			temp = r.ReadInt32();
		}

		int riggingStage = temp;
		if ( riggingStage == 2 ) riggingStage = 1;
		if ( riggingStage != 1 || faceVertexCount <= 0 )
			throw new NotSupportedException( "Only fully rigged Overgrowth skeletons with a reference mesh can be imported." );

		int numPoints = r.ReadInt32();
		for ( int i = 0; i < numPoints; i++ )
			data.Points.Add( ReadVec3( r ) );

		if ( version >= 8 )
			for ( int i = 0; i < numPoints; i++ )
				data.PointParents.Add( r.ReadInt32() );

		int numBones = r.ReadInt32();
		for ( int i = 0; i < numBones; i++ )
			data.Bones.Add( (r.ReadInt32(), r.ReadInt32()) );

		if ( version >= 8 )
			for ( int i = 0; i < numBones; i++ )
				data.BoneParents.Add( r.ReadInt32() );

		if ( version >= 6 )
			for ( int i = 0; i < numBones; i++ )
				data.BoneMass.Add( r.ReadSingle() );

		if ( version >= 7 )
			for ( int i = 0; i < numBones; i++ )
				data.BoneCom.Add( ReadVec3( r ) );
		else
			for ( int i = 0; i < numBones; i++ )
				data.BoneCom.Add( (data.Points[data.Bones[i].A] + data.Points[data.Bones[i].B]) * 0.5f );

		if ( version >= 9 )
			r.BaseStream.Position += numBones * 64;

		data.BindPose = new Transform[numBones];
		for ( int i = 0; i < numBones; i++ )
		{
			if ( data.Bones[i].A < 0 || data.Bones[i].A >= numPoints || data.Bones[i].B < 0 || data.Bones[i].B >= numPoints )
				throw new InvalidDataException( $"Overgrowth bone {i} references an invalid point." );
			var start = data.Points[data.Bones[i].A];
			var end = data.Points[data.Bones[i].B];
			data.BindPose[i] = ComputeBoneMat( start, end );
		}

		if ( riggingStage == 1 && faceVertexCount > 0 )
		{
			int numVerts = faceVertexCount;
			if ( version >= 11 )
				numVerts = r.ReadInt32();
			if ( numVerts != faceVertexCount )
				throw new InvalidDataException( $"Overgrowth skeleton has {numVerts} face vertices, but its reference mesh has {faceVertexCount}." );

			data.BoneWeights = new Vector4[numVerts];
			data.BoneIds = new Vector4[numVerts];

			for ( int i = 0; i < numVerts; i++ )
				data.BoneWeights[i] = ReadVec4( r );
			for ( int i = 0; i < numVerts; i++ )
				data.BoneIds[i] = ReadVec4( r );

			for ( int i = 0; i < numBones; i++ )
				data.HierParents.Add( r.ReadInt32() );

			int numJoints = r.ReadInt32();
			for ( int i = 0; i < numJoints; i++ )
				data.Joints.Add( ReadJoint( r ) );
		}

		if ( version >= 10 )
		{
			int numIk = r.ReadInt32();
			for ( int i = 0; i < numIk; i++ )
			{
				var bone = new SimpleIKBone { BoneId = r.ReadInt32(), ChainLength = r.ReadInt32() };
				int length = r.ReadInt32();
				if ( length < 0 || length > r.BaseStream.Length - r.BaseStream.Position )
					throw new InvalidDataException( "Invalid Overgrowth IK label length." );
				var name = Encoding.UTF8.GetString( r.ReadBytes( length ) );
				data.IKBones[name] = bone;
			}
		}

		for ( int i = 0; i < numBones; i++ )
		{
			int steps = 0;
			for ( int bone = i; bone != -1; bone = data.HierParents[bone] )
			{
				if ( bone < 0 || bone >= numBones || ++steps > numBones )
					throw new InvalidDataException( "Overgrowth skeleton has an invalid or cyclic bone hierarchy." );
			}
		}
		return data;
	}

	public static int GetClosestBone( SkeletonData skel, Vector3 point )
	{
		int closest = -1;
		float closestDist = float.MaxValue;

		for ( int i = 0; i < skel.Bones.Count; i++ )
		{
			var (a, b) = skel.Bones[i];
			var start = skel.Points[a];
			var end = skel.Points[b];
			float dist = DistanceToSegment( start, end, point );
			if ( dist < closestDist )
			{
				closestDist = dist;
				closest = i;
			}
		}

		return closest;
	}

	static float DistanceToSegment( Vector3 a, Vector3 b, Vector3 p )
	{
		var ab = b - a;
		float t = Vector3.Dot( p - a, ab );
		if ( t <= 0f )
			return (p - a).Length;

		float denom = ab.LengthSquared;
		if ( t >= denom )
			return (p - b).Length;

		return (p - (a + ab * (t / denom))).Length;
	}

	static JointData ReadJoint( System.IO.BinaryReader r )
	{
		var j = new JointData { Type = r.ReadInt32() };

		if ( j.Type == 1 )
		{
			j.StopAngles = new float[6];
			for ( int k = 0; k < 6; k++ )
				j.StopAngles[k] = r.ReadSingle();
			j.StopAngles[4] *= -1f;
			j.StopAngles[5] *= -1f;
			(j.StopAngles[4], j.StopAngles[5]) = (j.StopAngles[5], j.StopAngles[4]);
		}
		else if ( j.Type == 0 )
		{
			j.StopAngles = [r.ReadSingle(), r.ReadSingle()];
		}

		j.BoneA = r.ReadInt32();
		j.BoneB = r.ReadInt32();

		if ( j.Type == 0 )
			j.Axis = ReadVec3( r );

		return j;
	}

	static Transform ComputeBoneMat( Vector3 start, Vector3 end )
	{
		var diff = end - start;
		float len = diff.Length;
		if ( len == 0f )
			return new Transform( new Vector3( start.z, start.x, start.y ) * 40f, Rotation.Identity );

		var dir = diff / len;

		var up = new Vector3( 0, 0, 1 );
		if ( Math.Abs( Vector3.Dot( up, dir ) ) > 0.95f )
			up = new Vector3( 1, 0, 0 );

		var right = Vector3.Cross( up, dir ).Normal;
		up = Vector3.Cross( dir, right ).Normal;

		var quat = QuatFromMatrix(
			right.x, up.x, dir.x,
			right.y, up.y, dir.y,
			right.z, up.z, dir.z );

		var center = (start + end) * 0.5f;
		var pos = new Vector3( center.z, center.x, center.y ) * 40f;
		var rot = new Rotation( quat.z, quat.x, quat.y, quat.w );
		return new Transform( pos, rot );
	}

	public static Rotation QuatFromMatrix(
		float m00, float m01, float m02,
		float m10, float m11, float m12,
		float m20, float m21, float m22 )
	{
		float tr = m00 + m11 + m22;
		float x, y, z, w;
		if ( tr > 0 )
		{
			float s = MathF.Sqrt( tr + 1f ) * 2f;
			w = 0.25f * s;
			x = (m21 - m12) / s;
			y = (m02 - m20) / s;
			z = (m10 - m01) / s;
		}
		else if ( m00 > m11 && m00 > m22 )
		{
			float s = MathF.Sqrt( 1f + m00 - m11 - m22 ) * 2f;
			w = (m21 - m12) / s;
			x = 0.25f * s;
			y = (m01 + m10) / s;
			z = (m02 + m20) / s;
		}
		else if ( m11 > m22 )
		{
			float s = MathF.Sqrt( 1f + m11 - m00 - m22 ) * 2f;
			w = (m02 - m20) / s;
			x = (m01 + m10) / s;
			y = 0.25f * s;
			z = (m12 + m21) / s;
		}
		else
		{
			float s = MathF.Sqrt( 1f + m22 - m00 - m11 ) * 2f;
			w = (m10 - m01) / s;
			x = (m02 + m20) / s;
			y = (m12 + m21) / s;
			z = 0.25f * s;
		}
		return new Rotation( x, y, z, w );
	}

	static Vector3 ReadVec3( System.IO.BinaryReader r ) => new( r.ReadSingle(), r.ReadSingle(), r.ReadSingle() );
	static Vector4 ReadVec4( System.IO.BinaryReader r ) => new( r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle() );
}
