namespace IdTech3;

/// <summary>Quake III Arena, plus Team Arena (missionpack) and any installed mods.</summary>
public sealed class Quake3Mount : IdTech3Mount
{
	public override string Ident => "quake3";
	public override string Title => "Quake III Arena";
	protected override long AppId => 2200;
	protected override string BaseDirectory => "baseq3";
	protected override bool MountOtherDirectories => true;
	protected override string[] IgnoredDirectories => ["Docs", "Extras", "Punkbuster", "pb"];

	static readonly GameProfile _profile = Profiles.Quake3();
	public override GameProfile Profile => _profile;
}

/// <summary>Return to Castle Wolfenstein.</summary>
public sealed class RtcwMount : IdTech3Mount
{
	public override string Ident => "rtcw";
	public override string Title => "Return to Castle Wolfenstein";
	protected override long AppId => 9010;
	protected override string BaseDirectory => "Main";

	static readonly GameProfile _profile = Profiles.Rtcw();
	public override GameProfile Profile => _profile;
}

/// <summary>Call of Duty (2003).</summary>
public sealed class CallOfDutyMount : IdTech3Mount
{
	public override string Ident => "cod";
	public override string Title => "Call of Duty (2003)";
	protected override long AppId => 2620;
	protected override string BaseDirectory => "Main";

	// the Steam release ships most of its data (and maps) as pak*.zip
	protected override string[] ArchiveExtensions => [".pk3", ".zip"];

	static readonly GameProfile _profile = Profiles.CallOfDuty( "Call of Duty" );
	public override GameProfile Profile => _profile;
}

/// <summary>Star Wars Jedi Knight II: Jedi Outcast (maps, MD3 models, shaders, textures and sounds).</summary>
public sealed class JediOutcastMount : IdTech3Mount
{
	public override string Ident => "jk2";
	public override string Title => "Jedi Knight II: Jedi Outcast";
	protected override long AppId => 6030;
	protected override string BaseDirectory => "GameData/base";

	static readonly GameProfile _profile = Profiles.Raven( "Jedi Knight II: Jedi Outcast" );
	public override GameProfile Profile => _profile;
}

/// <summary>Star Wars Jedi Knight: Jedi Academy (maps, MD3 models, shaders, textures and sounds).</summary>
public sealed class JediAcademyMount : IdTech3Mount
{
	public override string Ident => "jka";
	public override string Title => "Jedi Knight: Jedi Academy";
	protected override long AppId => 6020;
	protected override string BaseDirectory => "GameData/base";

	static readonly GameProfile _profile = Profiles.Raven( "Jedi Knight: Jedi Academy" );
	public override GameProfile Profile => _profile;
}

static class Profiles
{
	static readonly string[] CommonSpawns =
	[
		"info_player_deathmatch", "info_player_start", "team_CTF_redplayer", "team_CTF_blueplayer",
		"team_CTF_redspawn", "team_CTF_bluespawn",
	];

	public static GameProfile Quake3()
	{
		var p = new GameProfile { Title = "Quake III Arena", LightShift = 2, SurfaceFlags = SurfaceFlagStyle.Quake3 };
		foreach ( var s in CommonSpawns ) p.SpawnClasses.Add( s );

		void Item( string cls, string model ) => p.ItemModels[cls] = model;
		Item( "item_armor_shard", "models/powerups/armor/shard.md3" );
		Item( "item_armor_combat", "models/powerups/armor/armor_yel.md3" );
		Item( "item_armor_body", "models/powerups/armor/armor_red.md3" );
		Item( "item_health_small", "models/powerups/health/small_cross.md3" );
		Item( "item_health", "models/powerups/health/medium_cross.md3" );
		Item( "item_health_large", "models/powerups/health/large_cross.md3" );
		Item( "item_health_mega", "models/powerups/health/mega_cross.md3" );
		Item( "weapon_gauntlet", "models/weapons2/gauntlet/gauntlet.md3" );
		Item( "weapon_shotgun", "models/weapons2/shotgun/shotgun.md3" );
		Item( "weapon_machinegun", "models/weapons2/machinegun/machinegun.md3" );
		Item( "weapon_grenadelauncher", "models/weapons2/grenadel/grenadel.md3" );
		Item( "weapon_rocketlauncher", "models/weapons2/rocketl/rocketl.md3" );
		Item( "weapon_lightning", "models/weapons2/lightning/lightning.md3" );
		Item( "weapon_railgun", "models/weapons2/railgun/railgun.md3" );
		Item( "weapon_plasmagun", "models/weapons2/plasma/plasma.md3" );
		Item( "weapon_bfg", "models/weapons2/bfg/bfg.md3" );
		Item( "weapon_grapplinghook", "models/weapons2/grapple/grapple.md3" );
		Item( "weapon_nailgun", "models/weapons/nailgun/nailgun.md3" );
		Item( "weapon_prox_launcher", "models/weapons/proxmine/proxmine.md3" );
		Item( "weapon_chaingun", "models/weapons/vulcan/vulcan.md3" );
		Item( "ammo_shells", "models/powerups/ammo/shotgunam.md3" );
		Item( "ammo_bullets", "models/powerups/ammo/machinegunam.md3" );
		Item( "ammo_grenades", "models/powerups/ammo/grenadeam.md3" );
		Item( "ammo_cells", "models/powerups/ammo/plasmaam.md3" );
		Item( "ammo_lightning", "models/powerups/ammo/lightningam.md3" );
		Item( "ammo_rockets", "models/powerups/ammo/rocketam.md3" );
		Item( "ammo_slugs", "models/powerups/ammo/railgunam.md3" );
		Item( "ammo_bfg", "models/powerups/ammo/bfgam.md3" );
		Item( "ammo_nails", "models/powerups/ammo/nailgunam.md3" );
		Item( "ammo_mines", "models/powerups/ammo/proxmineam.md3" );
		Item( "ammo_belt", "models/powerups/ammo/chaingunam.md3" );
		Item( "holdable_teleporter", "models/powerups/holdable/teleporter.md3" );
		Item( "holdable_medkit", "models/powerups/holdable/medkit.md3" );
		Item( "holdable_kamikaze", "models/powerups/kamikazi.md3" );
		Item( "holdable_portal", "models/powerups/holdable/porter.md3" );
		Item( "holdable_invulnerability", "models/powerups/holdable/invulnerability.md3" );
		Item( "item_quad", "models/powerups/instant/quad.md3" );
		Item( "item_enviro", "models/powerups/instant/enviro.md3" );
		Item( "item_haste", "models/powerups/instant/haste.md3" );
		Item( "item_invis", "models/powerups/instant/invis.md3" );
		Item( "item_regen", "models/powerups/instant/regen.md3" );
		Item( "item_flight", "models/powerups/instant/flight.md3" );
		Item( "item_scout", "models/powerups/scout.md3" );
		Item( "item_guard", "models/powerups/guard.md3" );
		Item( "item_doubler", "models/powerups/doubler.md3" );
		Item( "item_ammoregen", "models/powerups/ammo.md3" );
		Item( "item_redcube", "models/powerups/orb/r_orb.md3" );
		Item( "item_bluecube", "models/powerups/orb/b_orb.md3" );
		Item( "team_CTF_redflag", "models/flags/r_flag.md3" );
		Item( "team_CTF_blueflag", "models/flags/b_flag.md3" );
		Item( "team_CTF_neutralflag", "models/flags/n_flag.md3" );
		Item( "team_redobelisk", "models/powerups/obelisk/obelisk.md3" );
		Item( "team_blueobelisk", "models/powerups/obelisk/obelisk.md3" );
		Item( "team_neutralobelisk", "models/powerups/obelisk/obelisk.md3" );
		return p;
	}

	public static GameProfile Rtcw()
	{
		var p = new GameProfile { Title = "Return to Castle Wolfenstein", LightShift = 2, SurfaceFlags = SurfaceFlagStyle.Rtcw };
		foreach ( var s in CommonSpawns ) p.SpawnClasses.Add( s );
		p.SpawnClasses.Add( "info_player_checkpoint" );
		return p;
	}

	public static GameProfile CallOfDuty( string title )
	{
		var p = new GameProfile
		{
			Title = title,
			LightShift = 1,
			CallOfDuty = true,
			SurfaceFlags = SurfaceFlagStyle.CallOfDuty,
			ImageExtensions = [".dds", ".tga", ".jpg", ".png"],
		};
		p.SpawnClasses.Add( "info_player_start" );
		p.SpawnClasses.Add( "info_player_deathmatch" );
		return p;
	}

	public static GameProfile Raven( string title )
	{
		var p = new GameProfile { Title = title, LightShift = 1 };
		foreach ( var s in CommonSpawns ) p.SpawnClasses.Add( s );
		p.SpawnClasses.Add( "info_player_duel" );
		p.SpawnClasses.Add( "info_player_siegeteam1" );
		p.SpawnClasses.Add( "info_player_siegeteam2" );
		return p;
	}
}
