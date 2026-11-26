namespace Halo2;

public sealed record FiringCue( SoundEvent Sound, string Marker, int Repeats, MuzzleFlashDefinition[] Flash );

public class BatteryWeapon : Firearm
{
	[Property] public MuzzleFlashDefinition[] VentParticles { get; set; }
	double nextVentTick;

	[Property] public WeaponSoundTrack FiringTrack { get; set; }
	[Property] public WeaponSoundTrack OverheatTrack { get; set; }
	readonly TrackVoice firingVoice = new(), overheatVoice = new();
	double? lastPresentedShot;

	protected override void OnShootEffects( ShotEffect shot )
	{
		base.OnShootEffects( shot );
		if ( !shot.NoEvents && FiringTrack is not null && !Application.IsDedicatedServer )
		{
			lastPresentedShot = Time.NowDouble;
			UpdateTrack( firingVoice, FiringTrack, true );
		}
	}

	[Property] public float HeatPerShot { get; set; }
	[Property] public float SpentPerShot { get; set; }
	[Property] public float HeatLoss { get; set; }
	[Property] public float OverheatedLoss { get; set; }
	[Property] public float CooledThreshold { get; set; }
	[Property] public float OverheatThreshold { get; set; }

	[Property] public float MisfireThreshold { get; set; }
	[Property] public float MisfireChance { get; set; }
	[Property] public Vector2 FiringRate { get; set; }
	[Property] public float RateRise { get; set; }
	[Property] public float RateFall { get; set; }
	float rateFraction;
	double lastRateTick;
	double rateChangedAt;
	bool barrelAccelerating;
	[Property] public FiringCue[] FiringCues { get; set; }
	int cueIndex;
	int cueRemaining;

	[Sync( SyncFlags.FromHost )] public float Spent { get; private set; }
	[Sync( SyncFlags.FromHost )] float ShotHeat { get; set; }
	[Sync( SyncFlags.FromHost )] double LastEnergyShot { get; set; }
	[Sync( SyncFlags.FromHost )] bool Venting { get; set; }
	bool showingVentExit;
	(float Heat, double ShotTime, float VentRate, float BaseRate, float Threshold) coolingSample;
	double sampledTicks;
	float sampledHeat;

	float CoolingRate => Venting && OverheatedLoss > 0 ? OverheatedLoss : HeatLoss;
	// Sample on the original weapon clock. Cooling continues on dropped and holstered items.
	public float Heat => HeatAt( !Networking.IsHost && !IsProxy && IsHeld && OwnerToHostTicks is double offset
		? Time.NowDouble + offset / 30 : Time.NowDouble );

	float HeatAt( double time )
	{
		if ( ShotHeat <= 0 ) return 0;
		// Cooling starts one full weapon tick after the shot, even when it fired between scene ticks.
		double ticks = System.Math.Max( 0, System.Math.Floor( (System.Math.BitIncrement( time ) - LastEnergyShot) * 30 ) );
		var sample = (ShotHeat, LastEnergyShot, CoolingRate, HeatLoss, CooledThreshold);
		if ( coolingSample != sample || ticks < sampledTicks )
		{
			coolingSample = sample;
			sampledTicks = 0;
			sampledHeat = ShotHeat;
		}
		// Native rounds each subtraction to float before comparing the vent threshold.
		while ( sampledTicks < ticks && sampledHeat > 0 )
		{
			float rate = sampledHeat >= CooledThreshold ? CoolingRate : HeatLoss;
			float next = System.MathF.Max( 0, sampledHeat - rate / 30f );
			if ( next >= sampledHeat ) break;
			sampledHeat = next;
			sampledTicks++;
		}
		sampledTicks = ticks;
		return sampledHeat;
	}
	public bool Overheated => Venting && Heat >= CooledThreshold;
	internal override string RestAction => Overheated && !showingVentExit ? "overheated" : "idle";

	public override bool CanPrimaryAttack() => Spent < 1 && !Overheated && base.CanPrimaryAttack();
	public override bool CanReload() => false;
	protected override bool CanZoom => !Overheated && base.CanZoom;

	public override void PrimaryAttack()
	{
		if ( Spent >= 1 || Overheated ) return;
		double shotTime = RecordRound();
		var forward = AimRay.Forward;
		if ( Networking.IsHost )
		{
			FireEnergy( forward, false, shotTime: shotTime );
			return;
		}
		bool awaitCue = MisfireThreshold is > 0 and < 1 && Spent > MisfireThreshold;
		if ( !awaitCue ) OnShootEffects( default );
		TimeSinceShoot = 0;
		RequestRound( forward, awaitCue, FiringError, (long)System.Math.Round( shotTime * 30 ) );
	}

	protected override float RemoteFireDelay => FiringRate.y > 0 ? CooldownSeconds( (int)System.MathF.Ceiling( 30 / FiringRate.y ) ) : PrimaryDelay;
	protected override bool CanScheduleRound( double time ) => base.CanCycleBarrel && Spent < 1 && (!Venting || HeatAt( time ) < CooledThreshold);
	protected override void FireRemoteRound( Vector3 forward, bool awaitingCue, double shotTime ) => FireEnergy( forward, awaitingCue, shotTime: shotTime );

	protected void FireEnergy( Vector3 forward, bool awaitingCue, EnergyDischarge discharge = null, double? shotTime = null )
	{
		double time = shotTime ?? Time.NowDouble;
		float heat = System.MathF.Min( 1, HeatAt( time ) + (discharge?.Heat ?? HeatPerShot) );
		bool misfire = MisfireThreshold is > 0 and < 1 && Spent > MisfireThreshold
			&& (Spent - MisfireThreshold) * MisfireChance / (1 - MisfireThreshold) > BlamRandom.Next( ref BlamRandom.GameplayState( Scene ) ) / 65535.0;
		if ( discharge is null && FiringCues is { Length: > 0 } cues )
		{
			if ( cueRemaining == 0 )
			{
				cueIndex = (cueIndex + 1) % cues.Length;
				cueRemaining = cues[cueIndex].Repeats;
			}
			cueRemaining--;
			if ( !misfire ) PlayBarrelCue( cueIndex );
		}
		if ( misfire ) ViewModel?.GetComponent<HandheldWeaponModel>()?.Play( "misfire_1" );
		else if ( discharge is null && Instantaneous ) base.PrimaryAttack();
		else
		{
			if ( discharge is null ) ShootEffects();
			else PresentDischarge();
			LaunchBolt( forward, discharge );
			TimeSinceShoot = 0;
		}
		if ( IsProxy && ((awaitingCue && discharge is null) || misfire) ) PresentOwnerShot( misfire );
		Spent = System.MathF.Min( 1, Spent + (discharge?.Spent ?? SpentPerShot) );
		ShotHeat = heat;
		LastEnergyShot = time;
		Venting = heat >= OverheatThreshold;
		showingVentExit = false;
		if ( Venting )
		{
			// The charged pistol shot supplies its own vent entry animation.
			if ( IsProxy ) BeginOwnerVent( discharge is null );
			else BeginVent( discharge is null );
		}
	}

	protected override void PlayFiringSound()
	{
		if ( FiringCues is not { Length: > 0 } ) base.PlayFiringSound();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayBarrelCue( int index )
	{
		if ( Application.IsDedicatedServer || FiringCues is null || (uint)index >= FiringCues.Length ) return;
		var cue = FiringCues[index];
		PlayFiringSound( cue.Sound, cue.Marker );
		EmitMuzzleFlash( cue.Flash );
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	void PresentOwnerShot( bool misfire )
	{
		if ( misfire ) ViewModel?.GetComponent<HandheldWeaponModel>()?.Play( "misfire_1" );
		else OnShootEffects( default );
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	void BeginOwnerVent( bool playEntry ) => BeginVent( playEntry );

	void BeginVent( bool playEntry )
	{
		showingVentExit = false;
		SetZoom( 0 );
		if ( playEntry ) ViewModel?.GetComponent<HandheldWeaponModel>()?.Play( "overheating" );
	}

	protected virtual void PresentDischarge() => ShootEffects();

	protected override bool CanCycleBarrel => !Overheated && Spent < 1 && base.CanCycleBarrel;
	protected override double EarliestRateTick => rateChangedAt;

	protected override void OnControl()
	{
		if ( FiringRate.y > FiringRate.x && FiringRate.x > 0 )
		{
			bool held = CanCycleBarrel && Input.Down( TriggerButton );
			double tick = System.Math.Floor( Time.NowDouble * 30 );
			double ticks = tick - lastRateTick;
			if ( ticks > 0 )
			{
				rateFraction = (rateFraction + (float)ticks / 30f * (barrelAccelerating ? RateRise : -RateFall)).Clamp( 0, 1 );
				lastRateTick = tick;
			}
			barrelAccelerating = held;
			float rate = MathX.Lerp( FiringRate.x, FiringRate.y, rateFraction );
			var delay = CooldownSeconds( (int)System.MathF.Ceiling( 30 / rate ) );
			if ( delay != PrimaryDelay ) rateChangedAt = tick / 30;
			PrimaryDelay = delay;
		}
		base.OnControl();
	}

	protected override void OnDisabled()
	{
		firingVoice.Stop( FiringTrack );
		overheatVoice.Stop( OverheatTrack );
		lastPresentedShot = null;
		nextVentTick = 0;
		rateFraction = 0;
		lastRateTick = System.Math.Floor( Time.NowDouble * 30 );
		barrelAccelerating = false;
		base.OnDisabled();
	}

	protected override void UpdateWeaponState()
	{
		base.UpdateWeaponState();
		if ( !Application.IsDedicatedServer )
		{
			bool held = IsHeld && IsInHand;
			bool firing = lastPresentedShot is double shot && System.Math.Floor( Time.NowDouble * 30 ) - System.Math.Floor( shot * 30 ) <= 1;
			UpdateTrack( firingVoice, FiringTrack, held && firing );
			UpdateTrack( overheatVoice, OverheatTrack, held && Overheated );
		}
		if ( !Application.IsDedicatedServer && IsHeld && IsInHand && Overheated && VentParticles is { Length: > 0 } )
		{
			double now = Time.NowDouble;
			if ( nextVentTick == 0 ) nextVentTick = now + 1 / 30.0;
			while ( now >= nextVentTick )
			{
				EmitMuzzleFlash( VentParticles, continuous: true );
				nextVentTick += 1 / 30.0;
			}
		}
		else nextVentTick = 0;
		if ( !Venting || IsProxy ) return;
		float heat = Heat;
		if ( heat < CooledThreshold ) return;
		if ( showingVentExit || !ViewModel.IsValid() ) return;
		var model = ViewModel.GetComponent<HandheldWeaponModel>();
		if ( model?.AnimationData?.Actions.TryGetValue( "o_h_exit", out var exit ) != true ) return;
		int frames = ReleaseFrame( exit );
		if ( heat - CooledThreshold > CoolingRate * frames / 30 ) return;
		showingVentExit = true;
		model.Play( "o_h_exit" );
	}
}
