using System;

namespace Source1;

/// <summary>npc_zombine's grenade: one authored pull close to its enemy, then a sprint with the live grenade.</summary>
[Title( "HL2 Zombine Grenade" )]
[Category( "Half-Life 2" )]
public sealed class Hl2ZombineGrenade : SourceCreatureBehaviour
{
	const float PullMaxDistance = 256;
	const float PullCheckInterval = 0.5f;
	const float SharedPullDelay = 10;
	static RealTimeUntil _nextPullAnywhere;
	[Sync( SyncFlags.FromHost )] bool Pulling { get; set; }
	[Sync( SyncFlags.FromHost )] bool Holding { get; set; }
	[Sync( SyncFlags.FromHost )] bool Charging { get; set; }
	bool _used;
	TimeSince _started;
	TimeSince _lastCheck;
	TimeSince _lastPath = 10;
	float _releaseTime;
	float _pullDuration;
	string _pull;
	string _run;
	string _idle;
	Hl2NpcGrenade _grenade;
	IReadOnlyDictionary<string, float> _skill = new Dictionary<string, float>();
	string _mountName;
	public override string Animation => Pulling ? _pull : Holding ? Charging ? _run : _idle : null;
	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(_pull, false), (_run, true), (_idle, true)];

	protected override void OnConfigure( Model model, SourceModelSequences sequences )
	{
		_pull = sequences.FindActivity( "ACT_ZOMBINE_GRENADE_PULL" );
		_run = sequences.FindActivity( "ACT_ZOMBINE_GRENADE_RUN" );
		_idle = sequences.FindActivity( "ACT_ZOMBINE_GRENADE_IDLE" );
		_pullDuration = Duration( _pull, 1 );
		var marker = sequences.Get( _pull )?.Events.FirstOrDefault( e => e.Name == "AE_ZOMBINE_PULLPIN" );
		_releaseTime = Math.Clamp( marker?.Cycle ?? 0.4f, 0, 1 ) * _pullDuration;
	}

	protected override async void OnStart()
	{
		_mountName = SourceMount.Of( GameObject );
		_skill = await SourceSkillData.Read( _mountName, "skill_episodic.cfg" );
		if ( !IsValid || Application.IsDedicatedServer )
			return;

		if ( SourceSounds.Sound( "Zombine.ReadyGrenade", _mountName ) is { } ready )
			foreach ( var sound in ready.Sounds )
				await sound.LoadAsync();
	}

	bool WantsToPull( GameObject target )
	{
		if ( _used || _pull is null || !target.IsValid() || _lastCheck < PullCheckInterval || _nextPullAnywhere > 0 )
			return false;

		_lastCheck = 0;
		var distance = WorldPosition.Distance( target.WorldPosition );
		if ( distance > PullMaxDistance || Game.Random.Float() > 1 - distance / PullMaxDistance )
			return false;

		var trace = Scene.Trace.Ray( WorldPosition + Vector3.Up * 48, SourceNpcs.CapturePoint( target ) ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
		return !trace.Hit || trace.GameObject?.Root == target.Root;
	}

	public override bool Think( GameObject target )
	{
		var npc = Npc;
		if ( !Networking.IsHost || !npc.IsValid() )
			return false;

		if ( WantsToPull( target ) )
		{
			_used = true;
			_nextPullAnywhere = SharedPullDelay;
			Pulling = true;
			_started = 0;
			Ready();
		}

		if ( Pulling )
		{
			npc.Movement?.Stop();
			if ( _started >= _releaseTime && !_grenade.IsValid() )
			{
				_grenade = Hl2NpcGrenade.Spawn( npc, _mountName, Hl2NpcGrenade.FragFuse, _skill.Get( "sk_npc_dmg_fraggrenade", 75 ), _skill.Get( "sk_fraggrenade_radius", 250 ) );
				Holding = true;
			}

			if ( _started >= _pullDuration )
				Pulling = false;

			return true;
		}

		if ( !Holding )
			return false;

		if ( !_grenade.IsValid() )
		{
			Holding = false;
			Charging = false;
			return false;
		}

		Charging = target.IsValid();
		if ( target.IsValid() )
		{
			WorldRotation = Rotation.LookAt( (target.WorldPosition - WorldPosition).WithZ( 0 ) );
			if ( _lastPath > 0.4f )
			{
				_lastPath = 0;
				npc.Movement?.MoveTo( target.WorldPosition, 35, npc.Movement.RunSpeed, target );
			}
		}
		else
		{
			npc.Movement?.Stop();
		}

		return true;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Ready()
	{
		if ( SourceSounds.Sound( "Zombine.ReadyGrenade", SourceMount.Of( GameObject ) ) is { } sound )
			Sound.Play( sound, WorldPosition );
	}
}

/// <summary>A live frag grenade held by or thrown from an NPC; it survives its holder and keeps its remaining fuse.</summary>
[Title( "HL2 NPC Grenade" )]
[Category( "Half-Life 2" )]
public sealed class Hl2NpcGrenade : Component
{
	/// <summary>The frag fuse npc_combine and npc_zombine light: COMBINE_GRENADE_TIMER.</summary>
	public const float FragFuse = 3.5f;

	[Property] public GameObject Holder { get; set; }
	[Property] public GameObject Attacker { get; set; }
	[Property] public string MountName { get; set; }
	[Property] public float Fuse { get; set; }
	[Property] public float Damage { get; set; }
	[Property] public float Radius { get; set; }
	TimeSince _age;

	static Transform HandTransform( Sandbox.Npcs.BaseNpc npc ) => npc.Renderer.GetAttachment( "grenade_attachment" ) ?? npc.Renderer.GetAttachment( "lefthand" ) ?? npc.WorldTransform;

	public static Hl2NpcGrenade Spawn( Sandbox.Npcs.BaseNpc npc, string mountName, float fuse, float damage, float radius, Vector3? throwVelocity = null )
	{
		var go = new GameObject( false, "NPC grenade" ) { WorldTransform = HandTransform( npc ) };
		go.Tags.Add( "projectile" );
		go.Tags.Add( "removable" );
		var renderer = go.AddComponent<SkinnedModelRenderer>();
		var loader = go.AddComponent<SourceMountedModel>();
		loader.MountName = mountName;
		loader.ModelPath = "models/weapons/w_grenade.mdl";
		loader.Renderer = renderer;
		var collider = go.AddComponent<SphereCollider>();
		collider.Radius = 3;
		collider.Enabled = throwVelocity.HasValue;
		var body = go.AddComponent<Rigidbody>();
		body.MotionEnabled = throwVelocity.HasValue;
		body.MassOverride = 1;
		var grenade = go.AddComponent<Hl2NpcGrenade>();
		grenade.Holder = throwVelocity.HasValue ? null : npc.GameObject;
		grenade.Attacker = npc.GameObject;
		grenade.MountName = mountName;
		grenade.Fuse = fuse;
		grenade.Damage = damage;
		grenade.Radius = radius;
		go.Enabled = true;
		if ( throwVelocity is { } velocity )
			body.Velocity = velocity;
		go.NetworkSpawn();
		return grenade;
	}

	protected override void OnStart() => _age = 0;

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost )
			return;

		if ( _age >= Fuse )
		{
			new SourceExplosion { Source = GameObject, Attacker = Attacker, Position = WorldPosition, Radius = Radius, Damage = Damage,
				Prefab = SourceMount.Prefab( MountName, "explosion" ) }.Spawn();
			GameObject.Destroy();
			return;
		}

		if ( Holder is null )
			return;

		var npc = Holder.IsValid() ? Holder.GetComponent<Sandbox.Npcs.BaseNpc>() : null;
		if ( npc.IsValid() && npc.Health > 0 )
		{
			WorldTransform = HandTransform( npc );
			return;
		}

		Holder = null;
		GetComponent<SphereCollider>( true ).Enabled = true;
		GetComponent<Rigidbody>().MotionEnabled = true;
	}
}
