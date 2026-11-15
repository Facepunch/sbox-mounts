using System.Collections;
using System.Runtime.CompilerServices;
using System;
using Sandbox.Animation;

namespace Source1;

/// <summary>
/// A mounted model's animation skeleton and clips. The mount offers them as resources of their own,
/// named by the model's animation data; a clip is loaded the first time it is looked up.
/// </summary>
public sealed class SourceModelClips : IReadOnlyDictionary<string, Clip>
{
	static readonly ConditionalWeakTable<Model, SourceModelClips> Cache = new();
	static readonly SourceModelClips None = new( null );

	readonly SourceModelAnimationData _data;
	readonly HashSet<string> _names;
	readonly Dictionary<string, Clip> _loaded = new( StringComparer.OrdinalIgnoreCase );
	Skeleton _skeleton;
	bool _skeletonLoaded;

	SourceModelClips( SourceModelAnimationData data )
	{
		_data = data;
		_names = new( data?.Clips ?? [], StringComparer.OrdinalIgnoreCase );
	}

	public static SourceModelClips Of( Model model ) => model is null ? None : Cache.GetValue( model, x => new SourceModelClips( x.GetData<SourceModelAnimationData>() ) );

	public Skeleton Skeleton
	{
		get
		{
			if ( _skeletonLoaded || string.IsNullOrEmpty( _data?.Skeleton ) ) return _skeleton;
			_skeletonLoaded = true;
			return _skeleton = Skeleton.Load( _data.Skeleton );
		}
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

	public Clip this[string key] => TryGetValue( key, out var clip ) ? clip : throw new KeyNotFoundException( $"The model has no animation clip '{key}'." );
	public bool ContainsKey( string key ) => key is not null && _names.Contains( key );
	public int Count => _names.Count;
	public IEnumerable<string> Keys => _names;
	public IEnumerable<Clip> Values => _names.ToArray().Select( name => TryGetValue( name, out var clip ) ? clip : null ).Where( clip => clip is not null );
	public IEnumerator<KeyValuePair<string, Clip>> GetEnumerator() => _names.ToArray()
		.Select( name => new KeyValuePair<string, Clip>( name, TryGetValue( name, out var clip ) ? clip : null ) ).Where( x => x.Value is not null ).GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public static class SourceModelClipExtensions
{
	public static SourceModelClips AnimationClips( this Model model ) => SourceModelClips.Of( model );
	public static Skeleton AnimationSkeleton( this Model model ) => SourceModelClips.Of( model ).Skeleton;
}
