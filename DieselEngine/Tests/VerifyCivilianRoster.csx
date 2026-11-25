var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
var definitions = ((System.Collections.IEnumerable)mount.GetType().Assembly.GetType( "Diesel.NpcDefinition" ).GetProperty( "All" ).GetValue( null )).Cast<object>()
	.Where( d => (bool)d.GetType().GetProperty( "Civilian" ).GetValue( d ) ).ToArray();
foreach ( var definition in definitions )
{
	var id = (string)definition.GetType().GetProperty( "Id" ).GetValue( definition );
	await ResourceLibrary.LoadAsync<PrefabFile>( $"mount://payday/entities/npcs/{id}.prefab" );
}
var results = new System.Collections.Generic.List<string>();
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var ground = new GameObject( true, "Floor" ).AddComponent<BoxCollider>();
	ground.Scale = new Vector3( 1500, 1500, 20 ); ground.Center = new Vector3( 0, 0, -10 );
	await scene.NavMesh.Generate( scene.PhysicsWorld );
	void Tick( int count ) { for ( int i = 0; i < count; i++ ) scene.GameTick( .05 ); }
	var player = new GameObject( true, "Player" );
	player.AddComponent<PlayerController>().UseInputControls = false;
	player.NetworkSpawn( true, Connection.Local );
	foreach ( var definition in definitions )
	{
		var id = (string)definition.GetType().GetProperty( "Id" ).GetValue( definition );
		var go = GameObject.GetPrefab( $"mount://payday/entities/npcs/{id}.prefab" ).Clone( Vector3.Zero );
		var npc = go.GetComponent<Sandbox.Npcs.BaseNpc>();
		npc.GetType().GetProperty( "Wander" ).SetValue( npc, false );
		player.WorldPosition = new Vector3( -60, 0, 0 );
		Tick( 2 );
		if ( npc.GetDisposition( player ) != Sandbox.Npcs.Disposition.Neutral ) throw new Exception( $"{id}: non-neutral civilian." );
		void Command() => npc.GetType().GetMethod( "Command" ).Invoke( npc, new object[] { player } );
		Command();
		if ( !(bool)npc.GetType().GetProperty( "Surrendered" ).GetValue( npc ) ) throw new Exception( $"{id}: surrender failed." );
		Command();
		if ( npc.GetType().GetProperty( "Leader" ).GetValue( npc ) != player ) throw new Exception( $"{id}: follow failed." );
		Command();
		if ( npc.GetType().GetProperty( "Leader" ).GetValue( npc ) is not null ) throw new Exception( $"{id}: stay failed." );
		((Component.IDamageable)npc).OnDamage( new DamageInfo { Damage = 1000, Attacker = player } );
		Tick( 10 );
		var corpse = scene.GetAllComponents<ModelPhysics>().SingleOrDefault();
		if ( !go.IsDestroyed || corpse is null || corpse.Model is null || corpse.Model.IsError )
			throw new Exception( $"{id}: native skeletal ragdoll failed." );
		corpse.GameObject.Destroy(); Tick( 1 );
		results.Add( $"{id}: neutral, surrender/follow/stay, damage and ragdoll passed" );
	}
	return results;
}
finally { scene.Destroy(); }
