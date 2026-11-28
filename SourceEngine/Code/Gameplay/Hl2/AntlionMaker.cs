using Sandbox.Npcs;

namespace Source1.Hl2;

/// <summary>Spawns antlions from a named template at burrow hints belonging to this maker.</summary>
public sealed class AntlionMaker : Component, IMapInput
{
	Doo.Block IMapInput.BuildInput( string input, string parameter, bool hasUser ) => input.ToLowerInvariant() switch
	{
		"enable" when parameter.Length == 0 => this.Call( nameof( StartSpawning ) ),
		"disable" when parameter.Length == 0 => this.Call( nameof( StopSpawning ) ),
		"setfighttarget" when parameter.Length > 0 && (!parameter.StartsWith( '!' ) || parameter == "!player") =>
			this.Call( nameof( SetFightTarget ), new Doo.LiteralExpression { LiteralValue = parameter } ),
		_ => null
	};

	[Property] public string Group { get; set; }
	[Property] public string TemplateName { get; set; }
	[Property] public string ChildPrefab { get; set; }
	[Property] public string SpawnTarget { get; set; }
	[Property] public string FollowTarget { get; set; }
	[Property] public string FightTarget { get; set; }
	[Property] public float Radius { get; set; }
	[Property] public float Interval { get; set; }
	[Property] public int Remaining { get; set; }
	[Property] public int MaxAlive { get; set; }
	[Property] public bool Spawning { get; set; }
	[Property] public bool RandomBurrow { get; set; }

	[Property] public bool IgnoreBugbait { get; set; }

	readonly List<CreatureNpc> _children = [];
	GameObject _leader;
	GameObject _fightTarget;
	Vector3? _fightGoal;
	bool _following;

	internal static void OrderSquads( Scene scene, GameObject leader, Vector3? fightGoal )
	{
		foreach ( var maker in scene.GetAllComponents<AntlionMaker>() )
		{
			if ( maker.IgnoreBugbait ) continue;
			maker._following = !fightGoal.HasValue;
			if ( maker._following )
				maker._leader = leader;
			else
			{
				maker._fightGoal = fightGoal;
				maker._fightTarget = null;
			}
			maker.UpdateChildren();
		}
	}
	TimeUntil _nextSpawn;
	int _skin;

	public void StartSpawning()
	{
		Spawning = true;
		_nextSpawn = 0;
	}

	public void StopSpawning() => Spawning = false;

	protected override void OnStart() => _nextSpawn = 0.1f;

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || !Spawning || Remaining == 0 || _nextSpawn > 0 ) return;
		_nextSpawn = Interval;
		_children.RemoveAll( child => !child.IsValid() || child.Health <= 0 );
		if ( !NpcConVars.Enabled || (MaxAlive >= 0 && _children.Count >= MaxAlive) ) return;

		var target = ResolveTarget( SpawnTarget );
		var position = target.IsValid() ? target.WorldPosition : WorldPosition;
		var burrow = ChooseBurrow( position );
		if ( !burrow.IsValid() ) return;

		var start = burrow.WorldPosition;
		var ground = Scene.Trace.Ray( start, start + Vector3.Down * 2048 ).WithTag( "world" ).Run();
		if ( !ground.Hit || ground.StartedSolid ) return;
		var prefab = GameObject.GetPrefab( ChildPrefab );
		if ( !prefab.IsValid() ) return;

		var forward = (position - ground.EndPosition).WithZ( 0 );
		var rotation = target.IsValid() && forward.LengthSquared > 0 ? Rotation.LookAt( forward ) : burrow.WorldRotation;
		var child = prefab.Clone( new CloneConfig { Transform = new Transform( ground.EndPosition, rotation ), Name = TemplateName } );
		var npc = child.GetComponent<CreatureNpc>( true );
		if ( !npc.IsValid() )
		{
			child.Destroy();
			return;
		}
		_skin = (_skin + 1) % 4;
		if ( npc.Renderer.IsValid() ) npc.Renderer.MaterialGroup = $"Skin {_skin}";
		if ( !_leader.IsValid() ) _leader = ResolveTarget( FollowTarget );
		if ( !_fightGoal.HasValue && !_fightTarget.IsValid() && ResolveTarget( FightTarget ) is { } initialTarget )
		{
			_fightTarget = initialTarget;
			_following = false;
		}
		ApplyOrder( npc );
		_children.Add( npc );
		child.GetOrAddComponent<Unburrow>();
		child.NetworkSpawn();
		burrow.ReuseDelay = 2;
		if ( Remaining > 0 ) Remaining--;
	}

	GameObject ResolveTarget( string name )
	{
		if ( string.IsNullOrEmpty( name ) ) return null;
		if ( name == "!player" ) return Scene.GetAllComponents<PlayerController>().FirstOrDefault()?.GameObject;
		return Scene.Directory.FindByName( name ).FirstOrDefault();
	}

	public void SetFightTarget( string name )
	{
		FightTarget = name;
		_fightTarget = ResolveTarget( name );
		_fightGoal = null;
		_following = false;
		UpdateChildren();
	}

	void UpdateChildren()
	{
		foreach ( var child in _children )
			if ( child.IsValid() && child.Health > 0 ) ApplyOrder( child );
	}

	void ApplyOrder( CreatureNpc npc ) =>
		npc.SetSquadGoal( _leader, _following ? null : _fightGoal, _following ? null : _fightTarget );

	AntlionBurrow ChooseBurrow( Vector3 target )
	{
		AntlionBurrow best = null;
		var limit = Radius * Radius;
		var count = 0;
		foreach ( var hint in Scene.GetAllComponents<AntlionBurrow>() )
		{
			if ( hint.GameObject.Parent != GameObject.Parent || hint.Group != Group || !hint.Available || hint.ReuseDelay > 0 ) continue;
			var distance = (hint.WorldPosition - target).LengthSquared;
			if ( distance > limit ) continue;
			if ( RandomBurrow && Game.Random.Next( ++count ) != 0 ) continue;
			best = hint;
			if ( !RandomBurrow ) limit = distance;
		}
		return best;
	}
}
