using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>npc_barnacle: hangs from the ceiling, catches whatever touches its tongue, hauls it up and bites.</summary>
[Title( "HL2 Barnacle NPC" )]
[Category( "Half-Life 2" )]
public sealed class Hl2BarnacleNpc : BaseNpc
{
	[Property] public float AttackRange { get; set; } = 600;
	[Property] public float AttackDamage { get; set; } = 15;
	[Property] public float AttackInterval { get; set; } = 1;
	[Sync( SyncFlags.FromHost )] public GameObject CapturedTarget { get; private set; }
	[Sync( SyncFlags.FromHost )] int BiteSerial { get; set; }
	readonly SourceAnimationPlayer _animation = new();
	int _shownBite;
	string _idle, _bite;
	(string, bool)[] _choices = [];
	Model _model;
	TimeSince _lastBite = 10;

	public override string Faction => Factions.Monster;
	protected override void SetupRelationships()
	{
		SetRelationship( Factions.Player, Disposition.Hostile );
		SetRelationship( Factions.Ally, Disposition.Hostile );
		SetRelationship( Factions.Citizen, Disposition.Hostile );
		SetRelationship( Factions.Enemy, Disposition.Hostile );
	}

	protected override void OnStart()
	{
		base.OnStart();
		if ( IsProxy )
			_shownBite = BiteSerial;

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
			GetOrAddComponent<Hl2BarnacleTongue>().Npc = this;
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
			var sequences = SourceModelSequences.Of( _model );
			_idle = sequences.FindActivity( "ACT_IDLE", "idle01" );
			_bite = sequences.FindActivity( "ACT_BARNACLE_BITE_PLAYER", _idle );
			_choices = [(_idle, true), (_bite, false)];
		}

		if ( !_animation.Ensure( Renderer, _choices, _idle ) )
			return;

		if ( Networking.IsHost )
			HostThink();

		if ( BiteSerial != _shownBite )
		{
			_shownBite = BiteSerial;
			_animation.Play( _bite, restart: true );
		}
		else if ( _animation.Current != _bite || _animation.Finished )
		{
			_animation.Play( _idle );
		}

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
		var end = SourceNpcs.CapturePoint( victim );
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
			.Concat( Scene.GetAllComponents<BaseNpc>().Where( x => x != this ).Select( x => x.GameObject ) )
			.Where( x => (x.WorldPosition - mouth).WithZ( 0 ).Length < 35 && GetDisposition( x ) == Disposition.Hostile );
	}

	void HostThink()
	{
		if ( !NpcConVars.Enabled )
		{
			if ( CapturedTarget.IsValid() )
				Capture( null );
			return;
		}

		// A barnacle catches by tongue contact, independent of its eye direction.
		if ( !Reachable( CapturedTarget ) )
			Capture( Prey().FirstOrDefault( Reachable ) );

		if ( !CapturedTarget.IsValid() )
			return;

		var mouth = Mouth;
		var body = CapturedTarget.GetComponentInChildren<Rigidbody>();
		if ( body.IsValid() && !SourceNpcs.IsPlayer( CapturedTarget ) )
		{
			body.MotionEnabled = true;
			body.Velocity = ((mouth - Vector3.Up * 35 - SourceNpcs.CapturePoint( CapturedTarget )) * 4).ClampLength( 150 );
		}

		if ( mouth.Distance( SourceNpcs.CapturePoint( CapturedTarget ) ) > 60 || _lastBite < AttackInterval )
			return;

		_lastBite = 0;
		BiteSerial++;
		var damage = new DamageInfo { Damage = AttackDamage, Attacker = GameObject, Weapon = GameObject, Origin = mouth, Position = SourceNpcs.CapturePoint( CapturedTarget ) };
		damage.Tags.Add( "melee" );
		CapturedTarget.GetComponentInParent<Component.IDamageable>()?.OnDamage( damage );
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( !CapturedTarget.IsValid() )
			return;

		var controller = CapturedTarget.GetComponent<PlayerController>();
		// Player movement is owner simulated; only the owner applies the host's capture.
		if ( !controller.IsValid() || controller.IsProxy )
			return;

		controller.PreventGrounding( 0.1f );
		controller.WishVelocity = Vector3.Zero;
		controller.Body.Velocity = ((Mouth - Vector3.Up * 35 - SourceNpcs.CapturePoint( CapturedTarget )) * 4).ClampLength( 150 );
	}

	void Capture( GameObject victim )
	{
		if ( !Networking.IsHost )
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
		Capture( null );
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		Capture( null );
		_animation.Dispose();
		base.OnDestroy();
	}
}
