using System;

namespace Doom;

/// <summary>Vanilla sector specials (the low 5 bits of a Boom generalised special).</summary>
public enum SectorSpecial : ushort
{
	Normal = 0,

	/// <summary>Light flashes randomly (P_SpawnLightFlash).</summary>
	LightFlicker = 1,

	/// <summary>Light strobes, 0.5 seconds dark (P_SpawnStrobeFlash, FASTDARK).</summary>
	LightStrobeFast = 2,

	/// <summary>Light strobes, 1 second dark (SLOWDARK).</summary>
	LightStrobeSlow = 3,

	/// <summary>20% damage per second plus fast strobe.</summary>
	Damage20Strobe = 4,

	/// <summary>10% damage per second.</summary>
	Damage10 = 5,

	/// <summary>5% damage per second.</summary>
	Damage5 = 7,

	/// <summary>Light glows up and down.</summary>
	LightGlow = 8,

	/// <summary>Counts toward the secrets total when entered.</summary>
	Secret = 9,

	/// <summary>The ceiling closes 30 seconds after the level starts.</summary>
	DoorCloseIn30 = 10,

	/// <summary>20% damage per second; ends the level when health drops to 10% or less.</summary>
	ExitDamage20 = 11,

	/// <summary>Light strobes in sync with other sectors, 1 second dark.</summary>
	LightStrobeSlowSync = 12,

	/// <summary>Light strobes in sync with other sectors, 0.5 seconds dark.</summary>
	LightStrobeFastSync = 13,

	/// <summary>The door opens 5 minutes after the level starts.</summary>
	DoorRaiseIn5Minutes = 14,

	/// <summary>20% damage per second.</summary>
	Damage20 = 16,

	/// <summary>Light flickers like fire.</summary>
	LightFireFlicker = 17,
}

public static class SectorSpecials
{
	/// <summary>The vanilla part of a special. Boom generalised specials keep it in the low 5 bits.</summary>
	public static SectorSpecial Type( int special ) => (SectorSpecial)(special & 0x1F);

	/// <summary>Damage dealt every 32 tics to a player standing on the floor (P_PlayerInSpecialSector).</summary>
	public static int Damage( int special )
	{
		// Boom generalised: bits 5 and 6 hold 0, 5, 10 or 20.
		if ( special >= 32 )
		{
			return ((special >> 5) & 3) switch { 1 => 5, 2 => 10, 3 => 20, _ => 0 };
		}

		return Type( special ) switch
		{
			SectorSpecial.Damage5 => 5,
			SectorSpecial.Damage10 => 10,
			SectorSpecial.Damage20 or SectorSpecial.Damage20Strobe or SectorSpecial.ExitDamage20 => 20,
			_ => 0
		};
	}

	public static bool IsSecret( int special ) => special >= 32 ? (special & 0x80) != 0 : Type( special ) == SectorSpecial.Secret;
}

/// <summary>A sector's floor and ceiling height, in map units.</summary>
public readonly record struct SectorHeights( float Floor, float Ceiling );

/// <summary>
/// Where vanilla's movers send a floor or ceiling, worked out from the sector and its neighbours
/// (the P_Find* functions in p_spec.c). Shared by the map loader, which needs to know how far
/// planes can travel, and by <see cref="DoomSector"/>, which moves them.
/// </summary>
public static class SectorMoves
{
	public const float DoorGap = 4f;
	public const float CrushGap = 8f;

	/// <summary>P_FindLowestFloorSurrounding: includes the sector's own floor.</summary>
	public static float LowestFloor( SectorHeights self, IEnumerable<SectorHeights> neighbours )
		=> neighbours.Aggregate( self.Floor, ( min, n ) => MathF.Min( min, n.Floor ) );

	/// <summary>P_FindHighestFloorSurrounding: neighbours only, -500 if there are none.</summary>
	public static float HighestFloor( IEnumerable<SectorHeights> neighbours )
		=> neighbours.Aggregate( -500f, ( max, n ) => MathF.Max( max, n.Floor ) );

	/// <summary>P_FindNextHighestFloor: the lowest neighbouring floor above <paramref name="height"/>, else the height itself.</summary>
	public static float NextHigherFloor( float height, IEnumerable<SectorHeights> neighbours )
	{
		float next = float.MaxValue;
		foreach ( var n in neighbours )
			if ( n.Floor > height ) next = MathF.Min( next, n.Floor );
		return next == float.MaxValue ? height : next;
	}

	/// <summary>P_FindLowestCeilingSurrounding: neighbours only, MAXINT if there are none.</summary>
	public static float LowestCeiling( IEnumerable<SectorHeights> neighbours )
		=> neighbours.Aggregate( float.MaxValue, ( min, n ) => MathF.Min( min, n.Ceiling ) );

	/// <summary>P_FindHighestCeilingSurrounding: neighbours only, 0 if there are none.</summary>
	public static float HighestCeiling( IEnumerable<SectorHeights> neighbours )
		=> neighbours.Aggregate( 0f, ( max, n ) => MathF.Max( max, n.Ceiling ) );

	/// <summary>Where a door's ceiling opens to: 4 below the lowest neighbouring ceiling.</summary>
	public static float DoorTop( SectorHeights self, IEnumerable<SectorHeights> neighbours )
	{
		float lowest = LowestCeiling( neighbours );
		return lowest == float.MaxValue ? self.Ceiling : lowest - DoorGap;
	}

	/// <summary>
	/// Every height an action can send this sector's planes to, given its current state. The map
	/// loader uses it to size the wall pieces that travel with a moving plane.
	/// </summary>
	public static IEnumerable<(bool Ceiling, float Height)> Reach( SectorAction action, int amount, SectorHeights self,
		IReadOnlyList<SectorHeights> neighbours, float shortestLowerTexture )
	{
		switch ( action )
		{
			case SectorAction.DoorRaise or SectorAction.DoorOpen or SectorAction.DoorClose or SectorAction.DoorCloseWaitOpen
				or SectorAction.DoorBlazeRaise or SectorAction.DoorBlazeOpen or SectorAction.DoorBlazeClose:
				yield return (true, self.Floor);
				yield return (true, DoorTop( self, neighbours ));
				break;

			case SectorAction.FloorLowerToHighest:
				yield return (false, HighestFloor( neighbours ));
				break;

			case SectorAction.FloorTurboLower:
				var highest = HighestFloor( neighbours );
				yield return (false, highest != self.Floor ? highest + 8 : highest);
				break;

			case SectorAction.FloorLowerToLowest or SectorAction.FloorLowerAndChange:
				yield return (false, LowestFloor( self, neighbours ));
				break;

			case SectorAction.FloorRaiseToLowestCeiling or SectorAction.FloorRaiseCrush:
				yield return (false, RaiseFloorTarget( self, neighbours, action == SectorAction.FloorRaiseCrush ));
				break;

			case SectorAction.FloorRaiseToNearest or SectorAction.FloorRaiseTurbo or SectorAction.PlatRaiseToNearestAndChange:
				yield return (false, NextHigherFloor( self.Floor, neighbours ));
				break;

			case SectorAction.FloorRaise24 or SectorAction.FloorRaise24AndChange:
				yield return (false, self.Floor + 24);
				break;

			case SectorAction.FloorRaise512:
				yield return (false, self.Floor + 512);
				break;

			case SectorAction.FloorRaiseToTexture:
				yield return (false, self.Floor + shortestLowerTexture);
				break;

			case SectorAction.PlatRaiseAndChange:
				yield return (false, self.Floor + amount);
				break;

			case SectorAction.PlatDownWaitUpStay or SectorAction.PlatBlazeDownWaitUpStay:
				yield return (false, MathF.Min( LowestFloor( self, neighbours ), self.Floor ));
				break;

			case SectorAction.PlatPerpetualRaise:
				yield return (false, MathF.Min( LowestFloor( self, neighbours ), self.Floor ));
				yield return (false, MathF.Max( HighestFloor( neighbours ), self.Floor ));
				break;

			case SectorAction.CeilingLowerToFloor:
				yield return (true, self.Floor);
				break;

			case SectorAction.CeilingLowerAndCrush or SectorAction.CeilingCrushAndRaise
				or SectorAction.CeilingFastCrushAndRaise or SectorAction.CeilingSilentCrushAndRaise:
				yield return (true, self.Floor + CrushGap);
				break;

			case SectorAction.CeilingRaiseToHighest:
				yield return (true, HighestCeiling( neighbours ));
				break;
		}
	}

	/// <summary>raiseFloor / raiseFloorCrush: the lowest neighbouring ceiling, no higher than its own, 8 lower if crushing.</summary>
	public static float RaiseFloorTarget( SectorHeights self, IEnumerable<SectorHeights> neighbours, bool crush )
	{
		float target = MathF.Min( LowestCeiling( neighbours ), self.Ceiling );
		return crush ? target - CrushGap : target;
	}
}
