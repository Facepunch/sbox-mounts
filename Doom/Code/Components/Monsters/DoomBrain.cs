using System;

namespace Doom;

/// <summary>
/// The Icon of Sin (MT_BOSSBRAIN): the face behind the wall in Doom II's last level. Every hit makes
/// it cry out; when its health runs out it screams, explosions burst across it for a few seconds
/// (A_BrainScream), and the level is over (A_BrainDie).
/// </summary>
[Title( "Doom Icon of Sin" )]
[Category( "Doom" )]
[Icon( "face" )]
public sealed class DoomBrain : Component, Component.IDamageable
{
	[Property] public float Health { get; set; } = 250f;
	[Property] public float Scale { get; set; } = 1f;
	[Property] public SoundEvent PainSound { get; set; }
	[Property] public SoundEvent DeathSound { get; set; }

	/// <summary>The explosions that burst across it as it dies.</summary>
	[Property] public PrefabFile ExplosionPrefab { get; set; }

	[Sync( SyncFlags.FromHost )] public bool IsDead { get; private set; }

	/// <summary>A_BrainScream runs for S_BRAINDIE1's 100 tics, then two of 10 before A_BrainDie.</summary>
	private const float DyingTime = 120f / 35f;

	private TimeSince _sinceDeath;
	private TimeUntil _nextExplosion;
	private bool _over;

	void IDamageable.OnDamage( in DamageInfo damage )
	{
		if ( IsProxy || IsDead || damage.Damage <= 0 ) return;

		Health -= damage.Damage;

		// painchance 255: nearly every hit (A_BrainPain).
		if ( Health > 0 )
		{
			if ( Game.Random.Int( 0, 255 ) < 255 )
				PlaySound( PainSound );
			return;
		}

		IsDead = true;
		_sinceDeath = 0;
		PlaySound( DeathSound );
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy || !IsDead || _over ) return;

		// A_BrainScream: rockets bursting along the face, from 196 left to 320 right of it.
		if ( _nextExplosion && _sinceDeath < 100f / 35f )
		{
			_nextExplosion = 3f / 35f;
			var across = WorldRotation.Right * Game.Random.Float( -196f, 320f ) * Scale;
			var up = Vector3.Up * (128f + Game.Random.Float( 0f, 256f )) * Scale;
			Explode( WorldPosition + across + up );
		}

		if ( _sinceDeath >= DyingTime )
		{
			_over = true;
			Log.Info( $"Doom: the Icon of Sin is dead, {GameObject.Root.Name} is over" );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	private void Explode( Vector3 position )
	{
		if ( !ExplosionPrefab.IsValid() ) return;
		var go = GameObject.Clone( ExplosionPrefab, new Transform( position ), null, true );
		go.NetworkMode = NetworkMode.Never;
	}

	/// <summary>The brain's sounds are heard everywhere (S_StartSound with no origin).</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void PlaySound( SoundEvent sound )
	{
		if ( sound is not null )
			Sound.Play( sound );
	}
}

/// <summary>
/// The Icon of Sin's cube shooter (MT_BOSSSPIT), hidden in the map. It wakes once a player can see
/// it or the Icon (A_Look, A_BrainAwake), then every 181 tics spits a spawn cube at the next spawn
/// spot in turn (A_BrainSpit).
/// </summary>
[Title( "Doom Icon of Sin Shooter" )]
[Category( "Doom" )]
[Icon( "rocket_launch" )]
public sealed class DoomBrainShooter : Component
{
	/// <summary>The object tag on the spots cubes fly to.</summary>
	public const string TargetTag = "doom_brain_target";

	[Property] public PrefabFile CubePrefab { get; set; }
	[Property] public float Scale { get; set; } = 1f;
	[Property] public SoundEvent WakeSound { get; set; }
	[Property] public SoundEvent SpitSound { get; set; }

	/// <summary>S_BRAINEYE1: a cube every 181 tics.</summary>
	private const float SpitInterval = 181f / 35f;

	/// <summary>A_Look checks every 10 tics.</summary>
	private const float LookInterval = 10f / 35f;

	public bool IsAwake { get; private set; }

	private TimeUntil _next;
	private int _nextTarget;

	protected override void OnFixedUpdate()
	{
		if ( IsProxy || !_next ) return;

		if ( !IsAwake )
		{
			_next = LookInterval;
			if ( !PlayerSees() ) return;

			IsAwake = true;
			PlaySound( WakeSound );
			_next = 0;
			return;
		}

		_next = SpitInterval;
		Spit();
	}

	/// <summary>Whether a living player has a clear line to this shooter or to the Icon of Sin.</summary>
	private bool PlayerSees()
	{
		var root = GameObject.Root;
		var brain = Scene.GetAllComponents<DoomBrain>().FirstOrDefault( b => b.GameObject.Root == root && !b.IsDead );

		foreach ( var player in Scene.GetAllComponents<PlayerController>() )
		{
			if ( !Sandbox.Npcs.BaseNpc.IsLiveTarget( player.GameObject ) ) continue;

			var eye = Sandbox.Npcs.BaseNpc.CalculateEyePosition( player.GameObject );
			if ( Clear( eye, WorldPosition, player.GameObject ) ) return true;
			if ( brain.IsValid() && Clear( eye, brain.WorldPosition + Vector3.Up * 8f * Scale, player.GameObject ) ) return true;
		}

		return false;
	}

	private bool Clear( Vector3 from, Vector3 to, GameObject viewer )
	{
		var tr = Scene.Trace.Ray( from, to )
			.IgnoreGameObjectHierarchy( viewer )
			.WithoutTags( "npc", "corpse", "playerclip", "projectile", ThingTags.Thing, "player" )
			.Run();
		return !tr.Hit;
	}

	/// <summary>A_BrainSpit: a cube toward the next spot, arriving when it gets there.</summary>
	private void Spit()
	{
		var root = GameObject.Root;
		var targets = Scene.FindAllWithTag( TargetTag )
			.Where( t => t.Root == root )
			.OrderBy( t => t.WorldPosition.x ).ThenBy( t => t.WorldPosition.y )
			.ToArray();
		if ( targets.Length == 0 || !CubePrefab.IsValid() ) return;

		var target = targets[_nextTarget++ % targets.Length];

		var go = GameObject.Clone( CubePrefab, new Transform( WorldPosition ), GameObject.Parent, false );
		if ( go.GetComponent<DoomBrainCube>( true ) is { } cube )
			cube.Launch( target.WorldPosition );

		go.Enabled = true;
		go.NetworkSpawn( true, null );
		PlaySound( SpitSound );
	}

	/// <summary>The shooter's sounds are heard everywhere (S_StartSound with no origin).</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void PlaySound( SoundEvent sound )
	{
		if ( sound is not null )
			Sound.Play( sound );
	}
}

/// <summary>
/// A spawn cube (MT_SPAWNSHOT): flies straight through walls to its spot, then bursts into fire and
/// leaves a random monster there (A_SpawnFly), killing anything already standing in its place.
/// </summary>
[Title( "Doom Spawn Cube" )]
[Category( "Doom" )]
[Icon( "deployed_code" )]
public sealed class DoomBrainCube : Component
{
	/// <summary>World units per second.</summary>
	[Property] public float Speed { get; set; }

	/// <summary>What can arrive: each below its threshold out of 256, in the order of <see cref="BrainCube.Spawns"/>.</summary>
	[Property] public List<int> Thresholds { get; set; } = [];
	[Property] public List<PrefabFile> Monsters { get; set; } = [];

	[Property] public PrefabFile FirePrefab { get; set; }
	[Property] public SoundEvent FlySound { get; set; }
	[Property] public SoundEvent ArriveSound { get; set; }

	/// <summary>A_SpawnSound plays every 12 tics, once per turn of the cube.</summary>
	private const float FlySoundInterval = 12f / 35f;

	private Vector3 _from, _to;
	private float _flightTime;
	private TimeSince _sinceLaunch;
	private TimeUntil _nextFlySound;
	private bool _launched;

	/// <summary>Sends it toward a spot. Host only, before it's network spawned.</summary>
	public void Launch( Vector3 to )
	{
		_from = WorldPosition;
		_to = to;
		_flightTime = Speed > 0 ? _from.Distance( to ) / Speed : 0f;
		_sinceLaunch = 0;
		_launched = true;
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy || !_launched ) return;

		if ( _nextFlySound )
		{
			_nextFlySound = FlySoundInterval;
			PlaySoundAt( FlySound, WorldPosition );
		}

		if ( _sinceLaunch < _flightTime )
		{
			WorldPosition = Vector3.Lerp( _from, _to, _sinceLaunch / _flightTime );
			return;
		}

		Arrive();
	}

	private void Arrive()
	{
		_launched = false;
		WorldPosition = _to;

		SpawnFire( _to );
		PlaySoundAt( ArriveSound, _to );

		int roll = Game.Random.Int( 0, 255 );
		int index = Thresholds.FindIndex( below => roll < below );
		var prefab = index >= 0 && index < Monsters.Count ? Monsters[index] : null;

		if ( prefab.IsValid() )
		{
			var go = GameObject.Clone( prefab, new Transform( _to ), GameObject.Parent, false );
			var monster = go.GetComponent<DoomMonster>( true );

			// It turns to the nearest player, so it sees them and wakes (P_LookForPlayers, all around).
			var player = Scene.GetAllComponents<PlayerController>().OrderBy( p => p.WorldPosition.Distance( _to ) ).FirstOrDefault();
			if ( player.IsValid() )
				go.WorldRotation = Rotation.LookAt( (player.WorldPosition - _to).WithZ( 0 ) );

			Telefrag( _to, monster?.Radius ?? 32f, go );

			go.Enabled = true;
			go.NetworkSpawn( true, null );
		}

		GameObject.Destroy();
	}

	/// <summary>P_TeleportMove: whatever stands where the monster arrives dies.</summary>
	private void Telefrag( Vector3 at, float radius, GameObject arriving )
	{
		var victims = Scene.GetAllComponents<DoomMonster>().Select( m => (Component)m )
			.Concat( Scene.GetAllComponents<PlayerController>() )
			.Where( c => c.GameObject != arriving && (c.WorldPosition - at).WithZ( 0 ).Length < radius * 2f && MathF.Abs( c.WorldPosition.z - at.z ) < radius * 2f )
			.ToArray();

		foreach ( var victim in victims )
		{
			var damageable = victim.GameObject.GetComponentInChildren<Component.IDamageable>();
			damageable?.OnDamage( new DamageInfo { Damage = 10000, Attacker = GameObject, Position = at } );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	private void SpawnFire( Vector3 position )
	{
		if ( !FirePrefab.IsValid() ) return;
		var go = GameObject.Clone( FirePrefab, new Transform( position ), null, true );
		go.NetworkMode = NetworkMode.Never;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void PlaySoundAt( SoundEvent sound, Vector3 position )
	{
		if ( sound is not null )
			Sound.Play( sound, position );
	}
}
