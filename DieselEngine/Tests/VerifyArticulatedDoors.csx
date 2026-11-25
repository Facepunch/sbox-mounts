await Sandbox.Mounting.Directory.Mount( "payday" );
await ResourceLibrary.LoadAsync<PrefabFile>( "mount://payday/entities/npcs/civilian.prefab" );
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var results = new System.Collections.Generic.List<string>();
var tested = new System.Collections.Generic.HashSet<string>();
var expectedPanels = new System.Collections.Generic.Dictionary<string,int>
{
	["bank"]=26, ["suburbia"]=3, ["diamondheist"]=41, ["l4d"]=20, ["street"]=26,
	["bridge"]=3, ["slaughterhouse"]=75, ["apartment"]=2, ["secret_stash"]=25
};
foreach ( var level in new[] { "bank", "suburbia", "diamondheist", "l4d", "street", "bridge", "slaughterhouse", "apartment", "secret_stash" } )
{
	var loader = Sandbox.Mounting.MountUtility.FindLoader( $"mount://payday/levels/{level}/world.world.scene" );
	loader.GetType().GetMethod( "OnSceneUnloaded", flags ).Invoke( loader, null );
	var file = (SceneFile)loader.GetType().GetMethod( "Load", flags ).Invoke( loader, null );
	var imported = new Scene();
	try
	{
		using var importScope = imported.Push();
		if ( !imported.Load( file ) ) throw new Exception( $"{level}: load failed." );
		var doors = imported.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() )
			.Where( c => c.GetType().Name == "PaydayDoor" && (bool)c.GetType().GetProperty( "AuthoredMotion" ).GetValue( c ) ).ToArray();
		if ( doors.Length != expectedPanels[level] ) throw new Exception( $"{level}: expected {expectedPanels[level]} panels, found {doors.Length}." );
		foreach ( var source in doors.Select( d => d.GameObject.Parent ).Distinct() )
		{
			string family = System.Text.RegularExpressions.Regex.Replace( source.Name, @"_\d{3}$", "" );
			if ( !tested.Add( family ) ) continue;
			var scene = new Scene();
			try
			{
				using var scope = scene.Push(); scene.Enabled = true;
				var floor = new GameObject( true, "Floor" ).AddComponent<BoxCollider>();
				floor.Scale = new Vector3( 3000, 3000, 20 ); floor.Center = new Vector3( 0, 0, -10 );
				var root = source.Clone( new Vector3( 100, 100, 0 ), Rotation.FromYaw( 31 ) );
				var panels = root.Children.SelectMany( g => g.Components.GetAll() ).Where( c => c.GetType().Name == "PaydayDoor" ).ToArray();
				if ( panels.Length == 0 ) throw new Exception( $"{family}: no panels." );
				void Tick() { for ( int i = 0; i < 35; i++ ) scene.GameTick( .05 ); }
				void SetOpen( bool value )
				{
					foreach ( var panel in panels ) panel.GetType().GetMethod( "SetOpen", flags ).Invoke( panel, new object[] { value } );
				}
				Tick();
				SetOpen( false ); Tick();
				foreach ( var door in panels )
					if ( (float)door.GetType().GetProperty( "Amount" ).GetValue( door ) != 0 ) throw new Exception( $"{family}: close failed." );
				SetOpen( true ); Tick();
				foreach ( var door in panels )
				{
					var type = door.GetType();
					var leaf = (GameObject)type.GetProperty( "Leaf" ).GetValue( door );
					var target = (Transform)type.GetProperty( "TargetPose" ).GetValue( door );
					if ( (float)type.GetProperty( "Amount" ).GetValue( door ) < .99f
						|| leaf.LocalPosition.Distance( target.Position ) > .1f
						|| Vector3.Dot( leaf.LocalRotation.Forward, target.Rotation.Forward ) < .999f )
						throw new Exception( $"{family}: linked panel pose failed." );
					if ( leaf.GetComponent<ModelRenderer>()?.Model is not { IsError: false }
						|| leaf.GetComponent<ModelCollider>() is null ) throw new Exception( $"{family}: missing moving mesh/collider." );
				}
				var first = (GameObject)panels[0].GetType().GetProperty( "Leaf" ).GetValue( panels[0] );
				var renderer = first.GetComponent<ModelRenderer>();
				var targetPoint = renderer.Bounds.Center;
				var view = new GameObject( true, "Preview" ).AddComponent<CameraComponent>();
				view.WorldPosition = targetPoint + first.WorldRotation.Forward * 220 + first.WorldRotation.Left * 160 + Vector3.Up * 80;
				view.WorldRotation = Rotation.LookAt( targetPoint - view.WorldPosition );
				var light = new GameObject( true, "Light" ).AddComponent<PointLight>();
				light.WorldPosition = view.WorldPosition; light.Radius = 1200; light.LightColor = Color.White * 3;
				using var bitmap = new Bitmap( 600, 600 ); view.RenderToBitmap( bitmap );
				System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"articulated-{family}.png" ), bitmap.ToPng() );
				if ( family is "sliding_security_door_suburbia" or "hospital_elevator_doors" )
				{
					SetOpen( false ); Tick();
					await scene.NavMesh.Generate( scene.PhysicsWorld );
					var bounds = first.GetComponent<ModelRenderer>().Model.Bounds;
					var normal = root.WorldRotation * (bounds.Size.x < bounds.Size.y ? Vector3.Forward : Vector3.Left);
					var center = root.WorldTransform.PointToWorld( bounds.Center ).WithZ( 0 );
					var player = new GameObject( true, "Player" );
					player.WorldPosition = center - normal * 190;
					player.AddComponent<PlayerController>().UseInputControls = false;
					player.NetworkSpawn( true, Connection.Local );
					var npcObject = GameObject.GetPrefab( "mount://payday/entities/npcs/civilian.prefab" ).Clone( center - normal * 250 );
					var npc = npcObject.GetComponent<Sandbox.Npcs.BaseNpc>();
					npc.GetType().GetProperty( "Wander" ).SetValue( npc, false );
					scene.GameTick( .05 );
					var command = npc.GetType().GetMethod( "Command" );
					command.Invoke( npc, new object[] { player } ); command.Invoke( npc, new object[] { player } );
					player.WorldPosition = center + normal * 500;
					for ( int i = 0; i < 220; i++ ) scene.GameTick( .05 );
					if ( Vector3.Dot( npcObject.WorldPosition - center, normal ) < 70 )
						throw new Exception( $"{family}: NPC could not traverse linked/sliding doorway." );
				}
				results.Add( $"{family}: {panels.Length} linked panels PASS" );
			}
			finally { scene.Destroy(); }
		}
		results.Add( $"{level}: {doors.Length} articulated panels" );
	}
	finally { imported.Destroy(); }
}
return results;
