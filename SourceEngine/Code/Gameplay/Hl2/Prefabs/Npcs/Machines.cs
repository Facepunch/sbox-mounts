using Sandbox.Npcs.Layers;

namespace Source1.Hl2.Prefabs.Npcs;

[Library]
public static class Machines
{
	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> All()
	{
		yield return new( "hl2", EntityKind.Npc, "dropship", "Combine Dropship", "models/combine_dropship.mdl", "idle_exagg" )
		{
			RequiredPrefabs = ["entities/npcs/combine_soldier.prefab"],
			Build = p =>
			{
				var npc = p.Npc<Dropship>();
				var sensesLayer = p.Root.GetComponent<SensesLayer>( true );
				sensesLayer.SightRange = 2860;
				p.Skill( null, damage: "sk_npc_dmg_dropship" );

				npc.Health = npc.MaxHealth = 100;
				npc.Idle = npc.Move = npc.Attack = "idle_exagg";
				npc.Locomotion = CreatureNpc.Locomotions.Flying;
				npc.HatesCombine = false;
				npc.FlightHeight = 300;
				npc.MoveSpeed = 180;
				npc.AttackRange = 2200;
				npc.AttackDamage = 10;
				npc.AttackInterval = 0.3f;
				npc.AttackDelay = 0;
				npc.Voice = "NPC_CombineDropship";
				p.Root.AddComponent<SoundPointComponent>().SoundEvent = Sounds.Sound( "NPC_CombineDropship.RotorLoop", "hl2" );
				var deployPrefab = p.Root.AddComponent<DeployPrefab>();
				deployPrefab.Prefab = "combine_soldier";
				deployPrefab.Range = 1600;
				deployPrefab.Count = 3;
				p.BoxAt( Vector3.Zero, new( 400, 250, 180 ) );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "gunship", "Combine Gunship", "models/gunship.mdl", "prop_turn" )
		{
			Build = p =>
			{
				var npc = p.Npc<Gunship>();
				var sensesLayer = p.Root.GetComponent<SensesLayer>( true );
				sensesLayer.SightRange = 3250;
				sensesLayer.FieldOfView = 270;
				p.Skill( null, damage: "sk_npc_dmg_gunship", playerDamage: "sk_npc_dmg_gunship_to_plr" );

				npc.Health = npc.MaxHealth = 100;
				npc.Idle = npc.Move = npc.Attack = "prop_turn";
				npc.Locomotion = CreatureNpc.Locomotions.Flying;
				npc.HatesCombine = false;
				npc.FlightHeight = 350;
				npc.MoveSpeed = 220;
				npc.AttackRange = 2500;
				npc.Voice = "NPC_CombineGunship";
				p.Root.AddComponent<SoundPointComponent>().SoundEvent = Sounds.Sound( "NPC_CombineGunship.RotorSound", "hl2" );
				var npcRangedEffects = p.Root.AddComponent<NpcRangedEffects>();
				npcRangedEffects.FireSound = "NPC_CombineGunship.CannonSound";
				npcRangedEffects.ContinuousSound = true;
				p.BoxAt( Vector3.Zero, new( 400, 200, 100 ) );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "helicopter", "Hunter-Chopper", "models/combine_helicopter.mdl", "idle" )
		{
			Build = p =>
			{
				var npc = p.Npc<HunterChopper>();
				var sensesLayer = p.Root.GetComponent<SensesLayer>( true );
				sensesLayer.SightRange = 3900;
				p.Skill( "sk_helicopter_health", damage: "sk_npc_dmg_helicopter", playerDamage: "sk_npc_dmg_helicopter_to_plr" );

				npc.Health = npc.MaxHealth = 5600;
				npc.Move = "idle";
				npc.Attack = "idle";
				npc.Locomotion = CreatureNpc.Locomotions.Flying;
				npc.HatesCombine = false;
				npc.FlightHeight = 400;
				npc.MoveSpeed = 250;
				npc.AttackRange = 3000;
				npc.AttackDamage = 12;
				npc.AttackInterval = 0.15f;
				npc.AttackDelay = 0;
				npc.Voice = "NPC_AttackHelicopter";
				p.Root.AddComponent<SoundPointComponent>().SoundEvent = Sounds.Sound( "NPC_AttackHelicopter.Rotors", "hl2" );
				var npcRangedEffects = p.Root.AddComponent<NpcRangedEffects>();
				npcRangedEffects.MuzzleAttachment = "Muzzle";
				npcRangedEffects.FireSound = "NPC_AttackHelicopter.FireGun";
				npcRangedEffects.ContinuousSound = true;
				p.BoxAt( Vector3.Zero, new( 400, 180, 180 ) );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "hopper_mine", "Hopper Mine", "models/props_combine/combine_mine01.mdl", null )
		{
			Build = p =>
			{
				var npc = p.Npc<HopperMine>();
				var rigidbody = p.Root.GetComponent<Rigidbody>( true );
				rigidbody.Locking = rigidbody.Locking with { Pitch = false, Roll = false };
				rigidbody.MotionEnabled = true;
				p.Skill( "sk_hopper_mine_health" );

				npc.EyeHeight = 22.4f;
				p.Model.Collider = p.Root.AddComponent<ModelCollider>();
				p.ColliderObject();
			}
		};

		yield return new( "hl2", EntityKind.Npc, "hunter", "Hunter", "models/hunter.mdl", "idle1" )
		{
			Description = "Half-Life 2: Episode Two",
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( height: 110, radius: 32.5f );
				p.Skill( "sk_hunter_health", config: "skill_episodic.cfg" );

				npc.Health = 210;
				npc.Idle = "idle1";
				npc.Move = "canter_all";
				npc.Attack = "shoot_unplanted";
				npc.HatesCombine = false;
				npc.MoveSpeed = 180;
				npc.AttackRange = 1500;
				npc.AttackDamage = 20;
				npc.AttackInterval = 2.5f;
				npc.StandOff = true;
				npc.Voice = "NPC_Hunter";
				var npcProjectileAttack = p.Root.AddComponent<NpcProjectileAttack>();
				npcProjectileAttack.Kind = NpcProjectileAttack.Kinds.Flechette;
				npcProjectileAttack.Attachment = "top_eye";
				npcProjectileAttack.AlternateAttachment = "bottom_eye";
				npcProjectileAttack.FireSound = "NPC_Hunter.FlechetteShoot";
				var charge = p.Root.AddComponent<Charge>();
				charge.MaxRange = 1024;
				charge.Delay = 10;
				charge.RandomDelay = 5;
				charge.Chance = 0.25f;
				charge.DamageSetting = "sk_hunter_dmg_charge";
				charge.HitSound = "NPC_Hunter.ChargeHitEnemy";
				charge.CrashSound = "NPC_Hunter.ChargeHitWorld";
				charge.FaceTurnRate = 450;
				charge.FaceTurnRateStill = 350;
				charge.WindUpTurnRate = 350;
				charge.RunTurnRate = 50;
				charge.CloseTurnRate = 50;
				charge.NpcTurnRate = 0;
				charge.LightPropRatio = 1 / 3f;
				charge.Smashes = false;
				charge.HullWidth = 36;
				charge.RunDamageScale = 2;
				charge.CrashShakeAmplitude = 16;
				charge.CrashShakeRadius = 400;
				p.Box( 65, 110 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "manhack", "Manhack", "models/manhack.mdl", "idle" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Skill( "sk_manhack_health", damage: "sk_manhack_melee_dmg" );

				npc.Health = 25;
				npc.Move = "fly";
				npc.Attack = "fly2";
				npc.Locomotion = CreatureNpc.Locomotions.Flying;
				npc.HatesCombine = false;
				npc.MoveSpeed = 180;
				npc.AttackHeight = 40;
				npc.AttackDamage = 20;
				npc.Voice = "NPC_Manhack";
				npc.EyeHeight = 40;
				p.Root.AddComponent<Manhack>();
				p.BoxAt( Vector3.Zero, new( 24, 24, 24 ) );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "rollermine", "Rollermine", "models/roller.mdl", null )
		{
			Build = p =>
			{
				var npc = p.Npc<Rollermine>();
				var sensesLayer = p.Root.GetComponent<SensesLayer>( true );
				sensesLayer.HearingRange = 800;
				sensesLayer.SightRange = 1500;
				var rigidbody = p.Root.GetComponent<Rigidbody>( true );
				rigidbody.AngularDamping = 1.5f;
				rigidbody.LinearDamping = 0.3f;
				rigidbody.Locking = rigidbody.Locking with { Pitch = false, Roll = false };
				rigidbody.MotionEnabled = true;

				npc.Health = 35;
				var sphereCollider = p.Root.AddComponent<SphereCollider>();
				sphereCollider.Elasticity = 0.3f;
				sphereCollider.Friction = 0.8f;
				sphereCollider.Radius = 10;
				p.Root.AddComponent<IdleAnimation>();
				p.Renderer.BodyGroups = 1;
			}
		};

		yield return new( "hl2", EntityKind.Npc, "scanner", "City Scanner", "models/combine_scanner.mdl", "idle" )
		{
			Build = p =>
			{
				var npc = p.Npc<Scanner>();
				p.Skill( "sk_scanner_health" );

				npc.Health = 40;
				npc.Move = "idle";
				npc.Attack = "alert";
				npc.Locomotion = CreatureNpc.Locomotions.Flying;
				npc.HatesCombine = false;
				npc.MoveSpeed = 130;
				npc.AttackRange = 500;
				npc.AttackDamage = 0;
				npc.AttackInterval = 3;
				npc.Voice = "NPC_CScanner";
				npc.EyeHeight = 24;
				p.Root.AddComponent<ScannerPhoto>();
				p.BoxAt( Vector3.Zero, new( 35, 25, 30 ) );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "shield_scanner", "Shield Scanner", "models/shield_scanner.mdl", "Hover1" )
		{
			RequiredPrefabs = ["entities/npcs/hopper_mine.prefab"],
			Build = p =>
			{
				var npc = p.Npc<Scanner>();
				p.Skill( "sk_scanner_health" );

				npc.Health = 50;
				npc.Idle = "Hover1";
				npc.Move = "Hover4";
				npc.Attack = "Hover6";
				npc.Locomotion = CreatureNpc.Locomotions.Flying;
				npc.HatesCombine = false;
				npc.MoveSpeed = 130;
				npc.AttackRange = 80;
				npc.AttackDamage = 0;
				npc.AttackInterval = 2;
				npc.Voice = "NPC_SScanner";
				npc.EyeHeight = 36;
				var deployPrefab = p.Root.AddComponent<DeployPrefab>();
				deployPrefab.Prefab = "hopper_mine";
				p.BoxAt( Vector3.Zero, new( 30, 25, 45 ) );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "strider", "Strider", "models/combine_strider.mdl", "Idle01" )
		{
			Build = p =>
			{
				var npc = p.Npc<Strider>();
				p.Navigation( height: 480, radius: 50 );
				var sensesLayer = p.Root.GetComponent<SensesLayer>( true );
				sensesLayer.SightRange = 3120;
				p.Skill( "sk_strider_health", damage: "sk_npc_dmg_strider", playerDamage: "sk_npc_dmg_strider_to_plr" );

				npc.Health = npc.MaxHealth = 350;
				npc.Idle = "Idle01";
				npc.Move = "walk_all";
				npc.Attack = "Idle01";
				npc.HatesCombine = false;
				npc.MoveSpeed = 140;
				npc.AttackRange = 2400;
				npc.AttackInterval = 0.3f;
				npc.AttackDelay = 0;
				npc.AttackHeight = 430;
				npc.Voice = "NPC_Strider";
				npc.EyeHeight = 430;
				p.Root.AddComponent<StriderCannon>();
				p.Root.AddComponent<StriderStomp>();
				var npcRangedEffects = p.Root.AddComponent<NpcRangedEffects>();
				npcRangedEffects.MuzzleAttachment = "MiniGun";
				npcRangedEffects.FireSound = "NPC_Strider.FireMinigun";
				p.Renderer.LocalPosition = new( 0, 0, 480 );
				p.Box( 100, 480 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "turret_ceiling", "Ceiling Turret", "models/combine_turrets/ceiling_turret.mdl", null )
		{
			Build = p =>
			{
				var npc = p.Npc<TurretNpc>();
				var rigidbody = p.Root.GetComponent<Rigidbody>( true );
				rigidbody.Locking = rigidbody.Locking with { Pitch = false, Roll = false };
				rigidbody.Gravity = false;
				npc.Health = npc.MaxHealth = 1000;

				npc.EyeHeight = 48;
				var npcRangedEffects = p.Root.AddComponent<NpcRangedEffects>();
				npcRangedEffects.MuzzleAttachment = "eyes";
				npcRangedEffects.FireSound = "NPC_CeilingTurret.Shoot";
				p.Model.Collider = p.Root.AddComponent<ModelCollider>();
				p.ColliderObject();
			}
		};

		yield return FloorTurret( "turret_floor", "Floor Turret", false );

		yield return FloorTurret( "turret_floor_friendly", "Friendly Floor Turret", true );

		yield return new( "hl2", EntityKind.Npc, "turret_ground", "Ground Turret", "models/combine_turrets/ground_turret.mdl", null )
		{
			Build = p =>
			{
				var npc = p.Npc<TurretNpc>();
				var rigidbody = p.Root.GetComponent<Rigidbody>( true );
				rigidbody.Locking = rigidbody.Locking with { Pitch = false, Roll = false };
				rigidbody.Gravity = false;
				p.Skill( "sk_turret_ground_health" );

				npc.TracksWithBody = true;
				npc.EyeHeight = 48;
				var npcRangedEffects = p.Root.AddComponent<NpcRangedEffects>();
				npcRangedEffects.MuzzleAttachment = "eyes";
				npcRangedEffects.FireSound = "NPC_CeilingTurret.Shoot";
				p.Model.Collider = p.Root.AddComponent<ModelCollider>();
				p.ColliderObject();
			}
		};
	}

	static EntityDefinition FloorTurret( string name, string title, bool friendly ) =>
		new( "hl2", EntityKind.Npc, name, title, "models/combine_turrets/floor_turret.mdl", null )
		{
			Build = p =>
			{
				var npc = p.Npc<TurretNpc>();
				var rigidbody = p.Root.GetComponent<Rigidbody>( true );
				rigidbody.Locking = rigidbody.Locking with { Pitch = false, Roll = false };
				rigidbody.MotionEnabled = true;
				p.Skill( $"sk_{name}_health" );

				npc.Friendly = friendly;
				npc.FloorMounted = true;
				npc.EyeHeight = 48;
				var npcRangedEffects = p.Root.AddComponent<NpcRangedEffects>();
				npcRangedEffects.MuzzleAttachment = "eyes";
				npcRangedEffects.FireSound = "NPC_FloorTurret.Shoot";
				p.Model.Collider = p.Root.AddComponent<ModelCollider>();
				p.ColliderObject();
			}
		};
}
