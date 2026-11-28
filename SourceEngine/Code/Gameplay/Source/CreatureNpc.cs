using System;
using Sandbox.Npcs.Layers;
using Sandbox.Npcs.Schedules;
using Sandbox.Npcs;

namespace Source1;

/// <summary>A distinct creature behaviour, such as a leap or a charge, that <see cref="CreatureNpc"/> consults every frame.</summary>
[Alias( "Source1.SourceCreatureBehaviour", "SourceCreatureBehaviour" )]
public abstract class CreatureBehaviour : Component
{
	CreatureNpc _npc;
	protected CreatureNpc Npc => _npc.IsValid() ? _npc : _npc = GetComponent<CreatureNpc>();

	/// <summary>The model the clips were resolved for.</summary>
	protected Model ConfiguredModel { get; private set; }

	/// <summary>Resolves the behaviour's clips, once per model.</summary>
	public void Configure( Model model )
	{
		if ( ConfiguredModel == model )
			return;

		ConfiguredModel = model;
		OnConfigure( model, ModelSequences.Of( model ) );
	}

	protected abstract void OnConfigure( Model model, ModelSequences sequences );

	/// <summary>Clips the behaviour may play; the creature's graph is built with them.</summary>
	public abstract IEnumerable<(string Name, bool Loop)> AnimationChoices { get; }

	/// <summary>The clip the behaviour shows now, or null to leave the creature's own animation.</summary>
	public virtual string Animation => null;

	/// <summary>Host think; true while the behaviour owns the creature.</summary>
	public virtual bool Think( GameObject target ) => false;

	/// <summary>The clip to play for one of the creature's activities instead of its own; null keeps the creature's.</summary>
	public virtual string TranslateActivity( string activity ) => null;

	/// <summary>False holds the creature to its walk while it chases.</summary>
	public virtual bool AllowsRun => true;

	/// <summary>False keeps the creature from starting a melee attack.</summary>
	public virtual bool AllowsMelee => true;

	/// <summary>Host: lands one of the creature's melee hits, due at the clip's hit event, in its own way; true when it did.</summary>
	public virtual bool Strike( GameObject target, string hitEvent ) => false;

	/// <summary>Clips the behaviour may layer over the creature: additive or bone-masked.</summary>
	public virtual IEnumerable<string> GestureChoices => [];

	/// <summary>Host: a share of its usual pace it moves at now.</summary>
	public virtual float PaceScale => 1;

	/// <summary>Playback rate of the clip in <see cref="Animation"/>.</summary>
	public virtual float AnimationRate => 1;

	/// <summary>Every peer: an event of the clip the creature's body plays now, as its animation reaches it.</summary>
	public virtual void OnAnimationEvent( SourceModelSequenceData.Event e ) { }

	/// <summary>Host: the creature is about to say a line; true when the behaviour said it its own way or keeps it quiet.</summary>
	public virtual bool Speaks( VoiceLine line, GameObject about ) => false;

	/// <summary>Host: the creature has just started a melee swing with this clip.</summary>
	public virtual void SwingStarted( string clip ) { }

	/// <summary>The last gesture played; replayed on clients when <see cref="GestureSerial"/> changes.</summary>
	[Sync( SyncFlags.FromHost )] public string Gesture { get; private set; }
	[Sync( SyncFlags.FromHost )] public int GestureSerial { get; private set; }

	/// <summary>Host: layer one of <see cref="GestureChoices"/> over the creature.</summary>
	protected void PlayGesture( string clip )
	{
		if ( clip is null )
			return;

		Gesture = clip;
		GestureSerial++;
	}

	/// <summary>The clip for a gesture activity: additive, else bone-masked.</summary>
	protected static string FindGesture( Model model, ModelSequences sequences, string activity ) =>
		sequences.FindGesture( activity ) ?? sequences.All.FirstOrDefault( s => !s.Delta && s.BoneMask is not null
			&& string.Equals( s.Activity, activity, StringComparison.OrdinalIgnoreCase ) && model.AnimationClips().ContainsKey( s.Name ) )?.Name;

	protected float Duration( string clip, float fallback ) => ConfiguredModel?.AnimationClips().TryGetValue( clip ?? "", out var animation ) == true ? animation.Duration : fallback;

	/// <summary>Plays a sound where the creature is, on every peer.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	protected void PlaySound( string name ) => PlayLocalSound( name, follow: false );

	/// <summary>Plays a sound from the creature on this peer only, such as one an animation event cues on every peer; it follows the creature unless <paramref name="follow"/> is off.</summary>
	protected void PlayLocalSound( string name, bool follow = true )
	{
		if ( Application.IsDedicatedServer || Sounds.Sound( name, MountContent.Of( GameObject ) ) is not { } sound )
			return;

		if ( follow )
			GameObject.PlaySound( sound );
		else
			Sound.Play( sound, WorldPosition );
	}

	/// <summary>Turns toward a direction across the ground at up to a rate in degrees a second; returns the degrees left.</summary>
	protected float TurnToward( Vector3 direction, float rate )
	{
		direction = direction.WithZ( 0 );
		return direction.Length < 0.5f ? 0 : Npcs.TurnToward( GameObject, Rotation.LookAt( direction ).Yaw(), rate );
	}

	/// <summary>The yaw that faces a point across the ground.</summary>
	protected float YawTo( Vector3 point ) => Rotation.LookAt( (point - WorldPosition).WithZ( 0 ) ).Yaw();

	/// <summary>A spot on the navmesh in a random direction, a random distance from <paramref name="min"/> to <paramref name="max"/> away, or null.</summary>
	protected Vector3? RandomSpot( float min, float max ) =>
		Npc.Movement?.ProjectPoint( WorldPosition + Rotation.FromYaw( Game.Random.Float( 0, 360 ) ).Forward * Game.Random.Float( min, max ) );

	/// <summary>
	/// Host: the move it ordered <paramref name="sinceOrdered"/> seconds ago is over: arrived, failed, standing idle once it has
	/// had half a second to set off, or running past <paramref name="giveUp"/> seconds.
	/// </summary>
	protected bool MoveEnded( float sinceOrdered, float giveUp )
	{
		var status = Npc.Movement?.Status;
		return status is MovementLayer.MovementStatus.Arrived or MovementLayer.MovementStatus.Failed
			|| sinceOrdered > 0.5f && status == MovementLayer.MovementStatus.Idle || sinceOrdered > giveUp;
	}
}

/// <summary>
/// Shared mounted creature: idles, pursues and attacks with the model's own activities, timing hits from its
/// animation events. Distinct behaviours (leaps, zombie headcrabs, grenades, projectiles) are separate components.
/// </summary>
[Title( "Source Creature NPC" )]
[Category( "Source 1" )]
[Alias( "Source1.SourceCreatureNpc", "SourceCreatureNpc" )]
public sealed class CreatureNpc : BaseNpc, Component.IPressable, Component.IDamageable.IEvents
{
	/// <summary>How a creature gets around.</summary>
	public enum Locomotions { Ground, Flying, Stationary }

	/// <summary>GUNSHIP_MIN_DAMAGE_THRESHOLD in npc_combinegunship.cpp: smaller blasts are not a hit.</summary>
	const float BlastHitThreshold = 50;

	/// <summary>Discard a pending movement goal if the creature never reaches it.</summary>
	const float CommandTimeout = 30;

	/// <summary>Push a melee blow gives what it strikes, per point of damage.</summary>
	const float PushPerDamage = 300;

	/// <summary>A single hit over this is heavy, under <see cref="FlinchRules.HitGroup"/>.</summary>
	const float HeavyDamage = 20;

	const string FlinchLayer = "flinch";
	const string GestureLayer = "gesture";

	[Property] public string Idle { get; set; } = "idle";
	[Property] public string Move { get; set; } = "walk";
	[Property] public string Attack { get; set; } = "attack";
	[Property] public Locomotions Locomotion { get; set; }
	[Property] public bool Friendly { get; set; }
	[Property] public bool Passive { get; set; }
	[Property] public bool HatesCombine { get; set; } = true;
	[Property] public bool RespondsToBugbait { get; set; }
	[Property] public bool Aquatic { get; set; }
	[Property] public float MoveSpeed { get; set; } = 120;
	[Property] public float FlightHeight { get; set; } = 80;
	[Property] public float AttackRange { get; set; } = 65;
	/// <summary>How far a melee hit reaches, short of <see cref="AttackRange"/>; a victim's body adds 40.</summary>
	[Property] public float MeleeRange { get; set; } = 90;
	[Property] public float AttackDamage { get; set; } = 15;
	internal float? PlayerAttackDamage { get; set; }
	[Property] public float AttackInterval { get; set; } = 1.2f;
	[Property] public float AttackDelay { get; set; } = 0.3f;
	[Property] public float AttackHeight { get; set; } = 32;

	/// <summary>A shooter with a melee attack that holds its distance while it can shoot, as npc_hunter does, instead of closing in between shots.</summary>
	[Property] public bool StandOff { get; set; }

	/// <summary>How close it chases its enemy, origin to origin; 0 is 0.7 of its melee range, or half its attack range for a shooter that holds off.</summary>
	[Property] public float ChaseDistance { get; set; }

	/// <summary>How damage makes it flinch.</summary>
	public enum FlinchRules
	{
		/// <summary>A random bone-masked flinch gesture on any hit, at most once a second.</summary>
		Any,
		/// <summary>A gesture for where a hit landed; a heavy hit stops it for a full-body flinch unless it is mid-swing. Then a pause before the next.</summary>
		HitGroup,
		/// <summary>A big buckshot hit jerks its head back; a physics impact staggers it, even mid-swing.</summary>
		Zombie,
		None
	}

	[Property] public FlinchRules Flinches { get; set; }

	/// <summary>
	/// Melee swings start with the enemy within <see cref="AttackRange"/> between body centres, height counting only where the
	/// bodies don't overlap, and inside this cone of its facing (a dot product). 0 swings at the enemy within range of its origin.
	/// </summary>
	[Property, Group( "Swing" )] public float SwingCone { get; set; }
	/// <summary>A run at its enemy goes on to the end of the chase before it swings, rather than stopping once in reach.</summary>
	[Property, Group( "Swing" )] public bool SwingAfterChase { get; set; }
	/// <summary>Degrees a second it turns to face its enemy before a swing; 0 snaps round.</summary>
	[Property, Group( "Swing" )] public float FaceTurnRate { get; set; }
	/// <summary>Degrees a second it keeps turning toward its enemy through a swing.</summary>
	[Property, Group( "Swing" )] public float SwingTurnRate { get; set; }
	/// <summary>
	/// Side of the box a swing sweeps straight ahead to <see cref="AttackRange"/>; a swing needs something worth hitting there to
	/// start. 0 traces a line at the enemy within <see cref="MeleeRange"/> instead.
	/// </summary>
	[Property, Group( "Swing" )] public float SwingBox { get; set; }
	/// <summary>The view kick (pitch, yaw, roll) a right-hand blow gives a player; a left-hand one mirrors the yaw, a two-handed one knocks the view up.</summary>
	[Property, Group( "Swing" )] public Angles SwingKick { get; set; }
	/// <summary>The speed a right-hand blow throws a player at, forward and to the creature's right; a left-hand one mirrors it, a two-handed one only pushes forward.</summary>
	[Property, Group( "Swing" )] public Vector2 SwingShove { get; set; }
	[Property, Group( "Swing" )] public string SwingHitSound { get; set; }
	[Property, Group( "Swing" )] public string SwingMissSound { get; set; }

	/// <summary>
	/// Explosions that kill it, as npc_strider counts sk_strider_num_missiles and npc_combinegunship sk_gunship_health_increments:
	/// a blast of at least 50 takes that share of its health. Other damage still counts as dealt. 0 takes blasts as dealt.
	/// </summary>
	[Property] public int BlastHits { get; set; }

	/// <summary>The NPC class's sound script prefix, such as NPC_Strider, for its <see cref="NpcVoice"/> lines.</summary>
	[Property] public string Voice { get; set; }

	/// <summary>
	/// Plays movement clips that carry a blend grid over their cells. A walk on move_yaw then follows the way it travels
	/// against the way it faces, so it can step sideways or back while it faces its target.
	/// </summary>
	[Property] public bool BlendGrids { get; set; }

	/// <summary>With <see cref="BlendGrids"/>: within this of its target it faces the target on the move and steps along its path; beyond, it faces its travel. Zero: at any range.</summary>
	[Property] public float StrafeRange { get; set; }

	[Sync( SyncFlags.FromHost )] public GameObject Leader { get; set; }
	[Sync( SyncFlags.FromHost )] TimeSince LastAttack { get; set; } = 10;
	[Sync( SyncFlags.FromHost )] int AttackSerial { get; set; }
	[Sync( SyncFlags.FromHost )] string AttackClip { get; set; }
	[Sync( SyncFlags.FromHost )] string Flinch { get; set; }
	[Sync( SyncFlags.FromHost )] int FlinchSerial { get; set; }
	int _observedAttack;
	int _observedFlinch;
	string[] _flinches = [];
	readonly Dictionary<CreatureBehaviour, int> _observedGestures = [];
	TimeSince _lastFlinch = 10;
	Model _configuredModel;
	string _idle, _walk, _run, _melee, _ranged;
	string[] _meleeChoices = [];
	(string, bool)[] _choices = [];
	ModelSequences _sequences;
	HashSet<string> _attackActivities = [];
	AnimationPlayer _animation = new();
	LinkAnimation _link;
	CreatureBehaviour[] _behaviours = [];
	Hl2.Manhack _manhack;
	Hl2.NpcProjectileAttack _projectile;
	NpcRangedEffects _rangedEffects;
	Hl2.ScannerPhoto _photo;
	Rigidbody _body;
	Vector3 _home;
	Vector3? _command;
	double _lastBugbaitExpiration;
	Vector3? _squadGoal;
	GameObject _squadTarget;

	TimeSince _sinceCommand;
	TimeSince _lastPath;
	bool _wasHeld;
	GameObject _strikeTarget;
	(float Time, string Event)[] _strikes = [];
	int _strikeIndex;
	float _attackRecovery;
	bool _rangedStrike;
	bool _attackHolds;
	GameObject _facing;
	TimeSince _sinceFacing;
	GameObject _swingEnemy;
	bool _flinching;
	bool _busy;
	string[] _fullFlinches = [];
	float _hitDamage;
	readonly HashSet<string> _hitTags = [];
	TimeUntil _nextFlinch;
	bool _flinchedRecently;
	Vector3 _lastPosition;
	float _measuredSpeed, _moveYaw;
	GameObject _target;
	TimeUntil _nextIdleLine = 10;
	GameObject _scaredOf;
	TimeSince _sinceScared;
	bool _greeted;
	TimeUntil _nextChatter;
	float _headYaw, _headPitch;

	/// <summary>Passive walkers (citizens, scientists) potter about on the shared schedules instead of the creature think.</summary>
	bool Ambient => Passive && Locomotion == Locomotions.Ground;

	public override string Faction => Passive ? Factions.Citizen : Friendly ? Factions.Ally : HatesCombine ? Factions.Monster : Factions.Enemy;

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
		if ( HatesCombine )
			SetRelationship( Factions.Enemy, Disposition.Hostile );
	}

	protected override void OnAwake()
	{
		if ( Locomotion == Locomotions.Flying && !GetComponent<MovementLayer>( true ).IsValid() )
			AddComponent<FlyingLayer>().Bounds = Aquatic ? FlyingLayer.FlyingBounds.WaterOnly : FlyingLayer.FlyingBounds.Anywhere;

		base.OnAwake();
	}

	protected override void OnStart()
	{
		base.OnStart();
		_body = GetComponent<Rigidbody>( true );
		_behaviours = GetComponents<CreatureBehaviour>().ToArray();
		_animation.EventFired += OnAnimationEvent;
		_manhack = GetComponent<Hl2.Manhack>();
		_projectile = GetComponent<Hl2.NpcProjectileAttack>();
		_rangedEffects = GetComponent<NpcRangedEffects>();
		_photo = GetComponent<Hl2.ScannerPhoto>();
		_home = WorldPosition;
		if ( IsProxy )
		{
			_observedAttack = AttackSerial;
			_observedFlinch = FlinchSerial;
		}
		else if ( Locomotion == Locomotions.Flying )
		{
			LiftOff();
		}

		if ( Locomotion == Locomotions.Stationary && _body.IsValid() )
		{
			_body.Gravity = false;
			_body.MotionEnabled = false;
		}
	}

	/// <summary>Rises to flight height above the floor it was put on; one already that high, such as a dupe, stays where it is.</summary>
	void LiftOff()
	{
		var floor = Scene.Trace.Ray( WorldPosition + Vector3.Up * 8, WorldPosition - Vector3.Up * FlightHeight ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "player", "npc" ).Run();
		if ( !floor.Hit )
			return;

		_home = floor.EndPosition;
		WorldPosition = WorldPosition.WithZ( _home.z + FlightHeight );
	}

	/// <summary>
	/// A bugbait pheropod burst: responsive creatures investigate where it landed.
	/// Call on the host with where it burst and who threw it.
	/// </summary>
	public static void HearBugbait( Scene scene, Vector3 position, GameObject thrower )
	{
		Hl2.BugbaitCall.Emit( scene, position, thrower );
	}

	internal void SetSquadGoal( GameObject leader, Vector3? fightGoal, GameObject fightTarget )
	{
		Leader = leader;
		_squadGoal = fightGoal;
		_squadTarget = fightTarget;
		_command = null;
	}

	public void CommandTo( Vector3 point, GameObject thrower )
	{
		var radius = (float)Hl2.BugbaitCall.HearingRadius;
		ListenForBugbait( point, radius * radius, Time.NowDouble + Hl2.BugbaitCall.Duration );
	}

	internal void ListenForBugbait( Vector3 point, float radiusSquared, double expiration )
	{
		if ( !Networking.IsHost || !RespondsToBugbait || Health < 1 || expiration <= _lastBugbaitExpiration
			|| EyePosition.DistanceSquared( point ) > radiusSquared )
			return;

		_lastBugbaitExpiration = expiration;
		_command = point;
		_sinceCommand = 0;
	}

	string PickMelee() => _meleeChoices.Length > 0 ? Game.Random.FromArray( _meleeChoices ) : _melee;

	void Configure( Model model )
	{
		_configuredModel = model;
		_animation.UseBlendGrids = BlendGrids;
		_sequences = ModelSequences.Of( model );
		_idle = _sequences.FindActivity( "ACT_IDLE", Idle );
		_walk = _sequences.FindActivity( Locomotion == Locomotions.Flying ? "ACT_FLY" : "ACT_WALK", Move );
		_run = _sequences.FindActivity( "ACT_RUN", _walk );
		_melee = _sequences.FindActivity( "ACT_MELEE_ATTACK1" );
		// Leaps and crab throws are ACT_RANGE_ATTACK1 too; those belong to their components.
		_ranged = HasRangedAttack ? _sequences.FindActivity( "ACT_RANGE_ATTACK1" ) : null;
		_meleeChoices = _sequences.Clips( "ACT_MELEE_ATTACK1" );
		// Masked ACT_GESTURE_FLINCH_* sequences (zombies) play over whatever the creature is doing, as Source's gesture layer does.
		// The flinch rules also use the big and small flinch gestures.
		_flinches = _sequences.All.Where( s => !s.Delta && s.BoneMask is not null && model.AnimationClips().ContainsKey( s.Name ) && s.Activity is { } activity
				&& (Flinches == FlinchRules.Any ? activity.StartsWith( "ACT_GESTURE_FLINCH", StringComparison.Ordinal )
					: activity.StartsWith( "ACT_GESTURE_", StringComparison.Ordinal ) && activity.Contains( "FLINCH", StringComparison.Ordinal )) )
			.Select( s => s.Name ).ToArray();
		_fullFlinches = Flinches is FlinchRules.HitGroup or FlinchRules.Zombie
			? _sequences.All.Where( s => !s.Delta && s.BoneMask is null && model.AnimationClips().ContainsKey( s.Name )
				&& (s.Activity?.StartsWith( "ACT_FLINCH_", StringComparison.Ordinal ) == true || s.Activity is "ACT_BIG_FLINCH" or "ACT_SMALL_FLINCH") )
				.Select( s => s.Name ).Distinct().ToArray()
			: [];
		if ( _melee is null && !HasRangedAttack && !_behaviours.Any( x => x is Hl2.Leap ) )
			_melee = Attack;
		_attackActivities = _sequences.All.Where( s => !s.Looping && !string.IsNullOrEmpty( s.Activity ) && s.Activity != "ACT_IDLE" ).Select( s => s.Name )
			.ToHashSet( StringComparer.OrdinalIgnoreCase );

		foreach ( var behaviour in _behaviours )
			behaviour.Configure( model );

		// Behaviour gestures: additive ones on the gesture slot, bone-masked ones on their own layer
		var gestures = _behaviours.SelectMany( x => x.GestureChoices ).Where( x => x is not null ).Distinct().ToArray();
		_animation.Gestures = gestures.Where( x => _sequences.Get( x ) is { Delta: true } ).ToArray();
		_animation.OverlayLayers = new Dictionary<string, IReadOnlyList<string>>
		{
			[FlinchLayer] = _flinches,
			[GestureLayer] = gestures.Where( x => _sequences.Get( x ) is { Delta: false } ).ToArray()
		};

		_link = new LinkAnimation( _sequences );
		_choices = [(_idle, true), (_walk, true), (_run, true), (_ranged, false), .. _meleeChoices.Select( x => (x, false) ), (_melee, false),
			.. _fullFlinches.Select( x => (x, false) ),
			.. _behaviours.SelectMany( x => x.AnimationChoices ), .. _link.Choices];
	}

	float GroundSpeed( string clip ) => _sequences?.Get( clip )?.GroundSpeed ?? 0;

	void OnAnimationEvent( SourceModelSequenceData.Event e )
	{
		foreach ( var behaviour in _behaviours )
			if ( behaviour.IsValid() )
				behaviour.OnAnimationEvent( e );
	}

	/// <summary>Says a line unless one of its behaviours speaks it its own way.</summary>
	bool Say( VoiceLine line, GameObject about = null ) => !_behaviours.Any( x => x.Speaks( line, about ) ) && NpcVoice.Say( this, Voice, line, about );

	string Translate( string activity, string clip )
	{
		foreach ( var behaviour in _behaviours )
		{
			if ( behaviour.TranslateActivity( activity ) is { } translated )
				return translated;
		}

		return clip;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( !Renderer.IsValid() || !Renderer.Model.IsValid() )
			return;

		if ( _configuredModel != Renderer.Model )
			Configure( Renderer.Model );

		if ( !_animation.Ensure( Renderer, _choices, _idle ) )
			return;

		if ( FlinchSerial != _observedFlinch )
		{
			_observedFlinch = FlinchSerial;
			_animation.PlayOverlay( FlinchLayer, Flinch, restart: true );
		}

		foreach ( var behaviour in _behaviours )
		{
			// First sight only records the serial, so late joiners don't replay old gestures
			if ( _observedGestures.TryGetValue( behaviour, out var seen ) && seen != behaviour.GestureSerial
				&& !_animation.PlayGesture( behaviour.Gesture ) )
				_animation.PlayOverlay( GestureLayer, behaviour.Gesture, restart: true );
			_observedGestures[behaviour] = behaviour.GestureSerial;
		}

		UpdateHeld();
		if ( Networking.IsHost && !PhysicsCarrier.IsValid() )
			HostThink();

		var speed = Locomotion == Locomotions.Ground ? MeasureSpeed() : MoveSpeed;
		var moving = Locomotion == Locomotions.Flying || Movement?.IsMoving == true || speed > 10;
		string forced = null;
		var forcedRate = 1f;
		foreach ( var behaviour in _behaviours )
		{
			forced = behaviour.Animation;
			if ( forced is null )
				continue;

			forcedRate = behaviour.AnimationRate;
			break;
		}

		if ( forced is not null )
		{
			_animation.Play( forced, forcedRate );
		}
		else if ( _link.Clip( Animation, _animation, out var restart ) is { } linkClip )
		{
			_animation.Play( linkClip, restart: restart );
		}
		else if ( AttackSerial != _observedAttack && (_attackActivities.Contains( AttackClip ?? "" ) || !moving) )
		{
			_observedAttack = AttackSerial;
			_animation.Play( AttackClip, restart: _animation.Current != AttackClip || _animation.Finished );
		}
		else if ( _animation.Current != AttackClip || _animation.Finished || LastAttack >= Math.Max( AttackInterval, _animation.Duration )
			|| moving && !_attackActivities.Contains( AttackClip ?? "" ) )
		{
			var idle = Translate( "ACT_IDLE", _idle );
			var walk = Translate( "ACT_WALK", _walk );
			var run = _run != _walk ? Translate( "ACT_RUN", _run ) : walk;
			var walkSpeed = GroundSpeed( walk );
			var runSpeed = GroundSpeed( run );
			// Switch where both clips are equally far off in playback rate.
			var running = run != walk && (walkSpeed > 1 && runSpeed > walkSpeed ? speed > MathF.Sqrt( walkSpeed * runSpeed ) : speed > MoveSpeed * 1.25f);
			var clip = !moving || Aquatic && !IsUnderwater( WorldPosition ) ? idle : running ? run : walk;
			// Feet match the ground: rate is our speed over the clip's ground speed, or over the configured pace without one.
			var ground = StepSpeed( clip );
			var rate = clip == idle || Locomotion != Locomotions.Ground ? 1
				: ground > 1 ? Math.Clamp( speed / ground, 0.3f, 2.5f )
				: Math.Clamp( speed / (running ? MoveSpeed * 1.6f : MoveSpeed), 0.5f, 1.6f );
			_animation.Play( clip, rate );
		}
		LookWithHead();
		_animation.Tick();
	}

	/// <summary>The head follows the synced look target through the model's head_yaw and head_pitch, as CAI_BaseActor's head control does.</summary>
	void LookWithHead()
	{
		var looking = Animation.IsValid() && Animation.IsLooking;
		var aim = looking ? Rotation.LookAt( Animation.CalculateLookPosition() - EyePosition ).Angles() : default;
		var blend = Math.Clamp( Time.Delta * 6, 0, 1 );
		_headYaw = MathX.Lerp( _headYaw, looking ? Angles.NormalizeAngle( aim.yaw - WorldRotation.Yaw() ) : 0, blend );
		_headPitch = MathX.Lerp( _headPitch, looking ? aim.pitch : 0, blend );
		_animation.SetPoseParameter( "head_yaw", _headYaw );
		_animation.SetPoseParameter( "head_pitch", _headPitch );
	}

	/// <summary>Being carried stops the creature; a flyer's body simulates only while it is carried.</summary>
	void UpdateHeld()
	{
		var held = PhysicsCarrier.IsValid();
		if ( held == _wasHeld )
			return;

		_wasHeld = held;
		Movement?.Stop();

		// A flyer's body is handed to physics by its carrier, see Npc.PhysicsCarrier.
		if ( Locomotion == Locomotions.Stationary && _body.IsValid() )
			_body.MotionEnabled = _wasHeld;
	}

	/// <summary>Horizontal speed the body actually covers. The agent's reported velocity runs ahead of it, which slid the feet.</summary>
	float MeasureSpeed()
	{
		var moved = (WorldPosition - _lastPosition).WithZ( 0 );
		_lastPosition = WorldPosition;
		if ( Time.Delta > 0 && moved.Length < 100 )
		{
			_measuredSpeed = MathX.Lerp( _measuredSpeed, moved.Length / Time.Delta, Math.Clamp( Time.Delta * 10, 0, 1 ) );
			if ( BlendGrids )
				TravelYaw( moved );
		}
		return _measuredSpeed;
	}

	/// <summary>move_yaw eases toward the way it travels against the way it faces; straight ahead once it stops.</summary>
	void TravelYaw( Vector3 moved )
	{
		var target = _measuredSpeed > 20 ? Angles.NormalizeAngle( Rotation.LookAt( moved ).Yaw() - WorldRotation.Yaw() ) : 0;
		_moveYaw = Angles.NormalizeAngle( _moveYaw + Angles.NormalizeAngle( target - _moveYaw ) * Math.Clamp( Time.Delta * 8, 0, 1 ) );
		_animation.SetPoseParameter( "move_yaw", _moveYaw );
	}

	/// <summary>The clip's ground speed the way it is stepping: a move_yaw grid's cells blended at the current move_yaw.</summary>
	float StepSpeed( string clip )
	{
		if ( !BlendGrids || _sequences?.Get( clip ) is not { BlendAxes: [{ Parameter: "move_yaw" } axis] } sequence
			|| sequence.BlendGridSpeeds.Length != axis.Positions.Length || axis.Positions.Length < 2 )
			return GroundSpeed( clip );

		var positions = axis.Positions;
		var speeds = sequence.BlendGridSpeeds;
		var yaw = Math.Clamp( _moveYaw, positions[0], positions[^1] );
		for ( var i = 1; i < positions.Length; i++ )
		{
			if ( yaw <= positions[i] )
				return MathX.Lerp( speeds[i - 1], speeds[i], (yaw - positions[i - 1]) / MathF.Max( positions[i] - positions[i - 1], 0.001f ) );
		}
		return speeds[^1];
	}

	/// <summary>Source NPCs stop for the length of an attack activity; a stand-in clip (strider minigun, stalker beam) fires on the move.</summary>
	bool Attacking => _attackHolds && LastAttack < _attackRecovery;

	void HostThink()
	{
		if ( !NpcConVars.Enabled )
		{
			Movement?.Stop();
			_strikeTarget = null;
			return;
		}

		if ( Ambient )
		{
			Chatter();
			return;
		}

		if ( _command.HasValue && _sinceCommand >= CommandTimeout ) _command = null;
		if ( Aquatic )
		{
			var submerged = IsUnderwater( WorldPosition );
			if ( _body.IsValid() )
			{
				_body.Gravity = !submerged;
				_body.MotionEnabled = !submerged;
			}

			if ( !submerged )
			{
				Movement?.Stop();
				_strikeTarget = null;
				return;
			}
		}

		var target = Passive ? null : Senses?.FindBestTarget();
		if ( target.IsValid() && !_target.IsValid() )
			Say( VoiceLine.Alert, target );
		_target = target;
		if ( !target.IsValid() && _nextIdleLine )
		{
			_nextIdleLine = Game.Random.Float( 10, 30 );
			Say( VoiceLine.Idle );
		}

		React();
		_busy = false;
		foreach ( var behaviour in _behaviours )
		{
			if ( !behaviour.Think( target ) )
				continue;

			_strikeTarget = null;
			_facing = null;
			_busy = true;
			return;
		}

		if ( Aquatic && target.IsValid() && !IsUnderwater( target.GetBounds().Center ) )
			target = null;

		while ( _strikeTarget.IsValid() && _strikeIndex < _strikes.Length && LastAttack >= _strikes[_strikeIndex].Time )
		{
			Strike( _strikeTarget, _strikes[_strikeIndex].Event );
			if ( ++_strikeIndex == _strikes.Length )
				_strikeTarget = null;
		}

		// Through a swing it keeps turning toward its enemy, unless that has died
		if ( SwingTurnRate > 0 && Swinging && !Npcs.IsDead( _swingEnemy ) )
			TurnToward( _swingEnemy.WorldPosition, SwingTurnRate );

		var squadGoal = _squadTarget.IsValid() ? _squadTarget.WorldPosition : _squadGoal;
		var goal = target.IsValid() ? target.WorldPosition : _command ?? squadGoal ?? (Leader.IsValid() ? Leader.WorldPosition : _home);
		var distance = target.IsValid() ? WorldPosition.Distance( target.WorldPosition ) : WorldPosition.Distance( goal );
		var ranged = HasRangedAttack;
		var meleeRange = !ranged ? AttackRange : _melee is null ? 0 : Math.Min( AttackRange, MeleeRange );
		// Approximation of Source's chase between attacks: hostile shooters with a melee attack (antlion workers)
		// close in, other shooters keep half their range.
		var engage = ChaseDistance > 0 ? ChaseDistance : ranged && (Friendly || StandOff || _melee is null) ? AttackRange * 0.5f : meleeRange * 0.7f;
		var stop = target.IsValid() ? engage : Leader.IsValid() && !_command.HasValue && !squadGoal.HasValue ? 110 : 20;
		if ( _command.HasValue && !target.IsValid() && WorldPosition.Distance( _command.Value ) < 80 )
			_command = null;

		if ( Locomotion == Locomotions.Ground && !IsAirborne )
		{
			if ( Attacking || _strikeTarget.IsValid() || _facing.IsValid() )
			{
				if ( Movement?.MoveTarget is not null )
					Movement.Stop();
			}
			else if ( _lastPath > 0.4f )
			{
				if ( distance > stop )
					Movement?.MoveTo( goal, stop, PaceFor( target.IsValid() || _command.HasValue || squadGoal.HasValue ), FacedOnTheWay( target ) );
				else
					Movement?.Stop();
				_lastPath = 0;
			}
		}
		else if ( Locomotion == Locomotions.Flying && Movement is FlyingLayer flying )
		{
			flying.HoverHeight = target.IsValid() && AttackRange < 150 ? AttackHeight : FlightHeight;
			// A manhack goes for contact: it flies into its target and bounces off
			if ( _manhack.IsValid() )
				flying.MoveTo( goal + _manhack.Offset, target.IsValid() ? _manhack.AttackStopDistance : stop, MoveSpeed, target );
			else
				flying.MoveTo( goal, stop, MoveSpeed, target );
		}

		if ( _manhack.IsValid() )
		{
			_manhack.Contact( target );
			return;
		}

		if ( !target.IsValid() || Passive || !ranged && _melee is null || _strikeTarget.IsValid() || LastAttack < Math.Max( AttackInterval, _attackRecovery )
			|| !_behaviours.All( x => x.AllowsMelee ) )
		{
			_facing = null;
			return;
		}

		if ( SwingCone > 0 && !ranged )
		{
			if ( SwingAfterChase && _facing != target && Movement?.IsMoving == true || !FaceToSwing( target ) )
				return;
		}
		else if ( distance > AttackRange )
			return;
		else if ( Locomotion == Locomotions.Ground )
			WorldRotation = Rotation.LookAt( (target.WorldPosition - WorldPosition).WithZ( 0 ) );

		_rangedStrike = ranged && distance > meleeRange;
		var clip = _rangedStrike ? _ranged ?? Attack : Translate( "ACT_MELEE_ATTACK1", null ) ?? PickMelee() ?? Attack;
		_strikes = Npcs.AttackEvents( Renderer.Model, clip, AttackDelay );
		_strikeIndex = 0;
		_swingEnemy = target;
		_flinching = false;
		// A real attack activity holds the creature for its length; a stand-in clip (stalker, strider) only for the interval.
		// A swing under the swing rules holds for its clip even when the clip is marked to loop, as the fast zombie's is.
		var attackActivity = _attackActivities.Contains( clip ?? "" ) || SwingCone > 0 && !_rangedStrike && _meleeChoices.Contains( clip );
		var duration = ClipLength( clip );
		_attackHolds = attackActivity && duration > 0;
		_attackRecovery = _attackHolds ? duration : AttackInterval;
		LastAttack = 0;
		AttackClip = clip;
		AttackSerial++;
		_strikeTarget = target;
		if ( _rangedStrike && _strikes.Length > 0 )
			_rangedEffects?.PresentCharge( _strikes[0].Time );
		if ( !_rangedStrike )
			foreach ( var behaviour in _behaviours )
				behaviour.SwingStarted( clip );
	}

	/// <summary>The enemy it is after this think.</summary>
	internal GameObject Enemy => _target;

	/// <summary>In the middle of an attack, or a hit of one is still to land.</summary>
	internal bool InAttack => Attacking || _strikeTarget.IsValid();

	/// <summary>Every peer: the ground speed its body covers, smoothed over a few frames.</summary>
	internal float Speed => _measuredSpeed;

	/// <summary>Host: one claw of a swing a behaviour plays itself, by the swing rules' sweep: a hit or a miss.</summary>
	internal void Claw( GameObject target, string hitEvent ) => Swing( target, hitEvent );

	/// <summary>Sets one of the model's pose parameters, such as move_yaw, on this machine.</summary>
	internal void SetPose( string name, float value ) => _animation.SetPoseParameter( name, value );

	/// <summary>Height of its body: the top of its collider, else of its navigation hull.</summary>
	float BodyHeight => GetComponentInChildren<BoxCollider>() is { } box ? box.Center.z + box.Scale.z * 0.5f : GetComponent<NavMeshAgent>()?.Height ?? 72;

	internal Vector3 Centre => WorldPosition + Vector3.Up * (BodyHeight * 0.5f);

	/// <summary>Where the swing box starts: the body's centre, raised so the box clears the floor by a unit.</summary>
	Vector3 SwingCentre => Centre.WithZ( MathF.Max( Centre.z, WorldPosition.z + SwingBox * 0.5f + 1 ) );

	/// <summary>Swings neither start for nor strike corpses, debris and projectiles.</summary>
	static readonly string[] SwingPassThrough = ["trigger", "debris", "ragdoll", "projectile"];

	bool Swinging => Attacking && !_flinching;

	/// <summary>Distance between body centres; height counts only for the gap where the bodies don't overlap.</summary>
	float SwingDistance( GameObject target )
	{
		var player = target.GetComponent<PlayerController>();
		var bounds = target.GetBounds();
		var (bottom, top) = player.IsValid() ? (player.WorldPosition.z, player.WorldPosition.z + player.CurrentHeight) : (bounds.Mins.z, bounds.Maxs.z);
		var gap = MathF.Max( 0, MathF.Max( bottom - (WorldPosition.z + BodyHeight), WorldPosition.z - top ) );
		var flat = (Npcs.CapturePoint( target ) - Centre).WithZ( 0 ).Length;
		return MathF.Sqrt( flat * flat + gap * gap );
	}

	internal bool InSwingCone( GameObject target ) =>
		WorldRotation.Forward.WithZ( 0 ).Normal.Dot( (target.WorldPosition - WorldPosition).WithZ( 0 ).Normal ) >= SwingCone;

	/// <summary>Whether it may swing at its enemy now, by the swing rules where it has them, else by range.</summary>
	internal bool CanSwing( GameObject target ) => target.IsValid() && (SwingCone > 0
		? SwingDistance( target ) <= AttackRange && InSwingCone( target ) && (SwingBox <= 0 || WorthSwinging( target ))
		: WorldPosition.Distance( target.WorldPosition ) <= AttackRange);

	/// <summary>
	/// The first thing in its way straight ahead within reach is worth a swing: its enemy, any NPC (so a back row swings at the
	/// front row), or a breakable. Open air or anything else is not, and it closes in instead.
	/// </summary>
	bool WorthSwinging( GameObject target )
	{
		var centre = SwingCentre;
		var tr = Scene.Trace.Box( new Vector3( SwingBox ), centre, centre + WorldRotation.Forward.WithZ( 0 ).Normal * AttackRange )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( SwingPassThrough ).Run();
		var hit = tr.Hit ? tr.GameObject : null;
		return hit.IsValid() && (hit.Root == target.Root || hit.GetComponentInParent<BaseNpc>().IsValid()
			|| !Npcs.IsPlayer( hit.Root ) && hit.GetComponentInParent<Component.IDamageable>() is { Health: > 0 });
	}

	/// <summary>
	/// Out of the swing cone but in reach, it turns toward its enemy where it stands. Once it may swing it stops and turns to
	/// face the enemy exactly; true when it does.
	/// </summary>
	bool FaceToSwing( GameObject target )
	{
		if ( _facing != target )
		{
			_facing = null;
			if ( !CanSwing( target ) )
			{
				if ( Movement?.IsMoving != true && SwingDistance( target ) <= AttackRange )
					TurnToward( target.WorldPosition, FaceTurnRate );
				return false;
			}

			_facing = target;
			_sinceFacing = 0;
			Movement?.Stop();
		}

		if ( TurnToward( target.WorldPosition, FaceTurnRate ) > 0.5f && _sinceFacing < 2 )
			return false;

		_facing = null;
		return true;
	}

	/// <summary>Turns toward a point at up to a rate in degrees a second; 0 turns at once. Returns the degrees left.</summary>
	float TurnToward( Vector3 point, float rate )
	{
		var flat = (point - WorldPosition).WithZ( 0 );
		return flat.Length < 1 ? 0 : Npcs.TurnToward( GameObject, Rotation.LookAt( flat ).Yaw(), rate );
	}

	/// <summary>A passive walker greets the player once (TLK_HELLO) and otherwise chats now and then.</summary>
	void Chatter()
	{
		if ( !_nextChatter )
			return;

		_nextChatter = 1;
		var player = Senses?.FindNearestVisible( "player" );
		if ( !_greeted && player.IsValid() && player.WorldPosition.Distance( WorldPosition ) < 500 )
		{
			_greeted = NpcVoice.Say( this, Voice, VoiceLine.Greet, player );
			return;
		}

		if ( !_nextIdleLine )
			return;

		_nextIdleLine = Game.Random.Float( 10, 30 );
		NpcVoice.Say( this, Voice, VoiceLine.Idle );
	}

	/// <summary>Move at the walk or run clip's own ground speed, so the feet do not slide; the configured pace without one.</summary>
	internal float PaceFor( bool hurry )
	{
		hurry = hurry && _run != _walk && _behaviours.All( x => x.AllowsRun );
		var clip = hurry ? Translate( "ACT_RUN", _run ) : Translate( "ACT_WALK", _walk );
		var ground = GroundSpeed( clip );
		var scale = _behaviours.Aggregate( 1f, ( product, x ) => product * x.PaceScale );
		return (ground > 1 ? ground : hurry ? MoveSpeed * 1.6f : MoveSpeed) * scale;
	}

	/// <summary>A forward-only walker faces its target within this range of it, once its path this far ahead heads within this many degrees of it.</summary>
	const float FaceTargetRange = 128, FaceTargetAngle = 30, FaceTargetLook = 32;

	/// <summary>
	/// What it faces on its way to a target. One whose move clips blend by direction (<see cref="BlendGrids"/>) faces the
	/// target within <see cref="StrafeRange"/> and steps along its path. One that only walks forward faces the way it
	/// travels, so it doesn't slide sideways round corners, and faces its target only once it's close and heading straight
	/// at it. Beyond those ranges both face their travel.
	/// </summary>
	internal GameObject FacedOnTheWay( GameObject target )
	{
		if ( !target.IsValid() )
			return null;

		var to = (target.WorldPosition - WorldPosition).WithZ( 0 );
		if ( BlendGrids )
			return StrafeRange <= 0 || to.Length <= StrafeRange ? target : null;

		if ( to.Length > FaceTargetRange || GetComponent<NavMeshAgent>() is not { } agent || !agent.IsNavigating )
			return null;

		var ahead = (agent.GetLookAhead( FaceTargetLook ) - WorldPosition).WithZ( 0 );
		return ahead.Length > 1 && ahead.Normal.Dot( to.Normal ) >= MathF.Cos( FaceTargetAngle.DegreeToRadian() ) ? target : null;
	}

	bool HasRangedAttack => _projectile.IsValid() || _rangedEffects.IsValid() || _photo.IsValid();


	void Strike( GameObject target, string hitEvent )
	{
		if ( !_rangedStrike && _behaviours.Any( x => x.Strike( target, hitEvent ) ) )
			return;

		if ( !_rangedStrike && SwingBox > 0 )
		{
			Swing( target, hitEvent );
			return;
		}

		if ( _rangedStrike && _projectile.IsValid() )
		{
			_projectile.Fire( target );
			return;
		}

		if ( _photo.IsValid() )
		{
			_photo.Flash( target );
			return;
		}

		var presentation = _rangedStrike ? _rangedEffects : null;
		var start = presentation?.Muzzle?.Position ?? WorldPosition + Vector3.Up * AttackHeight;
		var end = Npcs.CapturePoint( target );
		var range = _rangedStrike ? AttackRange : Math.Min( AttackRange, MeleeRange );
		if ( start.Distance( end ) > range + 40 )
			return;

		var tr = Scene.Trace.Ray( start, end ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
		presentation?.PresentShot( new( tr.EndPosition, tr.Hit, tr.Normal, tr.GameObject, tr.Surface, start ) );
		if ( tr.Hit && tr.GameObject?.Root != target.Root )
			return;

		Damage( target, start, tr.EndPosition, presentation?.DamageTag ?? (_rangedStrike ? "bullet" : "melee") );
	}

	internal void Damage( GameObject target, Vector3 origin, Vector3 position, string tag, float? amount = null )
	{
		amount ??= Npcs.IsPlayer( target ) ? PlayerAttackDamage ?? AttackDamage : AttackDamage;
		var damage = new DamageInfo { Damage = amount.Value, Attacker = GameObject, Weapon = GameObject, Origin = origin, Position = position };
		damage.Tags.Add( tag );
		target.GetComponentInParent<Component.IDamageable>()?.OnDamage( damage );
	}

	/// <summary>
	/// A claw: the swing box swept straight ahead from its centre, and if that meets nothing, again from just above its head
	/// so whoever stands on it is clawed. Solid world between it and its enemy spoils the whole swing, silently.
	/// </summary>
	void Swing( GameObject target, string hitEvent )
	{
		var centre = Centre;
		if ( target.IsValid() && Scene.Trace.Box( new Vector3( 16 ), centre, Npcs.CapturePoint( target ) ).IgnoreDynamic().IgnoreGameObjectHierarchy( GameObject )
			.IgnoreGameObjectHierarchy( target ).WithoutTags( Npcs.PastCharacters ).Run().Hit )
			return;

		var start = SwingCentre;
		var end = start + WorldRotation.Forward.WithZ( 0 ).Normal * AttackRange;
		var hit = SweepHit( start, end, SwingBox, null, anyone: false );
		if ( !hit.Character.IsValid() && !hit.Prop.IsValid() )
			hit = SweepHit( WorldPosition + Vector3.Up * (BodyHeight + 1), end, SwingBox, null, anyone: false );

		var sound = hit.Character.IsValid() || hit.Prop.IsValid() ? SwingHitSound : SwingMissSound;
		if ( !string.IsNullOrEmpty( sound ) )
			PlaySound( sound );

		if ( Npcs.IsPlayer( hit.Character ) )
			Knock( hit.Character, hitEvent );
		else if ( hit.Character.IsValid() )
			Bleed( hit.Character, hitEvent );
	}

	/// <summary>What a melee sweep met: the character that stopped it, the last prop it passed through, whether anything stopped it, and where it ended.</summary>
	internal readonly record struct SweepResult( GameObject Character, GameObject Prop, bool Stopped, Vector3 End );

	/// <summary>
	/// Sweeps a box for a melee blow. Props on the way take the damage and a push and are passed through; the first character
	/// it hates, or any character with <paramref name="anyone"/>, takes the damage and stops it, as does the world. Other
	/// characters are passed by.
	/// </summary>
	internal SweepResult SweepHit( Vector3 from, Vector3 to, float size, float? amount, bool anyone )
	{
		GameObject prop = null;
		var passed = new HashSet<GameObject>();
		foreach ( var tr in Scene.Trace.Box( new Vector3( size ), from, to ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( SwingPassThrough ).RunAll().OrderBy( x => x.Fraction ) )
		{
			var hit = tr.GameObject;
			var character = hit?.GetComponentInParent<BaseNpc>()?.GameObject ?? hit?.GetComponentInParent<PlayerController>()?.GameObject;
			if ( character.IsValid() )
			{
				if ( !passed.Add( character ) || !anyone && GetDisposition( character ) != Disposition.Hostile )
					continue;

				Damage( character, from, tr.HitPosition, DamageTags.Melee, amount );
				return new( character, prop, true, tr.EndPosition );
			}

			// A loose or breakable prop; anything else fixed is the world
			var thing = hit?.GetComponentInParent<Rigidbody>() is { MotionEnabled: true } body ? body.GameObject
				: hit?.GetComponentInParent<Component.IDamageable>() is Component { } breakable and Component.IDamageable { Health: > 0 } ? breakable.GameObject : null;
			if ( !thing.IsValid() )
				return new( null, prop, true, tr.EndPosition );
			if ( !passed.Add( thing ) )
				continue;

			Hl2.Charge.Push( thing, (thing.GetBounds().Center - from).Normal * ((amount ?? AttackDamage) * PushPerDamage) );
			Damage( thing, from, tr.HitPosition, DamageTags.Melee, amount );
			prop = thing;
		}

		return new( null, prop, false, to );
	}

	static bool LeftHand( string hitEvent ) => hitEvent?.EndsWith( "_LEFT", StringComparison.Ordinal ) == true;
	static bool BothHands( string hitEvent ) => hitEvent?.EndsWith( "_BOTH", StringComparison.Ordinal ) == true;

	/// <summary>A clawed player has the view kicked and is thrown, by the hand that struck.</summary>
	void Knock( GameObject player, string hitEvent )
	{
		var forward = WorldRotation.Forward.WithZ( 0 ).Normal;
		var right = WorldRotation.Right.WithZ( 0 ).Normal;
		var (kick, shove) = BothHands( hitEvent ) ? (new Angles( 45, Game.Random.Int( -5, 5 ), Game.Random.Int( -5, 5 ) ), forward * SwingShove.x)
			: LeftHand( hitEvent ) ? (new Angles( SwingKick.pitch, -SwingKick.yaw, SwingKick.roll ), forward * SwingShove.x - right * SwingShove.y)
			: (SwingKick, forward * SwingShove.x + right * SwingShove.y);
		Hl2.Leap.Punch( player, kick );
		KnockbackPlayer( player, shove );
	}

	/// <summary>An NPC that bleeds sprays its blood at the hand that struck it, at both for a two-handed blow.</summary>
	void Bleed( GameObject victim, string hitEvent )
	{
		var tr = Scene.Trace.Ray( Centre, Npcs.CapturePoint( victim ) ).UseHitboxes().IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		if ( tr.GameObject?.Root != victim.Root || tr.Surface is null || WeaponEffects.BloodParticle( tr.Surface ) is not { } blood )
			return;

		string[] hands = BothHands( hitEvent ) ? ["blood_left", "blood_right"] : LeftHand( hitEvent ) ? ["blood_left"] : ["blood_right"];
		foreach ( var hand in hands )
		{
			if ( Renderer.GetAttachment( hand ) is { } at )
				SprayBlood( blood, at.Position );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void SprayBlood( string effect, Vector3 position ) =>
		MountedEffects.Spawn( effect, new Transform( position, Rotation.LookAt( -WorldRotation.Forward ) * Rotation.FromPitch( 90 ) ), MountContent.Of( GameObject ) );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlaySound( string name )
	{
		if ( Sounds.Sound( name, MountContent.Of( GameObject ) ) is { } sound )
			Sound.Play( sound, WorldPosition );
	}

	/// <summary>Throws a player, like Source's ApplyAbsVelocityImpulse. Player movement is owner simulated, so the owner applies the push.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void KnockbackPlayer( GameObject victim, Vector3 velocity )
	{
		var player = victim?.GetComponent<PlayerController>();
		if ( !player.IsValid() || player.IsProxy )
			return;

		player.PreventGrounding( 0.2f );
		player.Body.Velocity += velocity;
	}

	bool IsUnderwater( Vector3 point ) => WaterBody.Find( Scene, point ).IsValid();

	protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
	{
		INpcPart.Hurt( this, damage );

		var attacker = damage.Instigator;
		if ( Flinches == FlinchRules.Any && _flinches.Length > 0 && _lastFlinch > 1 && Health > 0 )
		{
			_lastFlinch = 0;
			Flinch = Game.Random.FromArray( _flinches );
			FlinchSerial++;
		}
		else if ( Flinches is FlinchRules.HitGroup or FlinchRules.Zombie && Health > 0 )
		{
			// Gathered until its next think, so a shotgun blast's pellets count as one hit
			_hitDamage += damage.AcceptedDamage;
			_hitTags.UnionWith( damage.DamageInfo.Tags.TryGetAll() );
		}
		if ( attacker.IsValid() && !Passive && GetDisposition( attacker ) != Disposition.Friendly )
			SetRelationship( attacker, Disposition.Hostile, priority: 10 );
		if ( Health >= 1 )
			Say( VoiceLine.Pain );
		if ( !Ambient || !attacker.IsValid() || attacker == GameObject )
			return;

		_scaredOf = attacker;
		_sinceScared = 0;
		EndCurrentSchedule();
	}

	static readonly (string Tag, string Part)[] HitGroups =
		[("head", "HEAD"), ("chest", "CHEST"), ("stomach", "STOMACH"), ("left_arm", "LEFTARM"), ("right_arm", "RIGHTARM"), ("left_leg", "LEFTLEG"), ("right_leg", "RIGHTLEG")];

	/// <summary>Flinches at what hit it since the last think.</summary>
	void React()
	{
		var amount = _hitDamage;
		_hitDamage = 0;
		if ( amount > 0 && Health > 0 )
		{
			if ( Flinches == FlinchRules.Zombie )
				Stagger( amount );
			else
				FlinchAt( amount );
		}

		_hitTags.Clear();
	}

	/// <summary>
	/// The shared rule. A heavy hit, unless it flinched lately or is mid-swing, stops it for a full-body flinch for where the hit
	/// landed, then none for 3 to 5 seconds. Any other hit tries a gesture for where it landed, then none for half a second to a
	/// second more than the gesture, even when the model has none to play.
	/// </summary>
	void FlinchAt( float amount )
	{
		if ( _nextFlinch )
			_flinchedRecently = false;

		var part = HitGroups.FirstOrDefault( x => _hitTags.Contains( x.Tag ) ).Part;
		if ( amount > HeavyDamage && !_flinchedRecently && !Swinging )
		{
			var clip = (part is null ? null : FlinchClip( _fullFlinches, "ACT_FLINCH_" + part ))
				?? FlinchClip( _fullFlinches, "ACT_BIG_FLINCH" ) ?? FlinchClip( _fullFlinches, "ACT_SMALL_FLINCH" );
			if ( !_nextFlinch || _busy || clip is null )
				return;

			FullFlinch( clip );
			_nextFlinch = Game.Random.Float( 3, 5 );
			_flinchedRecently = true;
			return;
		}

		if ( !_nextFlinch )
			return;

		var gesture = (part is null ? null : FlinchClip( _flinches, "ACT_GESTURE_FLINCH_" + part ))
			?? (_flinchedRecently ? null : FlinchClip( _flinches, "ACT_GESTURE_BIG_FLINCH" )) ?? FlinchClip( _flinches, "ACT_GESTURE_SMALL_FLINCH" );
		PlayFlinchGesture( gesture );
		_nextFlinch = Game.Random.Float( 0.5f, 1 ) + ClipLength( gesture );
		_flinchedRecently = true;
	}

	/// <summary>
	/// The zombie's own rule: a buckshot hit of over a third of its health jerks its head back, every time; a physics impact of
	/// 5 or more, not the gravity gun's punt, stops it and staggers it even mid-swing, though not again while it staggers.
	/// </summary>
	void Stagger( float amount )
	{
		if ( _hitTags.Contains( "buckshot" ) && amount > MaxHealth / 3 )
			PlayFlinchGesture( FlinchClip( _flinches, "ACT_GESTURE_FLINCH_HEAD" ) );

		var impact = (_hitTags.Contains( DamageTags.Crush ) || _hitTags.Contains( "impact" )) && !_hitTags.Contains( "physgun" );
		if ( impact && amount >= 5 && !_busy && !(_flinching && Attacking) && FlinchClip( _fullFlinches, "ACT_FLINCH_PHYSICS" ) is { } clip )
			FullFlinch( clip );
	}

	/// <summary>Stops whatever it is doing, a swing included, for a full-body flinch.</summary>
	void FullFlinch( string clip )
	{
		Movement?.Stop();
		_strikeTarget = null;
		_facing = null;
		_flinching = true;
		_attackHolds = true;
		_attackRecovery = ClipLength( clip );
		LastAttack = 0;
		AttackClip = clip;
		AttackSerial++;
	}

	void PlayFlinchGesture( string clip )
	{
		if ( clip is null )
			return;

		Flinch = clip;
		FlinchSerial++;
	}

	/// <summary>One of the clips for an activity, at random.</summary>
	string FlinchClip( string[] clips, string activity )
	{
		var found = clips.Where( x => _sequences.Get( x )?.Activity == activity ).ToArray();
		return found.Length > 0 ? Game.Random.FromArray( found ) : null;
	}

	float ClipLength( string clip ) => Renderer.Model.AnimationClips().TryGetValue( clip ?? "", out var animation ) ? animation.Duration : 0;

	/// <summary>Passive walkers step away from whoever hurt them or anything they fear, make way, follow, wander a little and otherwise watch who is around.</summary>
	protected override BaseSchedule SelectSchedule()
	{
		if ( !Ambient )
			return null;

		var threat = _scaredOf.IsValid() && _sinceScared < 5 ? _scaredOf : Senses.FindNearestVisible( Disposition.Fearful );
		if ( threat.IsValid() )
		{
			NpcVoice.Say( this, Voice, VoiceLine.Fear, threat );
			var flee = GetSchedule<FleeSchedule>();
			flee.Source = threat;
			flee.PanicLevel = 0;
			return flee;
		}

		var pusher = Senses.GetPushingPlayer();
		if ( pusher.IsValid() )
		{
			var aside = GetSchedule<MoveAsideSchedule>();
			aside.Blocker = pusher;
			aside.Speed = PaceFor( false );
			return aside;
		}

		if ( Leader.IsValid() )
		{
			var follow = GetSchedule<FollowSchedule>();
			follow.Target = Leader;
			return follow;
		}

		if ( Game.Random.Float() < 0.15f )
		{
			var wander = GetSchedule<WanderSchedule>();
			wander.Radius = 160;
			return wander;
		}

		return GetSchedule<IdleSchedule>();
	}

	/// <summary>
	/// Its body running into a player, or a player into it, is no blow either way, however fast: the physics impact the
	/// engine reports for the contact is dropped. Its bite or claw is all a leap or a pounce does.
	/// </summary>
	bool BodyContactWithPlayer( Component.IDamageable.DamageEvent e, GameObject other ) =>
		e.DamageInfo.Tags.Contains( "impact" ) && other.IsValid() && other.GetComponentInParent<PlayerController>().IsValid();

	void Component.IDamageable.IEvents.OnDamaging( Component.IDamageable.DamageEvent e )
	{
		if ( e.Victim.IsValid() && e.Victim != GameObject && e.DamageInfo.Attacker?.Root == GameObject.Root && BodyContactWithPlayer( e, e.Victim ) )
			e.Cancelled = true;
	}

	protected override void OnDamaging( Component.IDamageable.DamageEvent damage )
	{
		if ( BodyContactWithPlayer( damage, damage.DamageInfo.Attacker ) )
		{
			damage.Cancelled = true;
			return;
		}

		INpcPart.PreDamage( this, damage );

		if ( BlastHits > 0 && damage.AcceptedDamage >= BlastHitThreshold && damage.DamageInfo.Tags.Contains( DamageTags.Explosion ) )
			damage.AcceptedDamage = Math.Max( damage.AcceptedDamage, MaxHealth / BlastHits + 1 );
	}

	/// <summary>A model without a collision model (the gunship) leaves no ragdoll that can fall, so it drops as a box of its bounds.</summary>
	protected override GameObject CreateRagdoll()
	{
		if ( !Renderer.IsValid() || Renderer.Model is null || Renderer.Model.Physics?.Parts.Count > 0 )
			return base.CreateRagdoll();

		var wreck = new GameObject( false, "Wreck" );
		wreck.Tags.Add( "ragdoll" );
		wreck.Tags.Add( "removable" );
		wreck.WorldTransform = Renderer.WorldTransform;
		var renderer = wreck.AddComponent<SkinnedModelRenderer>();
		renderer.CopyFrom( Renderer );
		renderer.UseAnimGraph = false;
		var bounds = Renderer.Model.Bounds;
		var box = wreck.AddComponent<BoxCollider>();
		box.Center = bounds.Center;
		box.Scale = bounds.Size;
		var body = wreck.AddComponent<Rigidbody>();
		body.Velocity = _body.IsValid() ? _body.Velocity : Vector3.Zero;
		wreck.Enabled = true;
		wreck.NetworkSpawn( true, null );
		return wreck;
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		// Before the ragdoll: a part may change the body it is made from
		INpcPart.Death( this, death );

		NpcVoice.SayDeath( this, Voice );
		base.OnDeath( death );
	}

	bool IPressable.CanPress( IPressable.Event e ) => Friendly || Passive;

	bool IPressable.Press( IPressable.Event e )
	{
		Follow( e.Source.GameObject );
		return true;
	}

	[Rpc.Host]
	void Follow( GameObject source )
	{
		var player = source?.GetComponentInParent<PlayerController>();
		if ( !player.IsValid() || !(Friendly || Passive) || player.WorldPosition.Distance( WorldPosition ) > 160 )
			return;

		if ( Rpc.Caller is not null && player.Network.Owner != Rpc.Caller )
			return;

		Leader = Leader == player.GameObject ? null : player.GameObject;
		NpcVoice.Say( this, Voice, Leader.IsValid() ? VoiceLine.Follow : VoiceLine.Unfollow, player.GameObject );
		EndCurrentSchedule();
	}

	protected override void OnDestroy()
	{
		_animation?.Dispose();
		base.OnDestroy();
	}
}

[Title( "Source Idle Animation" )]
[Category( "Source 1" )]
[Alias( "Source1.SourceIdleAnimation", "SourceIdleAnimation" )]
public sealed class IdleAnimation : Component
{
	[Property] public string Clip { get; set; } = "idle";
	AnimationPlayer _animation = new();

	protected override void OnUpdate()
	{
		if ( _animation.Ensure( GetComponentInChildren<SkinnedModelRenderer>(), [(Clip, true)] ) )
			_animation.Tick();
	}

	protected override void OnDestroy() => _animation?.Dispose();
}
