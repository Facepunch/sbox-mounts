namespace Diesel;

sealed record NpcDefinition( string Id, string Title, string Unit, string Weapon = null, bool Female = false, float Health = 100,
	string Faction = "ally", bool HostileToPlayer = false )
{
	public bool Civilian => Weapon is null;
	public string Prefab => $"entities/npcs/{Id}.prefab";
	public string Model => $"characters/{Id}.model";
	// Recreate declarations after hotload instead of migrating old array entries by index.
	public static NpcDefinition[] All =>
	[
		new( "civilian", "Civilian", "units/characters/civilians/casual_male_1/casual_male_1" ),
		new( "civilian_female", "Civilian (female)", "units/characters/civilians/casual_female_1/casual_female_1", Female: true ),
		new( "bank_manager", "Bank manager", "units/characters/bank_manager/bank_manager2" ),
		new( "bank_staff", "Bank staff (male)", "units/characters/civilians/suit_male_1/suit_male_1" ),
		new( "bank_staff_female", "Bank staff (female)", "units/characters/civilians/suit_female_1/suit_female_1", Female: true ),
		new( "doctor", "Hospital doctor", "units/characters/civilians/hospital_doctor1/hospital_doctor1" ),
		new( "nurse", "Hospital nurse (female)", "units/characters/civilians/hospital_female_nurse1/hospital_female_nurse1", Female: true ),
		new( "nurse_male", "Hospital nurse (male)", "units/characters/civilians/hospital_male_nurse1/hospital_male_nurse1" ),
		new( "suburban_male", "Suburban resident (male)", "units/characters/civilians/suburbia_male1/suburbia_male1" ),
		new( "suburban_female", "Suburban resident (female)", "units/characters/civilians/suburbia_female1/suburbia_female1", Female: true ),
		new( "builder", "Construction worker", "units/characters/civilians/builder_1/builder_1" ),
		new( "butcher", "Slaughterhouse worker", "units/characters/civilians/butcher_1/butcher_1" ),
		new( "security", "Security guard", "units/characters/enemies/security/security_guard_01", "crosskill" ),
		new( "cop", "Police officer", "units/characters/enemies/cop/cop", "b9s" ),
		new( "swat", "SWAT", "units/characters/enemies/swat/swat", "amcar4", Health: 200, HostileToPlayer: true ),
		new( "security_2", "Security guard (variant 2)", "units/characters/enemies/security/security_guard_02", "crosskill" ),
		new( "cop_2", "Police officer (variant 2)", "units/characters/enemies/cop2/cop2", "b9s" ),
		new( "cop_3", "Police officer (variant 3)", "units/characters/enemies/cop3/cop3", "bronco44" ),
		new( "swat_2", "SWAT (variant 2)", "units/characters/enemies/swat2/swat2", "amcar4", Health: 200, HostileToPlayer: true ),
		new( "swat_3", "SWAT (variant 3)", "units/characters/enemies/swat3/swat3", "compact5", Health: 200, HostileToPlayer: true ),
		new( "heavy_swat", "Heavy SWAT", "units/characters/enemies/swat_kevlar1/swat_kevlar1", "amcar4", Health: 300, HostileToPlayer: true ),
		new( "heavy_swat_2", "Heavy SWAT (variant 2)", "units/characters/enemies/swat_kevlar2/swat_kevlar2", "amcar4", Health: 300, HostileToPlayer: true ),
		new( "fbi", "FBI agent", "units/characters/enemies/fbi1/fbi1", "compact5", HostileToPlayer: true ),
		new( "fbi_2", "FBI agent (variant 2)", "units/characters/enemies/fbi2/fbi2", "crosskill", HostileToPlayer: true ),
		new( "fbi_3", "FBI agent (variant 3)", "units/characters/enemies/fbi3/fbi3", "amcar4", HostileToPlayer: true ),
		new( "murkywater", "Murkywater mercenary", "units/characters/enemies/murky_water1/murky_water1", "amcar4", Health: 200, Faction: "enemy", HostileToPlayer: true ),
		new( "murkywater_2", "Murkywater mercenary (variant 2)", "units/characters/enemies/murky_water2/murky_water2", "amcar4", Health: 200, Faction: "enemy", HostileToPlayer: true ),
		new( "gangster", "Gangster", "units/characters/enemies/gangster1/gangster1", "crosskill", Faction: "enemy", HostileToPlayer: true ),
		new( "gangster_2", "Gangster (variant 2)", "units/characters/enemies/gangster2/gangster2", "ak", Faction: "enemy", HostileToPlayer: true ),
		new( "shield", "Shield", "units/characters/enemies/shield/shield", "b9s", Health: 100, HostileToPlayer: true ),
		new( "bulldozer", "Bulldozer", "units/characters/enemies/tank/tank", "reinbeck", Health: 1000, HostileToPlayer: true )
	];
	public static NpcDefinition Get( string id ) => All.First( n => n.Id == id );

	/// <summary>
	/// The closest roster entry for a mission spawn's unit path (<c>ElementSpawnCivilian/EnemyDummy.enemy</c>).
	/// Exact units first, then family. Unlisted enemy units (including special units) fall back
	/// to SWAT; unknown civilians to the casual roster. This is an appearance fallback only.
	/// </summary>
	public static NpcDefinition ForUnit( string unit )
	{
		if ( string.IsNullOrEmpty( unit ) ) return null;
		if ( All.FirstOrDefault( n => n.Unit == unit ) is { } exact ) return exact;
		string u = unit.ToLowerInvariant();
		bool female = u.Contains( "female" ) || u.Contains( "woman" );
		string id = u switch
		{
			_ when u.Contains( "/enemies/security" ) || u.Contains( "/enemies/guard" ) => "security",
			_ when u.Contains( "/enemies/cop" ) || u.Contains( "/enemies/police" ) => "cop",
			_ when u.Contains( "/enemies/" ) => "swat",
			_ when u.Contains( "bank_manager" ) => "bank_manager",
			_ when u.Contains( "hospital_doctor" ) => "doctor",
			_ when u.Contains( "nurse" ) => female ? "nurse" : "nurse_male",
			_ when u.Contains( "suburbia" ) => female ? "suburban_female" : "suburban_male",
			_ when u.Contains( "builder" ) || u.Contains( "worker" ) => "builder",
			_ when u.Contains( "butcher" ) => "butcher",
			_ when u.Contains( "suit" ) || u.Contains( "bank" ) || u.Contains( "office" ) => female ? "bank_staff_female" : "bank_staff",
			_ => female ? "civilian_female" : "civilian",
		};
		return Get( id );
	}
	public Dictionary<string, string> Clips()
	{
		if ( Id == "shield" ) return new()
		{
			["idle"] = "anims/shared/boss/shield/cbt_crh_idle",
			["reload"] = "anims/shared/boss/shield/cbt_crh_reload",
			["walk"] = "anims/shared/boss/shield/cbt_crh_walk_fwd",
			["run"] = "anims/shared/boss/shield/cbt_crh_run_fwd"
		};
		if ( Id == "bulldozer" ) return new()
		{
			["idle"] = "anims/shared/boss/tank/cbt_std_idle",
			["reload"] = "anims/shared/boss/tank/cbt_std_shotgun_reload",
			["walk"] = "anims/shared/boss/tank/cbt_std_walk_fwd_slow",
			["run"] = "anims/shared/boss/tank/cbt_std_run_fwd"
		};
		if ( Civilian ) return new()
		{
			["idle"] = "anims/shared/unarmed/neutral/" + (Female ? "ntl_irp_5_idle_b1_var1" : "ntl_irp_4_idle_b1_var1"),
			["walk"] = "anims/shared/unarmed/neutral/ntl_std_walk_fwd",
			["run"] = "anims/shared/unarmed/neutral/" + (Female ? "pnc_std_fe_run_fwd_var2" : "pnc_std_run_fwd"),
			["surrender"] = "anims/shared/unarmed/neutral/pnc_drop_tied_idle_1_var1"
		};
		string kind = WeaponDefinition.Get( Weapon ).Slot == 1 ? "pistol" : "rifle";
		return new()
		{
			["idle"] = $"anims/shared/{kind}/combat/cbt_std_idle",
			["reload"] = $"anims/shared/{kind}/combat/cbt_std_reload",
			["walk"] = $"anims/shared/{kind}/combat/cbt_std_walk_fwd_fast",
			["run"] = $"anims/shared/{kind}/combat/cbt_std_run_fwd"
		};
	}
}
