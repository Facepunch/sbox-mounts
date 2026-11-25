await Sandbox.Mounting.Directory.Mount( "payday" );
await ResourceLibrary.LoadAsync<PrefabFile>( "mount://payday/entities/npcs/civilian.prefab" );
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var loader = Sandbox.Mounting.MountUtility.FindLoader( "mount://payday/levels/suburbia/world.world.scene" );
loader.GetType().GetMethod( "OnSceneUnloaded", flags ).Invoke( loader, null );
var file = (SceneFile)loader.GetType().GetMethod( "Load", flags ).Invoke( loader, null );
var imported = new Scene();
var results = new System.Collections.Generic.List<string>();
string stage = "import";
try
{
	using var importScope = imported.Push();
	if ( !imported.Load( file ) ) throw new Exception( "Counterfeit import failed." );
	var doors = imported.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() )
		.Where( c => c.GetType().Name == "PaydayDoor" ).ToArray();
	if ( doors.Length != 15 ) throw new Exception( $"Expected 15 framed doors, got {doors.Length}." );
	foreach ( var source in doors.GroupBy( d => ((GameObject)d.GetType().GetProperty( "Leaf" ).GetValue( d )).GetComponent<ModelRenderer>().Model ).Select( g => g.First() ) )
	{
		var scene = new Scene();
		try
		{
			stage = $"clone {source.GameObject.Name}";
			using var scope = scene.Push();
			scene.Enabled = true;
			var floor = new GameObject( true, "Floor" ).AddComponent<BoxCollider>();
			floor.Scale = new Vector3( 2000, 2000, 20 ); floor.Center = new Vector3( 0, 0, -10 );
			var root = source.GameObject.Clone( new Vector3( 100, 100, 0 ), Rotation.FromYaw( 37 ) );
			var door = root.Components.GetAll().Single( c => c.GetType().Name == "PaydayDoor" );
			var type = door.GetType();
			var leaf = (GameObject)type.GetProperty( "Leaf" ).GetValue( door );
			var frame = root.Children.Single( c => c.Name == "Door frame" );
			var model = leaf.GetComponent<ModelRenderer>().Model;
			if ( model is null || model.IsError ) throw new Exception( "Missing leaf model." );
			var pane = leaf.Children.SingleOrDefault( c => c.Name == "Door glass" );
			var framePose = frame.WorldTransform;
			void Tick( int n ) { for ( int i = 0; i < n; i++ ) scene.GameTick( .05 ); }
			float Amount() => (float)type.GetProperty( "Amount" ).GetValue( door );
			Tick( 2 );
			float angle = (float)type.GetProperty( "InitialOpenAngle" ).GetValue( door );
			if ( MathF.Abs( angle ) < 80 || Amount() != 1 ) throw new Exception( $"Authored open pose missing: {angle}." );
			if ( (bool)type.GetProperty( "OpenBothWays" ).GetValue( door ) )
				throw new Exception( "Framed door permits reverse swing." );
			var authoredForward = leaf.LocalRotation.Forward;
			if ( frame.Tags.Has( "payday_door" ) || !leaf.Tags.Has( "payday_door" ) )
				throw new Exception( "Frame incorrectly excluded from navigation." );
			var player = new GameObject( true, "Player" );
			player.AddComponent<PlayerController>().UseInputControls = false;
			player.NetworkSpawn( true, Connection.Local );
			var across = model.Bounds.Size.x < model.Bounds.Size.y ? Vector3.Forward : Vector3.Left;
			void CheckMovingPane()
			{
				if ( pane is null ) return;
				var paneCenter = pane.WorldTransform.PointToWorld( pane.GetComponent<ModelRenderer>().Model.Bounds.Center );
				var paneNormal = pane.WorldRotation * Vector3.Forward;
				var paneHit = scene.Trace.Ray( paneCenter + paneNormal * 60, paneCenter - paneNormal * 60 ).Run();
				if ( paneHit.GameObject != pane )
					throw new Exception( $"Glass collision did not follow the open pose: hit={paneHit.GameObject}." );
			}
			CheckMovingPane();
			void Use( float side = 1 )
			{
				player.WorldPosition = leaf.WorldTransform.PointToWorld( model.Bounds.Center ).WithZ( 0 )
					+ leaf.WorldRotation * across * (70 * side);
				Tick( 1 );
				type.GetMethod( "Toggle" ).Invoke( door, new object[] { player } );
				player.WorldPosition = new Vector3( 700, 700, 0 );
				Tick( 20 );
			}
			Use();
			stage = $"closed {root.Name}";
			if ( Amount() > .01f ) throw new Exception( $"Framed door did not close: {Amount()}." );
			var center = root.WorldTransform.PointToWorld( model.Bounds.Center );
			var normal = root.WorldRotation * across;
			var closedHit = scene.Trace.Ray( center + normal * 80, center - normal * 80 ).Run().GameObject;
			if ( closedHit != leaf && (pane is null || closedHit != pane) )
				throw new Exception( "Closed leaf collision missing." );
			var view = new GameObject( true, "Door view" ).AddComponent<CameraComponent>();
			var light = new GameObject( true, "Light" ).AddComponent<PointLight>();
			light.Radius = 600; light.LightColor = Color.White * 4;
			void Screenshot( string state )
			{
				view.WorldPosition = center + normal * 180 + Vector3.Up * 25;
				view.WorldRotation = Rotation.LookAt( center - view.WorldPosition );
				light.WorldPosition = view.WorldPosition;
				using var bitmap = new Bitmap( 600, 600 );
				view.RenderToBitmap( bitmap );
				System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"framed-door-{results.Count}-{state}.png" ), bitmap.ToPng() );
			}
			Screenshot( "closed" );
			Use();
			if ( Amount() < .99f ) throw new Exception( "Framed door did not reopen." );
			if ( Vector3.Dot( leaf.LocalRotation.Forward, authoredForward ) < .999f )
				throw new Exception( "Opening changed the authored swing direction." );
			if ( frame.WorldTransform != framePose ) throw new Exception( "Door frame moved." );
			if ( scene.Trace.Ray( center + normal * 80, center - normal * 80 ).Run().GameObject == leaf )
				throw new Exception( "Open leaf still blocks closed doorway." );
			Screenshot( "open" );
			CheckMovingPane();
			Use();
			Use( -1 );
			if ( Amount() < .99f || Vector3.Dot( leaf.LocalRotation.Forward, authoredForward ) < .999f )
				throw new Exception( "Opposite-side use reversed the authored swing." );
			Use();
			if ( Amount() > .01f ) throw new Exception( "Door did not close before navigation check." );
			if ( pane is not null )
			{
				stage = $"glass {root.Name}";
				var glass = pane.Components.GetAll().Single( c => c.GetType().Name == "PaydayDoorGlass" );
				var glassRenderer = pane.GetComponent<ModelRenderer>();
				var glassCollider = pane.GetComponent<ModelCollider>();
				var glassModel = glassRenderer.Model;
				var glassCenter = pane.WorldTransform.PointToWorld( glassModel.Bounds.Center );
				var ray = new Ray( glassCenter + normal * 60, -normal );
				foreach ( float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity } )
					((Component.IDamageable)glass).OnDamage( new DamageInfo { Damage = invalid } );
				if ( (bool)glass.GetType().GetProperty( "Broken" ).GetValue( glass ) )
					throw new Exception( "Invalid damage broke pane." );
				var hit = BaseCombatWeapon.ShootBullet( scene, ray, 120, 0, 10, 0, player, null );
				if ( hit.GameObject != pane || !(bool)glass.GetType().GetProperty( "Broken" ).GetValue( glass ) )
					throw new Exception( $"Bullet did not break moving pane: hit={hit.GameObject}." );
				Tick( 1 );
				if ( glassRenderer.Enabled || glassCollider.Enabled )
					throw new Exception( "Broken pane remains visible/solid." );
				if ( scene.Trace.Ray( ray, 120 ).Run().Hit ) throw new Exception( "Broken pane still obstructs its opening." );
				var rail = root.WorldTransform.PointToWorld( model.Bounds.Center.WithZ( model.Bounds.Mins.z + 12 ) );
				if ( scene.Trace.Ray( rail + normal * 60, rail - normal * 60 ).Run().GameObject != leaf )
					throw new Exception( "Breaking glass removed the solid door rail." );
				Screenshot( "broken" );
			}
			await scene.NavMesh.Generate( scene.PhysicsWorld );
			stage = $"navigation {root.Name}";
			var closedCenter = center.WithZ( 0 );
			var npcObject = GameObject.GetPrefab( "mount://payday/entities/npcs/civilian.prefab" )
				.Clone( closedCenter - normal * 250 );
			var npc = npcObject.GetComponent<Sandbox.Npcs.BaseNpc>();
			npc.GetType().GetProperty( "Wander" ).SetValue( npc, false );
			player.WorldPosition = npcObject.WorldPosition + normal * 60;
			Tick( 2 );
			var command = npc.GetType().GetMethod( "Command" );
			command.Invoke( npc, new object[] { player } ); command.Invoke( npc, new object[] { player } );
			player.WorldPosition = closedCenter + normal * 500;
			Tick( 150 );
			if ( Amount() < .99f || Vector3.Dot( npcObject.WorldPosition - closedCenter, normal ) < 70 )
				throw new Exception( $"NPC failed framed passage: {npcObject.WorldPosition}, amount={Amount()}." );
			if ( Vector3.Dot( leaf.LocalRotation.Forward, authoredForward ) < .999f )
				throw new Exception( "NPC reversed the authored swing." );
			if ( pane is not null )
			{
				var glass = pane.Components.GetAll().Single( c => c.GetType().Name == "PaydayDoorGlass" );
				if ( ((ModelCollider)glass.GetType().GetProperty( "Collider" ).GetValue( glass )).Enabled )
					throw new Exception( "Opening the door restored broken glass collision." );
			}
			results.Add( $"{root.Name}: authored angle {angle}, fixed frame, closing/opening, collision and NPC passage PASS" );
		}
		finally { scene.Destroy(); }
	}
	return results;
}
catch ( Exception e ) { throw new Exception( $"{stage}: {e}", e ); }
finally { imported.Destroy(); }
