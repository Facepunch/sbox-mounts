using System;
using Sandbox.Npcs;

namespace Source1.Hl2;

/// <summary>npc_barnacle: hangs from the ceiling, catches whatever touches its tongue, hauls it up and bites.</summary>
[Title( "HL2 Barnacle NPC" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2BarnacleNpc", "Hl2BarnacleNpc" )]
public sealed class BarnacleNpc : BaseNpc, Component.IDamageable.IEvents
{
	[Property] public float AttackRange { get; set; } = 600;
	[Property] public float AttackDamage { get; set; } = 15;
	[Sync( SyncFlags.FromHost )] public GameObject CapturedTarget { get; private set; }
	[Sync( SyncFlags.FromHost )] string FeedingClip { get; set; }
	readonly AnimationPlayer _animation = new();
	string _idle, _lift, _bitePlayer, _biteHuman, _biteSmall, _chewHuman, _chewSmall;
	(string, bool)[] _choices = [];
	Model _model;
	TimeUntil _search, _digest;
	ModelPhysics _swallowing;
	float _swallowEnd;

	public override string Faction => Factions.Monster;
	protected override void SetupRelationships()
	{
		SetRelationship( Factions.Monster, Disposition.Hostile );
		SetRelationship( Factions.Player, Disposition.Hostile );
		SetRelationship( Factions.Ally, Disposition.Hostile );
		SetRelationship( Factions.Citizen, Disposition.Hostile );
		SetRelationship( Factions.Enemy, Disposition.Hostile );
	}

	protected override void OnStart()
	{
		base.OnStart();
		_animation.EventFired += OnAnimationEvent;

		if ( GetComponent<Rigidbody>( true ) is { } body )
		{
			body.Gravity = false;
			body.MotionEnabled = false;
		}

		if ( Networking.IsHost )
		{
			// The spawner lifts an entity by how far it hangs below its origin, which puts a barnacle aimed at a ceiling
			// above that ceiling; search up from the bottom of its bounds instead.
			var start = WorldPosition.WithZ( MathF.Min( GameObject.GetBounds().Mins.z, WorldPosition.z ) - 8 );
			var ceiling = Scene.Trace.Ray( start, start + Vector3.Up * 2000 ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "player", "npc", "trigger" ).Run();
			WorldPosition = ceiling.Hit ? ceiling.EndPosition - Vector3.Up * 4 : WorldPosition + Vector3.Up * 250;
		}

		if ( !Application.IsDedicatedServer )
			GetOrAddComponent<BarnacleTongue>().Npc = this;
	}

	public Vector3 Mouth => Renderer.IsValid() && Renderer.SceneModel.IsValid() && Renderer.Model.Bones.GetBone( "Barnacle.innards" ) is { } mouth
		? Renderer.SceneModel.GetBoneWorldTransform( mouth.Index ).Position
		: WorldPosition - Vector3.Up * 15;

	protected override void OnUpdate()
	{
		if ( !Renderer.IsValid() || !Renderer.Model.IsValid() )
			return;

		if ( _model != Renderer.Model )
		{
			_model = Renderer.Model;
			var sequences = ModelSequences.Of( _model );
			_idle = sequences.FindActivity( "ACT_IDLE", "idle01" );
			string Feeding( string activity ) => sequences.FindActivity( "ACT_BARNACLE_" + activity, _idle );
			_lift = Feeding( "SLURP" );
			_bitePlayer = Feeding( "BITE_PLAYER" );
			_biteHuman = Feeding( "BITE_HUMAN" );
			_biteSmall = Feeding( "BITE_SMALL_THINGS" );
			_chewHuman = Feeding( "CHEW_HUMAN" );
			_chewSmall = Feeding( "CHEW_SMALL_THINGS" );
			_choices = new[] { _idle, _lift, _bitePlayer, _biteHuman, _biteSmall, _chewHuman, _chewSmall }
				.Select( clip => (clip, sequences.Get( clip )?.Looping == true) ).ToArray();
		}

		if ( !_animation.Ensure( Renderer, _choices, _idle ) )
			return;

		if ( Networking.IsHost )
			HostThink();

		_animation.Play( FeedingClip ?? _idle );
		_animation.Tick();
	}

	bool Reachable( GameObject victim )
	{
		if ( !victim.IsValid() )
			return false;

		var npc = victim.GetComponent<BaseNpc>();
		if ( npc.IsValid() && npc.PhysicsCarrier.IsValid() && npc.PhysicsCarrier != GameObject )
			return false;

		var mouth = Mouth;
		var end = Npcs.CapturePoint( victim );
		if ( end.z > mouth.z || mouth.Distance( end ) > AttackRange || (end - mouth).WithZ( 0 ).Length > 35 )
			return false;

		var trace = Scene.Trace.Ray( mouth, end ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
		return !trace.Hit || trace.GameObject?.Root == victim.Root;
	}

	/// <summary>Anything hostile under the tongue, whether or not it is seen or heard: the tongue catches by touch.</summary>
	IEnumerable<GameObject> Prey()
	{
		var mouth = Mouth;
		return Scene.GetAllComponents<PlayerController>().Select( x => x.GameObject )
			.Concat( Scene.GetAllComponents<BaseNpc>().Where( x => x.Health > 0 && x is not (BarnacleNpc or TurretNpc or Rollermine) ).Select( x => x.GameObject ) )
			.Where( x => (x.WorldPosition - mouth).WithZ( 0 ).Length < 35 && GetDisposition( x ) == Disposition.Hostile );
	}

	void HostThink()
	{
		if ( !NpcConVars.Enabled )
		{
			Capture( null );
			ReleaseBody();
			FeedingClip = null;
			return;
		}

		if ( _animation.Current == FeedingClip && _animation.Finished )
		{
			if ( FeedingClip == _biteHuman ) FeedingClip = _chewHuman;
			else if ( FeedingClip == _biteSmall ) FeedingClip = _chewSmall;
			else if ( FeedingClip == _bitePlayer && !CapturedTarget.IsValid() ) FeedingClip = null;
			else if ( (FeedingClip == _chewHuman || FeedingClip == _chewSmall) && !_swallowing.IsValid() && _digest ) FeedingClip = null;
		}

		if ( !CapturedTarget.IsValid() )
		{
			if ( _swallowing.IsValid() || (FeedingClip is not null && FeedingClip != _lift) ) return;
			FeedingClip = null;
			if ( !_search ) return;
			_search = 0.1f;
			var prey = Prey().FirstOrDefault( Reachable );
			if ( !prey.IsValid() ) return;
			Capture( prey );
		}
		else if ( FeedingClip == _lift && !Reachable( CapturedTarget ) )
		{
			Capture( null );
			FeedingClip = null;
			return;
		}

		var mouth = Mouth;
		var body = CapturedTarget.GetComponentInChildren<Rigidbody>();
		if ( body.IsValid() && !Npcs.IsPlayer( CapturedTarget ) )
		{
			body.MotionEnabled = true;
			body.Velocity = PullVelocity();
		}

		if ( FeedingClip is not null && FeedingClip != _lift ) return;
		if ( mouth.Distance( Npcs.CapturePoint( CapturedTarget ) ) > 60 )
			FeedingClip = _lift;
		else if ( Npcs.IsPlayer( CapturedTarget ) )
			FeedingClip = _bitePlayer;
		else
			FeedingClip = (CapturedTarget.GetComponent<BoxCollider>()?.LocalBounds.Size.z ?? CapturedTarget.GetBounds().Size.z) < 40 ? _biteSmall : _biteHuman;
	}

	Vector3 PullVelocity() => ((Mouth - Vector3.Up * 35 - Npcs.CapturePoint( CapturedTarget )) * 4).ClampLength( 80 );

	void OnAnimationEvent( SourceModelSequenceData.Event e )
	{
		if ( !Networking.IsHost || FeedingClip != _animation.Current || e.Name != "AE_BARNACLE_BITE" || !CapturedTarget.IsValid() )
			return;

		var victim = CapturedTarget;
		var damage = new DamageInfo { Damage = victim.GetComponent<BaseNpc>()?.Health ?? AttackDamage, Attacker = GameObject, Weapon = GameObject, Origin = Mouth, Position = Npcs.CapturePoint( victim ) };
		damage.Tags.Add( "melee" );
		victim.GetComponentInParent<Component.IDamageable>()?.OnDamage( damage );
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( Networking.IsHost && _swallowing.IsValid() )
		{
			if ( _swallowing.WorldPosition.z >= _swallowEnd )
			{
				_swallowing.GameObject.Destroy();
				_digest = 10;
			}
			else
				foreach ( var body in _swallowing.Bodies )
					if ( body.Component.IsValid() ) body.Component.Velocity = Vector3.Up * 32;
		}

		if ( !CapturedTarget.IsValid() )
			return;

		var controller = CapturedTarget.GetComponent<PlayerController>();
		// Player movement is owner simulated; only the owner applies the host's capture.
		if ( !controller.IsValid() || controller.IsProxy )
			return;

		controller.PreventGrounding( 0.1f );
		controller.WishVelocity = Vector3.Zero;
		controller.Body.Velocity = PullVelocity();
	}

	void IDamageable.IEvents.OnDied( IDamageable.DeathEvent e )
	{
		if ( e.Instigator != GameObject || e.Victim != CapturedTarget || e.Ragdoll?.GetComponent<ModelPhysics>() is not { } physics )
			return;

		_swallowing = physics;
		_swallowEnd = WorldPosition.z + physics.WorldPosition.z - e.Ragdoll.GetBounds().Mins.z;
		foreach ( var body in physics.Bodies )
			if ( body.Component?.PhysicsBody is { } b ) b.EnableSolidCollisions = false;
	}

	void ReleaseBody()
	{
		if ( !_swallowing.IsValid() ) return;
		foreach ( var body in _swallowing.Bodies )
			if ( body.Component?.PhysicsBody is { } b ) b.EnableSolidCollisions = true;
		_swallowing = null;
	}

	void Capture( GameObject victim )
	{
		if ( !Networking.IsHost || CapturedTarget == victim )
			return;

		var previous = CapturedTarget.IsValid() ? CapturedTarget.GetComponent<BaseNpc>() : null;
		if ( previous.IsValid() )
			previous.StopCarry( GameObject );

		CapturedTarget = victim;
		var next = victim.IsValid() ? victim.GetComponent<BaseNpc>() : null;
		if ( next.IsValid() )
			next.StartCarry( GameObject );
	}

	protected override void OnDisabled()
	{
		ReleaseBody();
		Capture( null );
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		ReleaseBody();
		Capture( null );
		_animation.Dispose();
		base.OnDestroy();
	}
}
