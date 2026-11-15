using System;

namespace Source1;

/// <summary>One slot per base activity an armed NPC plays: an activity name in an acttable row, or the clip it resolved to.</summary>
public sealed record SourceNpcActivities( string Idle, string AngryIdle, string Attack, string Reload, string Walk, string WalkAim, string Run, string RunAim )
{
	public IEnumerable<string> All => [Idle, AngryIdle, Attack, Reload, Walk, WalkAim, Run, RunAim];

	public string Walking( bool aiming ) => aiming ? WalkAim : Walk;
	public string Running( bool aiming ) => aiming ? RunAim : Run;
	public string Standing( bool aiming ) => aiming ? AngryIdle : Idle;
}

/// <summary>
/// The clips an armed NPC plays for its weapon: the full-body activities, and per stance (idle, walk, run) the layers over the legs,
/// a masked upper-body weapon pose, a masked reload and the additive shots. Made by <see cref="Hl2ActTable"/> or <see cref="CssPlayerAnimState"/>.
/// </summary>
public sealed record SourceNpcWeaponLayers( SourceNpcActivities Clips )
{
	public const int Stances = 3;

	public string[] Upper { get; init; } = new string[Stances];
	public string[] Reloads { get; init; } = new string[Stances];
	public string[][] Shots { get; init; } = [[], [], []];

	/// <summary>The reload loops a shell insert, timed per round rather than by its clip.</summary>
	public bool ReloadLoops { get; init; }
}
