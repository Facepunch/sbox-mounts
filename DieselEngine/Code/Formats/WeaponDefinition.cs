namespace Diesel;

/// <summary>Unupgraded PDTH player arsenal. Damage uses the mount's 10x sandbox scale.</summary>
sealed record WeaponDefinition( string Id, string Title, string Unit, string World, string Animation, string State, string Bank, string Prefix,
	int Magazine, int Reserve, float Damage, float Interval, bool Automatic, float Spread, float Tactical, float Empty, int Slot = 0, bool Shotgun = false )
{
	public string ViewUnit => $"units/weapons/{Unit}";
	public string WorldUnit => $"units/weapons/{World}";
	public string Root => $"entities/weapons/{Id}.prefab";
	public string View => $"entities/parts/{Id}_view.prefab";
	public string WorldPrefab => $"entities/parts/{Id}_world.prefab";
	public string Ammo => $"entities/ammo/{Id}.ammo";
	public bool Grenade => Id == "gl40";
	public IEnumerable<string> Clips => Shotgun ? ["idle", "equip", "recoil", "reload_enter", "reload", "reload_exit"]
		: Grenade ? ["idle", "equip", "recoil", "reload"] : ["idle", "equip", "recoil", "reload", "reload_not_empty"];
	public string ClipPath( string clip ) => $"anims/fps/{Animation}/{Animation}_{clip}";
	public static WeaponDefinition Get( string id ) => All.First( w => w.Id == id );
	public static readonly WeaponDefinition[] All =
	[
		new("amcar4", "AMCAR-4", "m4_rifle/m4_rifle", "m4_rifle_npc/m4_rifle_npc", "m4", "m4", "amcar4", "m4", 20, 100, 20, .1f, true, 4, 1.4f, 1.9f),
		new("b9s", "B9-S", "beretta92/beretta92", "beretta92_npc/beretta92_npc", "beretta", "beretta92", "b9s", "beretta", 14, 42, 10, .08f, false, 3.5f, 1.8f, 2.6f, 1),
		new("crosskill", "Crosskill .45", "c45/c45", "c45_npc/c45_npc", "c45", "c45", "c45", "c45", 10, 40, 15, .12f, false, 4.5f, 1.9f, 2.6f, 1),
		new("bronco44", "Bronco .44", "raging_bull/raging_bull", "raging_bull_npc/raging_bull_npc", "raging_bull", "raging_bull", "bronco44", "ragingbull", 6, 30, 45, .22f, false, 2.5f, 4.6f, 4.6f, 1),
		new("stryk", "STRYK", "glock/glock", "glock_npc/glock_18_npc", "glock", "glock", "stryk_auto", "stryk", 20, 140, 10, .066f, true, 4, 2.5f, 2.7f, 1),
		new("reinbeck", "Reinbeck", "r870_shotgun/r870_shotgun", "r870_shotgun_npc/r870_shotgun_npc", "r870", "r870_shotgun", "reinbeck880", "remington", 6, 24, 60, .8f, false, 4, .65f, .65f, 0, true),
		new("locomotive", "Locomotive 12G", "mossberg/mossberg", "mossberg_npc/mossberg_npc", "mossberg", "mossberg", "locomotive12g", "mossberg", 4, 20, 40, .8f, false, 5, .65f, .65f, 2, true),
		new("compact5", "Compact-5", "mp5/mp5", "mp5_npc/mp5_npc", "mp5", "mp5", "com5", "mp5", 30, 120, 11.5f, .1f, true, 3.5f, 2.2f, 2.8f, 2),
		new("mark11", "Mark 11", "mac11/mac11", "mac11_npc/mac11_npc", "mac11", "mac11", "mark11", "mk11", 30, 120, 13, .066f, true, 4, 2.15f, 3.1f, 2),
		new("m308", "M308", "m14/m14", "m14_npc/m14_npc", "m14", "m14", "m308", "m14", 12, 60, 40, .1f, false, 7, 1.71f, 2.61f),
		new("brenner21", "Brenner 21", "hk21/hk21", "hk21_npc/hk21_npc", "hk21", "hk21", "b21", "b21", 80, 160, 25, .1333f, true, 2.5f, 4.25f, 5.4f),
		new("ak", "AK", "ak47/ak", "ak47_npc/ak47_npc", "ak47", "ak47", "ak47", "ak47", 25, 100, 27, .07f, true, 5, 2.3f, 3.9f),
		new("gl40", "GL40", "m79/m79", "m79_npc/m79_npc", "m79", "m79", "gl", "gl", 1, 3, 400, .1f, false, 7, 4.3f, 4.3f, 2)
	];
}
