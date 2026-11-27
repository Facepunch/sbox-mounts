using Sandbox.Npcs.Layers;

namespace Source1.Hl2.Prefabs.Npcs;

[Library]
public static class Combine
{
	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> All()
	{
		yield return new( "hl2", EntityKind.Npc, "combine_elite", "Combine Elite", "models/combine_super_soldier.mdl", "Idle1" )
		{
			RequiredPrefabs = ["entities/weapons/ar2.prefab"],
			Build = p =>
			{
				var npc = p.Npc<CombatNpc>();
				p.Navigation( run: 180, walk: 85 );
				p.Skill( "sk_combine_guard_health" );

				npc.DistractedByBugbait = true;
				npc.StepsIn = true;
				npc.Kicks = true;
				npc.PatrolRadius = 2048;
				npc.SearchTimeout = 20;
				npc.Elite = true;
				npc.Grenades = 5;
				npc.EyeHeight = 57.6f;
				p.Root.AddComponent<NpcAnimator>();
				p.Box( 26, 72 );
				npc.Weapon = p.Equip( "ar2" );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "combine_soldier", "Combine Soldier", "models/combine_soldier.mdl", "Idle1" )
		{
			RequiredPrefabs = ["entities/weapons/smg.prefab"],
			Build = p =>
			{
				var npc = p.Npc<CombatNpc>();
				p.Navigation( acceleration: 150, height: 64, radius: 16, run: 180, walk: 85 );
				var sensesLayer = p.Root.GetComponent<SensesLayer>( true );
				sensesLayer.FieldOfView = 200;
				sensesLayer.SightRange = 8192;
				p.Skill( "sk_combine_s_health" );

				npc.DistractedByBugbait = true;
				npc.StepsIn = true;
				npc.Kicks = true;
				npc.Health = 50;
				npc.PatrolRadius = 2048;
				npc.SearchTimeout = 20;
				npc.Grenades = 5;
				p.Root.AddComponent<NpcAnimator>();
				p.Box( 25, 75 );
				npc.Weapon = p.Equip( "smg" );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "metrocop", "Metrocop", "models/police.mdl", "pistolidle1" )
		{
			RequiredPrefabs = ["entities/weapons/pistol.prefab"],
			Build = p =>
			{
				var npc = p.Npc<CombatNpc>();
				p.Navigation( run: 180, walk: 85 );
				p.Skill( "sk_metropolice_health" );

				npc.Health = 40;
				npc.PatrolRadius = 2048;
				npc.SearchTimeout = 20;
				npc.Manhacks = 1;
				npc.Voice = "NPC_MetroPolice";
				npc.EyeHeight = 57.6f;
				p.Root.AddComponent<NpcAnimator>();
				p.Box( 26, 72 );
				npc.Weapon = p.Equip( "pistol" );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "metrocop_stunstick", "Metrocop (Stunstick)", "models/police.mdl", "batonangryidle1" )
		{
			RequiredPrefabs = ["entities/weapons/stunstick.prefab"],
			Build = p =>
			{
				var npc = p.Npc<CombatNpc>();
				p.Navigation( run: 180, walk: 85 );
				p.Skill( "sk_metropolice_health" );

				npc.Health = 40;
				npc.PatrolRadius = 2048;
				npc.SearchTimeout = 20;
				npc.Manhacks = 1;
				npc.Voice = "NPC_MetroPolice";
				npc.EyeHeight = 57.6f;
				p.Root.AddComponent<NpcAnimator>();
				p.Box( 26, 72 );
				npc.Weapon = p.Equip( "stunstick" );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "prison_guard", "Prison Guard", "models/combine_soldier_prisonguard.mdl", "Idle1_SMG1" )
		{
			RequiredPrefabs = ["entities/weapons/shotgun.prefab"],
			Build = p =>
			{
				var npc = p.Npc<CombatNpc>();
				p.Navigation( run: 180, walk: 85 );
				p.Skill( "sk_combine_s_health" );

				npc.DistractedByBugbait = true;
				npc.StepsIn = true;
				npc.Kicks = true;
				npc.Health = 60;
				npc.PatrolRadius = 2048;
				npc.SearchTimeout = 20;
				npc.Grenades = 5;
				npc.EyeHeight = 57.6f;
				p.Root.AddComponent<NpcAnimator>();
				p.Box( 26, 72 );
				npc.Weapon = p.Equip( "shotgun" );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "stalker", "Stalker", "models/stalker.mdl", "idle1" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation();
				p.Skill( "sk_stalker_health" );

				npc.Idle = "idle1";
				npc.Attack = "working";
				npc.AttackRange = 700;
				npc.AttackDamage = 8;
				npc.AttackInterval = 0.3f;
				npc.AttackDelay = 0.1f;
				npc.HatesCombine = false;
				npc.AttackHeight = 50;
				npc.Voice = "NPC_Stalker";
				npc.EyeHeight = 50;
				var npcRangedEffects = p.Root.AddComponent<NpcRangedEffects>();
				npcRangedEffects.MuzzleAttachment = "eyes";
				npcRangedEffects.FireSound = "NPC_Stalker.BurnWall";
				npcRangedEffects.ContinuousSound = true;
				npcRangedEffects.Beam = true;
				npcRangedEffects.TracerColor = new( 1, 0.2f, 0.05f, 1 );
				npcRangedEffects.BeamSprite = "sprites/laser";
				npcRangedEffects.BeamWidth = 3;
				npcRangedEffects.DamageTag = "burn";
				p.Box( 26, 72 );
			}
		};
	}
}
