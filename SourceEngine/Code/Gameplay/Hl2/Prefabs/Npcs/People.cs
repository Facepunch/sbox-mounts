namespace Source1.Hl2.Prefabs.Npcs;

[Library]
public static class People
{
	[Entities.Definitions]
	public static IEnumerable<EntityDefinition> All()
	{
		yield return new( "hl2", EntityKind.Npc, "alyx", "Alyx", "models/alyx.mdl", "Idle_subtle" )
		{
			RequiredPrefabs = ["entities/weapons/pistol.prefab"],
			Build = p =>
			{
				var npc = p.Npc<CombatNpc>();
				p.Navigation( run: 180, walk: 85, opensDoors: true );
				npc.Health = npc.MaxHealth = 80;

				npc.Friendly = true;
				npc.PatrolRadius = 2048;
				npc.SearchTimeout = 20;
				npc.Voice = "NPC_Alyx";
				npc.EyeHeight = 57.6f;
				p.Root.AddComponent<NpcAnimator>();
				p.Box( 26, 72 );
				npc.Weapon = p.Equip( "pistol" );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "barney", "Barney", "models/barney.mdl", "idle_subtle" )
		{
			RequiredPrefabs = ["entities/weapons/smg.prefab"],
			Build = p =>
			{
				var npc = p.Npc<CombatNpc>();
				p.Navigation( run: 180, walk: 85, opensDoors: true );
				npc.Health = npc.MaxHealth = 80;

				npc.Friendly = true;
				npc.PatrolRadius = 2048;
				npc.SearchTimeout = 20;
				npc.Voice = "NPC_Barney";
				npc.EyeHeight = 57.6f;
				p.Root.AddComponent<NpcAnimator>();
				p.Box( 26, 72 );
				npc.Weapon = p.Equip( "smg" );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "breen", "Dr. Breen", "models/breen.mdl", "idle_subtle" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( opensDoors: true );
				npc.Health = npc.MaxHealth = 8;

				npc.Idle = "idle_subtle";
				npc.Move = "walk_all";
				npc.Attack = "idle_subtle";
				npc.Passive = true;
				npc.MoveSpeed = 85;
				npc.EyeHeight = 57.6f;
				p.Box( 26, 72 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "citizen", "Citizen", "models/humans/group01/male_07.mdl", "idle_subtle" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( opensDoors: true );
				p.Skill( "sk_citizen_health" );

				npc.Idle = "idle_subtle";
				npc.Move = "walk_all";
				npc.Attack = "idle_subtle";
				npc.Passive = true;
				npc.MoveSpeed = 85;
				npc.EyeHeight = 57.6f;
				p.Box( 26, 72 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "eli", "Eli Vance", "models/eli.mdl", "idle_subtle" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( opensDoors: true );
				npc.Health = npc.MaxHealth = 8;

				npc.Idle = "idle_subtle";
				npc.Move = "walk_all";
				npc.Attack = "idle_subtle";
				npc.Passive = true;
				npc.MoveSpeed = 85;
				npc.EyeHeight = 57.6f;
				p.Box( 26, 72 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "gman", "G-Man", "models/gman.mdl", "idle_subtle" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( opensDoors: true );
				npc.Health = npc.MaxHealth = 8;

				npc.Idle = "idle_subtle";
				npc.Move = "walk_all";
				npc.Attack = "idle_subtle";
				npc.Passive = true;
				npc.MoveSpeed = 85;
				npc.EyeHeight = 57.6f;
				p.Box( 26, 72 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "grigori", "Father Grigori", "models/monk.mdl", "Idle01" )
		{
			RequiredPrefabs = ["entities/weapons/shotgun.prefab"],
			Build = p =>
			{
				var npc = p.Npc<CombatNpc>();
				p.Navigation( run: 180, walk: 85, opensDoors: true );
				npc.Health = npc.MaxHealth = 100;

				npc.Friendly = true;
				npc.PatrolRadius = 2048;
				npc.SearchTimeout = 20;
				npc.EyeHeight = 57.6f;
				p.Root.AddComponent<NpcAnimator>();
				p.Box( 26, 72 );
				npc.Weapon = p.Equip( "shotgun" );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "kleiner", "Dr. Kleiner", "models/kleiner.mdl", "idle_subtle" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( opensDoors: true );
				npc.Health = npc.MaxHealth = 8;

				npc.Idle = "idle_subtle";
				npc.Move = "walk_all";
				npc.Attack = "idle_subtle";
				npc.Passive = true;
				npc.MoveSpeed = 85;
				npc.EyeHeight = 57.6f;
				p.Box( 26, 72 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "mossman", "Dr. Mossman", "models/mossman.mdl", "idle_subtle" )
		{
			Build = p =>
			{
				var npc = p.Npc<CreatureNpc>();
				p.Navigation( opensDoors: true );
				npc.Health = npc.MaxHealth = 8;

				npc.Idle = "idle_subtle";
				npc.Move = "walk_all";
				npc.Attack = "idle_subtle";
				npc.Passive = true;
				npc.MoveSpeed = 85;
				npc.EyeHeight = 57.6f;
				p.Box( 26, 72 );
			}
		};

		yield return new( "hl2", EntityKind.Npc, "odessa", "Odessa Cubbage", "models/odessa.mdl", "idle_subtle" )
		{
			RequiredPrefabs = ["entities/weapons/smg.prefab"],
			Build = p =>
			{
				var npc = p.Npc<CombatNpc>();
				p.Navigation( run: 180, walk: 85, opensDoors: true );
				p.Skill( "sk_odessa_health" );

				npc.Friendly = true;
				npc.PatrolRadius = 2048;
				npc.SearchTimeout = 20;
				npc.EyeHeight = 57.6f;
				p.Root.AddComponent<NpcAnimator>();
				p.Box( 26, 72 );
				npc.Weapon = p.Equip( "smg" );
			}
		};

		yield return Rebel( "rebel", "Rebel", "group03", false );

		yield return Rebel( "rebel_medic", "Rebel Medic", "group03m", true );
	}

	static EntityDefinition Rebel( string name, string title, string model, bool medic ) =>
		new( "hl2", EntityKind.Npc, name, title, $"models/humans/{model}/male_07.mdl", "idle_subtle" )
		{
			RequiredPrefabs = ["entities/weapons/smg.prefab"],
			Build = p =>
			{
				var npc = p.Npc<CombatNpc>();
				p.Navigation( run: 180, walk: 85, opensDoors: true );
				p.Skill( "sk_citizen_health" );

				npc.Friendly = true;
				npc.Medic = medic;
				npc.PatrolRadius = 2048;
				npc.SearchTimeout = 20;
				npc.EyeHeight = 57.6f;
				p.Root.AddComponent<CitizenAnimator>();
				p.Box( 26, 72 );
				npc.Weapon = p.Equip( "smg" );
			}
		};
}
