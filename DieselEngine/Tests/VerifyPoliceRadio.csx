var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
await mount.RefreshInternal();
await ResourceLibrary.LoadAsync<PrefabFile>( "mount://payday/entities/npcs/security.prefab" );
await ResourceLibrary.LoadAsync<PrefabFile>( "mount://payday/entities/weapons/crosskill.prefab" );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var floor = new GameObject( true, "Floor" ).AddComponent<BoxCollider>();
	floor.Scale = new Vector3( 14000, 14000, 20 ); floor.Center = new Vector3( 0, 0, -10 );
	await scene.NavMesh.Generate( scene.PhysicsWorld );
	void Tick( int n ) { for ( int i = 0; i < n; i++ ) scene.GameTick( .05 ); }
	IDisposable Clock() => Time.Scope( (double)typeof(Scene).GetProperty( "TimeNow", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic ).GetValue( scene ), .05 );
	Sandbox.Npcs.BaseCombatNpc Officer( float x, float yaw )
	{
		var npc = GameObject.GetPrefab( "mount://payday/entities/npcs/security.prefab" )
			.Clone( new Vector3( x, 0, 0 ), Rotation.FromYaw( yaw ) ).GetComponent<Sandbox.Npcs.BaseCombatNpc>();
		npc.GetType().GetProperty( "RadioRange" ).SetValue( npc, 600f );
		return npc;
	}
	bool Calling( Component npc ) => (bool)npc.GetType().GetProperty( "CallingBackup" ).GetValue( npc );
	var player = new GameObject( true, "Offender" );
	player.WorldPosition = new Vector3( 300, 0, 0 );
	player.AddComponent<PlayerController>().UseInputControls = false;
	player.NetworkSpawn( true, Connection.Local );
	var innocent = new GameObject( true, "Innocent" );
	innocent.WorldPosition = new Vector3( 300, 400, 0 );
	innocent.AddComponent<PlayerController>().UseInputControls = false;
	var inventory = player.AddComponent<BaseInventoryComponent>(); inventory.GiveOnStart = false;
	var gun = GameObject.GetPrefab( "mount://payday/entities/weapons/crosskill.prefab" ).Clone().GetComponent<BaseCombatWeapon>();
	inventory.Add( gun ); inventory.Switch( gun );
	Tick( 20 );
	var witness = Officer( 0, 0 );
	var recipient = Officer( -350, 180 );
	var distant = Officer( -950, 180 );
	Tick( 10 );
	var initial = $"first: distant={distant.WorldPosition}/{distant.GetDisposition(player)}/{distant.ActiveSchedule}, callout={witness.CalloutRange}";
	if ( !Calling( witness ) || recipient.GetDisposition( player ) != Sandbox.Npcs.Disposition.Neutral )
		throw new Exception( "Backup did not wait for radio delay." );
	var seenPosition = player.WorldPosition;
	player.WorldPosition = new Vector3( 5000, 0, 0 );
	Tick( 55 );
	var after = $"after: distant={distant.WorldPosition}/{distant.GetDisposition(player)}/{distant.ActiveSchedule}";
	if ( Calling( witness ) || recipient.GetDisposition( player ) != Sandbox.Npcs.Disposition.Hostile )
		throw new Exception( "Completed radio call did not alert nearby officer." );
	var reported = (Vector3?)recipient.GetType().GetProperty( "ReportedPosition" ).GetValue( recipient );
	if ( reported is null || reported.Value.Distance( seenPosition ) > 1 )
		throw new Exception( $"Report tracked unseen player instead of last sighting: {reported}." );
	if ( recipient.ActiveSchedule?.GetType().Name != "PaydayInvestigateReport" ) throw new Exception( "Recipient did not investigate report." );
	if ( recipient.GetDisposition( innocent ) != Sandbox.Npcs.Disposition.Neutral ) throw new Exception( "Radio blamed innocent." );
	Tick( 80 );
	if ( distant.GetDisposition( player ) != Sandbox.Npcs.Disposition.Neutral || Calling( recipient ) )
		throw new Exception( $"Radio relayed indefinitely or exceeded range: {initial}, {after}, distant={distant.GetDisposition(player)}, recipientCalling={Calling(recipient)}, witness={witness.WorldPosition}, recipient={recipient.WorldPosition}, distant={distant.WorldPosition}, player={player.WorldPosition}." );
	witness.GameObject.Destroy(); recipient.GameObject.Destroy(); distant.GameObject.Destroy(); Tick( 1 );

	player.WorldPosition = new Vector3( 300, 0, 0 );
	witness = Officer( 0, 0 ); recipient = Officer( -350, 180 );
	Tick( 10 );
	using ( Clock() ) ((Component.IDamageable)witness).OnDamage( new DamageInfo { Damage = 1, Attacker = player } );
	if ( Calling( witness ) ) throw new Exception( "Damage failed to interrupt radio." );
	player.WorldPosition = new Vector3( 5000, 0, 0 );
	Tick( 80 );
	if ( recipient.GetDisposition( player ) != Sandbox.Npcs.Disposition.Neutral ) throw new Exception( "Interrupted call still alerted officer." );
	witness.GameObject.Destroy(); recipient.GameObject.Destroy(); Tick( 1 );

	player.WorldPosition = new Vector3( 300, 0, 0 );
	witness = Officer( 0, 0 ); recipient = Officer( -350, 180 );
	Tick( 10 );
	using ( Clock() ) ((Component.IDamageable)witness).OnDamage( new DamageInfo { Damage = 1000, Attacker = player } );
	player.WorldPosition = new Vector3( 5000, 0, 0 );
	Tick( 80 );
	if ( recipient.GetDisposition( player ) != Sandbox.Npcs.Disposition.Neutral ) throw new Exception( "Dead officer completed radio call." );
	return "PASS: delayed nearby backup, last-seen investigation, innocent/range isolation, no relay loop, and damage/death interruption.";
}
finally { scene.Destroy(); }
