using System;
using Sandbox.Npcs;

namespace Magicka;

/// <summary>
/// A Magicka creature or wizard on the engine's <see cref="BaseNpc"/>: idles, pursues, attacks
/// with clips from its paired skeleton, and leaves a timed dying corpse on death.
/// </summary>
[Title( "Magicka NPC" )]
[Category( "Magicka" )]
public sealed class MagickaNpc : BaseNpc, Component.IPressable
{
	[Property] public string CharacterName { get; set; } = "Magicka NPC";
	[Property] public float StartingHealth { get; set; } = 100f;
	[Property] public float MoveSpeed { get; set; } = 110f;
	[Property] public float AttackDamage { get; set; } = 15f;
	[Property] public float AttackDelay { get; set; } = 0.35f;
	[Property] public float AttackHeight { get; set; } = 32f;
	[Property] public float CorpseSeconds { get; set; } = 10f;
	[Property] public bool Friendly { get; set; }
	[Property] public bool Passive { get; set; }

	[Property] public bool HasMelee { get; set; } = true;
	[Property] public float MeleeRange { get; set; } = 84f;
	[Property] public float MeleeInterval { get; set; } = 1.3f;
	[Property] public bool HasRanged { get; set; }
	[Property] public float RangedMinRange { get; set; } = 0f;
	[Property] public float RangedMaxRange { get; set; } = 500f;
	[Property] public float RangedInterval { get; set; } = 2.0f;
	[Property] public bool CanSelfHeal { get; set; }
	[Property] public float HealCooldown { get; set; } = 8f;

	[Property] public string IdleClip { get; set; }
	[Property] public string WalkClip { get; set; }
	[Property] public string RunClip { get; set; }
	[Property] public string DeathClip { get; set; }
	[Property] public string HitClip { get; set; }
	[Property] public string[] AttackClips { get; set; } = [];
	[Property] public string[] MeleeClips { get; set; } = [];
	[Property] public string[] RangedClips { get; set; } = [];
	[Property] public string[] HealClips { get; set; } = [];
	[Property] public string MissileModel { get; set; }
	[Property] public Color MissileColor { get; set; } = Color.White;

	[Property] public string[] AlertSounds { get; set; } = [];
	[Property] public string[] AttackSounds { get; set; } = [];
	[Property] public string[] PainSounds { get; set; } = [];
	[Property] public string[] DeathSounds { get; set; } = [];
	[Property] public string[] VoiceSounds { get; set; } = [];

	[Sync( SyncFlags.FromHost )] private TimeSince LastAttack { get; set; } = 10;
	[Sync( SyncFlags.FromHost )] private int AttackSerial { get; set; }
	[Sync( SyncFlags.FromHost )] private string AttackClip { get; set; }
	[Sync( SyncFlags.FromHost )] private TimeSince LastHit { get; set; } = 10;
	[Sync( SyncFlags.FromHost )] private int HitSerial { get; set; }
	[Sync( SyncFlags.FromHost )] public GameObject Leader { get; set; }

	private int _observedAttack;
	private int _observedHit;
	private Vector3 _home;
	private Vector3 _lastPosition;
	private float _measuredSpeed;
	private TimeSince _lastPath;
	private bool _wasHeld;
	private GameObject _strikeTarget;
	private bool _strikePending;
	private float _attackRecovery;
	private bool _rangedStrike;
	private string _currentClip;
	private GameObject _lastTarget;
	private TimeSince _lastPainSound = 10;
	private TimeSince _lastAlertSound = 10;
	private TimeSince _lastHeal = 10;
	private float _currentCooldown = 1.3f;
	private SoundEvent _alertEvent, _attackEvent, _painEvent, _deathEvent, _voiceEvent;

	// Idle wander, investigation, and passive fear state.
	private Vector3? _wanderTarget;
	private TimeSince _wanderTimer = 0;
	private float _nextWanderDelay = 2f;
	private Vector3? _investigatePoint;
	private TimeSince _investigateTimer = 10;
	private GameObject _fleeSource;
	private TimeSince _timeSinceScared = 10;

	public override string Faction => Passive ? Factions.Citizen : Friendly ? Factions.Ally : Factions.Monster;

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e )
	{
		if ( !Friendly && !Passive )
			return null;

		return Leader.IsValid()
			? new IPressable.Tooltip( "#npc.action.stop_following", "person_off", null )
			: new IPressable.Tooltip( "#npc.action.follow", "follow_the_signs", null );
	}

	bool IPressable.CanPress( IPressable.Event e ) => Friendly || Passive;

	bool IPressable.Press( IPressable.Event e )
	{
		ToggleFollow( e.Source.GameObject );
		return true;
	}

	[Rpc.Host]
	private void ToggleFollow( GameObject presserObject )
	{
		if ( (!Friendly && !Passive) || !presserObject.IsValid() )
			return;

		var player = presserObject.Root.GetComponent<PlayerController>();
		if ( !player.IsValid() )
			return;

		if ( Rpc.Caller is not null && player.Network.Owner != Rpc.Caller )
			return;

		Leader = Leader == player.GameObject ? null : player.GameObject;
		_wanderTarget = null;
		_investigatePoint = null;

		var toPlayer = (player.WorldPosition - WorldPosition).WithZ( 0 );
		if ( toPlayer.LengthSquared > 0.01f )
			WorldRotation = Rotation.LookAt( toPlayer );

		PlayNpcSound( 4 );
	}

	protected override void SetupRelationships()
	{
		if ( Passive )
		{
			SetRelationship( Factions.Enemy, Disposition.Fearful );
			SetRelationship( Factions.Monster, Disposition.Fearful );
			return;
		}

		if ( Friendly )
		{
			SetRelationship( Factions.Enemy, Disposition.Hostile );
			SetRelationship( Factions.Monster, Disposition.Hostile );
			return;
		}

		SetRelationship( Factions.Player, Disposition.Hostile );
		SetRelationship( Factions.Ally, Disposition.Hostile );
		SetRelationship( Factions.Citizen, Disposition.Hostile );
		SetRelationship( Factions.Enemy, Disposition.Hostile );
	}

	protected override void OnStart()
	{
		base.OnStart();

		DisplayName = CharacterName;
		MaxHealth = StartingHealth;
		Health = MaxHealth;
		_home = WorldPosition;
		_lastPosition = WorldPosition;
		_nextWanderDelay = Game.Random.Float( 1.5f, 4f );

		if ( !Renderer.IsValid() )
			Renderer = Components.GetInDescendantsOrSelf<SkinnedModelRenderer>();

		if ( IsProxy )
			_observedAttack = AttackSerial;

		PlayClip( IdleClip, loop: true );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( !Renderer.IsValid() || !Renderer.Model.IsValid() )
			return;

		UpdateHeld();
		if ( Networking.IsHost && !PhysicsCarrier.IsValid() )
			HostThink();

		UpdateAnimation();
	}

	private void UpdateHeld()
	{
		var held = PhysicsCarrier.IsValid();
		if ( held == _wasHeld )
			return;

		_wasHeld = held;
		Movement?.Stop();
	}

	private void HostThink()
	{
		if ( !NpcConVars.Enabled )
		{
			Movement?.Stop();
			_strikePending = false;
			_strikeTarget = null;
			return;
		}

		if ( _strikePending && LastAttack >= AttackDelay )
		{
			_strikePending = false;
			if ( _strikeTarget.IsValid() )
			{
				if ( _rangedStrike )
					LaunchMissile( _strikeTarget );
				else
					Strike( _strikeTarget );
			}
			_strikeTarget = null;
		}

		// Passive NPCs flee from visible fearful threats or recent attackers.
		if ( Passive )
		{
			var threat = Senses?.FindNearestVisible( Disposition.Fearful );
			if ( threat.IsValid() )
			{
				_fleeSource = threat;
				_timeSinceScared = 0;
			}

			if ( _fleeSource.IsValid() && _timeSinceScared < 6f )
			{
				if ( !IsAirborne && _lastPath > 0.35f )
				{
					var away = (WorldPosition - _fleeSource.WorldPosition).WithZ( 0 );
					away = away.LengthSquared > 0.01f ? away.Normal : Vector3.Random.WithZ( 0 ).Normal;
					var fleeGoal = WorldPosition + away * 320f;
					Movement?.MoveTo( fleeGoal, 24f, MoveSpeed );
					_lastPath = 0;
				}
				return;
			}
			_fleeSource = null;
		}

		var target = Passive ? null : Senses?.FindBestTarget();
		if ( target.IsValid() && !_lastTarget.IsValid() && _lastAlertSound > 5f )
		{
			_lastAlertSound = 0;
			PlayNpcSound( 0 );
		}
		_lastTarget = target;

		// No combat target: follow leader, investigate disturbances, or wander naturally.
		if ( !target.IsValid() )
		{
			HostIdleThink();
			return;
		}

		_wanderTarget = null;
		_investigatePoint = null;

		// Self-heal when wounded if the character has a Life self-spell.
		if ( CanSelfHeal && !_strikePending && Health > 0 && Health < MaxHealth * 0.6f
			&& _lastHeal >= HealCooldown && LastAttack >= _attackRecovery )
		{
			_lastHeal = 0;
			Health = MathF.Min( MaxHealth, Health + MaxHealth * 0.25f );
			var healClip = PickClip( HealClips, RangedClips, AttackClips );
			_attackRecovery = 0.6f;
			_currentCooldown = 1.0f;
			LastAttack = 0;
			AttackClip = healClip;
			AttackSerial++;
			Movement?.Stop();
			PlayNpcSound( 1 );
			return;
		}

		var goal = target.IsValid() ? target.WorldPosition : _home;
		var distance = WorldPosition.Distance( goal );
		var attacking = LastAttack < _attackRecovery;

		var canMeleeNow = target.IsValid() && HasMelee && distance <= MeleeRange;
		var canRangedNow = target.IsValid() && HasRanged && !string.IsNullOrEmpty( MissileModel )
			&& distance <= RangedMaxRange
			&& (distance >= RangedMinRange || !HasMelee)
			&& HasLineOfSight( target );

		// Preferred stop distance: close in to melee reach, or hold comfortable ranged distance with LOS.
		var inPosition = target.IsValid()
			? (canMeleeNow || (canRangedNow && distance <= RangedMaxRange * 0.85f))
			: distance <= 24f;

		if ( !IsAirborne )
		{
			if ( attacking || _strikePending || inPosition )
			{
				if ( Movement?.MoveTarget is not null )
					Movement.Stop();

				if ( target.IsValid() )
				{
					var toFace = (target.WorldPosition - WorldPosition).WithZ( 0 );
					if ( toFace.LengthSquared > 0.01f )
						WorldRotation = Rotation.LookAt( toFace );
				}
			}
			else if ( _lastPath > 0.3f )
			{
				var desiredStop = target.IsValid()
					? (HasRanged && !HasMelee ? RangedMaxRange * 0.7f : MeleeRange * 0.75f)
					: 24f;
				Movement?.MoveTo( goal, desiredStop, target.IsValid() ? MoveSpeed : MoveSpeed * 0.65f, target );
				_lastPath = 0;
			}
		}

		if ( !target.IsValid() || Passive || _strikePending || LastAttack < MathF.Max( _currentCooldown, _attackRecovery ) )
			return;

		if ( !canMeleeNow && !canRangedNow )
			return;

		var toTarget = (target.WorldPosition - WorldPosition).WithZ( 0 );
		if ( toTarget.LengthSquared > 0.01f )
			WorldRotation = Rotation.LookAt( toTarget );

		// Prefer melee when right in melee range; otherwise fire ranged/spell.
		_rangedStrike = !canMeleeNow && canRangedNow;
		var interval = _rangedStrike ? RangedInterval : MeleeInterval;
		var clip = _rangedStrike
			? PickClip( RangedClips, AttackClips, MeleeClips )
			: PickClip( MeleeClips, AttackClips, RangedClips );

		_currentCooldown = interval;
		_attackRecovery = MathF.Min( interval, 0.75f );
		LastAttack = 0;
		AttackClip = clip;
		AttackSerial++;
		_strikeTarget = target;
		_strikePending = true;
		PlayNpcSound( 1 );
	}

	private void HostIdleThink()
	{
		if ( IsAirborne )
			return;

		// 1. Follow player leader if toggled on a friendly/passive NPC.
		if ( Leader.IsValid() )
		{
			var distToLeader = WorldPosition.Distance( Leader.WorldPosition );
			if ( distToLeader > 140f )
			{
				if ( _lastPath > 0.3f )
				{
					var speed = distToLeader > 280f ? MoveSpeed : MoveSpeed * 0.7f;
					Movement?.MoveTo( Leader.WorldPosition, 100f, speed, Leader );
					_lastPath = 0;
				}
			}
			else
			{
				if ( Movement?.MoveTarget is not null )
					Movement.Stop();

				var toLeader = (Leader.WorldPosition - WorldPosition).WithZ( 0 );
				if ( toLeader.LengthSquared > 0.01f )
					WorldRotation = Rotation.Slerp( WorldRotation, Rotation.LookAt( toLeader ), Time.Delta * 4f );
			}
			return;
		}

		// 2. Investigate nearby sounds/disturbances (gunshots, deaths).
		if ( !Passive && Senses?.Disturbance is { } disturbance && disturbance.Position.Distance( WorldPosition ) < 900f )
		{
			_investigatePoint = disturbance.Position;
			_investigateTimer = 0;
			_wanderTarget = null;
		}

		if ( _investigatePoint is { } spot )
		{
			var distToSpot = WorldPosition.Distance( spot );
			if ( distToSpot <= 72f || _investigateTimer > 6f )
			{
				_investigatePoint = null;
				_wanderTimer = 0;
				_nextWanderDelay = Game.Random.Float( 1.5f, 3.5f );
				Movement?.Stop();
			}
			else if ( _lastPath > 0.4f )
			{
				Movement?.MoveTo( spot, 64f, MoveSpeed * 0.75f );
				_lastPath = 0;
			}
			return;
		}

		// 3. Wander around home area with natural pauses.
		if ( _wanderTarget is { } dest )
		{
			var distToDest = WorldPosition.Distance( dest );
			if ( distToDest <= 36f || _wanderTimer > 7f )
			{
				_wanderTarget = null;
				_wanderTimer = 0;
				_nextWanderDelay = Game.Random.Float( 2.5f, 6f );
				Movement?.Stop();
			}
			else if ( _lastPath > 0.5f )
			{
				Movement?.MoveTo( dest, 28f, MoveSpeed * 0.55f );
				_lastPath = 0;
			}
			return;
		}

		if ( _wanderTimer >= _nextWanderDelay )
		{
			var dir = Vector3.Random.WithZ( 0 ).Normal;
			var radius = Game.Random.Float( 140f, 360f );
			var anchor = WorldPosition.Distance( _home ) > 550f ? _home : WorldPosition;
			_wanderTarget = anchor + dir * radius;
			_wanderTimer = 0;
			_lastPath = 1f;

			if ( _lastAlertSound > 12f && Game.Random.Float() < 0.25f )
			{
				_lastAlertSound = 0;
				PlayNpcSound( 0 );
			}
		}
	}

	private static string PickClip( string[] primary, string[] secondary, string[] tertiary )
	{
		if ( primary is { Length: > 0 } ) return Game.Random.FromArray( primary );
		if ( secondary is { Length: > 0 } ) return Game.Random.FromArray( secondary );
		if ( tertiary is { Length: > 0 } ) return Game.Random.FromArray( tertiary );
		return null;
	}

	private bool HasLineOfSight( GameObject target )
	{
		var start = WorldPosition + Vector3.Up * AttackHeight;
		var end = target.GetBounds().Center;
		var tr = Scene.Trace.Ray( start, end )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger" )
			.Run();
		return !tr.Hit || (tr.GameObject.IsValid() && tr.GameObject.Root == target.Root);
	}

	private void LaunchMissile( GameObject target )
	{
		var model = Model.Load( MissileModel );
		var start = WorldPosition + Vector3.Up * AttackHeight;
		var direction = (target.GetBounds().Center - start).Normal;
		MagickaProjectile.Spawn( GameObject, model, start + direction * 12f, direction, AttackDamage, MissileColor );
	}

	private void Strike( GameObject target )
	{
		var start = WorldPosition + Vector3.Up * AttackHeight;
		var end = target.GetBounds().Center;
		if ( start.Distance( end ) > MeleeRange + 40f )
			return;

		var tr = Scene.Trace.Ray( start, end ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
		if ( tr.Hit && tr.GameObject.IsValid() && tr.GameObject.Root != target.Root )
			return;

		var damage = new DamageInfo
		{
			Damage = AttackDamage,
			Attacker = GameObject,
			Weapon = GameObject,
			Origin = start,
			Position = tr.EndPosition,
		};
		damage.Tags.Add( "melee" );
		target.GetComponentInParent<Component.IDamageable>()?.OnDamage( damage );
	}

	private void UpdateAnimation()
	{
		if ( AttackSerial != _observedAttack )
		{
			_observedAttack = AttackSerial;
			if ( !string.IsNullOrEmpty( AttackClip ) )
			{
				PlayClip( AttackClip, loop: false, restart: true );
				return;
			}
		}

		if ( !string.IsNullOrEmpty( AttackClip ) && _currentClip == AttackClip && LastAttack < _attackRecovery )
			return;

		if ( HitSerial != _observedHit )
		{
			_observedHit = HitSerial;
			if ( !string.IsNullOrEmpty( HitClip ) )
			{
				PlayClip( HitClip, loop: false, restart: true );
				return;
			}
		}

		if ( !string.IsNullOrEmpty( HitClip ) && _currentClip == HitClip && LastHit < 0.32f )
			return;

		var speed = MeasureSpeed();
		var moving = Movement?.IsMoving == true || speed > 10f;
		if ( !moving )
		{
			PlayClip( IdleClip, loop: true );
			return;
		}

		var running = !string.IsNullOrEmpty( RunClip ) && RunClip != WalkClip && speed > MoveSpeed * 0.8f;
		PlayClip( running ? RunClip : WalkClip ?? IdleClip, loop: true );
	}

	private void PlayClip( string clip, bool loop, bool restart = false )
	{
		if ( string.IsNullOrEmpty( clip ) || !Renderer.IsValid() )
			return;

		if ( !restart && string.Equals( _currentClip, clip, StringComparison.OrdinalIgnoreCase ) )
			return;

		_currentClip = clip;
		Renderer.Sequence.Name = clip;
		Renderer.Sequence.Looping = loop;
		if ( restart )
			Renderer.Sequence.Time = 0;
	}

	private float MeasureSpeed()
	{
		var moved = (WorldPosition - _lastPosition).WithZ( 0 ).Length;
		_lastPosition = WorldPosition;
		if ( Time.Delta > 0 && moved < 100f )
			_measuredSpeed = MathX.Lerp( _measuredSpeed, moved / Time.Delta, Math.Clamp( Time.Delta * 10f, 0f, 1f ) );

		return _measuredSpeed;
	}

	protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
	{
		var attacker = damage.Instigator;
		if ( attacker.IsValid() )
		{
			if ( Passive )
			{
				_fleeSource = attacker;
				_timeSinceScared = 0;
			}
			else if ( GetDisposition( attacker ) != Disposition.Friendly )
			{
				SetRelationship( attacker, Disposition.Hostile, priority: 10 );
			}
		}

		if ( Health > 0 && _lastPainSound > 0.6f )
		{
			_lastPainSound = 0;
			PlayNpcSound( 2 );
		}

		if ( Health > 0 && !string.IsNullOrEmpty( HitClip ) && LastHit > 0.7f && !_strikePending )
		{
			LastHit = 0;
			HitSerial++;
		}
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		PlayNpcSound( 3 );
		base.OnDeath( death );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void PlayNpcSound( int kind )
	{
		var sound = kind switch
		{
			0 => _alertEvent ??= MagickaSounds.CreateEvent( AlertSounds, 0.85f ),
			1 => _attackEvent ??= MagickaSounds.CreateEvent( AttackSounds, 0.85f ),
			2 => _painEvent ??= MagickaSounds.CreateEvent( PainSounds, 0.85f ),
			3 => _deathEvent ??= MagickaSounds.CreateEvent( DeathSounds, 0.9f ),
			4 => _voiceEvent ??= MagickaSounds.CreateEvent( VoiceSounds.Length > 0 ? VoiceSounds : AlertSounds, 0.85f ),
			_ => null,
		};

		if ( sound is not null )
			Sound.Play( sound, WorldPosition + Vector3.Up * AttackHeight );
	}

	/// <summary>
	/// Plays the character's Magicka death animation first, then transitions seamlessly into
	/// a live <see cref="ModelPhysics"/> ragdoll once the clip finishes.
	/// </summary>
	protected override GameObject CreateRagdoll()
	{
		if ( !Renderer.IsValid() || !Renderer.Model.IsValid() )
			return null;

		var corpse = new GameObject( false, $"{CharacterName} Ragdoll" );
		corpse.Tags.Add( "ragdoll", "removable" );
		corpse.WorldTransform = Renderer.WorldTransform;

		var renderer = corpse.AddComponent<SkinnedModelRenderer>();
		renderer.CopyFrom( Renderer );
		renderer.UseAnimGraph = false;

		var hasRagdoll = Renderer.Model.Physics is { Parts.Count: > 1 };
		var hasDeathClip = !string.IsNullOrEmpty( DeathClip );

		var cleanup = corpse.AddComponent<MagickaCorpse>();
		cleanup.Lifetime = CorpseSeconds;
		cleanup.Renderer = renderer;
		cleanup.RagdollAfterClip = hasRagdoll && hasDeathClip;

		if ( hasRagdoll && !hasDeathClip )
		{
			var physics = corpse.AddComponent<ModelPhysics>();
			physics.Renderer = renderer;
			physics.Model = renderer.Model;
		}

		corpse.Enabled = true;
		if ( hasDeathClip )
		{
			renderer.Sequence.Name = DeathClip;
			renderer.Sequence.Looping = false;
			renderer.Sequence.Time = 0;
		}
		corpse.NetworkSpawn( true, null );
		return corpse;
	}
}

/// <summary>Plays a Magicka NPC's death clip, transitions to a ragdoll, and cleans up after Lifetime.</summary>
[Title( "Magicka Corpse" )]
[Category( "Magicka" )]
public sealed class MagickaCorpse : Component
{
	[Property] public float Lifetime { get; set; } = 10f;
	[Property] public SkinnedModelRenderer Renderer { get; set; }
	[Property] public bool RagdollAfterClip { get; set; }
	[Sync( SyncFlags.FromHost )] private TimeSince Born { get; set; }
	private bool _ragdolled;

	protected override void OnStart()
	{
		Born = 0;
	}

	protected override void OnUpdate()
	{
		if ( RagdollAfterClip && !_ragdolled && Renderer.IsValid() && Renderer.Model.IsValid() )
		{
			var duration = Renderer.Sequence.Duration;
			var finished = Renderer.Sequence.IsFinished || (duration > 0.05f && Born >= duration) || Born >= 2.5f;
			if ( finished )
			{
				_ragdolled = true;
				var physics = GameObject.AddComponent<ModelPhysics>();
				physics.Renderer = Renderer;
				physics.Model = Renderer.Model;
			}
		}

		if ( Networking.IsHost && Born >= Lifetime )
			GameObject.Destroy();
	}
}
