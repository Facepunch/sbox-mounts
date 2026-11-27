await Sandbox.Mounting.Directory.Mount( "payday" );
await Sandbox.Mounting.Directory.Mount( "hl1" );
const string path = "mount://payday/entities/npcs/cloaker.prefab";
const string opponent = "mount://hl1/entities/npcs/monster_human_grunt.prefab";
await ResourceLibrary.LoadAsync<PrefabFile>( path );
await ResourceLibrary.LoadAsync<PrefabFile>( opponent );
var results = new System.Collections.Generic.List<object>();
foreach ( bool interrupt in new[] { false, true } )
{
	var scene = new Scene();
	try
	{
		using var scope = scene.Push();
		scene.Enabled = true;
		var floor = new GameObject( true, "Floor" ).AddComponent<BoxCollider>();
		floor.Scale = new Vector3( 2000, 2000, 20 ); floor.Center = Vector3.Down * 10;
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var attacker = GameObject.GetPrefab( path ).Clone( Vector3.Zero, Rotation.Identity ).GetComponent<Sandbox.Npcs.BaseCombatNpc>();
		var victim = GameObject.GetPrefab( opponent ).Clone( new Vector3( 120, 0, 0 ), Rotation.FromYaw( 180 ) ).GetComponent<Sandbox.Npcs.BaseNpc>();
		victim.SetRelationship( attacker.GameObject, Sandbox.Npcs.Disposition.Neutral );
		attacker.SetRelationship( victim.GameObject, Sandbox.Npcs.Disposition.Neutral );
		var type = attacker.GetType();
		type.GetProperty( "UseRadio" ).SetValue( attacker, false );
		type.GetProperty( "DamagePerSecond" ).SetValue( attacker, 0f );
		type.GetProperty( "Cooldown" ).SetValue( attacker, 30f );
		float marker = (float)type.GetProperty( "Windup" ).GetValue( attacker );
		float duration = (float)type.GetProperty( "SpecialAnimationDuration" ).GetValue( attacker );
		if ( Math.Abs( marker - .96666664f ) > .001f || duration != 2 )
			throw new Exception( $"Original kick metadata mismatch: {marker}/{duration}." );
		string Phase() => type.GetProperty( "AttackPhase" ).GetValue( attacker ).ToString();
		// GameTick scopes Time.Now to this disposable scene; outside it the editor's clock is different.
		var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
		float Clock() => (float)(double)typeof(Scene).GetProperty( "TimeNow", flags ).GetValue( scene )
			- ((TimeSince)type.GetProperty( "StrikeStarted", flags ).GetValue( attacker )).Absolute;
		var animator = attacker.GameObject.Components.GetAll().First( c => c.GetType().Name == "PaydayNpcAnimator" );
		string Clip() => (string)animator.GetType().GetProperty( "CurrentClip" ).GetValue( animator );
		async Task Tick() { victim.Movement?.Stop(); scene.GameTick( .02 ); await Task.Delay( 5 ); }
		for ( int i = 0; i < 50; i++ ) await Tick();
		victim.Health = victim.MaxHealth = 10000;
		attacker.ClearRelationship( victim.GameObject );
		for ( int i = 0; i < 800 && Phase() != "Windup"; i++ ) await Tick();
		if ( Phase() != "Windup" ) throw new Exception( "Cloaker failed to begin its kick." );
		victim.Health = victim.MaxHealth = 10000;
		var light = new GameObject( true, "Key" ).AddComponent<DirectionalLight>();
		light.WorldRotation = Rotation.From( 45, 150, 0 ); light.LightColor = Color.White * 3;
		var camera = new GameObject( true, "Camera" ).AddComponent<CameraComponent>();
		camera.WorldPosition = new Vector3( 80, -200, 95 );
		camera.WorldRotation = Rotation.LookAt( new Vector3( 65, 0, 40 ) - camera.WorldPosition );
		camera.BackgroundColor = new Color( .12f, .14f, .18f );
		int captures = 0;
		bool interrupted = false, struck = false, followedThrough = false;
		float impact = 0;
		for ( int i = 0; i < 160; i++ )
		{
			await Tick();
			var time = Clock();
			if ( interrupt && !interrupted && time >= .4f )
			{
				((Component.IDamageable)attacker).OnDamage( new DamageInfo { Damage = 1 } );
				// Isolate animation/queued strike cleanup from ordinary firearm damage.
				attacker.SetRelationship( victim.GameObject, Sandbox.Npcs.Disposition.Neutral );
				interrupted = true;
				await Tick();
			}
			if ( interrupt )
			{
				if ( victim.Health != 10000 || victim.PhysicsCarrier.IsValid() )
					throw new Exception( "Interrupted windup dealt delayed damage or restrained the victim." );
				if ( interrupted && Clip() == "special" ) throw new Exception( "Interrupted animation remained active." );
				if ( time > duration + .1f ) break;
				continue;
			}
			if ( time < marker && victim.Health != 10000 ) throw new Exception( "Strike preceded the authored marker." );
			if ( !struck && victim.Health < 10000 )
			{
				impact = time; struck = true;
				// The live client also advances enabled scenes between awaited script ticks.
				if ( time < marker || time > marker + .1f || victim.Health != 9980 )
					throw new Exception( $"Strike timing/damage mismatch: {time}, health={victim.Health}." );
			}
			if ( time > marker + .1f && time < duration - .05f )
			{
				if ( Clip() != "special" || Phase() != "Holding" ) throw new Exception( "Kick follow-through was cut off." );
				followedThrough = true;
			}
			if ( time > duration + .04f && Clip() == "special" ) throw new Exception( "Kick looped past its duration." );
			if ( captures < 3 && time >= new[] { .4f, marker, 1.5f }[captures] )
			{
				using var bitmap = new Bitmap( 1000, 700 );
				camera.RenderToBitmap( bitmap );
				System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"payday-kick-{captures}.png" ), bitmap.ToPng() );
				captures++;
			}
			if ( time > duration + .1f ) break;
		}
		if ( !interrupt && (!struck || !followedThrough || victim.Health != 9980) )
			throw new Exception( $"Missing or repeated strike/follow-through: struck={struck}, follow={followedThrough}, health={victim.Health}." );
		results.Add( new { Interrupted = interrupt, Marker = marker, Duration = duration, Impact = impact, Result = "PASS" } );
	}
	finally { scene.Destroy(); }
}
return results;
