namespace Halo2;

public sealed record FiringCue( SoundEvent Sound, string Marker, int Repeats, MuzzleFlashDefinition Flash );

public class BatteryWeapon : HandheldWeapon
{
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
	[Property] public FiringCue[] FiringCues { get; set; }
	int cueIndex;
	int cueRemaining;

	[Sync( SyncFlags.FromHost )] public float Spent { get; private set; }
	[Sync( SyncFlags.FromHost )] float ShotHeat { get; set; }
	[Sync( SyncFlags.FromHost )] double LastEnergyShot { get; set; }
	[Sync( SyncFlags.FromHost )] bool Venting { get; set; }
	bool showingVentExit;

	float CoolingRate => Venting && OverheatedLoss > 0 ? OverheatedLoss : HeatLoss;
	// Sample on the original weapon clock. Cooling continues on dropped and holstered items.
	public float Heat => HeatAt( Time.NowDouble );

	float HeatAt( double time )
	{
		if ( ShotHeat <= 0 ) return 0;
		double ticks = System.Math.Max( 0, System.Math.Floor( time * 30 ) - System.Math.Floor( LastEnergyShot * 30 ) );
		double ventTicks = Venting && CoolingRate > 0
			? System.Math.Min( ticks, System.Math.Floor( (ShotHeat - CooledThreshold) * 30 / CoolingRate ) + 1 ) : 0;
		return (float)System.Math.Max( 0, ShotHeat - (ventTicks * CoolingRate + (ticks - ventTicks) * HeatLoss) / 30 );
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
		RequestEnergyShot( forward, awaitCue );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestEnergyShot( Vector3 forward, bool awaitingCue )
	{
		if ( !IsHeld || !IsActive || !CanPrimaryAttack() || !forward.LengthSquared.AlmostEqual( 1, 0.001f ) ) return;
		float delay = FiringRate.y > 0 ? CooldownSeconds( (int)System.MathF.Ceiling( 30 / FiringRate.y ) ) : PrimaryDelay;
		SetNextPrimaryFire( delay );
		FireEnergy( forward, awaitingCue );
	}

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
		if ( cue.Flash is {} flash && EffectMarker( flash.Marker ) is {} muzzle )
			muzzle.GetOrAddComponent<MuzzleFlash>().Emit( flash, WeaponModel?.GameObject == ViewModel );
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

	protected override void OnControl()
	{
		if ( FiringRate.y > FiringRate.x && FiringRate.x > 0 )
		{
			bool held = CanCycleBarrel && Input.Down( "attack1" );
			rateFraction = (rateFraction + Time.Delta * (held ? RateRise : -RateFall)).Clamp( 0, 1 );
			float rate = MathX.Lerp( FiringRate.x, FiringRate.y, rateFraction );
			PrimaryDelay = CooldownSeconds( (int)System.MathF.Ceiling( 30 / rate ) );
		}
		base.OnControl();
	}

	protected override void OnDisabled()
	{
		rateFraction = 0;
		base.OnDisabled();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Venting || IsProxy ) return;
		float heat = Heat;
		if ( heat < CooledThreshold ) return;
		if ( showingVentExit || !ViewModel.IsValid() ) return;
		var model = ViewModel.GetComponent<HandheldWeaponModel>();
		if ( model?.AnimationData?.Actions.TryGetValue( "o_h_exit", out var exit ) != true ) return;
		int frames = exit.Cues.FirstOrDefault( c => c.Kind == "allow_interruption" )?.Frame ?? exit.Frames;
		if ( heat - CooledThreshold > CoolingRate * frames / 30 ) return;
		showingVentExit = true;
		model.Play( "o_h_exit" );
	}
}
