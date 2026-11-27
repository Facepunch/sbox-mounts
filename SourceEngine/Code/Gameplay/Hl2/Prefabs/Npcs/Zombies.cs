namespace Source1.Hl2.Prefabs.Npcs;

[Library]
public static class Zombies
{
	/// <summary>
	/// The zombie claw: reach between body centres and the swing box, the turn rates before and through a swing, no pause
	/// between swings, and what a blow does to a player.
	/// </summary>
	static void Claws( CreatureNpc npc, float reach, float box, float faceTurnRate = 250, float swingTurnRate = 1200 )
	{
		npc.AttackRange = reach;
		npc.AttackInterval = 0;
		npc.SwingCone = 0.7f;
		npc.SwingBox = box;
		npc.FaceTurnRate = faceTurnRate;
		npc.SwingTurnRate = swingTurnRate;
		npc.SwingKick = new Angles( -15, -20, -10 );
		npc.SwingShove = new Vector2( 200, 100 );
		npc.SwingHitSound = "Zombie.AttackHit";
		npc.SwingMissSound = "Zombie.AttackMiss";
	}

	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> All()
	{
		yield return new( "hl2", EntityKind.Npc, "fast_headcrab", "Fast Headcrab", "models/headcrab.mdl", "Idle01" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( height: 20, radius: 12.5f );
				// The fast crab's spawn reads the classic crab's health setting
				p.Skill( "sk_headcrab_health", damage: "sk_headcrab_melee_dmg" );

				npc.Idle = "Idle01";
				npc.Move = "Run1";
				npc.MoveSpeed = 210;
				npc.AttackDamage = 10;
				npc.AttackHeight = 15;
				npc.AttackDelay = 0.1f;
				npc.AttackInterval = 0.7f;
				npc.Voice = "NPC_FastHeadcrab";
				npc.EyeHeight = 15;
				var leap = p.Root.AddComponent<Leap>();
				leap.Kind = Leap.Leaper.FastHeadcrab;
				leap.SoundPrefix = "NPC_FastHeadcrab";
				p.Box( 25, 20 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "fast_zombie", "Fast Zombie", "models/zombie/fast.mdl", "idle" )
		{
			RequiredPrefabs = ["entities/npcs/fast_headcrab.prefab"],
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation();
				p.Skill( "sk_zombie_health" );

				npc.Move = "Run";
				npc.Attack = "Melee";
				npc.MoveSpeed = 220;
				npc.AttackDamage = 3;
				npc.AttackDelay = 0.2f;
				Claws( npc, reach: 50, box: 26, swingTurnRate: 200 );
				npc.SwingKick = new Angles( -3, -5, -3 );
				npc.SwingShove = new Vector2( 0, -50 );
				npc.SwingHitSound = "NPC_FastZombie.AttackHit";
				npc.SwingMissSound = "NPC_FastZombie.AttackMiss";
				npc.Flinches = CreatureNpc.FlinchRules.HitGroup;
				npc.Voice = "NPC_FastZombie";
				npc.EyeHeight = 57.6f;
				var leap = p.Root.AddComponent<Leap>();
				leap.Kind = Leap.Leaper.FastZombie;
				leap.SoundPrefix = "NPC_FastZombie";
				var zombie = p.Root.AddComponent<Zombie>();
				zombie.Headcrab = "fast_headcrab";
				zombie.LegsModel = "models/gibs/fast_zombie_legs.mdl";
				zombie.TorsoModel = "models/gibs/fast_zombie_torso.mdl";
				zombie.GibsFromBlasts = true;
				p.Renderer.BodyGroups = 5;
				p.Box( 26, 72 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "headcrab", "Headcrab", "models/headcrabclassic.mdl", "Idle01" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( height: 16, radius: 12 );
				p.Skill( "sk_headcrab_health", damage: "sk_headcrab_melee_dmg" );

				npc.Idle = "Idle01";
				npc.Move = "Run1";
				npc.Attack = "jumpattack_broadcast";
				npc.MoveSpeed = 110;
				npc.AttackDamage = 10;
				npc.AttackHeight = 12;
				npc.AttackDelay = 0.2f;
				npc.AttackRange = 60;
				npc.Voice = "NPC_Headcrab";
				npc.EyeHeight = 12;
				var leap = p.Root.AddComponent<Leap>();
				leap.SoundPrefix = "NPC_Headcrab";
				p.Box( 24, 16 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "poison_headcrab", "Poison Headcrab", "models/headcrabblack.mdl", "Idle01" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( height: 22, radius: 14 );
				p.Skill( "sk_headcrab_poison_health", damage: "sk_headcrab_melee_dmg" );

				npc.Idle = "Idle01";
				npc.Move = "Run1";
				npc.Attack = "Spitattack";
				npc.MoveSpeed = 100;
				npc.AttackDamage = 30;
				npc.AttackHeight = 16;
				npc.AttackInterval = 2;
				npc.Voice = "NPC_BlackHeadcrab";
				npc.EyeHeight = 16;
				var leap = p.Root.AddComponent<Leap>();
				leap.Kind = Leap.Leaper.PoisonHeadcrab;
				leap.SoundPrefix = "NPC_BlackHeadcrab";
				p.Box( 28, 22 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "poison_zombie", "Poison Zombie", "models/zombie/poison.mdl", "Idle01" )
		{
			RequiredPrefabs = ["entities/npcs/poison_headcrab.prefab"],
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( height: 70, radius: 21 );
				p.Skill( "sk_zombie_poison_health", damage: "sk_zombie_dmg_one_slash" );

				npc.Idle = "Idle01";
				npc.Move = "Walk";
				npc.Attack = "Throw";
				npc.MoveSpeed = 65;
				npc.AttackDamage = 25;
				npc.AttackDelay = 0.8f;
				Claws( npc, reach: 70, box: 26 );
				npc.Flinches = CreatureNpc.FlinchRules.HitGroup;
				npc.Voice = "NPC_PoisonZombie";
				npc.EyeHeight = 56;
				var zombie = p.Root.AddComponent<Zombie>();
				zombie.Headcrab = "poison_headcrab";
				zombie.ThrowsCrabs = true;
				var props = p.Root.AddComponent<ZombieProps>();
				props.AttackSound = "NPC_PoisonZombie.Attack";
				p.Renderer.BodyGroups = 341;
				p.Box( 42, 70 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "zombie", "Zombie", "models/zombie/classic.mdl", "Idle01" )
		{
			RequiredPrefabs = ["entities/npcs/headcrab.prefab", "entities/npcs/zombie_torso.prefab"],
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation();
				p.Skill( "sk_zombie_health", damage: "sk_zombie_dmg_one_slash" );

				npc.Idle = "Idle01";
				npc.Attack = "attackA";
				npc.MoveSpeed = 55;
				npc.AttackDelay = 0.55f;
				Claws( npc, reach: 55, box: 26 );
				npc.Flinches = CreatureNpc.FlinchRules.Zombie;
				npc.Voice = "Zombie";
				npc.EyeHeight = 57.6f;
				var zombie = p.Root.AddComponent<Zombie>();
				zombie.Headcrab = "headcrab";
				zombie.Torso = "zombie_torso";
				zombie.LegsModel = "models/zombie/classic_legs.mdl";
				zombie.TorsoModel = "models/zombie/classic_torso.mdl";
				zombie.GibsFromBlasts = true;
				var props = p.Root.AddComponent<ZombieProps>();
				props.AttackSound = "Zombie.Attack";
				p.Renderer.BodyGroups = 5;
				p.Box( 26, 72 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "zombie_torso", "Zombie Torso", "models/zombie/classic_torso.mdl", "Idle01" )
		{
			RequiredPrefabs = ["entities/npcs/headcrab.prefab"],
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( height: 24 );
				p.Skill( "sk_zombie_health", damage: "sk_zombie_dmg_one_slash" );

				npc.Idle = "Idle01";
				npc.Move = "crawl";
				npc.MoveSpeed = 35;
				npc.AttackDamage = 10;
				npc.AttackHeight = 15;
				Claws( npc, reach: 55, box: 24, faceTurnRate: 600, swingTurnRate: 600 );
				npc.Flinches = CreatureNpc.FlinchRules.None;
				npc.Voice = "Zombie";
				npc.EyeHeight = 15;
				var zombie = p.Root.AddComponent<Zombie>();
				zombie.Headcrab = "headcrab";
				zombie.IsTorso = true;
				p.Renderer.BodyGroups = 5;
				p.Box( 26, 24 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "zombine", "Zombine", "models/zombie/zombie_soldier.mdl", "Idle01" )
		{
			Description = "Half-Life 2: Episode One",
			RequiredPrefabs = ["entities/npcs/headcrab.prefab"],
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation();
				p.Skill( "sk_zombie_soldier_health", damage: "sk_zombie_dmg_one_slash", config: "skill_episodic.cfg" );

				npc.Idle = "Idle01";
				npc.Move = "walk_All";
				npc.Attack = "AttackA";
				npc.MoveSpeed = 80;
				npc.AttackDamage = 10;
				Claws( npc, reach: 55, box: 26 );
				npc.Flinches = CreatureNpc.FlinchRules.HitGroup;
				npc.Voice = "Zombine";
				npc.EyeHeight = 57.6f;
				p.Root.AddComponent<ZombineGrenade>();
				var zombie = p.Root.AddComponent<Zombie>();
				zombie.Headcrab = "headcrab";
				zombie.LegsModel = "models/zombie/zombie_soldier_legs.mdl";
				zombie.TorsoModel = "models/zombie/zombie_soldier_torso.mdl";
				var props = p.Root.AddComponent<ZombieProps>();
				props.EnemyMustSeeProp = true;
				p.Renderer.BodyGroups = 5;
				p.Box( 26, 72 );
			}
		};
	}
}
