using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Schedules;
using Sandbox.Npcs.Tasks;

namespace Source1;

/// <summary>
/// npc_rollermine: a physics ball that rolls at its target, opens its spikes while hunting, leaps the last stretch and
/// shocks what it hits, then explodes when destroyed. Sounds are the game's NPC_RollerMine sound scripts. It has no
/// movement layer: one that walks on the ground would hold its body still whenever it touches the floor.
/// </summary>
[Title( "HL2 Rollermine" )]
[Category( "Half-Life 2" )]
public sealed class Hl2Rollermine : BaseNpc, Component.ICollisionListener
{
	/// <summary>Force applied each frame while rolling toward a target.</summary>
	[Property, Group( "Balance" )] public float RollForce { get; set; } = 40000f;

	/// <summary>Torque applied each frame to spin it the way it rolls.</summary>
	[Property, Group( "Balance" )] public float RollTorque { get; set; } = 20000f;

	/// <summary>Upward impulse when it's stuck.</summary>
	[Property, Group( "Balance" )] public float StuckJumpForce { get; set; } = 500f;

	/// <summary>Impulse of the leap at the target.</summary>
	[Property, Group( "Balance" )] public float LeapForce { get; set; } = 202500f;

	/// <summary>Upward part of the leap direction: 0 is flat, 1 is 45 degrees.</summary>
	[Property, Group( "Balance" )] public float LeapUpwardBias { get; set; } = 0.8f;

	/// <summary>Impulse it bounces off a victim with.</summary>
	[Property, Group( "Balance" )] public float BounceForce { get; set; } = 303750f;

	/// <summary>Damage to anything damageable it hits.</summary>
	[Property, Group( "Balance" )] public float ContactDamage { get; set; } = 20f;

	/// <summary>Distance at which it stops rolling and leaps.</summary>
	[Property, Group( "Balance" )] public float LeapRange { get; set; } = 160f;

	[Property, Group( "Explosion" )] public float BlastRadius { get; set; } = 128f;
	[Property, Group( "Explosion" )] public float BlastDamage { get; set; } = 25f;

	/// <summary>Speed at which the roll sound reaches its highest pitch.</summary>
	[Property, Group( "Sound" )] public float PitchSpeedMax { get; set; } = 600f;
	[Property, Group( "Sound" )] public float PitchMin { get; set; } = 0.6f;
	[Property, Group( "Sound" )] public float PitchMax { get; set; } = 1.6f;

	public Rigidbody Rigidbody { get; private set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnHuntingChanged ) )] public bool IsHunting { get; private set; }

	const float BounceCooldown = 0.25f;
	TimeSince _lastBounce;
	SphereCollider _collider;
	float _baseRadius;
	string _mount;
	SoundHandle _rollSound;
	SoundEvent _roll, _rollSpikes, _openSpikes, _closeSpikes, _shock, _taunt;
	Model _closed, _spikes;
	bool _exploded;

	// A ball has no steady forward, so it sees all round. It hunts players and their friends.
	public override string Faction => Factions.Monster;

	protected override void SetupRelationships()
	{
		SetRelationship( Factions.Player, Disposition.Hostile );
		SetRelationship( Factions.Ally, Disposition.Hostile );
		SetRelationship( Factions.Citizen, Disposition.Hostile );
	}

	protected override async void OnStart()
	{
		base.OnStart();
		Senses.FieldOfView = 360f;
		Rigidbody = GetComponent<Rigidbody>();
		_collider = GetComponent<SphereCollider>();
		if ( _collider.IsValid() ) _baseRadius = _collider.Radius;
		if ( Rigidbody.IsValid() ) Rigidbody.MotionEnabled = true;

		try
		{
			_mount = SourceMount.Of( GameObject );
			await Sandbox.Mounting.Directory.Mount( _mount );
			if ( !IsValid ) return;
			_roll = SourceSounds.Sound( "NPC_RollerMine.Roll", _mount );
			_rollSpikes = SourceSounds.Sound( "NPC_RollerMine.RollWithSpikes", _mount );
			_openSpikes = SourceSounds.Sound( "NPC_RollerMine.OpenSpikes", _mount );
			_closeSpikes = SourceSounds.Sound( "NPC_RollerMine.CloseSpikes", _mount );
			_shock = SourceSounds.Sound( "NPC_RollerMine.Shock", _mount );
			_taunt = SourceSounds.Sound( "NPC_RollerMine.Taunt", _mount );
			var spikes = $"mount://{_mount}/models/roller_spikes.mdl.vmdl";
			if ( Sandbox.Mounting.Directory.Get( _mount )?.GetByPath( spikes ) is not null )
				_spikes = await Model.LoadAsync( spikes );
			if ( !IsValid ) return;
			PlayRollSound();
		}
		catch ( Exception e )
		{
			Log.Warning( $"Mounted rollermine: {e.Message}" );
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		UpdateRollSound();
	}

	protected override void OnDestroy()
	{
		StopRollSound();
		base.OnDestroy();
	}

	protected override BaseSchedule SelectSchedule() => Senses.FindBestTarget().IsValid()
		? GetSchedule<Hl2RollermineChaseSchedule>()
		: GetSchedule<Hl2RollermineIdleSchedule>();

	/// <summary>Host: open the spikes and grow to them while hunting.</summary>
	internal void SetHunting( bool hunting )
	{
		if ( IsHunting == hunting ) return;
		IsHunting = hunting;

		if ( _collider.IsValid() )
			_collider.Radius = hunting ? _baseRadius * 1.4f : _baseRadius;

		if ( hunting && Rigidbody.IsValid() )
			Rigidbody.ApplyImpulse( Vector3.Up * 50000f );
	}

	void OnHuntingChanged( bool before, bool hunting )
	{
		if ( Renderer.IsValid() && Renderer.Model.IsValid() && _spikes.IsValid() )
		{
			if ( hunting && Renderer.Model != _spikes ) _closed = Renderer.Model;
			var model = hunting ? _spikes : _closed;
			if ( model.IsValid() ) Renderer.Model = model;
		}

		if ( (hunting ? _openSpikes : _closeSpikes) is { } spikes ) GameObject.PlaySound( spikes );
		StopRollSound();
		PlayRollSound();
	}

	[Rpc.Broadcast]
	public void BroadcastLeap()
	{
		if ( _taunt is not null ) GameObject.PlaySound( _taunt );
	}

	[Rpc.Broadcast]
	void BroadcastShock( Vector3 position )
	{
		if ( _shock is not null ) Sound.Play( _shock, position );
	}

	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( IsProxy || !Rigidbody.IsValid() || _lastBounce < BounceCooldown ) return;

		var root = collision.Other.GameObject?.Root;
		if ( !root.IsValid() || root == GameObject.Root ) return;

		if ( !root.Components.TryGet( out IDamageable damageable ) ) return;

		_lastBounce = 0f;
		var damage = new DamageInfo { Damage = ContactDamage, Attacker = GameObject, Weapon = GameObject, Origin = WorldPosition, Position = collision.Contact.Point };
		damage.Tags.Add( "shock" );
		damageable.OnDamage( damage );
		BroadcastShock( collision.Contact.Point );

		// Up and away from the victim rather than a plain reflection
		var away = (WorldPosition - root.WorldPosition).WithZ( 0 );
		if ( away.LengthSquared < 0.01f ) away = WorldRotation.Backward.WithZ( 0 );
		Rigidbody.Velocity = Vector3.Zero;
		Rigidbody.ApplyImpulse( (away.Normal + Vector3.Up * 2f).Normal * BounceForce );
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		if ( _exploded ) return;
		_exploded = true;
		EmitStimulus( StimulusKinds.Death, radius: 1024f, lifetime: 2f );
		foreach ( var collider in GetComponentsInChildren<Collider>() )
			collider.Enabled = false;

		new SourceExplosion
		{
			Source = GameObject,
			Attacker = GameObject,
			Position = WorldPosition,
			Rotation = Rotation.Identity,
			Radius = BlastRadius,
			Damage = BlastDamage,
			Prefab = SourceMount.Prefab( SourceMount.Of( GameObject ), "explosion" )
		}.Spawn();
		GameObject.Destroy();
	}

	void PlayRollSound()
	{
		var sound = IsHunting ? _rollSpikes ?? _roll : _roll;
		if ( sound is null || (_rollSound.IsValid() && !_rollSound.IsStopped) ) return;
		_rollSound = GameObject.PlaySound( sound );
		if ( _rollSound.IsValid() ) _rollSound.Pitch = PitchMin;
	}

	void StopRollSound()
	{
		if ( _rollSound.IsValid() ) _rollSound.Stop();
		_rollSound = default;
	}

	void UpdateRollSound()
	{
		if ( !_rollSound.IsValid() || _rollSound.IsStopped || !Rigidbody.IsValid() ) return;
		var t = MathX.Clamp( Rigidbody.Velocity.Length / PitchSpeedMax, 0f, 1f );
		_rollSound.Pitch = MathX.Lerp( PitchMin, PitchMax, t );
	}
}

/// <summary>Rolls at the target, then leaps at it. Ends after the leap and is picked again while there is a target.</summary>
public sealed class Hl2RollermineChaseSchedule : BaseSchedule
{
	public override int Priority => SchedulePriorities.Combat;

	protected override void OnStart()
	{
		(Npc as Hl2Rollermine)?.SetHunting( true );
		AddTask( new Hl2RollermineRollTask() );
		AddTask( new Hl2RollermineLeapTask() );
	}

	protected override void OnEnd() => (Npc as Hl2Rollermine)?.SetHunting( false );

	protected override void OnCancelled() => (Npc as Hl2Rollermine)?.SetHunting( false );

	protected override bool ShouldCancel() => !Npc.Senses.FindBestTarget().IsValid();
}

/// <summary>Waits a moment with nothing in sight, then looks again.</summary>
public sealed class Hl2RollermineIdleSchedule : BaseSchedule
{
	protected override void OnStart() => AddTask( new WaitTask( Game.Random.Float( 1f, 2.5f ) ) );
}

/// <summary>Rolls toward the target until within leap range, hopping when stuck. Fails when the target is lost.</summary>
public sealed class Hl2RollermineRollTask : BaseTask
{
	const float StuckSpeed = 40f;
	const float StuckTime = 1.2f;
	TimeSince _stuck;
	bool _stuckRunning;

	protected override TaskResult OnUpdate()
	{
		if ( Npc is not Hl2Rollermine mine || !mine.Rigidbody.IsValid() ) return TaskResult.Failed;
		var target = Npc.Senses.FindBestTarget();
		if ( !target.IsValid() ) return TaskResult.Failed;

		var flat = (target.WorldPosition - Npc.WorldPosition).WithZ( 0 );
		if ( flat.Length <= mine.LeapRange ) return TaskResult.Success;

		var dir = flat.Normal;
		var body = mine.Rigidbody;
		body.ApplyForce( dir * mine.RollForce );
		body.ApplyTorque( new Vector3( -dir.y, dir.x, 0f ) * mine.RollTorque );

		if ( body.Velocity.WithZ( 0 ).Length < StuckSpeed )
		{
			if ( !_stuckRunning ) { _stuck = 0f; _stuckRunning = true; }
			else if ( _stuck > StuckTime ) { body.ApplyImpulse( Vector3.Up * mine.StuckJumpForce ); _stuckRunning = false; }
		}
		else
		{
			_stuckRunning = false;
		}

		return TaskResult.Running;
	}

	protected override void Reset() => _stuckRunning = false;
}

/// <summary>Leaps at the target from a standstill, then waits before rolling again.</summary>
public sealed class Hl2RollermineLeapTask : BaseTask
{
	const float Cooldown = 1.2f;
	TimeUntil _cooldown;

	protected override void OnStart()
	{
		_cooldown = Cooldown;
		if ( Npc is not Hl2Rollermine mine || !mine.Rigidbody.IsValid() ) return;
		var target = Npc.Senses.FindBestTarget();
		if ( !target.IsValid() ) return;

		var dir = (target.WorldPosition - Npc.WorldPosition).Normal;
		mine.Rigidbody.Velocity = Vector3.Zero;
		mine.Rigidbody.AngularVelocity = Vector3.Zero;
		mine.Rigidbody.ApplyImpulse( (dir + Vector3.Up * mine.LeapUpwardBias).Normal * mine.LeapForce );
		mine.BroadcastLeap();
	}

	protected override TaskResult OnUpdate() => _cooldown ? TaskResult.Success : TaskResult.Running;
}
