using System;

namespace Source1;

/// <summary>What an animated model's skeleton and clips are created from.</summary>
internal sealed class SourceAnimations
{
	public const string SkeletonName = "skeleton.skel";
	public const string IkRelease = "ik_release";
	public static string ReleaseChannel( string bone ) => $"release:{bone}";
	public static string HoldRestClip( string name ) => $"__hold/{name}";

	public (string Name, string Parent, Transform Local)[] Bones { get; }
	public IReadOnlyList<(string Name, float[] Weights)> Masks { get; }
	public string[] IkEndBones { get; }
	public Dictionary<string, MdlFile.ImportedAnimation> Clips { get; } = new( StringComparer.OrdinalIgnoreCase );

	public SourceAnimations( (string Name, string Parent, Transform Local)[] bones, IReadOnlyList<(string Name, float[] Weights)> masks,
		IReadOnlyList<MdlFile.ImportedAnimation> clips, string[] ikEndBones )
	{
		Bones = bones;
		Masks = masks;
		IkEndBones = ikEndBones;
		foreach ( var clip in clips )
		{
			Clips.TryAdd( clip.Name, clip );
		}
	}

	// Both are resources below the model they come from.
	public static string SkeletonPath( string model ) => $"{model}/{SkeletonName}";
	public static string ClipFolder( string model ) => model;
}

internal sealed class SkeletonLoader : ResourceLoader<SourceGameMount>
{
	protected override object Load()
	{
		var animations = Host.Animations( Parent.RelativePath );
		if ( animations is null )
			return null;

		var builder = Sandbox.Animation.Skeleton.Builder;
		if ( animations.IkEndBones.Length > 0 ) builder.AddFloatChannelSet( SourceAnimations.IkRelease, Array.ConvertAll( animations.IkEndBones, SourceAnimations.ReleaseChannel ) );
		foreach ( var bone in animations.Bones )
		{
			builder.AddBone( bone.Name, bone.Local, bone.Parent );
		}

		foreach ( var mask in animations.Masks )
		{
			var weights = new Dictionary<string, float>();
			for ( var bone = 0; bone < animations.Bones.Length; bone++ )
			{
				weights[animations.Bones[bone].Name] = Math.Clamp( mask.Weights[bone], 0, 1 );
			}
			builder.AddBoneMask( mask.Name, weights );
		}

		return builder.Create( Path );
	}
}

internal sealed class ClipLoader( string name ) : ResourceLoader<SourceGameMount>
{
	protected override object Load()
	{
		var animations = Host.Animations( Parent.RelativePath );
		if ( animations is null || !animations.Clips.TryGetValue( name, out var clip ) )
			return null;

		var skeleton = Sandbox.Animation.Skeleton.Load( SourceAnimations.SkeletonPath( Parent.Path ) );
		if ( !skeleton.IsValid() )
			return null;

		MdlFile.BakedClip baked;
		try
		{
			baked = clip.ReadFrames();
		}
		catch ( Exception e ) when ( MdlFile.IsDataError( e ) )
		{
			Log.Warning( $"{Path}: animation was not decoded: {e.Message}" );
			return null;
		}

		if ( baked.Frames is not { Length: > 0 } )
			return null;

		var builder = Sandbox.Animation.Clip.Builder;
		builder.Skeleton = skeleton;
		builder.FrameRate = clip.FrameRate;
		builder.IsAdditive = clip.Delta;

		var bones = skeleton.Bones;
		var pose = new Transform[bones.Count];
		foreach ( var frame in baked.Frames )
		{
			for ( var bone = 0; bone < pose.Length; bone++ )
			{
				var local = bone < frame.Length ? frame[bone] : clip.Delta ? Transform.Zero : bones[bone].LocalTransform;
				// An additive clip stores scale as its difference from one.
				pose[bone] = clip.Delta ? local.WithScale( local.Scale.x - 1 ) : local;
			}

			builder.AddFrame( pose );
		}
		if ( baked.Releases is not null )
			foreach ( var (bone, samples) in baked.Releases )
				builder.AddFloatChannelTrack( SourceAnimations.IkRelease, SourceAnimations.ReleaseChannel( bone ), samples );

		return builder.Create( Path );
	}
}
