namespace Diesel;

sealed record NpcDefinition( string Id, string Title, string Unit, string Weapon = null, bool Female = false, float Health = 100 )
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
		new( "swat", "SWAT", "units/characters/enemies/swat/swat", "amcar4", Health: 200 )
	];
	public static NpcDefinition Get( string id ) => All.First( n => n.Id == id );

	/// <summary>
	/// The closest roster entry for a mission spawn's unit path (<c>ElementSpawnCivilian/EnemyDummy.enemy</c>).
	/// Exact units first, then family. Unported specials (FBI, heavy SWAT, Bulldozer, Shield, Taser, Cloaker,
	/// gangsters, ...) fall back to SWAT for law enforcement; unknown civilians to the casual roster.
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
		if ( Civilian ) return new()
		{
			["idle"] = "anims/shared/unarmed/neutral/" + (Female ? "ntl_irp_5_idle_b1_var1" : "ntl_irp_4_idle_b1_var1"),
			["walk"] = "anims/shared/unarmed/neutral/ntl_std_walk_fwd",
			["run"] = "anims/shared/unarmed/neutral/" + (Female ? "pnc_std_fe_run_fwd_var2" : "pnc_std_run_fwd"),
			["surrender"] = "anims/shared/unarmed/neutral/pnc_drop_tied_idle_1_var1"
		};
		string kind = Weapon == "amcar4" ? "rifle" : "pistol";
		return new()
		{
			["idle"] = $"anims/shared/{kind}/combat/cbt_std_idle",
			["reload"] = $"anims/shared/{kind}/combat/cbt_std_reload",
			["walk"] = $"anims/shared/{kind}/combat/cbt_std_walk_fwd_fast",
			["run"] = $"anims/shared/{kind}/combat/cbt_std_run_fwd"
		};
	}
}
