using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>Proximity mine gameplay using the mount's model, physics and sound scripts.</summary>
[Title( "HL2 Hopper Mine" )]
[Category( "Half-Life 2" )]
public sealed class Hl2HopperMine : BaseNpc, Component.ICollisionListener
{
	public enum MineState { Settling, Armed, Held, Hopping, Spent }
	[Property] public float TriggerRadius { get; set; } = 100;
	[Property] public float BlastRadius { get; set; } = 125;
	[Property] public float BlastDamage { get; set; } = 150;
	[Sync( SyncFlags.FromHost )] public MineState State { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool PlayerPlaced { get; private set; }
	TimeSince _phaseTime;
	Rigidbody _body;
	MineState? _presented;
	readonly Dictionary<string, SoundEvent> _sounds = new();
	readonly SourceAnimationPlayer _animation = new();
	Model _model;
	(string, bool)[] _choices = [];
	SourceModelSequenceData _hookSequence;
	float _hookPosition;
	public override string Faction => PlayerPlaced ? Factions.Ally : Factions.Enemy;
	protected override void SetupRelationships()
	{
		SetRelationship( Factions.Player, Disposition.Hostile );
		SetRelationship( Factions.Ally, Disposition.Hostile );
		SetRelationship( Factions.Citizen, Disposition.Hostile );
		SetRelationship( Factions.Monster, Disposition.Hostile );
	}

	public override Relationship GetRelationship( GameObject other )
	{
		if ( !PlayerPlaced )
			return base.GetRelationship( other );

		var faction = other?.GetComponent<BaseNpc>()?.Faction;
		return new Relationship( faction is Factions.Enemy or Factions.Monster ? Disposition.Hostile : Disposition.Friendly );
	}

	protected override async void OnStart()
	{
		base.OnStart();
		_body = GetComponent<Rigidbody>();
		_phaseTime = 0;
		try
		{
			var mount = SourceMount.Of( GameObject );
			await Sandbox.Mounting.Directory.Mount( mount );
			if ( !IsValid )
				return;

			foreach ( var name in new[] { "Hop", "TurnOn", "TurnOff" } )
				_sounds[name] = SourceSounds.Sound( "NPC_CombineMine." + name, mount );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Mounted mine sounds: {e.Message}" );
		}
	}

	protected override void OnUpdate()
	{
		if ( !Renderer.IsValid() || !Renderer.Model.IsValid() || !_body.IsValid() )
			return;

		if ( _model != Renderer.Model )
		{
			_model = Renderer.Model;
			var sequences = SourceModelSequences.Of( _model );
			var idle = sequences.All.FirstOrDefault( s => s.Activity == "ACT_IDLE" )?.Name;
			_hookSequence = sequences.Get( idle ) is { BlendPositions.Length: > 1 } hooks ? hooks : null;
			_choices = [(idle, false)];
		}

		if ( _animation.Ensure( Renderer, _choices ) )
		{
			if ( _hookSequence is not null )
			{
				var target = State == MineState.Armed ? _hookSequence.BlendPositions.Min() : _hookSequence.BlendPositions.Max();
				_hookPosition = MathX.LerpTo( _hookPosition, target, Time.Delta * 8 );
				_animation.SetPoseParameter( _hookSequence.BlendParameter, _hookPosition );
			}
			_animation.Tick();
		}

		if ( Networking.IsHost )
			HostThink();

		if ( State == _presented )
			return;

		_presented = State;
		var sound = State switch { MineState.Armed => "TurnOn", MineState.Held => "TurnOff", MineState.Hopping => "Hop", _ => null };
		if ( sound is not null && _sounds.GetValueOrDefault( sound ) is { } effect )
			Sound.Play( effect, WorldPosition );
	}

	void ChangeState( MineState state )
	{
		if ( State == state )
			return;

		State = state;
		_phaseTime = 0;
	}

	void HostThink()
	{
		if ( State == MineState.Spent )
			return;

		if ( PhysicsCarrier.IsValid() )
		{
			ChangeState( MineState.Held );
			return;
		}

		if ( State == MineState.Held )
		{
			PlayerPlaced = true;
			ChangeState( MineState.Settling );
		}

		if ( State == MineState.Settling )
		{
			Settle();
			return;
		}

		if ( State == MineState.Armed )
		{
			if ( _body.Velocity.Length > 50 )
			{
				ChangeState( MineState.Settling );
				return;
			}

			var target = Senses?.FindBestTarget();
			if ( target.IsValid() && WorldPosition.Distance( target.WorldPosition ) <= TriggerRadius )
				Hop();
		}

		// A fallback fuse handles sleeping contacts or a mine launched out of the map.
		if ( State == MineState.Hopping && _phaseTime > 3 )
			Detonate();
	}

	/// <summary>Arms once it has rested upright on the floor for a moment.</summary>
	void Settle()
	{
		var floor = Scene.Trace.Ray( WorldPosition + Vector3.Up * 2, WorldPosition - Vector3.Up * 32 ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		if ( _body.Velocity.Length > 15 || WorldRotation.Up.Dot( Vector3.Up ) < 0.6f || !floor.Hit )
		{
			_phaseTime = 0;
			return;
		}

		if ( _phaseTime > 0.7f )
			ChangeState( MineState.Armed );
	}

	void Hop()
	{
		ChangeState( MineState.Hopping );
		var center = _body.PhysicsBody.IsValid() ? _body.PhysicsBody.MassCenter : WorldPosition + Vector3.Up * 12;
		var ceiling = Scene.Trace.Ray( center, center + Vector3.Up * 200 ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		var height = ceiling.Hit ? Math.Clamp( ceiling.Distance - 24, 24, 200 ) : 200;
		_body.MotionEnabled = true;
		_body.Velocity = Vector3.Up * MathF.Sqrt( 2 * Math.Max( 1, Scene.PhysicsWorld.Gravity.Length ) * height );
	}

	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( !Networking.IsHost || State != MineState.Hopping || PhysicsCarrier.IsValid() || _phaseTime < 0.1f )
			return;

		if ( _phaseTime < 0.5f && collision.Other.GameObject?.GetComponentInParent<Component.IDamageable>() is null )
			return;

		Detonate();
	}

	void Detonate()
	{
		if ( !Networking.IsHost || State == MineState.Spent )
			return;

		ChangeState( MineState.Spent );
		foreach ( var collider in GetComponentsInChildren<Collider>() )
			collider.Enabled = false;

		// The authored origin sits on the floor. Use the body center so the ground
		// cannot occlude every blast ray when contact leaves the origin slightly buried.
		var center = _body.IsValid() && _body.PhysicsBody.IsValid() ? _body.PhysicsBody.MassCenter : WorldPosition + Vector3.Up * 12;
		new SourceExplosion
		{
			Source = GameObject,
			Attacker = GameObject,
			Position = center,
			Rotation = Rotation.Identity,
			Radius = BlastRadius,
			Damage = BlastDamage,
			ForceScale = 1.5f,
			Prefab = SourceMount.Prefab( SourceMount.Of( GameObject ), "explosion" )
		}.Spawn();
		GameObject.Destroy();
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent death ) => Detonate();

	protected override void OnDestroy()
	{
		_animation.Dispose();
		base.OnDestroy();
	}
}

/// <summary>
/// Spawns a prefab beneath the NPC once, when an enemy comes within range: the shield scanner's carried mine, and an
/// approximation of the dropship's soldier drop (no container or landing).
/// </summary>
[Title( "HL2 Deploy Prefab" )]
[Category( "Half-Life 2" )]
public sealed class Hl2DeployPrefab : Component
{
	/// <summary>The generated prefab for the NPC's mount, such as hopper_mine.</summary>
	[Property] public string Prefab { get; set; }

	/// <summary>Horizontal distance to the enemy that triggers the drop.</summary>
	[Property] public float Range { get; set; } = 240;
	[Property] public int Count { get; set; } = 1;

	/// <summary>Side by side distance between the spawned copies.</summary>
	[Property] public float Spacing { get; set; } = 45;
	[Sync( SyncFlags.FromHost )] public bool Deployed { get; private set; }

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || Deployed || !NpcConVars.Enabled )
			return;

		var npc = GetComponent<BaseNpc>();
		if ( !npc.IsValid() || npc.PhysicsCarrier.IsValid() )
			return;

		var target = npc.Senses?.FindBestTarget();
		if ( !target.IsValid() || (target.WorldPosition - WorldPosition).WithZ( 0 ).Length > Range )
			return;

		if ( SourceMount.Prefab( SourceMount.Of( GameObject ), Prefab ) is not { } prefab )
			return;

		Deployed = true;
		var bounds = npc.Renderer.IsValid() ? npc.Renderer.Bounds : GameObject.GetBounds();
		var bottom = new Vector3( bounds.Center.x, bounds.Center.y, bounds.Mins.z );
		var ground = Scene.Trace.Ray( bottom, bottom - Vector3.Up * 4000 ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		var floor = ground.Hit ? ground.EndPosition : bottom - Vector3.Up * 28;
		for ( var i = 0; i < Count; i++ )
			GameObject.GetPrefab( prefab.ResourcePath )?.Clone( floor + WorldRotation.Right * ((i - (Count - 1) * 0.5f) * Spacing) )?.NetworkSpawn();
	}
}
