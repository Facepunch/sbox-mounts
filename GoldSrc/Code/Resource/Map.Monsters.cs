using System;
using GoldSrc.Bsp;
using GoldSrc.HalfLife;

namespace GoldSrc.Maps;

/// <summary>
/// Monsters and the scripted_sequences that pose them.
/// </summary>
partial class MapBuilder
{
	private readonly Dictionary<string, Monster> _monsters = new( StringComparer.Ordinal );
	private readonly List<(string ClassName, Monster Monster)> _monsterList = [];

	/// <summary>
	/// The model each monster sets in its Spawn(). monster_generic is the only one that carries
	/// its own, in a "model" key.
	/// </summary>
	private static string MonsterModel( Entity entity ) => entity.ClassName switch
	{
		"monster_barney" => "models/barney.mdl",
		"monster_scientist" or "monster_sitting_scientist" => "models/scientist.mdl",
		"monster_gman" => "models/gman.mdl",
		"monster_generic" => entity.GetString( "model" ),
		_ => null
	};

	/// <summary>
	/// A sitting scientist picks one of five poses; everyone else is left to the Monster component,
	/// which resolves the model's own idle.
	/// </summary>
	private static string MonsterIdle( Entity entity ) => entity.ClassName switch
	{
		"monster_sitting_scientist" => SitPoses[Random.Shared.Next( SitPoses.Length )],
		_ => null
	};

	/// <summary>
	/// "sitlookleft" and the four sequences after it, which is what m_baseSequence + 0..4 lands on.
	/// Verified against scientist.mdl, sequences 71-75.
	/// </summary>
	private static readonly string[] SitPoses =
		["sitlookleft", "sitlookright", "sitscared", "sitting2", "sitting3"];

	// CPushable uses SOLID_BBOX, including chairs built as movable brushes.
	private IEnumerable<BBox> PushableBounds( BspFile bsp )
	{
		foreach ( var entity in bsp.Entities )
		{
			if ( entity.ClassName != "func_pushable" || entity.BrushModelIndex <= 0 || entity.BrushModelIndex >= bsp.Models.Length ) continue;
			var model = bsp.Models[entity.BrushModelIndex];
			var offset = entity.Origin + Vector3.Up;
			yield return new BBox( model.Mins + offset, model.Maxs + offset );
		}
	}

	private Vector3 PlacePerson( BspFile bsp, Vector3 origin, Npc npc )
	{
		var radius = npc.Definition.Radius;
		var mins = new Vector3( -radius, -radius, 0 );
		var floor = bsp.DropToHumanHull( origin, mins, npc is SittingScientist );
		var end = floor ?? origin + Vector3.Down * 256;
		foreach ( var box in PushableBounds( bsp ) )
		{
			if ( origin.x + radius <= box.Mins.x || origin.x - radius >= box.Maxs.x
				|| origin.y + radius <= box.Mins.y || origin.y - radius >= box.Maxs.y
				|| origin.z + npc.Definition.Height <= box.Mins.z || end.z > box.Maxs.z ) continue;

			// DROP_TO_FLOOR leaves the authored origin alone if the hull starts in solid.
			if ( origin.z <= box.Maxs.z ) return origin;
			floor = end = origin.WithZ( box.Maxs.z );
		}
		return floor ?? origin;
	}

	private bool TrySpawnNpc( BspFile bsp, Entity entity )
	{
		var path = $"mount://{Host.Ident}/entities/npcs/{entity.ClassName}.prefab";
		if ( Host.GetByPath( path ) is not NpcPrefabLoader ) return false;

		var prefab = PrefabFile.Load( path );
		var go = GameObject.Clone( prefab, new Transform( entity.Origin, entity.Rotation ), startEnabled: false );
		go.Name = EntityName( entity );
		if ( go.GetComponent<Npc>( true ) is { } npc )
		{
			if ( npc is Personnel or GMan )
			{
				npc.HoldMapPosition();
				if ( npc is Personnel person ) person.PreDisaster = (entity.SpawnFlags & 256) != 0;
				go.WorldPosition = PlacePerson( bsp, entity.Origin, npc );
				if ( npc is SittingScientist seated ) seated.PlacedOnMapHull = true;
			}
			if ( (entity.SpawnFlags & 4) != 0 ) MonsterClip.Restrict( go );
			npc.FadeCorpse = (entity.SpawnFlags & 512) != 0;
			npc.Body = entity.GetInt( "body" );
			npc.Skin = entity.GetInt( "skin" );
			if ( entity.GetFloat( "health" ) > 0 ) npc.Health = entity.GetFloat( "health" );
			if ( entity.ClassName is "monster_generic" or "monster_furniture"
				&& LoadMountedModel( entity.GetString( "model" ) ) is { IsError: false } model )
			{
				npc.ModelPath = model.ResourcePath;
				npc.Renderer.Model = model;
			}
			RegisterMonster( go, entity, npc.Renderer, npc );
		}
		else if ( !string.IsNullOrEmpty( entity.TargetName ) )
		{
			var marker = go.AddComponent<Relay>();
			marker.TargetName = entity.TargetName;
			RegisterTarget( entity, marker, null, null, null, nameof( MapEntity.Kill ) );
		}
		go.Enabled = true;
		return true;
	}

	private void SpawnMonster( GameObject go, Entity entity )
	{
		var model = LoadMountedModel( MonsterModel( entity ) );

		var renderer = go.AddComponent<SkinnedModelRenderer>();
		renderer.Model = model;
		renderer.UseAnimGraph = false;

		ApplyBody( renderer, entity );

		RegisterMonster( go, entity, renderer, null );
	}

	private void RegisterMonster( GameObject go, Entity entity, SkinnedModelRenderer renderer, Npc npc )
	{
		var monster = go.AddComponent<Monster>();
		monster.Npc = npc;
		monster.TargetName = entity.TargetName;
		if ( entity.GetInt( "TriggerCondition" ) == 2 ) monster.DamageTarget = entity.GetString( "TriggerTarget" );
		monster.Renderer = renderer;
		if ( npc is null ) monster.IdleSequence = MonsterIdle( entity );

		RegisterTarget( entity, monster );

		if ( !string.IsNullOrEmpty( entity.TargetName ) )
			_monsters.TryAdd( entity.TargetName, monster );

		_monsterList.Add( (entity.ClassName, monster) );
	}

	private Model LoadMountedModel( string relativePath )
	{
		if ( string.IsNullOrEmpty( relativePath ) )
			return null;

		var mountPath = Host.FindMountPath( relativePath );
		if ( mountPath is null )
			return null;

		return Model.Load( $"mount://{Host.Ident}/{mountPath}.vmdl" );
	}

	/// <summary>
	/// GoldSrc packs every bodygroup choice into a single "body" integer: each part reads
	/// (body / base) % count, where base is the product of the counts before it.
	///
	/// A scientist's -1 means "pick a head, any head", and head 2 is Luther, who gets his own skin
	/// so his hands match his face.
	/// </summary>
	private static void ApplyBody( SkinnedModelRenderer renderer, Entity entity )
	{
		const int ScientistHeads = 4;
		const int HeadLuther = 2;

		var body = entity.GetInt( "body" );
		var skin = entity.GetInt( "skin" );

		if ( entity.ClassName is "monster_scientist" or "monster_sitting_scientist" )
		{
			if ( body < 0 )
				body = Random.Shared.Next( ScientistHeads );

			if ( body == HeadLuther )
				skin = 1;
		}

		var parts = renderer.Model?.Parts.All;
		if ( parts is not null )
		{
			var b = 1;

			foreach ( var part in parts )
			{
				var count = part.Choices.Count;
				if ( count <= 0 )
					continue;

				renderer.SetBodyGroup( part.Name, body / b % count );
				b *= count;
			}
		}

		if ( skin > 0 )
			renderer.MaterialGroup = $"Skin {skin}";
	}

	private void SpawnScriptedSequence( GameObject go, Entity entity )
	{
		var script = go.AddComponent<ScriptedSequence>();
		script.TargetName = entity.TargetName;
		script.MoveTo = entity.GetInt( "m_fMoveTo" );
		script.ActorName = entity.GetString( "m_iszEntity" );
		script.PlaySequence = entity.GetString( "m_iszPlay" );

		RegisterTarget( entity, script );
		WireTrigger( entity, script );

		// Possession waits for the deferred pass, because the monster can appear later in the lump.
		_wiring.Add( () => Possess( entity, script ) );
	}

	private void Possess( Entity entity, ScriptedSequence script )
	{
		const int MoveInstant = 4;

		var actor = FindMonster( entity );
		if ( !actor.IsValid() )
			return;

		script.Actor = actor;

		var idle = entity.GetString( "m_iszIdle" );
		if ( !string.IsNullOrEmpty( entity.TargetName ) && string.IsNullOrEmpty( idle ) ) return;

		if ( script.MoveTo == MoveInstant )
		{
			if ( actor.Npc.IsValid() ) actor.Npc.HoldMapPosition();
			actor.WorldPosition = entity.Origin;
			actor.WorldRotation = Rotation.FromYaw( entity.Angles.yaw );
		}

		if ( string.IsNullOrEmpty( idle ) )
			return;

		// Only the property is set - the component plays it itself once the scene starts, which is
		// also the point at which the model is actually ready to be posed.
		if ( actor.Npc.IsValid() ) actor.Npc.ScriptedIdle = idle;
		else actor.IdleSequence = idle;
	}

	/// <summary>
	/// CCineMonster::FindEntity looks for "m_iszEntity" as a targetname first, then falls back to
	/// treating it as a classname and taking the nearest one inside "m_flRadius".
	/// </summary>
	private Monster FindMonster( Entity entity )
	{
		var name = entity.GetString( "m_iszEntity" );
		if ( string.IsNullOrEmpty( name ) )
			return null;

		if ( _monsters.TryGetValue( name, out var named ) )
			return named;

		var radius = entity.GetFloat( "m_flRadius" );
		var origin = entity.Origin;

		Monster closest = null;
		var closestDistance = float.MaxValue;

		foreach ( var (className, monster) in _monsterList )
		{
			if ( !className.Equals( name, StringComparison.Ordinal ) )
				continue;

			var distance = monster.WorldPosition.Distance( origin );
			if ( radius > 0f && distance > radius )
				continue;

			if ( distance >= closestDistance )
				continue;

			closest = monster;
			closestDistance = distance;
		}

		return closest;
	}
}
