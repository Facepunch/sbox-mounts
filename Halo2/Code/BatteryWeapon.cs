namespace Halo2;

public sealed class BatteryWeapon : RangedWeapon
{
	[Property] public float HeatPerShot { get; set; }
	[Property] public float SpentPerShot { get; set; }
	[Property] public float HeatLoss { get; set; }
	[Property] public float OverheatedLoss { get; set; }
	[Property] public float CooledThreshold { get; set; }
	[Property] public float OverheatThreshold { get; set; }

	[Sync] public float Spent { get; private set; }
	[Sync] float ShotHeat { get; set; }
	[Sync] double LastEnergyShot { get; set; }
	[Sync] bool Venting { get; set; }
	bool showingVentExit;

	float CoolingRate => Venting && OverheatedLoss > 0 ? OverheatedLoss : HeatLoss;
	// Sample on the original weapon clock. Cooling continues on dropped and holstered items.
	public float Heat
	{
		get
		{
			if ( ShotHeat <= 0 ) return 0;
			double ticks = System.Math.Max( 0, System.Math.Floor( (Time.NowDouble - LastEnergyShot) * 30 ) );
			double ventTicks = Venting && CoolingRate > 0
				? System.Math.Min( ticks, System.Math.Floor( (ShotHeat - CooledThreshold) * 30 / CoolingRate ) + 1 ) : 0;
			return (float)System.Math.Max( 0, ShotHeat - (ventTicks * CoolingRate + (ticks - ventTicks) * HeatLoss) / 30 );
		}
	}
	public bool Overheated => Venting && Heat >= CooledThreshold;
	internal override string RestAction => Overheated && !showingVentExit ? "overheated" : "idle";

	public override bool CanPrimaryAttack() => Spent < 1 && !Overheated && base.CanPrimaryAttack();
	public override bool CanReload() => false;
	protected override bool CanZoom => !Overheated && base.CanZoom;

	public override void PrimaryAttack()
	{
		if ( Spent >= 1 || Overheated ) return;
		float heat = System.MathF.Min( 1, Heat + HeatPerShot );
		base.PrimaryAttack();
		Spent = System.MathF.Min( 1, Spent + SpentPerShot );
		ShotHeat = heat;
		LastEnergyShot = Time.NowDouble;
		Venting = heat >= OverheatThreshold;
		showingVentExit = false;
		if ( Venting )
		{
			SetZoom( 0 );
			ViewModel?.GetComponent<RangedWeaponModel>()?.Play( "overheating" );
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Venting || IsProxy ) return;
		float heat = Heat;
		if ( heat < CooledThreshold ) return;
		if ( showingVentExit || !ViewModel.IsValid() ) return;
		var model = ViewModel.GetComponent<RangedWeaponModel>();
		if ( model?.AnimationData?.Actions.TryGetValue( "o_h_exit", out var exit ) != true ) return;
		int frames = exit.Cues.FirstOrDefault( c => c.Kind == "allow_interruption" )?.Frame ?? exit.Frames;
		if ( heat - CooledThreshold > CoolingRate * frames / 30 ) return;
		showingVentExit = true;
		model.Play( "o_h_exit" );
	}
}
