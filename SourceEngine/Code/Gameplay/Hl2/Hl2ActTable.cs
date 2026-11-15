using System;

namespace Source1;

/// <summary>
/// HL2's NPC weapon acttables: the weapon activity each base activity becomes, and the masked ACT_GESTURE_RELOAD that plays over locomotion.
/// A weapon that says nothing about how it is held is held like the SMG.
/// </summary>
public static class Hl2ActTable
{
	/// <summary>The row of an NPC holding a weapon it has no activities for: the base, unarmed activities.</summary>
	public const string Unarmed = "unarmed";

	sealed record Row( SourceNpcActivities Activities, string GestureReload );

	static readonly Row Base = new( new( "ACT_IDLE", "ACT_IDLE_ANGRY", "ACT_RANGE_ATTACK1", "ACT_RELOAD", "ACT_WALK", "ACT_WALK_AIM", "ACT_RUN", "ACT_RUN_AIM" ), "ACT_GESTURE_RELOAD" );

	static readonly Row Smg1 = new( new( "ACT_IDLE_SMG1", "ACT_IDLE_ANGRY_SMG1", "ACT_RANGE_ATTACK_SMG1", "ACT_RELOAD_SMG1", "ACT_WALK_RIFLE", "ACT_WALK_AIM_RIFLE", "ACT_RUN_RIFLE", "ACT_RUN_AIM_RIFLE" ), "ACT_GESTURE_RELOAD_SMG1" );

	static readonly Row Pistol = new( new( "ACT_IDLE_PISTOL", "ACT_IDLE_ANGRY_PISTOL", "ACT_RANGE_ATTACK_PISTOL", "ACT_RELOAD_PISTOL", "ACT_WALK_PISTOL", "ACT_WALK_AIM_PISTOL", "ACT_RUN_PISTOL", "ACT_RUN_AIM_PISTOL" ), "ACT_GESTURE_RELOAD_PISTOL" );

	static readonly Row Stunstick = new( new( "ACT_IDLE", "ACT_IDLE_ANGRY_MELEE", "ACT_MELEE_ATTACK_SWING", "ACT_RELOAD", "ACT_WALK", "ACT_WALK", "ACT_RUN", "ACT_RUN" ), null );

	static readonly Dictionary<string, Row> Rows = new( StringComparer.OrdinalIgnoreCase )
	{
		["weapon_smg1"] = Smg1,
		["weapon_ar2"] = Smg1 with { Activities = Smg1.Activities with { Attack = "ACT_RANGE_ATTACK_AR2" } },
		["weapon_shotgun"] = new( new( "ACT_IDLE_SMG1", "ACT_IDLE_ANGRY_SHOTGUN", "ACT_RANGE_ATTACK_SHOTGUN", "ACT_RELOAD_SHOTGUN", "ACT_WALK_RIFLE", "ACT_WALK_AIM_SHOTGUN", "ACT_RUN_RIFLE", "ACT_RUN_AIM_SHOTGUN" ), "ACT_GESTURE_RELOAD_SHOTGUN" ),
		["weapon_pistol"] = Pistol,
		// weapon_357 has no NPC acttable in HL2; it is held like the pistol.
		["weapon_357"] = Pistol,
		// weapon_stunstick translates only ACT_IDLE_ANGRY and ACT_MELEE_ATTACK1; the rest stay the unarmed base activities.
		// weapon_stunstick_player is HL2's player script for the same weapon.
		["weapon_stunstick"] = Stunstick,
		["weapon_stunstick_player"] = Stunstick,
		// weapon_crowbar also translates ACT_IDLE.
		["weapon_crowbar"] = Stunstick with { Activities = Stunstick.Activities with { Idle = "ACT_IDLE_ANGRY_MELEE" } },
		["weapon_rpg"] = new( new( "ACT_IDLE_RPG", "ACT_IDLE_ANGRY_RPG", "ACT_RANGE_ATTACK_RPG", "ACT_RELOAD", "ACT_WALK_RPG", "ACT_WALK_RPG", "ACT_RUN_RPG", "ACT_RUN_RPG" ), null ),
		[Unarmed] = Base,
	};

	/// <summary>The class whose row holds each well-known hold; the rest borrow a neighbour's (<see cref="SourceWeaponHolds"/>).</summary>
	static readonly Dictionary<string, string> HoldClasses = new( StringComparer.OrdinalIgnoreCase )
	{
		[WeaponHolds.Pistol] = "weapon_pistol",
		[WeaponHolds.PistolTwoHanded] = "weapon_357",
		[WeaponHolds.Smg] = "weapon_smg1",
		[WeaponHolds.Rifle] = "weapon_ar2",
		[WeaponHolds.Shotgun] = "weapon_shotgun",
		[WeaponHolds.Shoulder] = "weapon_rpg",
		[WeaponHolds.Melee] = "weapon_crowbar",
	};

	/// <summary>HL2MP's player acttables, by the hold type in their ACT_HL2MP_* activities. Garry's Mod's player models have them.</summary>
	static readonly Dictionary<string, string> PlayerHoldTypes = new( StringComparer.OrdinalIgnoreCase )
	{
		["weapon_357"] = "PISTOL",
		["weapon_pistol"] = "PISTOL",
		["weapon_smg1"] = "SMG1",
		["weapon_ar2"] = "AR2",
		["weapon_shotgun"] = "SHOTGUN",
		["weapon_rpg"] = "RPG",
		["weapon_crossbow"] = "CROSSBOW",
		["weapon_crowbar"] = "MELEE",
		["weapon_stunstick"] = "MELEE",
		["weapon_stunstick_player"] = "MELEE",
		["weapon_frag"] = "GRENADE",
		["weapon_physcannon"] = "PHYSGUN",
		["weapon_slam"] = "SLAM",
	};

	/// <summary>A weapon's HL2MP player activities: the standing idle, and the attack and reload gestures over it. Null without an HL2MP acttable.</summary>
	public static (string Idle, string Attack, string Reload)? PlayerActivities( string weaponClass ) => PlayerHoldTypes.TryGetValue( weaponClass ?? "", out var hold )
		? ($"ACT_HL2MP_IDLE_{hold}", $"ACT_HL2MP_GESTURE_RANGE_ATTACK_{hold}", $"ACT_HL2MP_GESTURE_RELOAD_{hold}")
		: null;

	/// <summary>
	/// The row a model holds a weapon with: its weapon script's class, else a class its HoldType names, else the class for its
	/// well-known hold or the closest one, whichever the model has activities for first. Unarmed past those; a weapon without a
	/// well-known hold is held like the SMG.
	/// </summary>
	public static string WeaponClass( SourceModelSequences sequences, BaseCombatWeapon weapon )
	{
		if ( SourceWeaponSetup.WeaponData( weapon )?.ClassName is { } scripted && Has( sequences, scripted ) )
			return scripted;

		if ( Has( sequences, weapon?.HoldType ) )
			return weapon.HoldType;

		var holds = SourceWeaponHolds.For( weapon ).ToArray();
		if ( holds.Select( x => HoldClasses.GetValueOrDefault( x ) ).FirstOrDefault( x => Has( sequences, x ) ) is { } held )
			return held;

		return holds.Length == 0 ? "weapon_smg1" : Unarmed;
	}

	/// <summary>True when the model has any of a weapon row's own (not unarmed) activities.</summary>
	static bool Has( SourceModelSequences sequences, string weaponClass ) => weaponClass is not null && weaponClass != Unarmed
		&& Rows.TryGetValue( weaponClass, out var row )
		&& row.Activities.All.Append( row.GestureReload ).Except( Base.Activities.All ).Any( x => x is not null && sequences.FindActivity( x ) is not null );

	/// <summary>
	/// The model's clips for a weapon's translated activities, with the gesture reload on every stance. A model without one takes the SMG's
	/// before the unarmed base activity, and without an armed aiming idle holds its armed attack, so the gun still comes up to aim
	/// (the mounted monk has neither shoot_shotgun nor Idle_SMG1_Aim clips).
	/// </summary>
	public static SourceNpcWeaponLayers Resolve( SourceModelSequences sequences, string weaponClass )
		=> Resolve( sequences, Rows.GetValueOrDefault( weaponClass ?? "" ) ?? Smg1 );

	/// <summary>Like <see cref="Resolve(SourceModelSequences, string)"/>, but false for a weapon HL2 gives no NPC acttable.</summary>
	public static bool TryResolve( SourceModelSequences sequences, string weaponClass, out SourceNpcWeaponLayers layers )
	{
		layers = Rows.TryGetValue( weaponClass ?? "", out var row ) ? Resolve( sequences, row ) : null;
		return layers is not null;
	}

	static SourceNpcWeaponLayers Resolve( SourceModelSequences sequences, Row row )
	{
		string Armed( Func<SourceNpcActivities, string> slot ) => sequences.FindActivity( slot( row.Activities ) ) ?? sequences.FindActivity( slot( Smg1.Activities ) );
		string Find( Func<SourceNpcActivities, string> slot ) => Armed( slot ) ?? sequences.FindActivity( slot( Base.Activities ) );

		var idle = Find( x => x.Idle );
		var walk = Find( x => x.Walk );
		var run = Find( x => x.Run );
		var clips = new SourceNpcActivities(
			Idle: idle,
			AngryIdle: Armed( x => x.AngryIdle ) ?? Armed( x => x.Attack ) ?? Find( x => x.AngryIdle ) ?? idle,
			Attack: Find( x => x.Attack ),
			Reload: Find( x => x.Reload ),
			Walk: walk,
			WalkAim: Find( x => x.WalkAim ) ?? walk,
			Run: run,
			RunAim: Find( x => x.RunAim ) ?? run );

		var gesture = sequences.FindActivity( row.GestureReload ) ?? sequences.FindActivity( Base.GestureReload );
		var reload = sequences.Get( gesture )?.BoneMask is not null ? gesture : null;
		return new SourceNpcWeaponLayers( clips ) { Reloads = [reload, reload, reload] };
	}
}
