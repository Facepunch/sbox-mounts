using System;
using Sandbox.Engine.Resources;

namespace Overgrowth;

internal sealed class LevelLoader( string levelPath ) : SceneLoader<GameMount>
{
	sealed class Actors
	{
		public readonly List<(RabbitGuard Guard, int Waypoint)> Guards = [];
		public readonly Dictionary<int, RabbitGuard> Characters = [];
		public readonly HashSet<int> PlayerIds = [];
		public string[] PlayerTeams = [];
		public readonly List<(int Id, GameObject Object, Model Model)> Items = [];
		public readonly Dictionary<int, (RabbitGuard Actor, XmlAsset Connection)> AttachedItems = [];
	}

	protected override void BuildScene()
	{
		var level = XmlAsset.Parse( Host.FileSystem.ReadAllText( levelPath ) );
		if ( level.Element( "Terrain" ) is { } terrain && terrain.Element("Heightmap") is not null )
		{
			var model = Model.Load( Host.MountedPath(levelPath) + ".vmdl" );
			if ( model is { IsError: false } ) AddModel( new GameObject(true,"Terrain"),model,true );
		}
		if ( level.Element("Sky")?.Element("RayToSun") is { } sun )
		{
			var go = new GameObject(true,"Sun");
			var ray = Vector(sun,"r",0);
			if (!ray.IsNearlyZero()) go.WorldRotation = Rotation.LookAt(-ray.Normal);
			go.AddComponent<DirectionalLight>().SkyColor = Color.White * 0.35f;
		}
		var jumpNodes = new Dictionary<int, XmlAsset>();
		var patrolPoints = new Dictionary<int, (PatrolPoint Point, XmlAsset Node)>();
		var actors = new Actors();
		foreach(var child in level.Children) Place(child, jumpNodes, patrolPoints, actors);
		foreach ( var (id, item, model) in actors.Items )
		{
			if ( !actors.AttachedItems.TryGetValue( id, out var link ) ) LevelItem.AddPhysics( item, model );
			else if ( link.Actor.IsValid() ) LevelItem.Attach( Host, item, model, link.Actor, (int)(link.Connection.Number( "type" ) ?? 0),
				link.Connection.Number( "mirrored" ) == 1, link.Connection.Attribute( "attachment" ) );
		}
		PlaceJumpLinks( jumpNodes );
		foreach ( var (point, node) in patrolPoints.Values )
			foreach ( var id in Connections( node ) )
				if ( patrolPoints.TryGetValue( id, out var next ) ) point.Connections.Add( next.Point );
		foreach ( var (guard, id) in actors.Guards )
		{
			guard.AlliedToPlayer = guard.Teams.Any( actors.PlayerTeams.Contains );
			if ( patrolPoints.TryGetValue( id, out var start ) ) guard.PatrolStart = start.Point;
			else if ( actors.Characters.TryGetValue( id, out var leader ) && leader != guard ) guard.PatrolCharacter = leader;
			else if ( actors.PlayerIds.Contains( id ) ) guard.PatrolPlayer = true;
		}
	}

	string[] GetTeams( XmlAsset actor, string file )
	{
		var teams = actor.Element( "parameters" )?.Elements( "parameter" ).FirstOrDefault( p => p.Attribute( "name" ) == "Teams" )?.Attribute( "val" );
		if ( teams is null && Host.GetActorPrefab( file ) is { } prefab &&
			Host.GetByPath( Host.MountedPath( System.IO.Path.ChangeExtension( prefab, ".vmdl" ) ) ) is CharacterLoader character )
			teams = character.DefaultTeams;
		return teams?.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) ?? [];
	}

	static IEnumerable<int> Connections( XmlAsset node )
	{
		foreach ( var connection in node.Element( "Connections" )?.Elements( "Connection" ) ?? [] )
			if ( int.TryParse( connection.Attribute( "id" ), out int id ) ) yield return id;
	}

	static void PlaceJumpLinks( Dictionary<int, XmlAsset> nodes )
	{
		var pairs = new HashSet<(int, int)>();
		foreach ( var (id, node) in nodes )
		{
			foreach ( var connection in node.Element( "NavMeshConnections" )?.Elements( "NavMeshConnection" ) ?? [] )
			{
				if ( !int.TryParse( connection.Attribute( "other_object_id" ), out int otherId ) || id == otherId ||
					!nodes.TryGetValue( otherId, out var other ) || !pairs.Add( (Math.Min( id, otherId ), Math.Max( id, otherId )) ) ) continue;
				var start = Vector( node, "t", 0 ) * 40f;
				var go = new GameObject( false, "Jump link" ) { WorldPosition = start };
				var link = go.AddComponent<NavMeshLink>();
				link.Type = NavMeshLinkType.Jump;
				link.WorldEndPosition = Vector( other, "t", 0 ) * 40f;
				link.IsBiDirectional = true;
				link.ConnectionRadius = 40f;
				go.Enabled = true;
			}
		}
	}

	void Place( XmlAsset node, Dictionary<int, XmlAsset> jumpNodes,
		Dictionary<int, (PatrolPoint Point, XmlAsset Node)> patrolPoints, Actors actors )
	{
		if ( node.Name == "NavmeshConnectionObject" && int.TryParse( node.Attribute( "id" ), out int id ) )
		{
			jumpNodes[id] = node;
			return;
		}
		if(node.Name is "Group" or "Prefab" || node.Attribute("t0") is null)
		{
			foreach(var child in node.Children) Place(child, jumpNodes, patrolPoints, actors);
			return;
		}
		var file=node.Attribute("type_file");
		var name = node.Attribute( "name" ) ?? System.IO.Path.GetFileNameWithoutExtension( file ) ?? node.Name;
		var transform = new Transform( Vector( node, "t", 0 ) * 40, Orientation( node ), Vector( node, "s", 1 ) );
		GameObject Object() => new GameObject( true, name ) { WorldTransform = transform };

		void ConnectItems( RabbitGuard actor )
		{
			foreach ( var link in node.Element( "ItemConnections" )?.Elements( "ItemConnection" ) ?? [] )
				if ( int.TryParse( link.Attribute( "id" ), out int itemId ) ) actors.AttachedItems[itemId] = (actor, link);
		}

		switch(node.Name)
		{
			case "PathPointObject" when int.TryParse( node.Attribute( "id" ), out int pointId ):
				var point = Object().AddComponent<PatrolPoint>();
				foreach ( var parameter in node.Element( "parameters" )?.Elements( "parameter" ) ?? [] )
				{
					if ( parameter.Attribute( "name" ) == "Wait" ) point.Wait = parameter.Number( "val" );
					if ( parameter.Attribute( "name" ) == "Type" ) point.Pose = parameter.Attribute( "val" );
				}
				patrolPoints[pointId] = (point, node);
				return;
			case "SpawnPoint":
				Object().AddComponent<SpawnPoint>();
				return;
			case "ActorObject" when node.Attribute("is_player")=="1":
				ConnectItems( null );
				actors.PlayerTeams = GetTeams( node, file );
				if ( int.TryParse( node.Attribute( "id" ), out int playerId ) ) actors.PlayerIds.Add( playerId );
				Object().AddComponent<SpawnPoint>();
				return;
			case "DynamicLightObject":
				var light=Object().AddComponent<PointLight>();
				light.Radius=(node.Number("s0")??1)*40;
				light.LightColor=new Color(node.Number("color_r")??1,node.Number("color_g")??1,node.Number("color_b")??1);
				return;
			case "ActorObject" when Host.GetActorPrefab( file ) is { } prefabName:
				var prefab = PrefabFile.Load( Host.MountedPath( prefabName ) );
				var actor = GameObject.Clone( prefab, transform, startEnabled: false, name: name );
				var fighter = actor.GetComponent<RabbitGuard>( includeDisabled: true );
				int start = Connections( node ).FirstOrDefault( -1 );
				actors.Guards.Add( (fighter, start) );
				if ( int.TryParse( node.Attribute( "id" ), out int actorId ) ) actors.Characters[actorId] = fighter;
				ConnectItems( fighter );
				fighter.AlwaysAfraidOnSight = node.Element( "parameters" )?.Elements( "parameter" ).Any( p => p.Attribute( "name" ) == "scared" ) == true;
				fighter.Teams = GetTeams( node, file );
				fighter.MetalArmor = node.Element( "parameters" )?.Elements( "parameter" ).Any( p => p.Attribute( "name" ) == "Armor" && p.Attribute( "val" ) == "metal" ) == true;
				foreach ( var parameter in node.Element( "parameters" )?.Elements( "parameter" ) ?? [] )
				{
					if ( parameter.Attribute( "name" ) == "Stick To Nav Mesh" && fighter.GetComponent<CharacterNavigation>( true ) is { } navigation )
						navigation.StickToNavMesh = parameter.Attribute( "type" ) != "int" || parameter.Number( "val" ) != 0f;
					if ( parameter.Attribute( "name" ) == "No Look Around" )
						fighter.NoLookAround = parameter.Attribute( "type" ) != "int" || parameter.Number( "val" ) != 0f;
					if ( parameter.Number( "val" ) is not { } value ) continue;
					switch ( parameter.Attribute( "name" ) )
					{
						case "Fear - Causes Fear On Sight": fighter.CausesFearOnSight = value != 0f; break;
						case "Fear - Always Afraid On Sight": fighter.AlwaysAfraidOnSight = value != 0f; break;
						case "Fear - Never Afraid On Sight": fighter.NeverAfraidOnSight = value != 0f; break;
						case "Fear - Afraid At Health Level": fighter.AfraidAtHealthLevel = Math.Clamp( value, 0f, 1f ); break;
						case "Hearing Modifier": fighter.HearingModifier = value; break;
						case "Focus FOV horizontal": fighter.FocusVision = fighter.FocusVision.WithX( value ); break;
						case "Focus FOV vertical": fighter.FocusVision = fighter.FocusVision.WithY( value ); break;
						case "Focus FOV distance": fighter.FocusVision = fighter.FocusVision.WithZ( value ); break;
						case "Peripheral FOV horizontal": fighter.PeripheralVision = fighter.PeripheralVision.WithX( value ); break;
						case "Peripheral FOV vertical": fighter.PeripheralVision = fighter.PeripheralVision.WithY( value ); break;
						case "Peripheral FOV distance": fighter.PeripheralVision = fighter.PeripheralVision.WithZ( value ); break;
						case "Weapon Catch Skill": fighter.WeaponCatchSkill = Math.Clamp( value, 0f, 1f ); break;
						case "Block Skill": fighter.BlockSkill = Math.Clamp( value, 0f, 1f ); break;
						case "Aggression": fighter.Aggression = Math.Clamp( value, 0f, 1f ); break;
						case "Ground Aggression": fighter.GroundAggression = Math.Clamp( value, 0f, 1f ); break;
						case "Attack Speed": fighter.AttackSpeed = Math.Clamp( value, 0.1f, 2f ); break;
					case "Movement Speed": fighter.MovementSpeed = Math.Clamp( value, 0.01f, 100f ); break;
						case "Attack Damage": fighter.AttackDamage = Math.Max( 0f, value ); break;
						case "fall_damage_mult" when !node.Element( "parameters" ).Elements( "parameter" ).Any( x => x.Attribute( "name" ) == "Fall Damage Multiplier" ):
						case "Fall Damage Multiplier": fighter.FallDamageMultiplier = Math.Max( 0.001f, value ); break;
						case "Wearing Metal Armor": fighter.MetalArmor = value != 0f; break;
						case "Cannot Be Disarmed": fighter.CannotBeDisarmed = value != 0f; break;
						case "Damage Resistance": fighter.DamageResistance = value; break;
					case "Knockout Shield": fighter.KnockoutShield = Math.Max( 0, (int)value ); break;
						case "Attack Knockback": fighter.AttackKnockback = Math.Max( 0f, value ); break;
						case "Throw Counter Probability": fighter.ThrowCounterProbability = Math.Clamp( value, 0f, 1f ); break;
						case "Block Follow-up": fighter.BlockFollowUp = Math.Clamp( value, 0f, 1f ); break;
						case "Left handed": fighter.LeftHanded = value != 0f; break;
					case "Invisible When Stationary": fighter.InvisibleWhenStationary = value != 0f; break;
					}
				}
				var renderer = fighter.Renderer;
				actor.WorldPosition = transform.PointToWorld( Vector3.Up * renderer.Model.Bounds.Mins.z );
				actor.Enabled = true;
				return;
			case "ActorObject":
				ConnectItems( null );
				if(string.IsNullOrEmpty(file)||!Host.FileSystem.FileExists(file))return;
				file=XmlAsset.Read(Host.FileSystem,file,"Actor").Element("Character")?.Value;
				break;
			case "EnvObject":
			case "ItemObject":
				break;
			default: return;
		}
		if(string.IsNullOrEmpty(file)) return;
		var model=Model.Load(Host.MountedPath(file)+".vmdl");
		if ( model is not { IsError: false } ) return;
		var go = Object();
		AddModel( go, model, node.Name == "EnvObject" );
		if ( node.Name == "ItemObject" )
			actors.Items.Add( (int.TryParse( node.Attribute( "id" ), out var itemId ) ? itemId : -1, go, model) );
	}

	static void AddModel(GameObject go,Model model,bool solid)
	{
		go.AddComponent<ModelRenderer>().Model=model;
		if(!solid || model.Physics is null || model.Physics.Parts.Count==0)return;
		go.Tags.Add("world");
		var collider=go.AddComponent<ModelCollider>();
		collider.Model=model;
		collider.Static=true;
	}

	static Vector3 Vector(XmlAsset n,string prefix,float fallback) => new(n.Number(prefix+"2")??fallback,n.Number(prefix+"0")??fallback,n.Number(prefix+"1")??fallback);

	static Rotation Orientation(XmlAsset n)
	{
		if(n.Attribute("q3") is not null) return new Rotation(n.Number("q2")??0,n.Number("q0")??0,n.Number("q1")??0,n.Number("q3")??1);
		if(n.Attribute("r0") is null)return Rotation.Identity;
		return Rotation.LookAt(new(n.Number("r10")??1,n.Number("r8")??0,n.Number("r9")??0),new(n.Number("r6")??0,n.Number("r4")??0,n.Number("r5")??1));
	}
}
