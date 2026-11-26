namespace Halo2;

public sealed record BarrelErrorProfile( Vector2 Rates, Vector2 DualRates, float DualMinimum, Vector2 DualRange, bool AlwaysUseDualError );

public abstract partial class HandheldWeapon
{
	[Property] public BarrelErrorProfile ErrorProfile { get; set; }
	public float BarrelError { get; private set; }
	double? errorTick;
	float errorAtTickStart;
	float otherHandBlend;
	bool barrelFiring;

	public Vector2 ShotCone
	{
		get
		{
			if ( ErrorProfile is not {} profile ) return new( MinimumPelletAngle, CurrentSpread.x );
			float low = Ballistics.SpreadBase.x;
			float high = low + Ballistics.SpreadGrowth.x;
			low = MathX.Lerp( low, profile.DualRange.x == 0 ? low : profile.DualRange.x, otherHandBlend );
			high = MathX.Lerp( high, profile.DualRange.y == 0 ? high : profile.DualRange.y, otherHandBlend );
			return new( MathX.Lerp( MinimumPelletAngle, profile.DualMinimum, otherHandBlend ), MathX.Lerp( low, high, BarrelError ) );
		}
	}

	float OppositeHandBlend( double tick )
	{
		if ( ErrorProfile.AlwaysUseDualError ) return IsDualWielding ? 1 : 0;
		var other = (IsOffhand ? Inventory.ActiveItem : Offhand) as HandheldWeapon;
		var latest = other?.lastBarrelTick;
		if ( latest > tick || (IsOffhand && latest == tick) ) latest = other.previousBarrelTick;
		if ( latest is not double shot || shot > tick ) return 0;
		return (1 - (float)(tick - shot) / 9).Clamp( 0, 1 );
	}

	float StepError( float error, bool firing, float blend )
	{
		var profile = ErrorProfile;
		float rate = firing ? MathX.Lerp( profile.Rates.x, profile.DualRates.x, blend )
			: -MathX.Lerp( profile.Rates.y, profile.DualRates.y, blend );
		return (error + rate / 30).Clamp( 0, 1 );
	}

	void UpdateBarrelError( bool firing )
	{
		if ( ErrorProfile is null ) return;
		double tick = System.Math.Floor( Time.NowDouble * 30 );
		if ( errorTick != tick )
		{
			// Catch up missed control ticks using the previous trigger state.
			for ( double i = (errorTick ?? tick) + 1; i < tick && (barrelFiring ? BarrelError < 1 : BarrelError > 0); i++ )
				BarrelError = StepError( BarrelError, barrelFiring, OppositeHandBlend( i ) );
			errorAtTickStart = BarrelError;
			errorTick = tick;
			barrelFiring = false;
		}
		barrelFiring |= firing;
		otherHandBlend = OppositeHandBlend( tick );
		BarrelError = StepError( errorAtTickStart, barrelFiring, otherHandBlend );
	}

	protected Vector2 FiringError => new( BarrelError, otherHandBlend );

	protected void ApplyFiringError( Vector2 state )
	{
		BarrelError = state.x.Clamp( 0, 1 );
		otherHandBlend = state.y.Clamp( 0, 1 );
	}
}
