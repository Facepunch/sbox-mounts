using System;
using Sandbox.Npcs;

namespace Source1.Hl2;

/// <summary>
/// The zombine walks, breaking into sprints that come more often when no player watches it or it holds a grenade, and
/// always at half health. Close to its enemy and in a player's sight it may pull its one grenade and fight on with it live.
/// </summary>
[Title( "HL2 Zombine Grenade" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2ZombineGrenade", "Hl2ZombineGrenade" )]
public sealed class ZombineGrenade : CreatureBehaviour
{
	/// <summary>Sprint and pull decisions are rolled ten times a second, not every frame.</summary>
	const float DecisionInterval = 0.1f;

	/// <summary>Out of 100 per decision; doubled with no player looking, four times with a grenade.</summary>
	const int SprintChance = 10;

	const float SprintMaxDistance = 1024;
	const float FastSwingTime = 1;
	const float PullMaxDistance = 256;
	const float SharedPullDelay = 10;
	const float GrenadeDamage = 200;

	/// <summary>No zombine decides to pull while another has just done so; any zombine spawning clears it.</summary>
	static RealTimeUntil _nextPullAnywhere;

	[Sync( SyncFlags.FromHost )] bool Pulling { get; set; }
	[Sync( SyncFlags.FromHost )] bool Holding { get; set; }
	bool _used;
	TimeSince _pullStarted;
	TimeUntil _nextDecision;
	TimeUntil _nextPullCheck;
	float _sprintEnd = float.NegativeInfinity;
	float _restEnd;
	float _fastSwingEnd = float.NegativeInfinity;
	float _releaseTime;
	float _pullDuration;
	string _pull, _idle, _walk, _run, _fastAttack;
	NpcGrenade _grenade;
	IReadOnlyDictionary<string, float> _skill = new Dictionary<string, float>();
	string _mountName;

	bool Sprinting => Time.Now < _sprintEnd;
	bool FastSwing => Time.Now <= _fastSwingEnd;
	bool LowHealth => Npc.Health <= Npc.MaxHealth * 0.5f;

	public override string Animation => Pulling ? _pull : null;
	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(_pull, false), (_idle, true), (_walk, true), (_run, true), (_fastAttack, false)];
	public override bool AllowsRun => Sprinting;

	public override string TranslateActivity( string activity ) => activity switch
	{
		"ACT_IDLE" when Holding => _idle,
		"ACT_WALK" when Holding => _walk,
		"ACT_RUN" when Holding => _run,
		"ACT_MELEE_ATTACK1" when Holding || FastSwing => _fastAttack,
		_ => null
	};

	protected override void OnConfigure( Model model, ModelSequences sequences )
	{
		_pull = sequences.FindActivity( "ACT_ZOMBINE_GRENADE_PULL" );
		_idle = sequences.FindActivity( "ACT_ZOMBINE_GRENADE_IDLE" );
		_walk = sequences.FindActivity( "ACT_ZOMBINE_GRENADE_WALK" );
		_run = sequences.FindActivity( "ACT_ZOMBINE_GRENADE_RUN" );
		_fastAttack = sequences.FindActivity( "ACT_ZOMBINE_ATTACK_FAST" );
		_pullDuration = Duration( _pull, 1 );
		var marker = sequences.Get( _pull )?.Events.FirstOrDefault( e => e.Name == "AE_ZOMBINE_PULLPIN" );
		_releaseTime = Math.Clamp( marker?.Cycle ?? 0.4f, 0, 1 ) * _pullDuration;
	}

	protected override async void OnStart()
	{
		_nextPullAnywhere = 0;
		_mountName = MountContent.Of( GameObject );
		_skill = await SkillData.Read( _mountName, "skill_episodic.cfg" );
		if ( !IsValid || Application.IsDedicatedServer )
			return;

		if ( Sounds.Sound( "Zombine.ReadyGrenade", _mountName ) is { } ready )
			foreach ( var sound in ready.Sounds )
				await sound.LoadAsync();
	}

	public override bool Think( GameObject target )
	{
		var npc = Npc;
		if ( Pulling )
		{
			// The pull plays to its end whatever happens; the grenade comes out at the pin event
			if ( npc.Movement?.MoveTarget is not null )
				npc.Movement.Stop();
			if ( !Holding && _pullStarted >= _releaseTime )
			{
				_grenade = NpcGrenade.Spawn( npc, _mountName, NpcGrenade.FragFuse, GrenadeDamage, _skill.Get( "sk_fraggrenade_radius", 250 ) );
				Holding = true;
				PlaySound( "Zombine.ReadyGrenade" );
			}

			Pulling = _pullStarted < _pullDuration;
			return true;
		}

		if ( Holding && (!_grenade.IsValid() || _grenade.Holder != GameObject) )
			Holding = false;

		if ( !_nextDecision )
			return false;

		_nextDecision = DecisionInterval;
		var distance = target.IsValid() ? WorldPosition.Distance( target.WorldPosition ) : float.MaxValue;
		var inReach = distance <= npc.AttackRange;
		if ( WantsToPull( npc, target, distance ) )
		{
			_used = true;
			_nextPullAnywhere = SharedPullDelay;
			Pulling = true;
			_pullStarted = 0;
			return true;
		}

		if ( Sprinting && inReach )
		{
			_fastSwingEnd = Time.Now + FastSwingTime;

			// With a grenade it slows to a walk at its enemy, giving those nearby a moment to get away
			if ( Holding )
			{
				_sprintEnd = Time.Now;
				_restEnd = Time.Now + Game.Random.Float( 2.5f, 5 );
			}
		}
		else if ( WantsToSprint( npc, target, inReach ) )
		{
			_sprintEnd = Time.Now + (Holding || LowHealth ? float.PositiveInfinity : Game.Random.Float( 3.5f, 5.5f ));
			_restEnd = _sprintEnd + Game.Random.Float( 2.5f, 5 );
			PlaySound( "Zombine.Charge" );
		}

		return false;
	}

	bool WantsToSprint( CreatureNpc npc, GameObject target, bool inReach )
	{
		// Only while chasing an enemy it has not reached yet
		if ( Sprinting || inReach || npc.Movement?.MoveTarget is null || !target.IsValid()
			|| GameObject.GetBounds().Center.Distance( target.GetBounds().Center ) > SprintMaxDistance )
			return false;

		if ( LowHealth )
			return true;

		if ( Time.Now < _restEnd )
			return false;

		// A player looks at it when it is within about 40° of their flat facing
		var chance = SprintChance;
		var player = Watcher( target );
		if ( player is null || player.EyeAngles.Forward.WithZ( 0 ).Normal.Dot( (WorldPosition - player.WorldPosition).WithZ( 0 ).Normal ) <= 0.766f )
			chance *= 2;
		if ( Holding )
			chance *= 4;

		return Game.Random.Next( 0, 101 ) <= chance;
	}

	bool WantsToPull( CreatureNpc npc, GameObject target, float distance )
	{
		if ( _used || _pull is null || Holding || Sprinting || FastSwing || _nextPullAnywhere > 0 || !_nextPullCheck )
			return false;

		// It only pulls in sight of its enemy, and where a player close by can see it happen
		if ( !target.IsValid() || !Sees( npc.EyePosition, GameObject, target, Npcs.CapturePoint( target ) ) )
			return false;

		var player = Watcher( target );
		if ( player is null || !Sees( player.EyePosition, player.GameObject, GameObject, GameObject.GetBounds().Center )
			|| player.WorldPosition.Distance( WorldPosition ) > PullMaxDistance || distance > PullMaxDistance )
			return false;

		_nextPullCheck = 0.5f;
		return Game.Random.Float() <= 1 - distance / PullMaxDistance;
	}

	/// <summary>The player whose view counts: its enemy when that is a player, else the nearest one.</summary>
	PlayerController Watcher( GameObject target ) => target.GetComponent<PlayerController>()
		?? Scene.GetAllComponents<PlayerController>().OrderBy( x => x.WorldPosition.DistanceSquared( WorldPosition ) ).FirstOrDefault();

	bool Sees( Vector3 from, GameObject self, GameObject target, Vector3 point )
	{
		var trace = Scene.Trace.Ray( from, point ).IgnoreGameObjectHierarchy( self ).WithoutTags( "trigger" ).Run();
		return !trace.Hit || trace.GameObject?.Root == target.Root;
	}
}

/// <summary>A live frag grenade held by or thrown from an NPC; it survives its holder and keeps its remaining fuse.</summary>
[Title( "HL2 NPC Grenade" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2NpcGrenade", "Hl2NpcGrenade" )]
public sealed class NpcGrenade : Component
{
	/// <summary>The frag fuse npc_combine and npc_zombine light: COMBINE_GRENADE_TIMER.</summary>
	public const float FragFuse = 3.5f;

	/// <summary>The fuse is checked ten times a second; it blips every 10 checks, then every 3 over its last 1.5 s.</summary>
	const float FuseCheck = 0.1f;

	[Property] public GameObject Holder { get; set; }
	[Property] public GameObject Attacker { get; set; }
	[Property] public string MountName { get; set; }
	[Property] public float Fuse { get; set; }
	[Property] public float Damage { get; set; }
	[Property] public float Radius { get; set; }
	TimeSince _age;
	int _checks;
	int _nextBlip = 10;
	bool _lit;

	static Transform HandTransform( Sandbox.Npcs.BaseNpc npc ) => npc.Renderer.GetAttachment( "grenade_attachment" ) ?? npc.Renderer.GetAttachment( "lefthand" ) ?? npc.WorldTransform;

	public static NpcGrenade Spawn( Sandbox.Npcs.BaseNpc npc, string mountName, float fuse, float damage, float radius, Vector3? throwVelocity = null )
	{
		var go = new GameObject( false, "NPC grenade" ) { WorldTransform = HandTransform( npc ) };
		go.Tags.Add( "projectile" );
		go.Tags.Add( "removable" );
		var renderer = go.AddComponent<SkinnedModelRenderer>();
		var loader = go.AddComponent<MountedModel>();
		loader.MountName = mountName;
		loader.ModelPath = "models/weapons/w_grenade.mdl";
		loader.Renderer = renderer;
		var collider = go.AddComponent<SphereCollider>();
		collider.Radius = 3;
		collider.Enabled = throwVelocity.HasValue;
		var body = go.AddComponent<Rigidbody>();
		body.MotionEnabled = throwVelocity.HasValue;
		body.MassOverride = 1;
		var grenade = go.AddComponent<NpcGrenade>();
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

	protected override void OnStart()
	{
		_age = 0;
		if ( Networking.IsHost )
			Blip();
	}

	protected override void OnUpdate()
	{
		if ( !_lit && !Application.IsDedicatedServer )
			Light();

		if ( !Networking.IsHost || Holder is null )
			return;

		// In its holder's hand; once the holder is dead or gone it falls loose from where it was, at rest
		var npc = Holder.IsValid() ? Holder.GetComponent<Sandbox.Npcs.BaseNpc>() : null;
		if ( npc.IsValid() && npc.Health >= 1 )
		{
			WorldTransform = HandTransform( npc );
			return;
		}

		Holder = null;
		GetComponent<SphereCollider>( true ).Enabled = true;
		GetComponent<Rigidbody>().MotionEnabled = true;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost )
			return;

		var fuse = (int)MathF.Round( Fuse / FuseCheck );
		while ( _age >= (_checks + 1) * FuseCheck )
		{
			if ( ++_checks >= fuse )
			{
				Explode();
				return;
			}

			if ( _checks < _nextBlip )
				continue;

			Blip();
			_nextBlip = _checks + (_checks >= fuse - 15 ? 3 : 10);
		}
	}

	void Explode()
	{
		var holder = Holder.IsValid() ? Holder.GetComponent<Sandbox.Npcs.BaseNpc>() : null;
		var healthBefore = holder.IsValid() ? holder.Health : 0;
		new Explosion { Source = GameObject, Attacker = Attacker, Position = WorldPosition, Radius = Radius, Damage = Damage,
			Prefab = MountContent.Prefab( MountName, "explosion" ) }.Spawn();

		// The radius damage aims past the hand and the floor can shade it, but the blast is at the holder's own body
		if ( holder.IsValid() && holder.Health >= 1 && holder.Health >= healthBefore )
		{
			var point = holder.GameObject.GetBounds().ClosestPoint( WorldPosition );
			var info = new DamageInfo { Damage = Damage * (1 - WorldPosition.Distance( point ) / Radius), Attacker = Attacker.IsValid() ? Attacker : GameObject,
				Weapon = GameObject, Origin = WorldPosition, Position = point };
			info.Tags.Add( DamageTags.Explosion );
			holder.GameObject.GetComponent<Component.IDamageable>()?.OnDamage( info );
		}

		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Blip()
	{
		if ( !Application.IsDedicatedServer && Sounds.Sound( "Grenade.Blip", MountName ) is { } sound )
			GameObject.PlaySound( sound );
	}

	/// <summary>The lit fuse: a red glow and short red trail on the fuse attachment, once the model has loaded.</summary>
	void Light()
	{
		var renderer = GetComponent<SkinnedModelRenderer>();
		if ( !renderer.Model.IsValid() || renderer.Model.IsError )
			return;

		_lit = true;
		var fuse = new GameObject( GameObject, false, "Fuse" ) { NetworkMode = NetworkMode.Never };
		if ( renderer.GetAttachment( "fuse" ) is { } attachment )
			fuse.LocalTransform = WorldTransform.ToLocal( attachment );

		MountedEffects.SetSprite( MountedEffects.AddSprite( fuse, "sprites/redglow1", MountName ), 0.2f, 200 );
		if ( MountedEffects.BeamTexture( "sprites/bluelaser1", MountName ) is { } texturing )
		{
			var trail = fuse.AddComponent<TrailRenderer>();
			trail.Texturing = texturing;
			trail.Face = SceneLineObject.FaceMode.Camera;
			trail.Opaque = false;
			trail.MaxPoints = 128;
			trail.PointDistance = 2;
			trail.Color = Gradient.FromColors( Color.Red, Color.Red.WithAlpha( 0 ) );
			trail.Width = new Curve( new Curve.Frame( 0, 8 ) { Mode = Curve.HandleMode.Linear }, new Curve.Frame( 1, 1 ) );
			trail.LifeTime = 0.5f;
		}

		fuse.Enabled = true;
	}
}
