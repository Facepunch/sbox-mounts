namespace Doom;

/// <summary>How a linedef special is set off.</summary>
public enum LineTrigger
{
	/// <summary>Not triggered by anything (scrollers and other continuous effects).</summary>
	None,

	/// <summary>W: the player crosses the line.</summary>
	Walk,

	/// <summary>S: the player uses (presses) the front of the line, usually a switch.</summary>
	Use,

	/// <summary>G: the player shoots the line.</summary>
	Shoot,

	/// <summary>D: a door used directly. Acts on the sector behind the line, not on a tag.</summary>
	Door,
}

public enum DoomKey { None, Blue, Yellow, Red }

/// <summary>
/// What a special does, named after vanilla's EV_* calls and their floor_e, ceiling_e, vldoor_e and
/// plattype_e types. <see cref="DoomSector"/> runs them; <see cref="SectorMoves"/> works out where they go.
/// </summary>
public enum SectorAction
{
	None,

	// Doors (EV_DoDoor / EV_VerticalDoor). Doors move the ceiling.
	DoorRaise,
	DoorOpen,
	DoorClose,
	DoorCloseWaitOpen,
	DoorBlazeRaise,
	DoorBlazeOpen,
	DoorBlazeClose,

	// Floors (EV_DoFloor).
	FloorLowerToHighest,
	FloorLowerToLowest,
	FloorTurboLower,
	FloorLowerAndChange,
	FloorRaiseToLowestCeiling,
	FloorRaiseCrush,
	FloorRaiseToNearest,
	FloorRaiseTurbo,
	FloorRaise24,
	FloorRaise24AndChange,
	FloorRaise512,
	FloorRaiseToTexture,

	// Ceilings (EV_DoCeiling, EV_CeilingCrushStop).
	CeilingLowerToFloor,
	CeilingLowerAndCrush,
	CeilingRaiseToHighest,
	CeilingCrushAndRaise,
	CeilingFastCrushAndRaise,
	CeilingSilentCrushAndRaise,
	CeilingCrushStop,

	// Lifts (EV_DoPlat, EV_StopPlat). Amount is the raise height for PlatRaiseAndChange.
	PlatDownWaitUpStay,
	PlatBlazeDownWaitUpStay,
	PlatPerpetualRaise,
	PlatRaiseAndChange,
	PlatRaiseToNearestAndChange,
	PlatStop,

	// Everything else.
	Stairs8,
	Stairs16Turbo,
	Donut,
	Teleport,
	Exit,
	SecretExit,

	/// <summary>Light to Amount, or to the brightest neighbour when Amount is 0 (EV_LightTurnOn).</summary>
	LightTurnOn,

	/// <summary>Light to the darkest neighbour (EV_TurnTagLightsOff).</summary>
	LightTurnOff,

	LightStartStrobing,

	/// <summary>Not an action: the wall's texture scrolls left one unit per tic.</summary>
	ScrollTextureLeft,
}

/// <summary>A linedef special, the way vanilla's p_spec.c and p_switch.c treat it.</summary>
/// <param name="Id">The special number stored on the linedef.</param>
/// <param name="Trigger">How it is set off.</param>
/// <param name="Repeat">If false the special is cleared after it fires (once for walk lines, once it succeeds for switches).</param>
/// <param name="Action">What it does.</param>
/// <param name="Secondary">A second action run on the same tag, after the first.</param>
/// <param name="Amount">Extra parameter: plat raise height or light level.</param>
/// <param name="Monsters">Monsters can trigger it too.</param>
/// <param name="Key">Key needed to use it.</param>
public sealed record LineSpecial( int Id, LineTrigger Trigger, bool Repeat, SectorAction Action,
	SectorAction Secondary = SectorAction.None, int Amount = 0, bool Monsters = false, DoomKey Key = DoomKey.None )
{
	/// <summary>True if the special acts on tagged sectors, so it is meaningless without a tag.</summary>
	public bool UsesTag => Trigger is not LineTrigger.Door and not LineTrigger.None
		&& Action is not (SectorAction.Exit or SectorAction.SecretExit);
}

public static class LineSpecials
{
	/// <summary>Every vanilla Doom and Doom II linedef special (1 to 141).</summary>
	public static readonly IReadOnlyDictionary<int, LineSpecial> Vanilla = Build(
	[
		// Doors used directly
		D( 1, true, SectorAction.DoorRaise, monsters: true ),
		D( 26, true, SectorAction.DoorRaise, key: DoomKey.Blue ),
		D( 27, true, SectorAction.DoorRaise, key: DoomKey.Yellow ),
		D( 28, true, SectorAction.DoorRaise, key: DoomKey.Red ),
		D( 31, false, SectorAction.DoorOpen ),
		D( 32, false, SectorAction.DoorOpen, key: DoomKey.Blue ),
		D( 33, false, SectorAction.DoorOpen, key: DoomKey.Red ),
		D( 34, false, SectorAction.DoorOpen, key: DoomKey.Yellow ),
		D( 117, true, SectorAction.DoorBlazeRaise ),
		D( 118, false, SectorAction.DoorBlazeOpen ),

		// Walk over, once
		W( 2, false, SectorAction.DoorOpen ),
		W( 3, false, SectorAction.DoorClose ),
		W( 4, false, SectorAction.DoorRaise, monsters: true ),
		W( 5, false, SectorAction.FloorRaiseToLowestCeiling ),
		W( 6, false, SectorAction.CeilingFastCrushAndRaise ),
		W( 8, false, SectorAction.Stairs8 ),
		W( 10, false, SectorAction.PlatDownWaitUpStay, monsters: true ),
		W( 12, false, SectorAction.LightTurnOn ),
		W( 13, false, SectorAction.LightTurnOn, amount: 255 ),
		W( 16, false, SectorAction.DoorCloseWaitOpen ),
		W( 17, false, SectorAction.LightStartStrobing ),
		W( 19, false, SectorAction.FloorLowerToHighest ),
		W( 22, false, SectorAction.PlatRaiseToNearestAndChange ),
		W( 25, false, SectorAction.CeilingCrushAndRaise ),
		W( 30, false, SectorAction.FloorRaiseToTexture ),
		W( 35, false, SectorAction.LightTurnOn, amount: 35 ),
		W( 36, false, SectorAction.FloorTurboLower ),
		W( 37, false, SectorAction.FloorLowerAndChange ),
		W( 38, false, SectorAction.FloorLowerToLowest ),
		W( 39, false, SectorAction.Teleport, monsters: true ),
		W( 40, false, SectorAction.CeilingRaiseToHighest, SectorAction.FloorLowerToLowest ),
		W( 44, false, SectorAction.CeilingLowerAndCrush ),
		W( 52, false, SectorAction.Exit ),
		W( 53, false, SectorAction.PlatPerpetualRaise ),
		W( 54, false, SectorAction.PlatStop ),
		W( 56, false, SectorAction.FloorRaiseCrush ),
		W( 57, false, SectorAction.CeilingCrushStop ),
		W( 58, false, SectorAction.FloorRaise24 ),
		W( 59, false, SectorAction.FloorRaise24AndChange ),
		W( 100, false, SectorAction.Stairs16Turbo ),
		W( 104, false, SectorAction.LightTurnOff ),
		W( 108, false, SectorAction.DoorBlazeRaise ),
		W( 109, false, SectorAction.DoorBlazeOpen ),
		W( 110, false, SectorAction.DoorBlazeClose ),
		W( 119, false, SectorAction.FloorRaiseToNearest ),
		W( 121, false, SectorAction.PlatBlazeDownWaitUpStay ),
		W( 124, false, SectorAction.SecretExit ),
		W( 125, false, SectorAction.Teleport, monsters: true ),
		W( 130, false, SectorAction.FloorRaiseTurbo ),
		W( 141, false, SectorAction.CeilingSilentCrushAndRaise ),

		// Walk over, repeatable
		W( 72, true, SectorAction.CeilingLowerAndCrush ),
		W( 73, true, SectorAction.CeilingCrushAndRaise ),
		W( 74, true, SectorAction.CeilingCrushStop ),
		W( 75, true, SectorAction.DoorClose ),
		W( 76, true, SectorAction.DoorCloseWaitOpen ),
		W( 77, true, SectorAction.CeilingFastCrushAndRaise ),
		W( 79, true, SectorAction.LightTurnOn, amount: 35 ),
		W( 80, true, SectorAction.LightTurnOn ),
		W( 81, true, SectorAction.LightTurnOn, amount: 255 ),
		W( 82, true, SectorAction.FloorLowerToLowest ),
		W( 83, true, SectorAction.FloorLowerToHighest ),
		W( 84, true, SectorAction.FloorLowerAndChange ),
		W( 86, true, SectorAction.DoorOpen ),
		W( 87, true, SectorAction.PlatPerpetualRaise ),
		W( 88, true, SectorAction.PlatDownWaitUpStay, monsters: true ),
		W( 89, true, SectorAction.PlatStop ),
		W( 90, true, SectorAction.DoorRaise ),
		W( 91, true, SectorAction.FloorRaiseToLowestCeiling ),
		W( 92, true, SectorAction.FloorRaise24 ),
		W( 93, true, SectorAction.FloorRaise24AndChange ),
		W( 94, true, SectorAction.FloorRaiseCrush ),
		W( 95, true, SectorAction.PlatRaiseToNearestAndChange ),
		W( 96, true, SectorAction.FloorRaiseToTexture ),
		W( 97, true, SectorAction.Teleport, monsters: true ),
		W( 98, true, SectorAction.FloorTurboLower ),
		W( 105, true, SectorAction.DoorBlazeRaise ),
		W( 106, true, SectorAction.DoorBlazeOpen ),
		W( 107, true, SectorAction.DoorBlazeClose ),
		W( 120, true, SectorAction.PlatBlazeDownWaitUpStay ),
		W( 126, true, SectorAction.Teleport, monsters: true ),
		W( 128, true, SectorAction.FloorRaiseToNearest ),
		W( 129, true, SectorAction.FloorRaiseTurbo ),

		// Switches, once
		S( 7, false, SectorAction.Stairs8 ),
		S( 9, false, SectorAction.Donut ),
		S( 11, false, SectorAction.Exit ),
		S( 14, false, SectorAction.PlatRaiseAndChange, amount: 32 ),
		S( 15, false, SectorAction.PlatRaiseAndChange, amount: 24 ),
		S( 18, false, SectorAction.FloorRaiseToNearest ),
		S( 20, false, SectorAction.PlatRaiseToNearestAndChange ),
		S( 21, false, SectorAction.PlatDownWaitUpStay ),
		S( 23, false, SectorAction.FloorLowerToLowest ),
		S( 29, false, SectorAction.DoorRaise ),
		S( 41, false, SectorAction.CeilingLowerToFloor ),
		S( 49, false, SectorAction.CeilingCrushAndRaise ),
		S( 50, false, SectorAction.DoorClose ),
		S( 51, false, SectorAction.SecretExit ),
		S( 55, false, SectorAction.FloorRaiseCrush ),
		S( 71, false, SectorAction.FloorTurboLower ),
		S( 101, false, SectorAction.FloorRaiseToLowestCeiling ),
		S( 102, false, SectorAction.FloorLowerToHighest ),
		S( 103, false, SectorAction.DoorOpen ),
		S( 111, false, SectorAction.DoorBlazeRaise ),
		S( 112, false, SectorAction.DoorBlazeOpen ),
		S( 113, false, SectorAction.DoorBlazeClose ),
		S( 122, false, SectorAction.PlatBlazeDownWaitUpStay ),
		S( 127, false, SectorAction.Stairs16Turbo ),
		S( 131, false, SectorAction.FloorRaiseTurbo ),
		S( 133, false, SectorAction.DoorBlazeOpen, key: DoomKey.Blue ),
		S( 135, false, SectorAction.DoorBlazeOpen, key: DoomKey.Red ),
		S( 137, false, SectorAction.DoorBlazeOpen, key: DoomKey.Yellow ),
		S( 140, false, SectorAction.FloorRaise512 ),

		// Switches, repeatable
		S( 42, true, SectorAction.DoorClose ),
		S( 43, true, SectorAction.CeilingLowerToFloor ),
		S( 45, true, SectorAction.FloorLowerToHighest ),
		S( 60, true, SectorAction.FloorLowerToLowest ),
		S( 61, true, SectorAction.DoorOpen ),
		S( 62, true, SectorAction.PlatDownWaitUpStay ),
		S( 63, true, SectorAction.DoorRaise ),
		S( 64, true, SectorAction.FloorRaiseToLowestCeiling ),
		S( 65, true, SectorAction.FloorRaiseCrush ),
		S( 66, true, SectorAction.PlatRaiseAndChange, amount: 24 ),
		S( 67, true, SectorAction.PlatRaiseAndChange, amount: 32 ),
		S( 68, true, SectorAction.PlatRaiseToNearestAndChange ),
		S( 69, true, SectorAction.FloorRaiseToNearest ),
		S( 70, true, SectorAction.FloorTurboLower ),
		S( 99, true, SectorAction.DoorBlazeOpen, key: DoomKey.Blue ),
		S( 114, true, SectorAction.DoorBlazeRaise ),
		S( 115, true, SectorAction.DoorBlazeOpen ),
		S( 116, true, SectorAction.DoorBlazeClose ),
		S( 123, true, SectorAction.PlatBlazeDownWaitUpStay ),
		S( 132, true, SectorAction.FloorRaiseTurbo ),
		S( 134, true, SectorAction.DoorBlazeOpen, key: DoomKey.Red ),
		S( 136, true, SectorAction.DoorBlazeOpen, key: DoomKey.Yellow ),
		S( 138, true, SectorAction.LightTurnOn, amount: 255 ),
		S( 139, true, SectorAction.LightTurnOn, amount: 35 ),

		// Shoot
		G( 24, false, SectorAction.FloorRaiseToLowestCeiling ),
		G( 46, true, SectorAction.DoorOpen ),
		G( 47, false, SectorAction.PlatRaiseToNearestAndChange ),

		// Continuous
		new( 48, LineTrigger.None, true, SectorAction.ScrollTextureLeft ),
	] );

	private static IReadOnlyDictionary<int, LineSpecial> Build( LineSpecial[] specials ) => specials.ToDictionary( s => s.Id );

	private static LineSpecial D( int id, bool repeat, SectorAction action, bool monsters = false, DoomKey key = DoomKey.None )
		=> new( id, LineTrigger.Door, repeat, action, Monsters: monsters, Key: key );

	private static LineSpecial W( int id, bool repeat, SectorAction action, SectorAction secondary = SectorAction.None, int amount = 0, bool monsters = false )
		=> new( id, LineTrigger.Walk, repeat, action, secondary, amount, monsters );

	private static LineSpecial S( int id, bool repeat, SectorAction action, int amount = 0, DoomKey key = DoomKey.None )
		=> new( id, LineTrigger.Use, repeat, action, Amount: amount, Key: key );

	private static LineSpecial G( int id, bool repeat, SectorAction action )
		=> new( id, LineTrigger.Shoot, repeat, action );
}
