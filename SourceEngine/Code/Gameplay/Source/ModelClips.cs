using System.Collections;
using System.Runtime.CompilerServices;
using System;
using Sandbox.Animation;

namespace Source1;

/// <summary>
/// A mounted model's animation skeleton and clips. The mount offers them as resources of their own,
/// named by the model's animation data; a clip is loaded the first time it is looked up.
/// </summary>
public sealed class ModelClips : IReadOnlyDictionary<string, Clip>
{
	static readonly ConditionalWeakTable<Model, ModelClips> Cache = new();
	static readonly ModelClips None = new( null );

	readonly SourceModelAnimationData _data;
	readonly HashSet<string> _names;
	readonly Dictionary<string, Clip> _loaded = new( StringComparer.OrdinalIgnoreCase );
	Skeleton _skeleton;
	bool _skeletonLoaded;
	Graph _pickupPose;
	Dictionary<string, Graph> _sequenceGraphs;
	List<PlaybackGraph> _playbackGraphs;

	sealed record PlaybackGraph( (string Name, bool Loop)[] Choices, Graph Graph );

	ModelClips( SourceModelAnimationData data )
	{
		_data = data;
		_names = new( data?.Clips ?? [], StringComparer.OrdinalIgnoreCase );
	}

	public static ModelClips Of( Model model ) => model is null ? None : Cache.GetValue( model, x => new ModelClips( x.GetData<SourceModelAnimationData>() ) );

	internal string[] GroundBones => _data?.GroundBones ?? [];

	public Skeleton Skeleton
	{
		get
		{
			if ( _skeletonLoaded || string.IsNullOrEmpty( _data?.Skeleton ) ) return _skeleton;
			_skeletonLoaded = true;
			return _skeleton = Skeleton.Load( _data.Skeleton );
		}
	}

	internal Graph GetPlaybackGraph( (string Name, bool Loop)[] choices, Func<Graph> build )
	{
		_playbackGraphs ??= [];
		foreach ( var entry in _playbackGraphs )
			if ( choices.SequenceEqual( entry.Choices ) ) return entry.Graph;

		var graph = build();
		_playbackGraphs.Add( new( choices, graph ) );
		return graph;
	}

	internal Graph SequenceGraph( SourceModelSequenceData sequence )
	{
		if ( sequence is null || !TryGetValue( sequence.Name, out var clip ) || Skeleton is null ) return null;
		_sequenceGraphs ??= new( StringComparer.OrdinalIgnoreCase );
		if ( _sequenceGraphs.TryGetValue( sequence.Name, out var graph ) ) return graph;

		var builder = Graph.Builder;
		builder.WithSkeleton( Skeleton );
		var playback = builder.Clip( clip, new() { Looping = sequence.Looping, SampleRootMotion = false } );
		return _sequenceGraphs[sequence.Name] = builder.Create( playback, sequence.Name );
	}

	// Dropped weapons use the first frame of their world model's default sequence.
	internal Graph PickupPose( Model model )
	{
		if ( _pickupPose is not null ) return _pickupPose;
		var sequence = ModelSequences.Of( model ).All.FirstOrDefault();
		if ( !TryGetValue( sequence?.Name, out var clip ) || Skeleton is null ) return null;

		var builder = Graph.Builder;
		builder.WithSkeleton( Skeleton );
		return _pickupPose = builder.Create( builder.Pose( clip, new GraphBuilder.PoseSettings() ), "Weapon pickup pose" );
	}

	public bool TryGetValue( string key, out Clip value )
	{
		value = null;
		if ( key is null || !_names.Contains( key ) ) return false;
		if ( !_loaded.TryGetValue( key, out value ) )
		{
			value = Clip.Load( $"{_data.ClipFolder}/{key}.anim" );
			if ( value is null ) _names.Remove( key );
			else _loaded[key] = value;
		}

		return value is not null;
	}

	Dictionary<Clip, Clip> _reloadGrips;

	internal Clip ReloadGrip( Clip action, SourceModelSequenceData sequence, int helper, int driver )
	{
		_reloadGrips ??= [];
		if ( _reloadGrips.TryGetValue( action, out var result ) ) return result;
		var source = action.Skeleton.Bones;
		int Hand( string name ) => source.FirstOrDefault( b => b.Name == (sequence.IkTargets.FirstOrDefault( x => x.Bone == name )?.TargetBone ?? name) )?.Index ?? -1;
		var right = Hand( WeaponGrip.Right );
		var left = Hand( WeaponGrip.Left );
		var parent = Skeleton.Bones[Skeleton.Bones[driver].ParentIndex].Name;
		var chest = source.FirstOrDefault( b => b.Name == parent );
		if ( right < 0 || left < 0 || chest is null ) return null;

		var arms = (from joint in source where WeaponGrip.IsArm( joint.Name )
			join bone in Skeleton.Bones on joint.Name equals bone.Name select (joint.Index, Target: bone.Index)).ToArray();
		var frame = Skeleton.Bones.Select( b => b.LocalTransform ).ToArray();
		var builder = new ClipBuilder { Skeleton = Skeleton, FrameRate = action.FrameRate };
		for ( var i = 0; i < action.FrameCount; i++ )
		{
			var cycle = i / (float)Math.Max( 1, action.FrameCount - 1 );
			Transform Sample( int bone )
			{
				var transform = action.GetLocalTransform( cycle, bone );
				for ( var parent = source[bone].ParentIndex; parent >= 0; parent = source[parent].ParentIndex )
					transform = action.GetLocalTransform( cycle, parent ).ToWorld( transform );
				return transform;
			}
			var wrist = Sample( right );
			frame[driver] = Sample( chest.Index ).ToLocal( wrist );
			frame[helper] = wrist.ToLocal( Sample( left ) );
			foreach ( var (joint, target) in arms )
				frame[target] = frame[target].WithRotation( action.GetLocalTransform( cycle, joint ).Rotation );
			builder.AddFrame( frame );
		}

		return _reloadGrips[action] = builder.Create();
	}

	readonly Dictionary<Clip, Clip> _fingerPoses = new();

	internal Clip FingerPose( Clip hold, int? helper, Transform support )
	{
		if ( _fingerPoses.TryGetValue( hold, out var pose ) ) return pose;
		var fingers = hold.Skeleton.Bones.Where( b => SourceAnimations.IsFinger( b.Name ) ).ToDictionary( b => b.Name );
		var frame = Skeleton.Bones.Select( b => fingers.TryGetValue( b.Name, out var finger )
			? b.LocalTransform.WithRotation( hold.GetLocalTransform( 0, finger.Index ).Rotation ) : b.LocalTransform ).ToArray();
		if ( helper is { } index ) frame[index] = support;
		var builder = new ClipBuilder { Skeleton = Skeleton };
		pose = builder.AddFrame( frame ).Create();
		_fingerPoses.Add( hold, pose );
		return pose;
	}

	public Clip this[string key] => TryGetValue( key, out var clip ) ? clip : throw new KeyNotFoundException( $"The model has no animation clip '{key}'." );
	public bool ContainsKey( string key ) => key is not null && _names.Contains( key );
	public int Count => _names.Count;
	public IEnumerable<string> Keys => _names;
	public IEnumerable<Clip> Values => _names.ToArray().Select( name => TryGetValue( name, out var clip ) ? clip : null ).Where( clip => clip is not null );
	public IEnumerator<KeyValuePair<string, Clip>> GetEnumerator() => _names.ToArray()
		.Select( name => new KeyValuePair<string, Clip>( name, TryGetValue( name, out var clip ) ? clip : null ) ).Where( x => x.Value is not null ).GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public static class ModelClipExtensions
{
	public static ModelClips AnimationClips( this Model model ) => ModelClips.Of( model );
	public static Skeleton AnimationSkeleton( this Model model ) => ModelClips.Of( model ).Skeleton;
}
