using System.Runtime.CompilerServices;
using System;

namespace Source1;

/// <summary>
/// A mounted model's sequence data, read once per model: the engine caches only the JSON, so every
/// GetData call deserializes it again. Names are deduplicated, the first sequence of a name wins.
/// </summary>
public sealed class ModelSequences
{
	static readonly ConditionalWeakTable<Model, ModelSequences> Cache = new();

	readonly Model _model;
	readonly Dictionary<string, SourceModelSequenceData> _byName = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, string> _activities = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, string> _gestures = new( StringComparer.OrdinalIgnoreCase );

	public IReadOnlyList<SourceModelSequenceData> All { get; }
	public IEnumerable<SourceModelSequenceData.Event> Events => All.SelectMany( sequence => sequence.Events );

	ModelSequences( Model model )
	{
		_model = model;
		All = model.GetData<SourceModelSequenceData[]>() ?? [];
		foreach ( var sequence in All )
			_byName.TryAdd( sequence.Name, sequence );
	}

	public static ModelSequences Of( Model model ) => Cache.GetValue( model, x => new ModelSequences( x ) );

	public SourceModelSequenceData Get( string name ) => name is not null && _byName.TryGetValue( name, out var sequence ) ? sequence : null;

	/// <summary>
	/// A clip's ground motion and turn from its start to <paramref name="time"/>, in its starting frame, along its path where it
	/// varies. A looping clip goes round lap after lap, unless <paramref name="loop"/> says otherwise; other clips stop at their end.
	/// </summary>
	public (Vector3 Position, float Yaw) Travelled( string clip, float duration, float time, bool? loop = null )
	{
		if ( Get( clip ) is not { } sequence || duration <= 0 )
			return default;

		var looping = loop ?? sequence.Looping;
		var loops = looping ? MathF.Floor( time / duration ) : 0;
		var cycle = looping ? time / duration - loops : Math.Clamp( time / duration, 0, 1 );
		if ( sequence.MovementPath is not { Length: > 1 } path )
			return (sequence.Movement * (loops + cycle), 0);

		var position = cycle * (path.Length - 1);
		var index = Math.Min( (int)position, path.Length - 2 );
		var t = position - index;
		var yaw = sequence.MovementYaw is { Length: > 1 } turn ? loops * turn[^1] + MathX.Lerp( turn[index], turn[index + 1], t ) : 0;
		return (sequence.Movement * loops + Vector3.Lerp( path[index], path[index + 1], t ), yaw);
	}

	/// <summary>Every full-body sequence for an activity that the model has a clip for.</summary>
	public string[] Clips( string activity ) =>
		All.Where( s => !s.Delta && s.Activity == activity && _model.AnimationClips().ContainsKey( s.Name ) ).Select( s => s.Name ).ToArray();

	/// <summary>The highest weighted full-body sequence with a clip for an activity; additive gesture layers are skipped.</summary>
	public string FindActivity( string activity, string fallback = null ) => Find( _activities, activity, delta: false ) ?? fallback;

	/// <summary>The additive delta sequence with a clip for an ACT_GESTURE_* activity.</summary>
	public string FindGesture( string activity ) => Find( _gestures, activity, delta: true );

	string Find( Dictionary<string, string> found, string activity, bool delta )
	{
		if ( string.IsNullOrEmpty( activity ) )
			return null;

		if ( found.TryGetValue( activity, out var name ) )
			return name;

		name = All.Where( x => x.Delta == delta && string.Equals( x.Activity, activity, StringComparison.OrdinalIgnoreCase ) && _model.AnimationClips().ContainsKey( x.Name ) )
			.OrderByDescending( x => x.ActivityWeight ).FirstOrDefault()?.Name;
		found[activity] = name;
		return name;
	}
}
