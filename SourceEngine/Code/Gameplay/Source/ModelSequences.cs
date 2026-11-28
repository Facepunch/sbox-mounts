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
	readonly Dictionary<string, SourceModelSequenceData[]> _variants = new( StringComparer.OrdinalIgnoreCase );
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

	/// <summary>The highest weighted full-body sequence with a clip for an activity; additive gesture layers are skipped.</summary>
	public string FindActivity( string activity, string fallback = null ) => Find( _activities, activity, delta: false ) ?? fallback;

	internal string PickActivity( string activity, int seed )
	{
		if ( !_variants.TryGetValue( activity, out var variants ) )
		{
			variants = All.Where( s => !s.Delta && string.Equals( s.Activity, activity, StringComparison.OrdinalIgnoreCase )
				&& _model.AnimationClips().ContainsKey( s.Name ) ).ToArray();
			_variants[activity] = variants;
		}
		return variants.Length > 1 ? Pick( variants, new Random( seed ) ) : variants.FirstOrDefault()?.Name;
	}

	internal static string Pick( SourceModelSequenceData[] sequences, Random random, string current = null )
	{
		var total = 0;
		foreach ( var sequence in sequences )
		{
			if ( sequence.ActivityWeight < 0 && sequence.Name == current ) return sequence.Name;
			total += Math.Abs( sequence.ActivityWeight );
		}
		var choice = random.Next( total );
		foreach ( var sequence in sequences )
		{
			choice -= Math.Abs( sequence.ActivityWeight );
			if ( choice < 0 ) return sequence.Name;
		}
		return sequences.FirstOrDefault()?.Name;
	}

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
