using Sandbox.Animation;

namespace Diesel;

partial class PaydayCharacter
{
	Skeleton skeleton;

	public Skeleton GetSkeleton( string modelPath )
	{
		if ( skeleton is not null ) return skeleton;
		var builder = new SkeletonBuilder { IsProp = scripted };
		foreach ( var bone in Bones )
		{
			var parent = bone.Parent == 0 ? null : Bones.First( b => b.Id == bone.Parent ).Name;
			var local = bone.Parent == 0 ? Bind[bone.Id] : Bind[bone.Parent].ToLocal( Bind[bone.Id] );
			builder.AddBone( bone.Name, local, parent );
		}
		return skeleton = builder.Create( $"{modelPath}/rig.skel" );
	}

	/// <summary>Sample source tracks into native parent-local frames in the character's root-point space.</summary>
	public Clip BuildClip( BundleDatabase db, string source, string modelPath, string clipPath )
	{
		var bytes = db.Find( "animation", source ) ?? throw new InvalidDataException( $"Missing character animation: {source}" );
		var animation = new AnimationFile( bytes );
		if ( !File.RigNodes.Any( b => animation.Tracks.ContainsKey( b.Name ) ) )
			throw new InvalidDataException( $"Animation {source} has no tracks matching this character's rig." );
		var target = GetSkeleton( modelPath );
		var builder = new ClipBuilder { Skeleton = target };
		// Include both endpoints and preserve the source duration exactly.
		int intervals = Math.Max( 1, (int)MathF.Ceiling( animation.Duration * 30 ) );
		builder.FrameRate = intervals / animation.Duration;
		var firstPose = File.Pose( animation, 0, preserveBoneLengths: !scripted );
		System.Numerics.Matrix4x4.Invert( File.NodePose( firstPose, orientation ), out var firstRoot );
		var byName = Bones.ToDictionary( b => b.Name );
		var frame = new Transform[target.BoneCount];
		for ( int i = 0; i <= intervals; i++ )
		{
			var sourcePose = File.Pose( animation, i * animation.Duration / intervals, preserveBoneLengths: !scripted );
			// Diesel root_point carries the authored world orientation as well as travel.
			// Normalize it like the NPC sampler, rather than tipping the whole character.
			System.Numerics.Matrix4x4.Invert( File.NodePose( sourcePose, orientation ), out var root );
			var pose = sourcePose.ToDictionary( p => p.Key, p => ModelFile.Convert( p.Value * root ) );
			foreach ( var bone in target.Bones )
			{
				var node = byName[bone.Name];
				frame[bone.Index] = node.Parent == 0 ? pose[node.Id] : pose[node.Parent].ToLocal( pose[node.Id] );
			}
			// Keep world travel separate so graphs can opt into root motion without changing the skin pose.
			builder.AddFrame( frame, ModelFile.Convert( File.NodePose( sourcePose, orientation ) * firstRoot ) );
		}
		return builder.Create( clipPath );
	}

	/// <summary>Full embedded controller timeline in authored model space, including vehicle travel.</summary>
	public Clip BuildEmbeddedClip( string modelPath, string clipPath )
	{
		var tracks = File.Controllers( File.RigNodes.Select( b => b.Name ).ToHashSet() );
		float duration = tracks.Values.SelectMany( t => t.PositionTimes.Concat( t.RotationTimes ) ).DefaultIfEmpty().Max();
		if ( duration <= 0 ) throw new InvalidDataException( "This model has no timed embedded motion." );
		var target = GetSkeleton( modelPath );
		int intervals = Math.Max( 1, (int)MathF.Ceiling( duration * 30 ) );
		var builder = new ClipBuilder { Skeleton = target, FrameRate = intervals / duration };
		var byName = Bones.ToDictionary( b => b.Name );
		var frame = new Transform[target.BoneCount];
		for ( int i = 0; i <= intervals; i++ )
		{
			// Do not retarget vehicle offsets or remove the orientation node's motion:
			// the embedded controllers author translations as well as joint rotations.
			var pose = File.Pose( null, i * duration / intervals, tracks )
				.ToDictionary( p => p.Key, p => ModelFile.Convert( p.Value * bindRoot ) );
			foreach ( var bone in target.Bones )
			{
				var node = byName[bone.Name];
				frame[bone.Index] = node.Parent == 0 ? pose[node.Id] : pose[node.Parent].ToLocal( pose[node.Id] );
			}
			builder.AddFrame( frame );
		}
		return builder.Create( clipPath );
	}
}
