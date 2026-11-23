namespace Doom;

/// <summary>
/// What a monster does beyond standing there, from vanilla's mobjinfo and its states (info.c).
/// The spawn animation, size and solidity stay on its <see cref="ThingType"/>. Animations repeat a
/// letter shown for longer than the others, so each plays back at one frame rate.
/// </summary>
/// <param name="Id">Editor number, matching the thing type.</param>
/// <param name="Health">Spawn health.</param>
/// <param name="Speed">Map units moved per chase step. Each step lasts one tic count of <see cref="See"/>.</param>
/// <param name="PainChance">Out of 256: the chance a hit makes it flinch.</param>
/// <param name="See">The chase animation (see state). Its tics are the time between chase steps.</param>
/// <param name="Pain">The flinch animation.</param>
/// <param name="Death">The death animation, ending on the corpse frame.</param>
/// <param name="GibDeath">The death when overkilled by more than its spawn health (xdeath), if it has one.</param>
/// <param name="Melee">The melee animation, its <see cref="ThingAnimation.Action"/> where the hit lands.</param>
/// <param name="Missile">The ranged attack animation, its <see cref="ThingAnimation.Action"/> where it fires.</param>
/// <param name="MeleeDice">Melee damage is (random % dice + 1) * multiplier, like A_TroopAttack.</param>
/// <param name="MeleeMultiplier">See <paramref name="MeleeDice"/>.</param>
/// <param name="SeeSounds">Played on waking up, one at random.</param>
/// <param name="ActiveSound">Played now and then while chasing.</param>
/// <param name="PainSound">Played when it flinches.</param>
/// <param name="DeathSounds">Played on dying, one at random.</param>
/// <param name="AttackSound">Played as a melee attack starts (mobjinfo attacksound).</param>
/// <param name="MeleeHitSound">Played when a melee attack lands (sfx_claw in A_TroopAttack).</param>
/// <param name="Floats">MF_FLOAT: flies rather than walks.</param>
/// <param name="Attack">What <paramref name="Missile"/> does when its action runs.</param>
/// <param name="Raisable">Whether an arch-vile can bring its corpse back (it has a raise state).</param>
/// <param name="Heal">The arch-vile's resurrecting animation (S_VILE_HEAL), if it raises the dead.</param>
/// <param name="NoCorpse">Its death ends in nothing (S_NULL): the lost soul and pain elemental leave no body.</param>
/// <param name="StepSounds">Sounds its walk makes, by frame of <paramref name="See"/>: the bosses' and arachnotron's footsteps (A_Metal, A_Hoof, A_BabyMetal).</param>
/// <param name="SplashImmune">Takes no damage from explosions (PIT_RadiusAttack spares the Cyberdemon and Spider Mastermind).</param>
public sealed record MonsterInfo( int Id, int Health, int Speed, int PainChance,
	ThingAnimation See, ThingAnimation Pain, ThingAnimation Death, ThingAnimation GibDeath = null,
	ThingAnimation Melee = null, ThingAnimation Missile = null, int MeleeDice = 0, int MeleeMultiplier = 0,
	string[] SeeSounds = null, string ActiveSound = null, string PainSound = null, string[] DeathSounds = null,
	string AttackSound = null, string MeleeHitSound = null, bool Floats = false, MonsterAttack Attack = null,
	bool Raisable = true, ThingAnimation Heal = null, bool NoCorpse = false, (int Frame, string Sound)[] StepSounds = null,
	bool SplashImmune = false )
{
	/// <summary>
	/// Getting back up after an arch-vile raises it: the death animation backwards, starting from the
	/// corpse frame (the S_*_RAISE states).
	/// </summary>
	public ThingAnimation Raise => Raisable
		? new ThingAnimation( Death.Sprite, new string( Death.Frames.Distinct().Reverse().ToArray() ), Death.Tics )
		: null;

	/// <summary>Every animation the monster has, by the name its sprite gives it, the spawn one excluded.</summary>
	public IEnumerable<(string Name, ThingAnimation Animation)> Animations
	{
		get
		{
			yield return (MonsterAnimations.See, See);
			yield return (MonsterAnimations.Pain, Pain);
			yield return (MonsterAnimations.Death, Death);
			if ( GibDeath is not null ) yield return (MonsterAnimations.GibDeath, GibDeath);
			if ( Melee is not null ) yield return (MonsterAnimations.Melee, Melee);
			if ( Missile is not null ) yield return (MonsterAnimations.Missile, Missile);
			if ( Raise is { } raise ) yield return (MonsterAnimations.Raise, raise);
			if ( Heal is not null ) yield return (MonsterAnimations.Heal, Heal);
		}
	}
}

/// <summary>
/// A monster's ranged attack. Hitscan fires <see cref="Pellets"/> bullets at once (A_PosAttack,
/// A_SPosAttack), each doing (random % 5 + 1) * 3 and spread up to 22.5 degrees either side. A
/// projectile attack launches a <see cref="ProjectileInfo"/> (A_TroopAttack).
/// </summary>
/// <param name="Projectile">The projectile's name in <see cref="ProjectileInfos"/>, or null for hitscan.</param>
/// <param name="Pellets">Bullets per shot.</param>
/// <param name="Sound">Played on each shot (the projectile's own launch sound plays as well).</param>
/// <param name="MeleeIfClose">Claws instead when the target is in melee range, like the imp and the barons.</param>
/// <param name="Spread">
/// For each firing in the animation, the angles (degrees, counter-clockwise) its projectiles go
/// off at from the line to the target: the mancubus fires pairs (A_FatAttack1-3). Null fires one, straight.
/// </param>
/// <param name="RefireChance">Out of 256: the chance a refiring attack keeps going though the target is out of sight (40 A_CPosRefire, 10 A_SpidRefire).</param>
/// <param name="MissileHeight">Map units above its feet a projectile starts: 32, or 48 for the revenant (A_SkelMissile).</param>
/// <param name="StartSound">Played as the attack starts, such as the mancubus's roar (A_FatRaise).</param>
/// <param name="Special">
/// A unique attack instead: "vile" for the arch-vile's fire (A_VileTarget, A_VileAttack), "skull" for
/// the lost soul's charge (A_SkullAttack), "pain" for the pain elemental launching a lost soul (A_PainAttack).
/// </param>
/// <param name="MinRange">P_CheckMissileRange: no ranged attack closer than this, in map units (the revenant's 196).</param>
/// <param name="MaxRange">No ranged attack further than this, in map units, or 0 for no limit (the arch-vile's 14 * 64).</param>
/// <param name="HalveRange">Counts the distance as half when deciding to fire, so it fires more from afar (revenant, bosses, lost soul).</param>
/// <param name="RangeCap">The distance counted is capped here: 200, or 160 for the Cyberdemon, so up close it nearly always fires.</param>
public sealed record MonsterAttack( string Projectile = null, int Pellets = 1, string Sound = null, bool MeleeIfClose = false,
	float[][] Spread = null, int RefireChance = 40, int MissileHeight = 32, string StartSound = null, string Special = null,
	int MinRange = 0, int MaxRange = 0, bool HalveRange = false, int RangeCap = 200 );

/// <summary>
/// A missile thing (MF_MISSILE): flies straight at <see cref="Speed"/> and, on hitting something,
/// does (random % 8 + 1) * <see cref="Damage"/> and plays its explosion. Its frames are full bright.
/// </summary>
/// <param name="Name">Unique name, used for its resources.</param>
/// <param name="Sprite">4 letter sprite name.</param>
/// <param name="Frames">Flight frame letters, looped.</param>
/// <param name="Tics">Tics per flight frame.</param>
/// <param name="Explosion">The death animation it plays where it hits.</param>
/// <param name="Radius">Collision radius in map units.</param>
/// <param name="Height">Collision height in map units.</param>
/// <param name="Speed">Map units per tic.</param>
/// <param name="Damage">Damage multiplier.</param>
/// <param name="LaunchSound">Played when it's fired (seesound).</param>
/// <param name="ExplodeSound">Played when it hits (deathsound).</param>
/// <param name="Homing">Steers after its target (A_Tracer), like the revenant's missile.</param>
/// <param name="Splash">Blast damage at the centre of its explosion, falling to nothing this many map units out (A_Explode: 128 for a rocket), or 0.</param>
public sealed record ProjectileInfo( string Name, string Sprite, string Frames, int Tics, ThingAnimation Explosion,
	int Radius, int Height, int Speed, int Damage, string LaunchSound, string ExplodeSound, bool Homing = false, int Splash = 0 )
{
	/// <summary>The projectile as a thing type, for building its sprite: flight as the spawn animation, the explosion as its death.</summary>
	public ThingType AsThing => new( 0, Name, ThingCategory.Decoration, Sprite, Frames, Tics, Radius, Height, ThingTypeFlags.Bright, Death: Explosion );
}

public static class ProjectileInfos
{
	/// <summary>The vanilla missiles monsters fire.</summary>
	[SkipHotload]
	public static readonly IReadOnlyDictionary<string, ProjectileInfo> Vanilla = new ProjectileInfo[]
	{
		// MT_TROOPSHOT: S_TBALL1/2, S_TBALLX1-3
		new( "imp_fireball", "BAL1", "AB", 4, new ThingAnimation( "BAL1", "CDE", 6 ), 6, 8, 10, 3, "DSFIRSHT", "DSFIRXPL" ),

		// MT_BRUISERSHOT: S_BRBALL1/2, S_BRBALLX1-3
		new( "baron_fireball", "BAL7", "AB", 4, new ThingAnimation( "BAL7", "CDE", 6 ), 6, 16, 15, 8, "DSFIRSHT", "DSFIRXPL" ),

		// MT_TRACER: S_TRACER, S_TRACEEXP1-3
		new( "revenant_missile", "FATB", "AB", 2, new ThingAnimation( "FBXP", "ABC", 6 ), 11, 8, 10, 10, "DSSKEATK", "DSBAREXP", Homing: true ),

		// MT_FATSHOT: S_FATSHOT1/2, S_FATSHOTX1-3
		new( "mancubus_fireball", "MANF", "AB", 4, new ThingAnimation( "MISL", "BCD", 6 ), 6, 8, 20, 8, "DSFIRSHT", "DSFIRXPL" ),

		// MT_ARACHPLAZ: S_ARACH_PLAZ, S_ARACH_PLEX
		new( "arachnotron_plasma", "APLS", "AB", 5, new ThingAnimation( "APBX", "ABCDE", 5 ), 13, 8, 25, 5, "DSPLASMA", "DSFIRXPL" ),

		// MT_HEADSHOT: S_RBALL1/2, S_RBALLX1-3
		new( "cacodemon_fireball", "BAL2", "AB", 4, new ThingAnimation( "BAL2", "CDE", 6 ), 6, 8, 10, 5, "DSFIRSHT", "DSFIRXPL" ),

		// MT_ROCKET: S_ROCKET, S_EXPLODE1-3, blasting 128 around where it hits (A_Explode).
		new( "rocket", "MISL", "A", 1, new ThingAnimation( "MISL", "BCD", 6 ), 11, 8, 20, 20, "DSRLAUNC", "DSBAREXP", Splash: 128 ),
	}.ToDictionary( p => p.Name );
}

/// <summary>Short-lived sprites shot impacts leave: a bullet puff on walls (MT_PUFF) and blood on things that bleed (MT_BLOOD).</summary>
public static class HitEffects
{
	[SkipHotload] public static readonly ThingType Puff = new( 0, "bullet_puff", ThingCategory.Decoration, "PUFF", "ABCD", 4, Flags: ThingTypeFlags.Bright );
	[SkipHotload] public static readonly ThingType Blood = new( 0, "blood", ThingCategory.Decoration, "BLUD", "CBA", 8 );

	/// <summary>The arch-vile's fire on its target (MT_FIRE, S_FIRE1-30), shown for the length of the attack.</summary>
	[SkipHotload] public static readonly ThingType VileFire = new( 0, "vile_fire", ThingCategory.Decoration, "FIRE", "ABABCBCBCDEDEDEFGHGHGH", 2, Flags: ThingTypeFlags.Bright );

	/// <summary>The burst a monster arrives in from the Icon of Sin's cube (MT_SPAWNFIRE, S_SPAWNFIRE1-8).</summary>
	[SkipHotload] public static readonly ThingType SpawnFire = new( 0, "spawn_fire", ThingCategory.Decoration, "FIRE", "ABCDEFGH", 4, Flags: ThingTypeFlags.Bright );

	/// <summary>A rocket's explosion on its own, for the Icon of Sin's death throes (A_BrainScream).</summary>
	[SkipHotload] public static readonly ThingType RocketExplosion = new( 0, "rocket_explosion", ThingCategory.Decoration, "MISL", "BCD", 6, Flags: ThingTypeFlags.Bright );

	public static IEnumerable<ThingType> All => [Puff, Blood, VileFire, SpawnFire, RocketExplosion];
}

/// <summary>The Icon of Sin's spawn cube (MT_SPAWNSHOT, S_SPAWNSHOT): flies through walls to a spawn spot and leaves a monster there.</summary>
public static class BrainCube
{
	[SkipHotload] public static readonly ThingType Type = new( 0, "spawn_cube", ThingCategory.Decoration, "BOSF", "ABCD", 3, 6, 32, ThingTypeFlags.Bright );

	/// <summary>Map units per tic.</summary>
	public const int Speed = 10;

	/// <summary>
	/// A_SpawnFly: what arrives, by a random number out of 256: below each threshold, that editor
	/// number. Imps are commonest; arch-viles rarest.
	/// </summary>
	[SkipHotload]
	public static readonly (int Below, int Monster)[] Spawns =
	[
		(50, 3001), (90, 9), (120, 58), (130, 71), (160, 3005), (162, 64), (172, 66), (192, 68), (222, 67), (246, 69), (256, 3003),
	];
}

/// <summary>Names of a monster sprite's animations. Each has one copy per rotation, named with its number (1 to 8) after it.</summary>
public static class MonsterAnimations
{
	public const string Spawn = "spawn", See = "see", Pain = "pain", Death = "death", GibDeath = "xdeath", Melee = "melee", Missile = "missile",
		Raise = "raise", Heal = "heal";

	/// <summary>The sprite animation for a state seen from a rotation, 1 (front) to 8.</summary>
	public static string Name( string state, int rotation ) => $"{state}{rotation}";
}

public static class MonsterInfos
{
	static ThingAnimation A( string sprite, string frames, int tics, char action = default ) => new( sprite, frames, tics, action );

	static readonly string[] Posit = ["DSPOSIT1", "DSPOSIT2", "DSPOSIT3"];
	static readonly string[] Podth = ["DSPODTH1", "DSPODTH2", "DSPODTH3"];

	/// <summary>
	/// Every vanilla monster that fights. Commander Keen and the Icon of Sin aren't here: they don't
	/// move or attack, and are their own things (<see cref="ThingTypeFlags.KeenDie"/>, <see cref="ThingTypeFlags.BossBrain"/>).
	/// </summary>
	/// <remarks>Built afresh on hotload rather than carried over, so edited records come through whole.</remarks>
	[SkipHotload]
	public static readonly IReadOnlyDictionary<int, MonsterInfo> Vanilla = new MonsterInfo[]
	{
		// Zombieman: S_POSS_*
		new( 3004, 20, 8, 200,
			See: A( "POSS", "AABBCCDD", 4 ), Pain: A( "POSS", "GG", 3 ),
			Death: A( "POSS", "HIJKL", 5 ), GibDeath: A( "POSS", "MNOPQRSTU", 5 ),
			Missile: A( "POSS", "EEEEEFFFFEEEE", 2, 'F' ), Attack: new( Pellets: 1, Sound: "DSPISTOL" ),
			SeeSounds: Posit, ActiveSound: "DSPOSACT", PainSound: "DSPOPAIN", DeathSounds: Podth ),

		// Shotgun guy: S_SPOS_*
		new( 9, 30, 8, 170,
			See: A( "SPOS", "AABBCCDD", 3 ), Pain: A( "SPOS", "GG", 3 ),
			Death: A( "SPOS", "HIJKL", 5 ), GibDeath: A( "SPOS", "MNOPQRSTU", 5 ),
			Missile: A( "SPOS", "EFE", 10, 'F' ), Attack: new( Pellets: 3, Sound: "DSSHOTGN" ),
			SeeSounds: Posit, ActiveSound: "DSPOSACT", PainSound: "DSPOPAIN", DeathSounds: Podth ),

		// Heavy weapon dude: S_CPOS_*
		new( 65, 70, 8, 170,
			See: A( "CPOS", "AABBCCDD", 3 ), Pain: A( "CPOS", "GG", 3 ),
			Death: A( "CPOS", "HIJKLMN", 5 ), GibDeath: A( "CPOS", "OPQRST", 5 ),
			// E10, then F4 F4 firing on each (A_CPosAttack) and F1 deciding whether to keep firing (A_CPosRefire).
			Missile: new( "CPOS", "EEEEEEEEEEFFFFFFFFF", 1, ActionFrames: [10, 14], LoopFrame: 10 ),
			Attack: new( Pellets: 1, Sound: "DSSHOTGN" ),
			SeeSounds: Posit, ActiveSound: "DSPOSACT", PainSound: "DSPOPAIN", DeathSounds: Podth ),

		// Imp: S_TROO_*. Melee and missile share the same states; A_TroopAttack claws in range.
		new( 3001, 60, 8, 200,
			See: A( "TROO", "AABBCCDD", 3 ), Pain: A( "TROO", "HH", 2 ),
			Death: A( "TROO", "IIIIJJJJKKKLLLM", 2 ), GibDeath: A( "TROO", "NOPQRSTU", 5 ),
			Melee: A( "TROO", "EEEEFFFFGGG", 2, 'G' ), Missile: A( "TROO", "EEEEFFFFGGG", 2, 'G' ),
			Attack: new( Projectile: "imp_fireball", MeleeIfClose: true ),
			MeleeDice: 8, MeleeMultiplier: 3,
			SeeSounds: ["DSBGSIT1", "DSBGSIT2"], ActiveSound: "DSBGACT", PainSound: "DSPOPAIN",
			DeathSounds: ["DSBGDTH1", "DSBGDTH2"], MeleeHitSound: "DSCLAW" ),

		// Demon: S_SARG_*
		new( 3002, 150, 10, 180,
			See: A( "SARG", "AABBCCDD", 2 ), Pain: A( "SARG", "HH", 2 ),
			Death: A( "SARG", "IIJJKLMN", 4 ),
			Melee: A( "SARG", "EFG", 8, 'G' ), MeleeDice: 10, MeleeMultiplier: 4,
			SeeSounds: ["DSSGTSIT"], ActiveSound: "DSDMACT", PainSound: "DSDMPAIN",
			DeathSounds: ["DSSGTDTH"], AttackSound: "DSSGTATK" ),

		// Spectre: the demon, drawn with the fuzz effect in vanilla.
		new( 58, 150, 10, 180,
			See: A( "SARG", "AABBCCDD", 2 ), Pain: A( "SARG", "HH", 2 ),
			Death: A( "SARG", "IIJJKLMN", 4 ),
			Melee: A( "SARG", "EFG", 8, 'G' ), MeleeDice: 10, MeleeMultiplier: 4,
			SeeSounds: ["DSSGTSIT"], ActiveSound: "DSDMACT", PainSound: "DSDMPAIN",
			DeathSounds: ["DSSGTDTH"], AttackSound: "DSSGTATK" ),

		// Hell knight: S_BOS2_*. Claws in range (A_BruisAttack), throws otherwise.
		new( 69, 500, 8, 50,
			See: A( "BOS2", "AABBCCDD", 3 ), Pain: A( "BOS2", "HH", 2 ),
			Death: A( "BOS2", "IJKLMNO", 8 ),
			Melee: A( "BOS2", "EFG", 8, 'G' ), Missile: A( "BOS2", "EFG", 8, 'G' ),
			Attack: new( Projectile: "baron_fireball", MeleeIfClose: true ),
			MeleeDice: 8, MeleeMultiplier: 10,
			SeeSounds: ["DSKNTSIT"], ActiveSound: "DSDMACT", PainSound: "DSDMPAIN",
			DeathSounds: ["DSKNTDTH"], MeleeHitSound: "DSCLAW" ),

		// Baron of hell: S_BOSS_*
		new( 3003, 1000, 8, 50,
			See: A( "BOSS", "AABBCCDD", 3 ), Pain: A( "BOSS", "HH", 2 ),
			Death: A( "BOSS", "IJKLMNO", 8 ),
			Melee: A( "BOSS", "EFG", 8, 'G' ), Missile: A( "BOSS", "EFG", 8, 'G' ),
			Attack: new( Projectile: "baron_fireball", MeleeIfClose: true ),
			MeleeDice: 8, MeleeMultiplier: 10,
			SeeSounds: ["DSBRSSIT"], ActiveSound: "DSDMACT", PainSound: "DSDMPAIN",
			DeathSounds: ["DSBRSDTH"], MeleeHitSound: "DSCLAW" ),

		// Wolfenstein SS: S_SSWV_*. E10 F10, then G4 F6 G4 firing on each G (A_CPosAttack), F1 to refire.
		new( 84, 50, 8, 170,
			See: A( "SSWV", "AABBCCDD", 3 ), Pain: A( "SSWV", "HH", 3 ),
			Death: A( "SSWV", "IJKLM", 5 ), GibDeath: A( "SSWV", "NOPQRSTUV", 5 ),
			Missile: new( "SSWV", "EEEEEEEEEEFFFFFFFFFFGGGGFFFFFFGGGGF", 1, ActionFrames: [20, 30], LoopFrame: 10 ),
			Attack: new( Pellets: 1, Sound: "DSSHOTGN" ),
			SeeSounds: ["DSSSSIT"], ActiveSound: "DSPOSACT", PainSound: "DSPOPAIN", DeathSounds: ["DSSSDTH"] ),

		// Revenant: S_SKEL_*. Punches up close (A_SkelFist, swung with A_SkelWhoosh), otherwise fires
		// a homing missile from higher up than most (A_SkelMissile).
		new( 66, 300, 10, 100,
			See: A( "SKEL", "AABBCCDDEEFF", 2 ), Pain: A( "SKEL", "LL", 5 ),
			Death: A( "SKEL", "LMNOPQ", 7 ),
			Melee: A( "SKEL", "GGGHHHIII", 2, 'I' ), MeleeDice: 10, MeleeMultiplier: 6,
			Missile: A( "SKEL", "JKK", 10, 'K' ),
			Attack: new( Projectile: "revenant_missile", MissileHeight: 48, MinRange: 196, HalveRange: true ),
			SeeSounds: ["DSSKESIT"], ActiveSound: "DSSKEACT", PainSound: "DSPOPAIN", DeathSounds: ["DSSKEDTH"],
			AttackSound: "DSSKESWG", MeleeHitSound: "DSSKEPCH" ),

		// Mancubus: S_FATT_*. G20 (A_FatRaise), then three volleys of two fireballs each (A_FatAttack1-3).
		new( 67, 600, 8, 80,
			See: A( "FATT", "AABBCCDDEEFF", 4 ), Pain: A( "FATT", "JJ", 3 ),
			Death: A( "FATT", "KLMNOPQRST", 6 ),
			Missile: new( "FATT", "GGGGHHIGHHIGHHIG", 5, ActionFrames: [4, 8, 12] ),
			Attack: new( Projectile: "mancubus_fireball", StartSound: "DSMANATK",
				Spread: [[0f, 11.25f], [0f, -22.5f], [-5.625f, 5.625f]] ),
			SeeSounds: ["DSMANSIT"], ActiveSound: "DSPOSACT", PainSound: "DSMNPAIN", DeathSounds: ["DSMANDTH"] ),

		// Arachnotron: S_BSPI_*. A20 aiming, then plasma every 9 tics while A_SpidRefire keeps it going.
		new( 68, 500, 12, 128,
			See: A( "BSPI", "AABBCCDDEEFF", 3 ), Pain: A( "BSPI", "II", 3 ),
			Death: A( "BSPI", "JJJKLMNOP", 7 ),
			Missile: new( "BSPI", "AAAAAAAAAAAAAAAAAAAAGGGGHHHHH", 1, ActionFrames: [20], LoopFrame: 20 ),
			Attack: new( Projectile: "arachnotron_plasma", RefireChance: 10 ),
			SeeSounds: ["DSBSPSIT"], ActiveSound: "DSBSPACT", PainSound: "DSDMPAIN", DeathSounds: ["DSBSPDTH"],
			StepSounds: [(0, "DSBSPWLK"), (6, "DSBSPWLK")] ),

		// Arch-vile: S_VILE_*. Sets its target alight (A_VileTarget at H), then blasts it if it's still in
		// sight (A_VileAttack at O). Raises corpses it walks past (A_VileChase), and can't itself be raised.
		new( 64, 700, 15, 10,
			See: A( "VILE", "AABBCCDDEEFF", 2 ), Pain: A( "VILE", "QQ", 5 ),
			Death: A( "VILE", "QRSTUVWXYZ", 7 ),
			Missile: new( "VILE", "GGGGGHHHHIIIIJJJJKKKKLLLLMMMMNNNNOOOOPPPPPPPPPP", 2, ActionFrames: [5, 33] ),
			Attack: new( Special: "vile", StartSound: "DSVILATK", MaxRange: 14 * 64 ),
			Heal: A( "VILE", "[\\]", 10 ), Raisable: false,
			SeeSounds: ["DSVILSIT"], ActiveSound: "DSVILACT", PainSound: "DSVIPAIN", DeathSounds: ["DSVILDTH"] ),

		// Cacodemon: S_HEAD_*. Floats. Bites up close and spits fireballs otherwise (A_HeadAttack),
		// with no melee state of its own, so it's keen to fire from afar.
		new( 3005, 400, 8, 128,
			See: A( "HEAD", "A", 3 ), Pain: A( "HEAD", "EEFF", 3 ),
			Death: A( "HEAD", "GHIJKL", 8 ),
			Missile: A( "HEAD", "BCD", 5, 'D' ), MeleeDice: 6, MeleeMultiplier: 10,
			Attack: new( Projectile: "cacodemon_fireball", MeleeIfClose: true ),
			SeeSounds: ["DSCACSIT"], ActiveSound: "DSDMACT", PainSound: "DSDMPAIN", DeathSounds: ["DSCACDTH"],
			Floats: true ),

		// Lost soul: S_SKULL_*. Floats, and attacks by flying at its target (A_SkullAttack), doing
		// (random % 8 + 1) * 3 to whatever it hits. C10, then D4 C4 D4 looping while it flies. Never
		// flinches mid-flight, and burns away to nothing.
		new( 3006, 100, 8, 256,
			See: A( "SKUL", "AB", 6 ), Pain: A( "SKUL", "EE", 3 ),
			Death: A( "SKUL", "FGHIJK", 6 ),
			Missile: new( "SKUL", "CCCCCCCCCCDDDDCCCCDDDD", 1, ActionFrames: [10], LoopFrame: 14 ),
			Attack: new( Special: "skull", HalveRange: true ), MeleeDice: 8, MeleeMultiplier: 3,
			ActiveSound: "DSDMACT", PainSound: "DSDMPAIN", DeathSounds: ["DSFIRXPL"], AttackSound: "DSSKLATK",
			Floats: true, NoCorpse: true, Raisable: false ),

		// Pain elemental: S_PAIN_*. Floats. Launches a lost soul at its target (A_PainAttack after F),
		// and three more as it dies (A_PainDie). Leaves nothing behind.
		new( 71, 400, 8, 128,
			See: A( "PAIN", "AABBCC", 3 ), Pain: A( "PAIN", "GG", 6 ),
			Death: A( "PAIN", "HIJKLM", 8 ),
			Missile: new( "PAIN", "DEF", 5, ActionFrames: [3] ),
			Attack: new( Special: "pain" ),
			SeeSounds: ["DSPESIT"], ActiveSound: "DSDMACT", PainSound: "DSPEPAIN", DeathSounds: ["DSPEDTH"],
			Floats: true, NoCorpse: true, Raisable: false ),

		// Spider Mastermind: S_SPID_*. A20 aiming, then a three-pellet blast every 4 tics while
		// A_SpidRefire keeps it going. Metal footsteps (A_Metal on A, C and E).
		new( 7, 3000, 12, 40,
			See: A( "SPID", "AABBCCDDEEFF", 3 ), Pain: A( "SPID", "II", 3 ),
			Death: A( "SPID", "JJKLMNOPQRS", 10 ),
			Missile: new( "SPID", "AAAAAAAAAAAAAAAAAAAAGGGGHHHHHHHHHHH", 1, ActionFrames: [20, 24], LoopFrame: 20 ),
			Attack: new( Pellets: 3, Sound: "DSSHOTGN", RefireChance: 10, HalveRange: true ),
			SeeSounds: ["DSSPISIT"], ActiveSound: "DSDMACT", PainSound: "DSDMPAIN", DeathSounds: ["DSSPIDTH"],
			StepSounds: [(0, "DSMETAL"), (4, "DSMETAL"), (8, "DSMETAL")], SplashImmune: true, Raisable: false ),

		// Cyberdemon: S_CYBER_*. Three rockets a volley (A_CyberAttack on each F), and the keenest of all
		// to fire. Hooves and metal underfoot (A_Hoof on A, A_Metal on D).
		new( 16, 4000, 16, 20,
			See: A( "CYBR", "AABBCCDD", 3 ), Pain: A( "CYBR", "G", 10 ),
			Death: A( "CYBR", "HIJKLMNOP", 10 ),
			Missile: new( "CYBR", "EFFEEFFEEFF", 6, ActionFrames: [1, 5, 9] ),
			Attack: new( Projectile: "rocket", HalveRange: true, RangeCap: 160 ),
			SeeSounds: ["DSCYBSIT"], ActiveSound: "DSDMACT", PainSound: "DSDMPAIN", DeathSounds: ["DSCYBDTH"],
			StepSounds: [(0, "DSHOOF"), (6, "DSMETAL")], SplashImmune: true, Raisable: false ),
	}.ToDictionary( m => m.Id );
}
