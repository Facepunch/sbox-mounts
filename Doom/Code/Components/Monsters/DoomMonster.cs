using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Schedules;

namespace Doom;

/// <summary>
/// A Doom monster on the s&amp;box NPC system. It stands until it sees or hears a hostile (A_Look),
/// then chases it across the navmesh (A_Chase) and attacks when it can. Hits can make it flinch
/// (painchance) and turn it on whoever dealt them, other monsters included. It dies into a corpse
/// sprite, or a gib one when overkilled. Values are in world units and seconds, built from
/// <see cref="MonsterInfo"/> by the prefab.
/// </summary>
[Title( "Doom Monster" )]
[Category( "Doom" )]
[Icon( "pest_control" )]
public sealed partial class DoomMonster : BaseNpc
{
	/// <summary>The monster's editor number. Monsters of one kind don't take each other on.</summary>
	[Property] public int ThingId { get; set; }

	/// <summary>World units per map unit.</summary>
	[Property] public float Scale { get; set; } = 1f;

	/// <summary>Collision radius in world units.</summary>
	[Property] public float Radius { get; set; } = 20f;

	/// <summary>Collision height in world units.</summary>
	[Property] public float Height { get; set; } = 56f;

	/// <summary>Out of 256: the chance a hit makes it flinch.</summary>
	[Property] public int PainChance { get; set; }

	/// <summary>Seconds between chase steps (the see state's tics). Retaliation lasts 100 of them (BASETHRESHOLD).</summary>
	[Property] public float StepTime { get; set; } = 3f / 35f;

	/// <summary>MTF_AMBUSH: waits for sight, ignoring noise.</summary>
	[Property] public bool Ambush { get; set; }

	[Property, Group( "Melee" )] public bool HasMelee { get; set; }

	/// <summary>Seconds into the melee animation the hit lands.</summary>
	[Property, Group( "Melee" )] public float MeleeHitTime { get; set; }

	/// <summary>Melee damage is (random % dice + 1) * multiplier.</summary>
	[Property, Group( "Melee" )] public int MeleeDice { get; set; }

	[Property, Group( "Melee" )] public int MeleeMultiplier { get; set; }

	[Property, Group( "Missile" )] public bool HasMissile { get; set; }

	/// <summary>Seconds into the missile animation at which it fires, once each.</summary>
	[Property, Group( "Missile" )] public List<float> MissileFireTimes { get; set; } = [];

	/// <summary>Where a refiring attack goes back to, in seconds into the animation, or -1 if it doesn't refire.</summary>
	[Property, Group( "Missile" )] public float MissileLoopTime { get; set; } = -1f;

	/// <summary>The missile it launches, or empty to fire bullets.</summary>
	[Property, Group( "Missile" )] public PrefabFile ProjectilePrefab { get; set; }

	/// <summary>Bullets per shot when it fires bullets.</summary>
	[Property, Group( "Missile" )] public int Pellets { get; set; } = 1;

	/// <summary>Claws instead of firing when the target is in melee range (A_TroopAttack).</summary>
	[Property, Group( "Missile" )] public bool MeleeIfClose { get; set; }

	/// <summary>
	/// For each firing in the missile animation, the two angles (degrees) its projectiles go off at
	/// from the line to the target, like the mancubus's volleys. Empty fires one projectile straight.
	/// </summary>
	[Property, Group( "Missile" )] public List<Vector2> MissileSpread { get; set; } = [];

	/// <summary>Out of 256: the chance a refiring attack keeps going though the target is out of sight.</summary>
	[Property, Group( "Missile" )] public int RefireChance { get; set; } = 40;

	/// <summary>World units above its feet its projectiles start.</summary>
	[Property, Group( "Missile" )] public float MissileHeight { get; set; } = 48f;

	/// <summary>A unique attack in place of bullets or projectiles: "vile" for the arch-vile's fire.</summary>
	[Property, Group( "Missile" )] public string SpecialAttack { get; set; }

	/// <summary>P_CheckMissileRange: no ranged attack closer than this many map units.</summary>
	[Property, Group( "Missile" )] public int MissileMinRange { get; set; }

	/// <summary>No ranged attack further than this many map units, or 0 for no limit.</summary>
	[Property, Group( "Missile" )] public int MissileMaxRange { get; set; }

	/// <summary>Counts the distance as half when deciding to fire.</summary>
	[Property, Group( "Missile" )] public bool HalveMissileRange { get; set; }

	/// <summary>The distance counted when deciding to fire is capped here, in map units.</summary>
	[Property, Group( "Missile" )] public int MissileRangeCap { get; set; } = 200;

	/// <summary>What it launches at its target, for the pain elemental: a lost soul.</summary>
	[Property, Group( "Missile" )] public PrefabFile SpawnedMonster { get; set; }

	[Property, Group( "Missile" )] public PrefabFile PuffPrefab { get; set; }
	[Property, Group( "Missile" )] public PrefabFile BloodPrefab { get; set; }

	[Property, Group( "Animation" )] public DoomSpriteAnimator Animator { get; set; }

	/// <summary>Whether it has an xdeath to play when overkilled.</summary>
	[Property, Group( "Animation" )] public bool HasGibDeath { get; set; }

	/// <summary>Its death ends in nothing: no corpse is left once the animation has played.</summary>
	[Property, Group( "Animation" )] public bool NoCorpse { get; set; }

	/// <summary>Letters in its walk (see) animation, for timing footsteps.</summary>
	[Property, Group( "Animation" )] public int SeeFrames { get; set; } = 1;

	/// <summary>Walk frames that make a footstep sound, matching <see cref="StepSounds"/>.</summary>
	[Property, Group( "Sounds" )] public List<int> StepFrames { get; set; } = [];

	[Property, Group( "Sounds" )] public List<SoundEvent> StepSounds { get; set; } = [];

	/// <summary>Takes no damage from explosions, like the Cyberdemon and Spider Mastermind.</summary>
	[Property] public bool SplashImmune { get; set; }

	[Property, Group( "Sounds" )] public List<SoundEvent> SeeSounds { get; set; } = [];
	[Property, Group( "Sounds" )] public SoundEvent ActiveSound { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent PainSound { get; set; }
	[Property, Group( "Sounds" )] public List<SoundEvent> DeathSounds { get; set; } = [];
	[Property, Group( "Sounds" )] public SoundEvent AttackSound { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent MeleeHitSound { get; set; }

	/// <summary>Played on each ranged shot, such as the zombieman's pistol.</summary>
	[Property, Group( "Sounds" )] public SoundEvent ShotSound { get; set; }

	/// <summary>Played as a ranged attack starts, such as the mancubus's roar.</summary>
	[Property, Group( "Sounds" )] public SoundEvent MissileStartSound { get; set; }

	/// <summary>What it's after. Kept until it dies or goes, so a monster that's woken chases for good. Host only.</summary>
	public GameObject Target { get; private set; }

	/// <summary>MELEERANGE: 64 map units.</summary>
	private float MeleeRange => 64f * Scale;

	// Set by a hit that made it flinch, cleared when the pain schedule starts.
	internal bool PainPending { get; set; }

	/// <summary>MISSILERANGE: how far bullets reach, 32 * 64 map units.</summary>
	private float MissileRange => 2048f * Scale;

	/// <summary>What a chase step decided to do instead of walking on.</summary>
	internal enum ChaseAction { Melee, Missile, Heal }

	/// <summary>The action the chase picked, for the attack task to carry out.</summary>
	internal ChaseAction PendingAction { get; set; }

	// Until this runs out it keeps its target rather than turning on whoever hits it next (threshold).
	private TimeUntil _threshold;

	// A_Chase's counters, in chase steps: no shooting until reactiontime runs down, and only when
	// movecount (steps left in the current direction) is at zero.
	private int _reactionTime = 8;
	private int _moveCount;

	// MF_JUSTATTACKED: the step after an attack only moves. MF_JUSTHIT: the next missile check after flinching fires back.
	private bool _justAttacked;
	private bool _justHit;
	private float _healthBeforeHit;
	private bool _gibbed;

	public override string Faction => Factions.Monster;

	protected override void SetupRelationships()
	{
		SetRelationship( Factions.Player, Disposition.Hostile );
	}

	protected override void OnStart()
	{
		base.OnStart();
		Animator ??= GetComponent<DoomSpriteAnimator>();
	}

	protected override void Think()
	{
		UpdateTarget();

		// A charging lost soul is out of its own hands until it hits something.
		if ( IsCharging ) return;

		base.Think();
	}

	protected override BaseSchedule SelectSchedule()
	{
		if ( Raising ) return GetSchedule<DoomRaiseSchedule>();
		if ( PainPending ) return GetSchedule<DoomPainSchedule>();
		if ( Target.IsValid() ) return GetSchedule<DoomChaseSchedule>();
		return GetSchedule<DoomLookSchedule>();
	}

	/// <summary>
	/// Drops a target that died or went, then looks for one: P_LookForPlayers by sight, or the source
	/// of a gunshot it heard unless it's waiting in ambush. Finding one from nothing wakes it.
	/// </summary>
	private void UpdateTarget()
	{
		if ( Target is not null && !IsLiveTarget( Target ) )
			Target = null;

		if ( Target.IsValid() ) return;

		var seen = LookForPlayers();
		if ( !seen.IsValid() && !Ambush && Senses.IsValid() && Senses.Disturbance is { Kind: StimulusKinds.Gunshot, Source: { } source }
			&& Combatant( source ) is { } shooter && IsLiveTarget( shooter ) && GetDisposition( shooter ) == Disposition.Hostile )
			seen = shooter;

		if ( seen.IsValid() )
			Wake( seen );
	}

	/// <summary>A_Look runs on every spawn state frame: 10 tics for most monsters.</summary>
	private const float LookInterval = 10f / 35f;

	private TimeUntil _nextLook;

	/// <summary>
	/// P_LookForPlayers: the first live hostile player it can see (<see cref="CanSee"/>) in the half
	/// in front of it, or anywhere around it within melee range. Nothing else wakes it by sight.
	/// </summary>
	private GameObject LookForPlayers()
	{
		if ( !_nextLook ) return null;
		_nextLook = LookInterval;

		foreach ( var player in Scene.GetAllComponents<PlayerController>() )
		{
			var go = player.GameObject;
			if ( !IsLiveTarget( go ) || GetDisposition( go ) != Disposition.Hostile ) continue;

			var delta = (go.WorldPosition - WorldPosition).WithZ( 0 );
			bool behind = Vector3.Dot( delta, WorldRotation.Forward.WithZ( 0 ) ) < 0f;
			if ( behind && delta.Length > MeleeRange ) continue;

			if ( CanSee( go ) ) return go;
		}

		return null;
	}

	private void Wake( GameObject target )
	{
		bool wasAwake = Target.IsValid();
		Target = target;

		if ( !wasAwake )
			PlaySound( Random( SeeSounds ) );
	}

	/// <summary>Turns to face a target at once, like A_FaceTarget.</summary>
	internal void FaceTarget()
	{
		if ( !Target.IsValid() ) return;

		var direction = (Target.WorldPosition - WorldPosition).WithZ( 0 );
		if ( !direction.IsNearlyZero() )
			WorldRotation = Rotation.LookAt( direction.Normal, Vector3.Up );
	}

	/// <summary>
	/// P_CheckMeleeRange: within MELEERANGE - 20 plus the target's radius, overlapping it in height,
	/// and in sight.
	/// </summary>
	internal bool CanMelee() => HasMelee && InMeleeRange();

	/// <summary>Whether the target is in reach of a claw or bite, melee state or not (the cacodemon bites from its missile state).</summary>
	internal bool InMeleeRange()
	{
		if ( !Target.IsValid() ) return false;

		var reach = MeleeRange - 20f * Scale + RadiusOf( Target );
		var delta = Target.WorldPosition - WorldPosition;
		if ( delta.WithZ( 0 ).Length >= reach ) return false;
		if ( MathF.Abs( delta.z ) > EyeHeight + MeleeRange ) return false;

		return CanSee( Target );
	}

	/// <summary>
	/// P_CheckSight: whether any of the target, from its feet to its eyes, can be seen from this
	/// monster's eyes. Only the level's walls block sight: not things, and not impassable lines
	/// such as windows, which stop movement but not sight.
	/// </summary>
	internal bool CanSee( GameObject target )
	{
		var feet = target.WorldPosition;
		var eyes = CalculateEyePosition( target );

		return SightClear( target, eyes ) || SightClear( target, Vector3.Lerp( feet, eyes, 0.5f ) ) || SightClear( target, feet + Vector3.Up * 4f );
	}

	private bool SightClear( GameObject target, Vector3 point )
	{
		var tr = Scene.Trace.Ray( EyePosition, point )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "npc", "corpse", "playerclip", "projectile", ThingTags.Thing )
			.Run();

		return !tr.Hit || IsPartOf( tr.GameObject, target );
	}

	/// <summary>
	/// Who's behind a GameObject in a fight: the NPC or player it belongs to, or its root. Monsters sit
	/// under the map, so a monster's root is the map, not the monster.
	/// </summary>
	internal static GameObject Combatant( GameObject go )
	{
		if ( !go.IsValid() ) return null;

		if ( go.GetComponentInParent<BaseNpc>() is { } npc ) return npc.GameObject;
		if ( go.GetComponentInParent<PlayerController>() is { } player ) return player.GameObject;
		return go.Root;
	}

	/// <summary>Whether <paramref name="go"/> is <paramref name="owner"/> or somewhere below it.</summary>
	private static bool IsPartOf( GameObject go, GameObject owner )
	{
		for ( var current = go; current.IsValid(); current = current.Parent )
		{
			if ( current == owner ) return true;
		}

		return false;
	}

	/// <summary>A_TroopAttack's claw and the like: hits the target if it's still in reach.</summary>
	internal void MeleeAttack()
	{
		FaceTarget();
		if ( !InMeleeRange() ) return;

		int damage = (Game.Random.Int( 0, 255 ) % MeleeDice + 1) * MeleeMultiplier;
		PlaySound( MeleeHitSound );
		Hurt( Target, damage, "melee" );
	}

	/// <summary>
	/// One step of A_Chase's decision making, run every <see cref="StepTime"/>: an arch-vile first
	/// looks for a corpse to raise (A_VileChase); then count down reaction time, skip the step after
	/// an attack, and when its current direction runs out, maybe fire (P_CheckMissileRange).
	/// Null to keep walking.
	/// </summary>
	internal ChaseAction? ChaseStep()
	{
		if ( FindCorpseToRaise() is { } corpse )
		{
			HealTarget = corpse;
			return ChaseAction.Heal;
		}

		if ( _reactionTime > 0 ) _reactionTime--;

		// MF_JUSTATTACKED: this step only picks a new direction (P_NewChaseDir), with a fresh movecount.
		if ( _justAttacked )
		{
			_justAttacked = false;
			_moveCount = Game.Random.Int( 0, 255 ) & 15;
			return null;
		}

		if ( HasMissile && _moveCount == 0 && CheckMissileRange() )
		{
			_justAttacked = true;
			return ChaseAction.Missile;
		}

		// P_NewChaseDir picks a new heading for up to 15 more steps.
		if ( --_moveCount < 0 )
			_moveCount = Game.Random.Int( 0, 255 ) & 15;

		return null;
	}

	/// <summary>
	/// P_CheckMissileRange: in sight, and then likelier the closer it is. Always after flinching,
	/// never before reaction time runs out. Monsters without a melee attack are keener. Some have
	/// their own limits: the arch-vile won't attack from too far, the revenant won't fire too close,
	/// and the revenant, bosses and lost soul count the distance as half.
	/// </summary>
	private bool CheckMissileRange()
	{
		if ( !Target.IsValid() || !CanSee( Target ) ) return false;

		if ( _justHit )
		{
			_justHit = false;
			return true;
		}

		if ( _reactionTime > 0 ) return false;

		float distance = (Target.WorldPosition - WorldPosition).WithZ( 0 ).Length / Scale - 64f;
		if ( !HasMelee ) distance -= 128f;

		if ( MissileMaxRange > 0 && distance > MissileMaxRange ) return false;
		if ( MissileMinRange > 0 && distance < MissileMinRange ) return false;
		if ( HalveMissileRange ) distance *= 0.5f;

		distance = MathF.Min( distance, MissileRangeCap );

		return Game.Random.Int( 0, 255 ) >= distance;
	}

	/// <summary>
	/// The missile state's action, the <paramref name="action"/>th in its animation: claws if close
	/// and able to (A_TroopAttack), otherwise launches its projectiles or fires its bullets, or runs
	/// its special attack.
	/// </summary>
	internal void MissileAttack( int action )
	{
		FaceTarget();
		if ( !Target.IsValid() ) return;

		switch ( SpecialAttack )
		{
			case "vile":
				VileAction( action );
				return;

			case "skull":
				StartCharge();
				return;

			case "pain":
				LaunchSoul( WorldRotation.Yaw() );
				return;
		}

		if ( MeleeIfClose && InMeleeRange() )
		{
			MeleeAttack();
			return;
		}

		PlaySound( ShotSound );

		if ( !ProjectilePrefab.IsValid() )
		{
			FireBullets();
		}
		else if ( action < MissileSpread.Count )
		{
			LaunchProjectile( MissileSpread[action].x );
			LaunchProjectile( MissileSpread[action].y );
		}
		else
		{
			LaunchProjectile( 0f );
		}
	}

	/// <summary>Plays the sound a ranged attack opens with, if it has one.</summary>
	internal void PlayMissileStartSound() => PlaySound( MissileStartSound );

	/// <summary>
	/// A_CPosRefire and A_SpidRefire: keeps firing while the target is alive and in sight, or on a
	/// <see cref="RefireChance"/> in 256 regardless.
	/// </summary>
	internal bool ShouldRefire()
		=> Target.IsValid() && (Game.Random.Int( 0, 255 ) < RefireChance || CanSee( Target ));

	/// <summary>Where shots go: the middle of the target's body.</summary>
	private Vector3 AimPoint( GameObject target ) => Vector3.Lerp( target.WorldPosition, CalculateEyePosition( target ), 0.5f );

	/// <summary>
	/// P_SpawnMissile: from <see cref="MissileHeight"/> up, at the target, turned
	/// <paramref name="angle"/> degrees off that line. A homing missile is given the target to follow.
	/// Projectiles from monsters of one kind pass through each other harmlessly.
	/// </summary>
	private void LaunchProjectile( float angle )
	{
		if ( !ProjectilePrefab.IsValid() ) return;

		var go = GameObject.Clone( ProjectilePrefab, global::Transform.Zero, GameObject.Parent, false );
		if ( !go.IsValid() ) return;

		var start = WorldPosition + Vector3.Up * MissileHeight;
		go.WorldPosition = start;

		// It's still disabled, so look past that.
		var projectile = go.GetComponent<DoomProjectile>( true );
		if ( projectile.IsValid() )
		{
			// Aim the missile's middle at the target's middle.
			var aim = AimPoint( Target ) - Vector3.Up * projectile.Height * 0.5f;
			var direction = Rotation.FromAxis( Vector3.Up, angle ) * (aim - start);
			projectile.Launch( GameObject, direction, Target );
			projectile.PassThrough = [.. MonstersAround( start + Vector3.Up * projectile.Height * 0.5f )];
		}

		go.Enabled = true;
		go.NetworkSpawn( true, null );
	}

	/// <summary>
	/// A_PosAttack and A_SPosAttack: each bullet does (random % 5 + 1) * 3, aimed at the target but
	/// thrown off up to 22.5 degrees either side (random - random), from half its height plus 8.
	/// </summary>
	private void FireBullets()
	{
		var start = WorldPosition + Vector3.Up * (Height * 0.5f + 8f * Scale);
		var aim = (AimPoint( Target ) - start).Normal;
		var crowding = MonstersAround( start ).ToArray();

		for ( int i = 0; i < Pellets; i++ )
		{
			float spread = (Game.Random.Int( 0, 255 ) - Game.Random.Int( 0, 255 )) * 360f / 4096f;
			var direction = Rotation.FromAxis( Vector3.Up, spread ) * aim;

			// Like the other mounts' NPCs: bullet collision rules, players hit on their model's hitboxes
			// rather than their movement capsule. Bullets pass through decorations: only shootable things
			// stop them (PTR_ShootTraverse).
			var trace = Scene.Trace.Ray( start, start + direction * MissileRange )
				.IgnoreGameObjectHierarchy( GameObject )
				.WithCollisionRules( "bullet" )
				.WithoutTags( "playercontroller", "playerclip", "projectile", "corpse", ThingTags.Decoration )
				.UseHitboxes();

			foreach ( var neighbour in crowding )
				trace = trace.IgnoreGameObjectHierarchy( neighbour );

			var tr = trace.Run();
			if ( !tr.Hit || tr.GameObject?.Tags.Has( "sky" ) == true ) continue;

			var hit = tr.GameObject;
			var position = tr.HitPosition;

			bool bleeds = hit.IsValid() && Bleeds( hit );

			if ( hit.IsValid() )
				Hurt( hit, (Game.Random.Int( 0, 255 ) % 5 + 1) * 3, "bullet", position );

			SpawnHitEffect( position - direction * 4f, bleeds );
		}
	}

	/// <summary>
	/// Other monsters whose hull contains a point. Doom's monsters are solid and never overlap, so a
	/// shot never starts inside one; here agents can crowd into each other, and a shot fired from
	/// inside a neighbour must pass out of it rather than hit it point blank.
	/// </summary>
	internal IEnumerable<GameObject> MonstersAround( Vector3 point )
	{
		foreach ( var other in Scene.GetAllComponents<DoomMonster>() )
		{
			if ( other == this ) continue;

			var local = point - other.WorldPosition;
			if ( local.WithZ( 0 ).Length <= other.Radius && local.z >= 0f && local.z <= other.Height )
				yield return other.GameObject;
		}
	}

	/// <summary>Monsters and players bleed where bullets hit; everything else puffs.</summary>
	private static bool Bleeds( GameObject go )
		=> go.GetComponentInParent<BaseNpc>().IsValid() || go.GetComponentInParent<PlayerController>().IsValid();

	/// <summary>A bullet puff or blood where a shot hit, spawned by every client for itself.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	private void SpawnHitEffect( Vector3 position, bool blood )
	{
		var prefab = blood ? BloodPrefab : PuffPrefab;
		if ( !prefab.IsValid() ) return;

		var go = GameObject.Clone( prefab, new Transform( position ), null, true );
		go.NetworkMode = NetworkMode.Never;
	}

	/// <summary>Missiles from a monster don't hurt its own kind; barons and hell knights count as one.</summary>
	public static bool SameSpecies( int a, int b )
		=> a != 0 && (a == b || (a is 69 or 3003 && b is 69 or 3003));

	/// <summary>
	/// Damages whatever was hit: the damageable it belongs to, or for a combatant (a melee target),
	/// the damageable anywhere on it. Never anything else under the same map.
	/// </summary>
	private void Hurt( GameObject hit, float damage, string tag, Vector3? position = null )
	{
		var damageable = hit.GetComponentInParent<Component.IDamageable>();
		if ( damageable is null && ((Component)hit.GetComponentInParent<BaseNpc>() ?? hit.GetComponentInParent<PlayerController>()) is { } owner )
			damageable = owner.GameObject.GetComponentInChildren<Component.IDamageable>();
		if ( damageable is null ) return;

		var info = new DamageInfo
		{
			Damage = damage,
			Attacker = GameObject,
			Position = position ?? hit.WorldPosition,
		};
		info.Tags.Add( tag );

		damageable.OnDamage( info );
	}

	private float RadiusOf( GameObject target )
		=> target.GetComponent<DoomMonster>() is { } monster ? monster.Radius : 16f * Scale;

	protected override void OnDamaging( Component.IDamageable.DamageEvent e )
	{
		_healthBeforeHit = Health;

		// PIT_RadiusAttack: the bosses shrug off explosions.
		if ( SplashImmune && e.DamageInfo?.Tags.Has( "explosion" ) == true )
			e.Cancelled = true;
	}

	/// <summary>The arch-vile: never fought back against, and itself fights back against anything.</summary>
	private bool IsVile => SpecialAttack == "vile";

	/// <summary>
	/// P_DamageMobj: turn on the attacker unless still set on its target (threshold), and maybe flinch.
	/// Nobody turns on an arch-vile, and an arch-vile turns on anyone regardless.
	/// </summary>
	protected override void OnDamaged( Component.IDamageable.DamageEvent e )
	{
		var attacker = Combatant( e.Instigator );
		bool fromVile = attacker.IsValid() && attacker.GetComponent<DoomMonster>() is { IsVile: true };
		bool free = !Target.IsValid() || _threshold || IsVile;
		if ( attacker.IsValid() && attacker != GameObject && !fromVile && IsLiveTarget( attacker ) && free )
		{
			Wake( attacker );
			_threshold = 100 * StepTime;
		}

		// A lost soul in flight doesn't flinch (MF_SKULLFLY).
		if ( Health >= 1f && !IsCharging && Game.Random.Int( 0, 255 ) < PainChance )
		{
			PainPending = true;
			_justHit = true;
			EndCurrentSchedule();
		}
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent e )
	{
		// P_KillMobj: overkill by more than the spawn health gibs it, if it has a gib death.
		float overkill = (e.DamageInfo?.Damage ?? 0f) - _healthBeforeHit;
		_gibbed = HasGibDeath && overkill > MaxHealth;

		// The monster is gone once this returns, so the sound stays where it died.
		PlaySoundAt( Random( DeathSounds ), WorldPosition );

		// A_PainDie: three lost souls burst out, to the sides and behind.
		if ( SpecialAttack == "pain" )
		{
			float yaw = WorldRotation.Yaw();
			LaunchSoul( yaw + 90f );
			LaunchSoul( yaw + 180f );
			LaunchSoul( yaw + 270f );
		}

		base.OnDeath( e );
		BossDeath();
	}

	/// <summary>Leaves the death animation behind as its own sprite, holding on the corpse frame. Corpses don't block.</summary>
	protected override GameObject CreateRagdoll()
	{
		if ( !Networking.IsHost || !Animator.IsValid() || !Animator.Renderer.IsValid() )
			return null;

		var source = Animator.Renderer;
		var go = new GameObject( false, $"{DisplayName} (dead)" );
		go.Parent = GameObject.Parent;
		go.WorldTransform = WorldTransform;
		go.Tags.Add( "corpse" );
		go.NetworkMode = NetworkMode.Object;

		var renderer = go.AddComponent<SpriteRenderer>();
		renderer.Sprite = source.Sprite;
		renderer.Size = source.Size;
		renderer.TextureFilter = source.TextureFilter;
		renderer.Billboard = source.Billboard;
		renderer.Opaque = source.Opaque;
		renderer.IsSorted = source.IsSorted;
		renderer.Shadows = source.Shadows;
		renderer.Lighting = source.Lighting;
		renderer.Color = source.Color;

		var animator = go.AddComponent<DoomSpriteAnimator>();
		animator.Renderer = renderer;
		var death = _gibbed ? MonsterAnimations.GibDeath : MonsterAnimations.Death;
		animator.Play( death );

		// Some deaths end in nothing: the lost soul burns away, the pain elemental bursts.
		if ( NoCorpse )
		{
			go.AddComponent<DoomEffect>().Lifetime = Animator.Duration( death );
			go.Enabled = true;
			go.NetworkSpawn( true, null );
			return go;
		}

		// It falls and settles on the level, carrying on the way it was going. No collider: nothing
		// bumps into a corpse.
		var corpse = go.AddComponent<DoomCorpse>();
		corpse.Radius = Radius;
		corpse.Scale = Scale;
		corpse.DeathTime = Animator.Duration( death );
		corpse.Velocity = Animation.IsValid() ? Animation.MoveVelocity : Vector3.Zero;

		// An arch-vile can bring it back, unless it was gibbed.
		if ( Raisable && !_gibbed )
			corpse.MonsterPrefab = GameObject.PrefabInstanceSource;

		go.Enabled = true;
		go.NetworkSpawn( true, null );
		return go;
	}

	internal void PlayActiveSound() => PlaySound( ActiveSound );
	internal void PlayPainSound() => PlaySound( PainSound );
	internal void PlayAttackSound() => PlaySound( AttackSound );

	/// <summary>Plays a sound from the monster on every client, following it as it moves.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void PlaySound( SoundEvent sound )
	{
		if ( sound is null ) return;

		var handle = GameObject.PlaySound( sound, 0 );
		if ( handle.IsValid() )
			handle.FollowParent = true;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void PlaySoundAt( SoundEvent sound, Vector3 position )
	{
		if ( sound is not null )
			Sound.Play( sound, position );
	}

	private static SoundEvent Random( List<SoundEvent> sounds )
		=> sounds is { Count: > 0 } ? sounds[Game.Random.Int( 0, sounds.Count - 1 )] : null;
}
