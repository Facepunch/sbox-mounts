using System;

namespace Doom;

/// <summary>
/// The vanilla thinkers that move sectors: T_VerticalDoor, T_MoveFloor, T_MoveCeiling and
/// T_PlatRaise, with the speeds and waits from p_spec.h. Speeds are map units per tic.
/// Crushing and blocked movers reversing aren't simulated.
/// </summary>
partial class DoomSector
{
	public const float TicRate = 35f;

	const float FloorSpeed = 1f;
	const float CeilingSpeed = 1f;
	const float VDoorSpeed = 2f;
	const float PlatSpeed = 1f;

	/// <summary>VDOORWAIT, 150 tics.</summary>
	const float DoorWait = 150 / TicRate;

	/// <summary>PLATWAIT, 3 seconds.</summary>
	const float PlatWait = 3f;

	/// <summary>Floors and ceilings play the stone grinding sound every 8 tics while moving.</summary>
	const float StoneInterval = 8 / TicRate;

	private Mover _mover;

	/// <summary>
	/// Starts a tagged special on this sector (EV_DoDoor, EV_DoFloor, EV_DoCeiling, EV_DoPlat,
	/// EV_BuildStairs, EV_DoDonut). False if the sector is already busy or the action isn't a mover.
	/// </summary>
	public bool Start( SectorAction action, int amount = 0 )
	{
		switch ( action )
		{
			case SectorAction.CeilingCrushStop: return StopCrusher();
			case SectorAction.PlatStop: return StopPlat();
			case SectorAction.Stairs8: return BuildStairs( 8, FloorSpeed / 4 );
			case SectorAction.Stairs16Turbo: return BuildStairs( 16, FloorSpeed * 4 );
			case SectorAction.Donut: return StartDonut();
		}

		if ( IsBusy ) return false;

		_mover = CreateMover( action, amount );
		return _mover is not null;
	}

	/// <summary>
	/// EV_VerticalDoor: a door used directly. Using a raising door while it's open or opening closes
	/// it again, and using it while it closes reopens it. Only players close doors this way.
	/// </summary>
	public bool UseDoor( SectorAction action, bool repeatable, GameObject user )
	{
		if ( _mover is DoorMover door )
		{
			if ( !repeatable ) return false;

			if ( door.Direction == -1 )
				door.Direction = 1;
			else if ( user.IsValid() && user.GetComponentInParent<PlayerController>().IsValid() )
				door.Direction = -1;

			return true;
		}

		if ( IsBusy ) return false;

		_mover = CreateMover( action, 0 );
		return _mover is not null;
	}

	private Mover CreateMover( SectorAction action, int amount )
	{
		var self = Heights;
		var neighbours = NeighbourHeights.ToArray();

		switch ( action )
		{
			case SectorAction.DoorRaise or SectorAction.DoorOpen or SectorAction.DoorBlazeRaise or SectorAction.DoorBlazeOpen:
			{
				bool blaze = action is SectorAction.DoorBlazeRaise or SectorAction.DoorBlazeOpen;
				var kind = action switch
				{
					SectorAction.DoorRaise => DoorMover.Kind.Raise,
					SectorAction.DoorOpen => DoorMover.Kind.Open,
					SectorAction.DoorBlazeRaise => DoorMover.Kind.BlazeRaise,
					_ => DoorMover.Kind.BlazeOpen,
				};

				var door = new DoorMover( this, kind, blaze ? VDoorSpeed * 4 : VDoorSpeed ) { Direction = 1, Top = SectorMoves.DoorTop( self, neighbours ) };
				if ( door.Top != self.Ceiling )
					Play( blaze ? BlazeOpenSound : DoorOpenSound, true );
				return door;
			}

			case SectorAction.DoorClose or SectorAction.DoorBlazeClose:
			{
				bool blaze = action == SectorAction.DoorBlazeClose;
				Play( blaze ? BlazeCloseSound : DoorCloseSound, true );
				return new DoorMover( this, blaze ? DoorMover.Kind.BlazeClose : DoorMover.Kind.Close, blaze ? VDoorSpeed * 4 : VDoorSpeed )
				{
					Direction = -1,
					Top = SectorMoves.DoorTop( self, neighbours ),
				};
			}

			case SectorAction.DoorCloseWaitOpen:
				Play( DoorCloseSound, true );
				return new DoorMover( this, DoorMover.Kind.CloseWaitOpen, VDoorSpeed ) { Direction = -1, Top = self.Ceiling };

			case SectorAction.FloorLowerToHighest:
				return new FloorMover( this, SectorMoves.HighestFloor( neighbours ), FloorSpeed );

			case SectorAction.FloorLowerToLowest or SectorAction.FloorLowerAndChange:
				return new FloorMover( this, SectorMoves.LowestFloor( self, neighbours ), FloorSpeed );

			case SectorAction.FloorTurboLower:
			{
				float target = SectorMoves.HighestFloor( neighbours );
				if ( target != self.Floor ) target += 8;
				return new FloorMover( this, target, FloorSpeed * 4 );
			}

			case SectorAction.FloorRaiseToLowestCeiling or SectorAction.FloorRaiseCrush:
				return new FloorMover( this, SectorMoves.RaiseFloorTarget( self, neighbours, action == SectorAction.FloorRaiseCrush ), FloorSpeed );

			case SectorAction.FloorRaiseToNearest:
				return new FloorMover( this, SectorMoves.NextHigherFloor( self.Floor, neighbours ), FloorSpeed );

			case SectorAction.FloorRaiseTurbo:
				return new FloorMover( this, SectorMoves.NextHigherFloor( self.Floor, neighbours ), FloorSpeed * 4 );

			case SectorAction.FloorRaise24 or SectorAction.FloorRaise24AndChange:
				return new FloorMover( this, self.Floor + 24, FloorSpeed );

			case SectorAction.FloorRaise512:
				return new FloorMover( this, self.Floor + 512, FloorSpeed );

			case SectorAction.FloorRaiseToTexture:
				return ShortestLowerTexture > 0 ? new FloorMover( this, self.Floor + ShortestLowerTexture, FloorSpeed ) : null;

			case SectorAction.CeilingLowerToFloor:
				return new CeilingMover( this, CeilingMover.Kind.LowerToFloor, CeilingSpeed ) { Bottom = self.Floor, Direction = -1 };

			case SectorAction.CeilingLowerAndCrush:
				return new CeilingMover( this, CeilingMover.Kind.LowerAndCrush, CeilingSpeed ) { Bottom = self.Floor + SectorMoves.CrushGap, Direction = -1 };

			case SectorAction.CeilingRaiseToHighest:
				return new CeilingMover( this, CeilingMover.Kind.RaiseToHighest, CeilingSpeed ) { Top = SectorMoves.HighestCeiling( neighbours ), Direction = 1 };

			case SectorAction.CeilingCrushAndRaise or SectorAction.CeilingFastCrushAndRaise or SectorAction.CeilingSilentCrushAndRaise:
			{
				var kind = action switch
				{
					SectorAction.CeilingFastCrushAndRaise => CeilingMover.Kind.FastCrushAndRaise,
					SectorAction.CeilingSilentCrushAndRaise => CeilingMover.Kind.SilentCrushAndRaise,
					_ => CeilingMover.Kind.CrushAndRaise,
				};

				return new CeilingMover( this, kind, kind == CeilingMover.Kind.FastCrushAndRaise ? CeilingSpeed * 2 : CeilingSpeed )
				{
					Top = self.Ceiling,
					Bottom = self.Floor + SectorMoves.CrushGap,
					Direction = -1,
				};
			}

			case SectorAction.PlatDownWaitUpStay or SectorAction.PlatBlazeDownWaitUpStay:
			{
				bool blaze = action == SectorAction.PlatBlazeDownWaitUpStay;
				Play( PlatStartSound, false );
				return new PlatMover( this, blaze ? PlatMover.Kind.BlazeDownWaitUpStay : PlatMover.Kind.DownWaitUpStay, PlatSpeed * (blaze ? 8 : 4) )
				{
					Low = MathF.Min( SectorMoves.LowestFloor( self, neighbours ), self.Floor ),
					High = self.Floor,
					Wait = PlatWait,
					Status = PlatMover.State.Down,
				};
			}

			case SectorAction.PlatPerpetualRaise:
				Play( PlatStartSound, false );
				return new PlatMover( this, PlatMover.Kind.PerpetualRaise, PlatSpeed * 4 )
				{
					Low = MathF.Min( SectorMoves.LowestFloor( self, neighbours ), self.Floor ),
					High = MathF.Max( SectorMoves.HighestFloor( neighbours ), self.Floor ),
					Wait = PlatWait,
					Status = Random.Shared.Next( 2 ) == 0 ? PlatMover.State.Up : PlatMover.State.Down,
				};

			case SectorAction.PlatRaiseAndChange or SectorAction.PlatRaiseToNearestAndChange:
				Play( StoneMoveSound, false );
				return new PlatMover( this, PlatMover.Kind.RaiseAndChange, PlatSpeed / 2 )
				{
					Low = self.Floor,
					High = action == SectorAction.PlatRaiseAndChange ? self.Floor + amount : SectorMoves.NextHigherFloor( self.Floor, neighbours ),
					Status = PlatMover.State.Up,
				};
		}

		return null;
	}

	/// <summary>EV_BuildStairs: each step of the staircase rises one step higher than the last.</summary>
	private bool BuildStairs( float stepSize, float speed )
	{
		if ( IsBusy ) return false;

		float height = FloorHeight + stepSize;
		_mover = new FloorMover( this, height, speed );

		for ( var step = StairNext; step.IsValid() && !step.IsBusy; step = step.StairNext )
		{
			height += stepSize;
			step._mover = new FloorMover( step, height, speed );
		}

		return true;
	}

	/// <summary>EV_DoDonut: the pillar sinks and the ring around it rises, both to the floor beyond the ring.</summary>
	private bool StartDonut()
	{
		if ( IsBusy || !DonutRing.IsValid() || DonutRing.IsBusy ) return false;

		DonutRing._mover = new FloorMover( DonutRing, DonutHeight, FloorSpeed / 2 );
		_mover = new FloorMover( this, DonutHeight, FloorSpeed / 2 );
		return true;
	}

	/// <summary>EV_CeilingCrushStop.</summary>
	private bool StopCrusher()
	{
		if ( _mover is not CeilingMover { Direction: not 0 } ceiling ) return false;

		ceiling.OldDirection = ceiling.Direction;
		ceiling.Direction = 0;
		return true;
	}

	/// <summary>P_ActivateInStasisCeiling.</summary>
	public bool ResumeCrusher()
	{
		if ( _mover is not CeilingMover { Direction: 0 } ceiling ) return false;

		ceiling.Direction = ceiling.OldDirection;
		return true;
	}

	/// <summary>EV_StopPlat.</summary>
	private bool StopPlat()
	{
		if ( _mover is not PlatMover plat || plat.Status == PlatMover.State.Stasis ) return false;

		plat.OldStatus = plat.Status;
		plat.Status = PlatMover.State.Stasis;
		return true;
	}

	/// <summary>P_ActivateInStasis.</summary>
	public bool ResumePlat()
	{
		if ( _mover is not PlatMover { Status: PlatMover.State.Stasis } plat ) return false;

		plat.Status = plat.OldStatus;
		return true;
	}

	// ------------------------------------------------------------------
	// Thinkers
	// ------------------------------------------------------------------

	private abstract class Mover( DoomSector sector, float speed )
	{
		protected readonly DoomSector Sector = sector;
		public float Speed = speed;
		private float _stoneTimer;

		/// <summary>Advances the mover. False once it has finished and should be removed.</summary>
		public abstract bool Tick( float delta );

		protected void GrindSound( float delta, bool ceiling )
		{
			_stoneTimer -= delta;
			if ( _stoneTimer > 0 ) return;

			_stoneTimer += StoneInterval;
			Sector.Play( Sector.StoneMoveSound, ceiling );
		}
	}

	/// <summary>T_VerticalDoor. Direction: 1 opening, -1 closing, 0 waiting open, 2 waiting closed.</summary>
	private sealed class DoorMover( DoomSector sector, DoorMover.Kind kind, float speed ) : Mover( sector, speed )
	{
		public enum Kind { Raise, Open, Close, CloseWaitOpen, RaiseIn5Minutes, BlazeRaise, BlazeOpen, BlazeClose }

		public Kind Type = kind;
		public int Direction;
		public float Top;
		public float Countdown;

		public override bool Tick( float delta )
		{

			switch ( Direction )
			{
				case 0:
					Countdown -= delta;
					if ( Countdown > 0 ) return true;

					switch ( Type )
					{
						case Kind.BlazeRaise:
							Direction = -1;
							Sector.Play( Sector.BlazeCloseSound, true );
							break;
						case Kind.Raise:
							Direction = -1;
							Sector.Play( Sector.DoorCloseSound, true );
							break;
						case Kind.CloseWaitOpen:
							Direction = 1;
							Sector.Play( Sector.DoorOpenSound, true );
							break;
					}
					return true;

				case 2:
					Countdown -= delta;
					if ( Countdown > 0 ) return true;

					if ( Type == Kind.RaiseIn5Minutes )
					{
						Direction = 1;
						Type = Kind.Raise;
						Sector.Play( Sector.DoorOpenSound, true );
					}
					return true;

				case -1:
					if ( !Sector.MovePlane( true, Sector.FloorHeight, Speed, delta ) ) return true;

					switch ( Type )
					{
						case Kind.BlazeRaise or Kind.BlazeClose:
							Sector.Play( Sector.BlazeCloseSound, true );
							return false;
						case Kind.Raise or Kind.Close:
							return false;
						case Kind.CloseWaitOpen:
							Direction = 0;
							Countdown = 30f;
							return true;
					}
					return true;

				case 1:
					if ( !Sector.MovePlane( true, Top, Speed, delta ) ) return true;

					switch ( Type )
					{
						case Kind.BlazeRaise or Kind.Raise:
							Direction = 0;
							Countdown = DoorWait;
							return true;
						case Kind.CloseWaitOpen or Kind.BlazeOpen or Kind.Open:
							return false;
					}
					return true;
			}

			return false;
		}
	}

	/// <summary>T_MoveFloor.</summary>
	private sealed class FloorMover( DoomSector sector, float destination, float speed ) : Mover( sector, speed )
	{
		public override bool Tick( float delta )
		{
			GrindSound( delta, false );

			if ( !Sector.MovePlane( false, destination, Speed, delta ) ) return true;

			Sector.Play( Sector.PlatStopSound, false );
			return false;
		}
	}

	/// <summary>T_MoveCeiling. Direction: 1 up, -1 down, 0 stopped (in stasis).</summary>
	private sealed class CeilingMover( DoomSector sector, CeilingMover.Kind kind, float speed ) : Mover( sector, speed )
	{
		public enum Kind { LowerToFloor, RaiseToHighest, LowerAndCrush, CrushAndRaise, FastCrushAndRaise, SilentCrushAndRaise }

		public float Top;
		public float Bottom;
		public int Direction;
		public int OldDirection;

		public override bool Tick( float delta )
		{
			if ( Direction == 0 ) return true;

			if ( kind != Kind.SilentCrushAndRaise )
				GrindSound( delta, true );

			if ( Direction == 1 )
			{
				if ( !Sector.MovePlane( true, Top, Speed, delta ) ) return true;

				switch ( kind )
				{
					case Kind.RaiseToHighest:
						return false;
					case Kind.SilentCrushAndRaise:
						Sector.Play( Sector.PlatStopSound, true );
						Direction = -1;
						return true;
					case Kind.FastCrushAndRaise or Kind.CrushAndRaise:
						Direction = -1;
						return true;
				}
				return true;
			}

			if ( !Sector.MovePlane( true, Bottom, Speed, delta ) ) return true;

			switch ( kind )
			{
				case Kind.SilentCrushAndRaise:
					Sector.Play( Sector.PlatStopSound, true );
					Speed = CeilingSpeed;
					Direction = 1;
					return true;
				case Kind.CrushAndRaise:
					Speed = CeilingSpeed;
					Direction = 1;
					return true;
				case Kind.FastCrushAndRaise:
					Direction = 1;
					return true;
				default:
					return false;
			}
		}
	}

	/// <summary>T_PlatRaise.</summary>
	private sealed class PlatMover( DoomSector sector, PlatMover.Kind kind, float speed ) : Mover( sector, speed )
	{
		public enum Kind { PerpetualRaise, DownWaitUpStay, BlazeDownWaitUpStay, RaiseAndChange }
		public enum State { Up, Down, Waiting, Stasis }

		public float Low;
		public float High;
		public float Wait;
		public State Status;
		public State OldStatus;
		private float _count;

		public override bool Tick( float delta )
		{

			switch ( Status )
			{
				case State.Up:
					if ( kind == Kind.RaiseAndChange )
						GrindSound( delta, false );

					if ( !Sector.MovePlane( false, High, Speed, delta ) ) return true;

					_count = Wait;
					Status = State.Waiting;
					Sector.Play( Sector.PlatStopSound, false );
					return kind == Kind.PerpetualRaise;

				case State.Down:
					if ( !Sector.MovePlane( false, Low, Speed, delta ) ) return true;

					_count = Wait;
					Status = State.Waiting;
					Sector.Play( Sector.PlatStopSound, false );
					return true;

				case State.Waiting:
					_count -= delta;
					if ( _count > 0 ) return true;

					Status = Sector.FloorHeight == Low ? State.Up : State.Down;
					Sector.Play( Sector.PlatStartSound, false );
					return true;
			}

			return true;
		}
	}
}
