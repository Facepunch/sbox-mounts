namespace Source1.Hl2.Prefabs.Npcs;

[Library]
public static class Creatures
{
	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> All()
	{
		yield return new( "hl2", EntityKind.Npc, "antlion", "Antlion", "models/antlion.mdl", "Idle" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( height: 55, radius: 25 );
				p.Skill( "sk_antlion_health", damage: "sk_antlion_swipe_damage" );

				npc.Idle = "Idle";
				npc.Move = "run_all";
				npc.Attack = "attack1";
				npc.MoveSpeed = 180;
				npc.AttackDamage = 20;
				npc.RespondsToBugbait = true;
				npc.AttackHeight = 35;
				npc.Voice = "NPC_Antlion";
				npc.EyeHeight = 24;
				p.Root.AddComponent<Unburrow>();
				p.Root.AddComponent<AntlionJump>();
				p.Box( 50, 55 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "antlion_guard", "Antlion Guard", "models/antlion_guard.mdl", "idle" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( height: 130, radius: 45 );
				p.Skill( "sk_antlionguard_health", playerDamage: "sk_antlionguard_dmg_shove" );

				npc.Health = 500;
				npc.Move = "run1";
				npc.Attack = "shove";
				npc.MoveSpeed = 220;
				npc.AttackDamage = 250;
				npc.AttackRange = 140;
				// The shove reaches 156 with the 40 a victim's body adds
				npc.MeleeRange = 116;
				npc.AttackHeight = 70;
				npc.Voice = "NPC_AntlionGuard";
				npc.EyeHeight = 70;
				var unburrow = p.Root.AddComponent<Unburrow>();
				unburrow.Activity = "ACT_ANTLIONGUARD_UNBURROW";
				unburrow.Sound = "NPC_AntlionGuard.Roar";
				p.Root.AddComponent<AntlionGuard>();
				p.Root.AddComponent<Charge>();
				p.BoxAt( new( 0, 0, 65 ), new( 90, 80, 130 ) );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "antlion_worker", "Antlion Worker", "models/antlion_worker.mdl", "Idle" )
		{
			Description = "Half-Life 2: Episode Two",
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( height: 45, radius: 25 );
				p.Skill( "sk_antlion_worker_health", config: "skill_episodic.cfg" );

				npc.Health = 60;
				npc.Idle = "Idle";
				npc.Move = "run_all";
				npc.Attack = "spit";
				npc.MoveSpeed = 140;
				npc.AttackRange = 900;
				npc.AttackDamage = 5;
				npc.AttackInterval = 2;
				npc.RespondsToBugbait = true;
				npc.Voice = "NPC_Antlion";
				npc.EyeHeight = 24;
				var npcProjectileAttack = p.Root.AddComponent<NpcProjectileAttack>();
				npcProjectileAttack.FireSound = "NPC_Antlion.PoisonShoot";
				p.Root.AddComponent<Unburrow>();
				p.Box( 50, 45 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "barnacle", "Barnacle", "models/barnacle.mdl", "idle01" )
		{
			Build = p =>
			{
				var npc = p.Npc<BarnacleNpc>();
				p.Skill( "sk_barnacle_health" );

				npc.Health = 35;
				npc.EyeHeight = 24;
				p.Box( 40, 30 );
			}
		};

		yield return FlyingBird( "crow", "Crow", "Fly01" );

		yield return new( "hl2", EntityKind.Npc, "dog", "Dog", "models/dog.mdl", "idle01" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( height: 105, radius: 30 );
				p.Skill( "sk_dog_health" );

				npc.Friendly = true;
				npc.Health = 500;
				npc.Idle = "idle01";
				npc.Move = "run_all";
				npc.Attack = "pound";
				npc.AttackDamage = 80;
				npc.MoveSpeed = 220;
				npc.AttackRange = 130;
				npc.AttackHeight = 65;
				npc.Voice = "NPC_Dog";
				npc.EyeHeight = 65;
				p.Box( 60, 105 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "ichthyosaur", "Ichthyosaur", "models/ichthyosaur.mdl", "idle" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Skill( "sk_ichthyosaur_health", damage: "sk_ichthyosaur_melee_dmg" );

				npc.Health = 200;
				npc.Move = "swim";
				npc.Attack = "attackstart";
				npc.Locomotion = CreatureNpc.Locomotions.Flying;
				npc.FlightHeight = 0;
				npc.MoveSpeed = 180;
				npc.AttackRange = 140;
				npc.AttackDamage = 40;
				npc.Aquatic = true;
				npc.Voice = "NPC_Ichthyosaur";
				npc.EyeHeight = 48;
				p.BoxAt( Vector3.Zero, new( 160, 60, 60 ) );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "leech", "Leech", "models/leech.mdl", "loop1" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Skill( "sk_leech_health" );

				npc.Health = 5;
				npc.Idle = "loop1";
				npc.Move = "loop2";
				npc.Attack = "attackloop1";
				npc.Locomotion = CreatureNpc.Locomotions.Flying;
				npc.FlightHeight = 0;
				npc.AttackHeight = 0;
				npc.MoveSpeed = 70;
				npc.AttackRange = 35;
				npc.AttackDamage = 2;
				npc.Aquatic = true;
				npc.EyeHeight = 0;
				p.BoxAt( Vector3.Zero, new( 12, 12, 8 ) );
			}
		};

		yield return FlyingBird( "pigeon", "Pigeon", "Fly01" );

		yield return FlyingBird( "seagull", "Seagull", "Fly" );

		yield return new( "hl2", EntityKind.Npc, "vortigaunt", "Vortigaunt", "models/vortigaunt.mdl", "Idle01" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation();
				p.Skill( "sk_vortigaunt_health", damage: "sk_vortigaunt_dmg_zap" );

				npc.Friendly = true;
				npc.Idle = "Idle01";
				npc.Move = "Walk_all";
				npc.Attack = "zapattack1";
				npc.AttackRange = 900;
				npc.AttackDamage = 40;
				npc.AttackInterval = 2;
				npc.AttackDelay = 0.7f;
				npc.AttackHeight = 50;
				npc.Voice = "NPC_Vortigaunt";
				npc.EyeHeight = 50;
				var npcRangedEffects = p.Root.AddComponent<NpcRangedEffects>();
				npcRangedEffects.MuzzleAttachment = "leftclaw";
				npcRangedEffects.FireSound = "NPC_Vortigaunt.Shoot";
				npcRangedEffects.MuzzleParticle = "vortigaunt_hand_glow";
				npcRangedEffects.Beam = true;
				npcRangedEffects.TracerColor = new( 0.7f, 1, 0.4f, 1 );
				npcRangedEffects.BeamSprite = "sprites/lgtning";
				npcRangedEffects.BeamWidth = 6;
				npcRangedEffects.BeamNoise = 16;
				npcRangedEffects.DamageTag = "shock";
				npcRangedEffects.ChargeParticle = "vortigaunt_hand_glow";
				npcRangedEffects.ChargeAttachments = "leftclaw,rightclaw";
				npcRangedEffects.ChargeSound = "NPC_Vortigaunt.ZapPowerup";
				p.Box( 26, 72 );
			}
		};
	}

	static EntityDefinition FlyingBird( string name, string title, string fly ) =>
		new( "hl2", EntityKind.Npc, name, title, $"models/{name}.mdl", "Idle01" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Skill( $"sk_{name}_health" );

				npc.Health = 5;
				npc.Idle = "Idle01";
				npc.Move = fly;
				npc.Attack = "Idle01";
				npc.Passive = true;
				npc.Locomotion = CreatureNpc.Locomotions.Flying;
				npc.Voice = $"NPC_{title}";
				npc.EyeHeight = 14.4f;
				p.Root.AddComponent<Bird>();
				p.BoxAt( Vector3.Zero, new( 15, 15, 18 ) );
			}
		};
}
