namespace Source1.Hl2.Prefabs.Npcs;

[Library]
public static class Zombies
{
	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> All()
	{
		yield return new( "hl2", EntityKind.Npc, "fast_headcrab", "Fast Headcrab", "models/headcrab.mdl", "Idle01" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( height: 20, radius: 12.5f );
				p.Skill( "sk_headcrab_fast_health", damage: "sk_headcrab_melee_dmg" );

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
				npc.AttackInterval = 0.8f;
				npc.AttackDelay = 0.2f;
				npc.Voice = "NPC_FastZombie";
				npc.EyeHeight = 57.6f;
				var leap = p.Root.AddComponent<Leap>();
				leap.SoundPrefix = "NPC_FastZombie";
				leap.LeapSound = "NPC_FastZombie.LeapAttack";
				var zombie = p.Root.AddComponent<Zombie>();
				zombie.Headcrab = "fast_headcrab";
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
				npc.AttackRange = 70;
				npc.AttackInterval = 1.5f;
				npc.AttackDelay = 0.8f;
				npc.Voice = "NPC_PoisonZombie";
				npc.EyeHeight = 56;
				var zombie = p.Root.AddComponent<Zombie>();
				zombie.Headcrab = "poison_headcrab";
				zombie.ThrowsCrabs = true;
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
				npc.AttackInterval = 1.9f;
				npc.AttackDelay = 0.55f;
				npc.Voice = "Zombie";
				npc.EyeHeight = 57.6f;
				var zombie = p.Root.AddComponent<Zombie>();
				zombie.Headcrab = "headcrab";
				zombie.Torso = "zombie_torso";
				zombie.LegsModel = "models/zombie/classic_legs.mdl";
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
				npc.Voice = "Zombie";
				npc.EyeHeight = 15;
				var zombie = p.Root.AddComponent<Zombie>();
				zombie.Headcrab = "headcrab";
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
				npc.AttackRange = 75;
				npc.AttackDamage = 10;
				npc.Voice = "Zombie";
				npc.EyeHeight = 57.6f;
				p.Root.AddComponent<ZombineGrenade>();
				var zombie = p.Root.AddComponent<Zombie>();
				zombie.Headcrab = "headcrab";
				p.Renderer.BodyGroups = 5;
				p.Box( 26, 72 );
			}
		};
	}
}
