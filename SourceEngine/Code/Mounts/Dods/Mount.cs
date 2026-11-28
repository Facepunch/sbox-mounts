namespace Source1.Dods;

public sealed class Mount : SourceGameMount
{
	protected override string ContentDirectory => "dod";
	internal override IEnumerable<string> PlayerStartClasses => ["info_player_allies", "info_player_axis"];
	public override string Ident => "dods";
	public override string Title => "Day of Defeat: Source";
	public override long AppId => 300;
	protected override string WeaponScriptKey => "Wl0u5B3F";
	protected internal override bool UseDrawSequenceDuration => true;
	protected internal override ViewModelMotion.Settings ViewModelMovement { get; } = new( BobCycle: 0.8f, ForwardBob: 0.4f,
		SideBob: 0, BobAcceleration: 320, TurnSway: 2.6f, MirrorSway: false );

	private protected override (string Title, int? Capacity) AmmoDefaults( string ammo ) => ammo.ToUpperInvariant() switch
	{
		"DOD_AMMO_RIFLEGRENADE_GER" => ("German Rifle Grenades", 2),
		"DOD_AMMO_RIFLEGRENADE_GER_LIVE" => ("Live German Rifle Grenades", 2),
		"DOD_AMMO_RIFLEGRENADE_US" => ("US Rifle Grenades", 2),
		"DOD_AMMO_RIFLEGRENADE_US_LIVE" => ("Live US Rifle Grenades", 2),
		"DOD_AMMO_HANDGRENADE_EX" => ("Live US Frag Grenades", 1),
		"DOD_AMMO_STICKGRENADE_EX" => ("Live German Stick Grenades", 1),
		"DOD_AMMO_STICKGRENADE" => ("German Stick Grenades", 2),
		"DOD_AMMO_HANDGRENADE" => ("US Frag Grenades", 2),
		"DOD_AMMO_SMOKEGRENADE_US" => ("US Smoke Grenades", 2),
		"DOD_AMMO_SMOKEGRENADE_GER" => ("German Smoke Grenades", 2),
		"DOD_AMMO_ROCKET" => ("Rockets", 5),
		"DOD_AMMO_30CAL" => ("Browning .30 Cal Ammo", 300),
		"DOD_AMMO_MG42" => ("MG42 Ammo", 500),
		"DOD_AMMO_SPRING" => ("Springfield Ammo", 55),
		"DOD_AMMO_K98" => ("Kar98k Ammo", 65),
		"DOD_AMMO_GARAND" => ("M1 Garand Ammo", 88),
		"DOD_AMMO_BAR" => ("BAR Ammo", 260),
		"DOD_AMMO_SUBMG" => ("Submachine Gun Ammo", 210),
		"DOD_AMMO_COLT" => ("Colt M1911 Ammo", 21),
		"DOD_AMMO_P38" => ("Walther P38 Ammo", 24),
		"DOD_AMMO_C96" => ("Mauser C96 Ammo", 60),
		"DOD_AMMO_M1CARBINE" => ("M1 Carbine Ammo", 165),
		_ => base.AmmoDefaults( ammo )
	};

	internal override void ApplyWeaponDefaults( BaseCombatWeapon weapon, WeaponSetup setup, string worldModel )
	{
		if ( weapon is not BulletWeapon gun || setup.Data is not { } data ) return;
		if ( data.Recoil is { } recoil )
		{
			gun.RecoilPitch = new( -recoil, -recoil );
			gun.RecoilYaw = new( -recoil / 4, recoil / 4 );
			gun.PunchRoll = Vector2.Zero;
		}
	}
}
