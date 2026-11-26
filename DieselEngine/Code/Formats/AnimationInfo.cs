namespace Diesel;

/// <summary>An authored marker. Time is seconds from the source clip's start.</summary>
public sealed record PaydayAnimationCue( float Time, string Name );

/// <summary>Read-only source metadata; does not load a model, execute events or select a character rig.</summary>
public sealed record PaydayAnimationInfo( string Source, float Duration, int TrackCount, IReadOnlyList<PaydayAnimationCue> Cues );

/// <summary>A source animation group and controllers attached to its explicitly listed objects.</summary>
/// <param name="Enabled">Authored expression, or null when omitted.</param>
/// <param name="Speed">Authored expression, not an evaluated gameplay value.</param>
/// <param name="Time">Authored initial-time expression, such as "0/30".</param>
public sealed record PaydayAnimationGroup( string Name, IReadOnlyList<string> Objects, int TrackCount,
	float StartTime, float EndTime, string Enabled, string Speed, string Time );

/// <summary>Embedded timeline metadata. Group expressions are preserved verbatim, not executed.</summary>
public sealed record PaydayUnitAnimationInfo( string Unit, int TrackCount, float StartTime, float EndTime,
	IReadOnlyList<PaydayAnimationGroup> Groups );
