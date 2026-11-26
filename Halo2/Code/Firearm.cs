namespace Halo2;

public abstract class Firearm : HandheldWeapon
{
	readonly Queue<(Vector3 Forward, bool AwaitingCue, Vector2 Error, double Tick)> pendingRounds = new();
	double nextRemoteRound;
	[Sync( SyncFlags.FromHost )] protected double? OwnerToHostTicks { get; private set; }

	protected virtual bool UsesRemoteAutomaticFire => PrimaryAutomatic;
	protected virtual float RemoteFireDelay => PrimaryDelay;
	protected virtual bool CanScheduleRound( double time ) => CanCycleBarrel;
	protected abstract void FireRemoteRound( Vector3 forward, bool awaitingCue, double shotTime );
	protected override bool CanCatchUpRounds => Networking.IsHost || UsesRemoteAutomaticFire;

	[Rpc.Host( NetFlags.OwnerOnly )]
	protected void RequestRound( Vector3 forward, bool awaitingCue, Vector2 error, long ownerTick )
	{
		if ( !IsHeld || !IsInHand || !forward.LengthSquared.AlmostEqual( 1, 0.001f ) ) return;
		if ( !float.IsFinite( error.x ) || !float.IsFinite( error.y ) ) return;
		if ( UsesRemoteAutomaticFire )
		{
			FirePendingRounds();
			double now = System.Math.Floor( Time.NowDouble * 30 );
			int interval = (int)System.Math.Round( RemoteFireDelay * 30 );
			interval = System.Math.Max( 1, interval );
			// Batched requests can span multiple shots between network updates.
			double window = System.Math.Max( interval, System.Math.Ceiling( 60.0 / ProjectSettings.Networking.UpdateRate.Clamp( 1, 500 ) ) );
			OwnerToHostTicks ??= now - ownerTick;
			double tick = ownerTick + OwnerToHostTicks.Value;
			if ( System.Math.Abs( tick - now ) > window )
			{
				OwnerToHostTicks = now - ownerTick;
				tick = now;
			}
			tick = System.Math.Max( tick, nextRemoteRound );
			if ( tick > now + window || !CanScheduleRound( tick / 30 ) ) return;
			nextRemoteRound = tick + interval;
			pendingRounds.Enqueue( (forward, awaitingCue, error, tick) );
			FirePendingRounds();
			return;
		}
		if ( !CanPrimaryAttack() ) return;
		ApplyFiringError( error );
		SetNextPrimaryFire( RemoteFireDelay );
		FireRemoteRound( forward, awaitingCue, Time.NowDouble );
	}

	void FirePendingRounds()
	{
		while ( pendingRounds.TryPeek( out var shot ) )
		{
			if ( Time.NowDouble < shot.Tick / 30 ) return;
			if ( !IsInHand || !IsHeld || !CanCycleBarrel || !CanPrimaryAttack() )
			{
				pendingRounds.Clear();
				return;
			}
			pendingRounds.Dequeue();
			ApplyFiringError( shot.Error );
			FireRemoteRound( shot.Forward, shot.AwaitingCue, shot.Tick / 30 );
		}
	}

	protected override void UpdateWeaponState()
	{
		base.UpdateWeaponState();
		FirePendingRounds();
	}

	protected override void OnDisabled()
	{
		pendingRounds.Clear();
		nextRemoteRound = 0;
		if ( Networking.IsHost ) OwnerToHostTicks = null;
		base.OnDisabled();
	}
}
