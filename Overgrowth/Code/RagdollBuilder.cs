// Adapted from Overgrowth's skeleton.cpp; modified for s&box.
// Copyright 2022 Wolfire Games LLC. Apache-2.0; see LICENSE.
using System;
using System.IO;

namespace Overgrowth;

static class RagdollBuilder
{
	const float Units = 40f;
	const float Degrees = 180f / MathF.PI;
	static readonly Vector3[] SpherePoints = CreateSpherePoints();

	record struct Shape( string Kind, FzxObject Object );
	record struct Joint( int A, int B, int Type, Vector3 Anchor, Vector3 Axis, float[] Limits );

	public static int[] Add( BaseFileSystem fs, ModelBuilder builder, SkeletonData skeleton, string fzxPath, Vector3 center, float scale, float massScale, out Dictionary<int, Vector3> bodyCenters )
	{
		int count = skeleton.Bones.Count;
		if ( skeleton.BoneMass.Count != count )
			throw new InvalidDataException( "Overgrowth skeleton has no authored bone masses." );
		var shapes = Enumerable.Range( 0, count ).Select( _ => new List<Shape>() ).ToArray();
		bool hasFzx = fs.FileExists( fzxPath );
		if ( hasFzx )
			LoadShapes( FzxLoader.Load( fs, fzxPath ), skeleton, shapes );

		var assigned = new bool[count];
		for ( int i = 0; i < skeleton.BoneWeights.Length; i++ )
			for ( int j = 0; j < 4; j++ )
				if ( skeleton.BoneWeights[i][j] > 0 )
					assigned[(int)skeleton.BoneIds[i][j]] = true;

		var fixedBones = new bool[count];
		var masses = skeleton.BoneMass.Select( mass => mass * massScale * massScale * massScale ).ToArray();
		var originalMasses = masses.ToArray();
		foreach ( var joint in skeleton.Joints.Where( x => x.Type == 2 ) )
		{
			if ( skeleton.HierParents[joint.BoneA] == joint.BoneB )
				FixBone( joint.BoneA, joint.BoneB );
			else if ( skeleton.HierParents[joint.BoneB] == joint.BoneA )
				FixBone( joint.BoneB, joint.BoneA );
		}
		foreach ( var label in new[] { "left_leg", "right_leg" } )
			if ( skeleton.IKBones.TryGetValue( label, out var leg ) )
				FixBone( leg.BoneId, Parent( leg.BoneId, 1 ) );
		foreach ( var label in new[] { "leftarm", "rightarm" } )
		{
			if ( !skeleton.IKBones.TryGetValue( label, out var arm ) )
				continue;
			int recipient = Parent( arm.BoneId, 3 );
			for ( int i = 0; i < count; i++ )
				if ( i == arm.BoneId || Parent( i, 1 ) == arm.BoneId || Parent( i, 2 ) == arm.BoneId )
					FixBone( i, recipient );
		}

		var joints = new Dictionary<(int, int), Joint>();
		for ( int a = 0; a < count; a++ )
			for ( int b = a + 1; b < count; b++ )
			{
				int shared = SharedPoint( skeleton.Bones[a], skeleton.Bones[b] );
				if ( shared >= 0 )
					joints[(a, b)] = new Joint( a, b, -1, Convert( skeleton.Points[shared] ) * scale, default, null );
			}
		var tail = new HashSet<int>();
		if ( skeleton.IKBones.TryGetValue( "tail", out var tailChain ) )
			for ( int i = 0; i < tailChain.ChainLength; i++ )
				tail.Add( Parent( tailChain.BoneId, i ) );
		foreach ( var source in skeleton.Joints )
		{
			if ( source.BoneA == source.BoneB )
			{
				Log.Warning( $"Overgrowth joint connects bone {source.BoneA} to itself; skipping it as Overgrowth does." );
				continue;
			}
			int point = SharedPoint( skeleton.Bones[source.BoneA], skeleton.Bones[source.BoneB] );
			if ( point < 0 )
			{
				var bone = skeleton.Bones[source.BoneA];
				point = skeleton.PointParents.Count > bone.A && skeleton.PointParents[bone.A] == bone.B ? bone.B : bone.A;
			}
			var limits = source.StopAngles?.ToArray();
			if ( source.Type == 1 && !tail.Contains( source.BoneA ) && !tail.Contains( source.BoneB ) )
				for ( int i = 0; i < 6; i += 2 )
				{
					float mid = (limits[i] + limits[i + 1]) * 0.5f;
					limits[i] = (limits[i] + mid) * 0.5f;
					limits[i + 1] = (limits[i + 1] + mid) * 0.5f;
				}
			joints[(Math.Min( source.BoneA, source.BoneB ), Math.Max( source.BoneA, source.BoneB ))] =
				new Joint( source.BoneA, source.BoneB, source.Type, Convert( skeleton.Points[point] ) * scale, Convert( source.Axis ) / Units, limits );
		}

		bodyCenters = new();
		var bodyIds = Enumerable.Repeat( -1, count ).ToArray();
		var sourcePoses = new Transform[count];
		int bodyCount = 0;
		int nonColliding = 0;
		int ellipsoids = 0;
		for ( int i = 0; i < count; i++ )
		{
			var pose = skeleton.BindPose[i];
			if ( shapes[i].Count > 0 )
			{
				var weightedCenter = Vector3.Zero;
				float totalVolume = 0;
				foreach ( var shape in shapes[i] )
				{
					float volume = Volume( shape, scale );
					weightedCenter += (Convert( shape.Object.Position ) - center) * scale * volume;
					totalVolume += volume;
				}
				if ( !float.IsFinite( totalVolume ) || totalVolume <= 0 )
					throw new InvalidDataException( $"Invalid Overgrowth shape volume for bone {i} in {fzxPath}." );
				pose = pose.WithPosition( weightedCenter / totalVolume );
			}
			sourcePoses[i] = pose;
			if ( fixedBones[i] || !assigned[i] )
				continue;
			bodyIds[i] = bodyCount++;
			bodyCenters.Add( i, pose.Position );
			var bindPose = skeleton.BindPose[i];
			var body = builder.AddBody( masses[i], boneName: $"bone_{i}" ).SetBindPose( bindPose );
			foreach ( var shape in shapes[i] )
				if ( AddShape( body, shape, bindPose, center, scale ) )
					ellipsoids++;
			if ( shapes[i].Count == 0 )
			{
				if ( hasFzx )
					nonColliding++;
				var bone = skeleton.Bones[i];
				body.AddCapsule( new Capsule( bindPose.PointToLocal( Convert( skeleton.Points[bone.A] ) * scale ),
					bindPose.PointToLocal( Convert( skeleton.Points[bone.B] ) * scale ), 0.05f * Units ) );
			}
		}

		int angularJoints = 0;
		foreach ( var source in joints.Values )
		{
			int a = ResolveFixed( source.A );
			int b = ResolveFixed( source.B );
			if ( a < 0 || b < 0 || a == b || bodyIds[a] < 0 || bodyIds[b] < 0 )
				continue;
			var rotation = Rotation.Identity;
			if ( source.Type == 0 )
			{
				if ( source.Axis.LengthSquared < 0.000001f )
					throw new InvalidDataException( $"Overgrowth hinge has a camera-dependent zero axis in {fzxPath}." );
				var axis = source.Axis.Normal;
				var reference = MathF.Abs( axis.z ) > 0.95f ? Vector3.Forward : Vector3.Up;
				rotation = Rotation.LookAt( Vector3.Cross( reference, axis ), axis );
			}
			else if ( source.Type == 1 )
			{
				var x = (sourcePoses[source.A].Position - source.Anchor).Normal;
				var right = Vector3.Cross( x, new Vector3( 0.000003f, 0.0001f, 1.0001f ) ).Normal;
				var up = Vector3.Cross( x, right ).Normal;
				right = Vector3.Cross( x, up ).Normal;
				var z = Vector3.Cross( x, right );
				rotation = PhxbnLoader.QuatFromMatrix( right.x, z.x, x.x, right.y, z.y, x.y, right.z, z.z, x.z );
			}
			var frame = new Transform( source.Anchor, rotation );
			var frameA = skeleton.BindPose[a].ToLocal( frame );
			var frameB = skeleton.BindPose[b].ToLocal( frame );
			switch ( source.Type )
			{
				case -1:
					builder.AddBallJoint( bodyIds[a], bodyIds[b], frameA, frameB );
					break;
				case 0:
					builder.AddHingeJoint( bodyIds[a], bodyIds[b], frameA, frameB )
						.WithTwistLimit( Math.Min( source.Limits[0], source.Limits[1] ) * Degrees, Math.Max( source.Limits[0], source.Limits[1] ) * Degrees );
					break;
				case 1:
					var limits = source.Limits;
					float swing = Math.Max( Math.Max( Math.Abs( limits[2] ), Math.Abs( limits[3] ) ),
						Math.Max( Math.Abs( limits[4] ), Math.Abs( limits[5] ) ) ) * Degrees;
					builder.AddBallJoint( bodyIds[a], bodyIds[b], frameA, frameB ).WithSwingLimit( swing )
						.WithTwistLimit( Math.Min( limits[0], limits[1] ) * Degrees, Math.Max( limits[0], limits[1] ) * Degrees );
					angularJoints++;
					break;
				case 2:
					builder.AddFixedJoint( bodyIds[a], bodyIds[b], frameA, frameB );
					break;
				default:
					throw new InvalidDataException( $"Unknown Overgrowth joint type {source.Type}." );
			}
		}
		if ( angularJoints > 0 || ellipsoids > 0 || nonColliding > 0 )
			Log.Warning( $"Overgrowth physics {fzxPath}: {angularJoints} three-axis limits use swing/twist joints; {ellipsoids} ellipsoids use convex hulls; {nonColliding} source non-colliding capsules retain collisions because model-builder body filters are unavailable." );
		return shapes.Where( (_, i) => bodyIds[i] >= 0 ).Select( x => Math.Max( 1, x.Count ) ).ToArray();

		int Parent( int bone, int steps )
		{
			for ( int i = 0; i < steps && bone >= 0; i++ )
				bone = skeleton.HierParents[bone];
			return bone;
		}
		void FixBone( int bone, int recipient )
		{
			if ( bone < 0 || recipient < 0 )
				throw new InvalidDataException( "Overgrowth fixed bone has no mass recipient." );
			fixedBones[bone] = true;
			masses[recipient] += originalMasses[bone];
		}
		int ResolveFixed( int bone )
		{
			while ( bone >= 0 && fixedBones[bone] && assigned[bone] )
				bone = skeleton.HierParents[bone];
			return bone;
		}
	}

	static void LoadShapes( List<FzxObject> objects, SkeletonData skeleton, List<Shape>[] shapes )
	{
		var symmetry = Enumerable.Repeat( -1, skeleton.Bones.Count ).ToArray();
		for ( int i = 0; i < symmetry.Length; i++ )
			for ( int j = i; j < symmetry.Length; j++ )
			{
				var a = skeleton.Points[skeleton.Bones[j].A];
				var b = skeleton.Points[skeleton.Bones[j].B];
				a.x = -a.x;
				b.x = -b.x;
				if ( (a - skeleton.Points[skeleton.Bones[i].A]).LengthSquared < 0.0001f &&
					(b - skeleton.Points[skeleton.Bones[i].B]).LengthSquared < 0.0001f )
					(symmetry[i], symmetry[j]) = (j, i);
			}
		foreach ( var obj in objects )
		{
			string side = null, part = null, kind = null;
			bool mirror = false;
			foreach ( var word in obj.Label.ToLowerInvariant().Split( ' ', StringSplitOptions.RemoveEmptyEntries ) )
			{
				if ( word is "l" or "r" ) side = word == "l" ? "left" : "right";
				else if ( word is "capsule" or "box" or "sphere" ) kind = word;
				else if ( word == "m" ) mirror = true;
				else if ( word is "eartip" or "earbase" or "head" or "chest" or "upperarm" or "forearm" or "abdomen" or "hip" or "thigh" or "shin" or "foot" or "tail1" or "tail2" or "tail3" or "tail4" or "tail5" or "tail6" ) part = word;
			}
			if ( kind is null || part is null )
				continue;
			var (label, step) = part switch
			{
				"eartip" => ($"{side}ear", 0),
				"earbase" => ($"{side}ear", 1),
				"upperarm" => ($"{side}arm", 5),
				"forearm" => ($"{side}arm", 3),
				"head" => ("head", 0),
				"chest" => ("torso", 0),
				"abdomen" => ("torso", 1),
				"hip" => ("torso", 2),
				"thigh" => ($"{side}_leg", 5),
				"shin" => ($"{side}_leg", 3),
				"foot" => ($"{side}_leg", 1),
				_ => ("tail", -int.Parse( part.AsSpan( 4 ) ))
			};
			if ( !skeleton.IKBones.TryGetValue( label, out var chain ) )
				throw new InvalidDataException( $"FZX shape '{obj.Label}' requires missing IK chain '{label}'." );
			if ( step < 0 ) step += chain.ChainLength;
			int bone = chain.BoneId;
			for ( int i = 0; i < step && bone >= 0; i++ )
				bone = skeleton.HierParents[bone];
			if ( bone < 0 )
				throw new InvalidDataException( $"FZX shape '{obj.Label}' extends beyond its IK chain." );
			shapes[bone].Add( new Shape( kind, obj ) );
			if ( mirror && symmetry[bone] >= 0 )
			{
				var mirrored = obj;
				mirrored.Position.x = -mirrored.Position.x;
				mirrored.Rotation = new Rotation( obj.Rotation.x, -obj.Rotation.y, -obj.Rotation.z, obj.Rotation.w );
				shapes[symmetry[bone]].Add( new Shape( kind, mirrored ) );
			}
		}
	}

	static float Volume( Shape shape, float scale )
	{
		var size = shape.Object.Scale * scale;
		return shape.Kind switch
		{
			"sphere" => 4f / 3f * 3.1417f * size.x * size.y * size.z,
			"capsule" => 4f / 3f * 3.1417f * size.x * size.x * size.x + 3.1417f * size.x * size.x * shape.Object.Scale.z * 2,
			"box" => 8 * size.x * size.y * size.z,
			_ => throw new InvalidDataException( "Invalid FZX shape type." )
		};
	}

	static bool AddShape( PhysicsBodyBuilder body, Shape shape, Transform pose, Vector3 center, float scale )
	{
		var obj = shape.Object;
		var rotation = new Rotation( obj.Rotation.z, obj.Rotation.x, obj.Rotation.y, obj.Rotation.w );
		var local = pose.ToLocal( new Transform( (Convert( obj.Position ) - center) * scale, rotation ) );
		var size = new Vector3( obj.Scale.y, obj.Scale.x, obj.Scale.z ) * (Units * scale);
		switch ( shape.Kind )
		{
			case "box":
				body.AddBox( size, local );
				break;
			case "capsule":
				body.AddCapsule( new Capsule( Vector3.Up * -size.z, Vector3.Up * size.z, size.y ), local );
				break;
			case "sphere" when MathF.Abs( size.x - size.y ) < 0.00001f && MathF.Abs( size.x - size.z ) < 0.00001f:
				body.AddSphere( new Sphere( Vector3.Zero, size.x ), local );
				break;
			case "sphere":
				body.AddHull( SpherePoints.Select( p => p * size ).ToArray(), local );
				return true;
		}
		return false;
	}

	static Vector3[] CreateSpherePoints()
	{
		float phi = (1 + MathF.Sqrt( 5 )) * 0.5f;
		var points = new List<Vector3>( 42 );
		foreach ( float a in new[] { -1f, 1f } )
			foreach ( float b in new[] { -phi, phi } )
			{
				points.Add( new Vector3( 0, a, b ).Normal );
				points.Add( new Vector3( a, b, 0 ).Normal );
				points.Add( new Vector3( b, 0, a ).Normal );
			}
		for ( int i = 0; i < 12; i++ )
			for ( int j = i + 1; j < 12; j++ )
				if ( Vector3.Dot( points[i], points[j] ) > 0 )
					points.Add( (points[i] + points[j]).Normal );
		return points.ToArray();
	}

	static int SharedPoint( (int A, int B) a, (int A, int B) b )
	{
		int point = -1;
		if ( a.A == b.A || a.A == b.B ) point = a.A;
		if ( a.B == b.A || a.B == b.B ) point = a.B;
		return point;
	}

	static Vector3 Convert( Vector3 value ) => new Vector3( value.z, value.x, value.y ) * Units;
}
