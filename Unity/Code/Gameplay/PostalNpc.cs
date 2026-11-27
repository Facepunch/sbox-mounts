using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using Sandbox.Npcs.Schedules;

namespace UnityMount;

public enum PostalNpcArchetype
{
	Civilian = 0,
	MeleeEnemy = 1,
	RangedEnemy = 2,
}

public enum PostalNpcAttackStyle
{
	None = 0,
	MeleeRush = 1,
	KarenBrawler = 2,
	GoliathSlam = 3,
	RangedSingle = 4,
	RangedBurst = 5,
	ShotgunSpread = 6,
	CrossbowBolt = 7,
	PlasmaSpit = 8,
}

/// <summary>
/// Authentic POSTAL: Brain Damaged NPC with grounded gravity/step-up movement, door kicking,
/// crowd separation, telegraphed melee wind-ups, gunfire civilian panic, 2x headshot criticals,
/// real Unity skeletal animations, and instant limp Source 2 ModelPhysics ragdolls on death!
/// </summary>
[Title( "POSTAL NPC" ), Category( "Unity Mount" ), Icon( "person" )]
public sealed class PostalNpc : BaseNpc, Component.IPressable
{
	static readonly string[] FleshHitSounds =
	[
		"mount://pbd/sounds/master/brs_bullet_hit_body_flesh_01.vsnd",
		"mount://pbd/sounds/master/brs_bullet_hit_body_flesh_02.vsnd",
		"mount://pbd/sounds/master/brs_bullet_hit_body_flesh_03.vsnd",
		"mount://pbd/sounds/master/brs_bullet_hit_body_flesh_04.vsnd",
		"mount://pbd/sounds/master/brs_bullet_hit_body_flesh_05.vsnd",
		"mount://pbd/sounds/master/brs_bullet_hit_body_flesh_06.vsnd",
	];

	[Property] public PostalNpcArchetype Archetype { get; set; } = PostalNpcArchetype.MeleeEnemy;
	[Property] public PostalNpcAttackStyle AttackStyle { get; set; } = PostalNpcAttackStyle.MeleeRush;
	[Property] public string ModelPath { get; set; } = "";
	[Property] public float MoveSpeed { get; set; } = 75f;
	[Property] public float RunSpeed { get; set; } = 155f;
	[Property] public float AttackDamage { get; set; } = 14f;
	[Property] public float AttackRange { get; set; } = 88f;
	[Property] public float AttackCooldown { get; set; } = 1.05f;
	[Property] public float ModelScale { get; set; } = 1f;
	[Property] public string NoticeSoundPath { get; set; } = "";
	[Property] public string AttackSoundPath { get; set; } = "";
	[Property] public string PainSoundPath { get; set; } = "";
	[Property] public string DeathSoundPath { get; set; } = "";

	public override string Faction => Archetype == PostalNpcArchetype.Civilian ? Factions.Citizen : Factions.Enemy;

	GameObject _visualPivot;
	ModelRenderer _visualMr;
	SkinnedModelRenderer _skinnedMr;
	Model _loadedModel;
	Vector3 _baseVisualLocalPos;
	float _capsuleRadius = 16f;
	float _capsuleHeight = 68f;

	// Resolved Unity skeletal sequence names
	string _seqIdle;
	string _seqWalk;
	string _seqMove;
	string _seqAttack;
	string _seqStagger;
	string _seqFlee;
	string _seqSpecial;
	float _actionAnimEndTime;
	float _staggerUntil;
	bool _isMovingAnim;

	// Telegraphed melee wind-up & ranged bursts
	bool _hasPendingMelee;
	float _pendingMeleeHitAt;
	GameObject _pendingMeleeTarget;
	Vector3 _pendingMeleeDir;
	bool _pendingMeleeStomp;
	int _burstRemaining;
	float _nextBurstShot;
	GameObject _burstTarget;

	// Civilian panic on nearby gunfire/explosions
	Vector3 _panicOrigin;
	float _panicUntil;
	Vector3 _fleeGoal;
	float _nextFleePickTime;

	// Clean NavMesh path cache + obstacle steering (zero NavMeshAgent rubber-banding)
	readonly List<Vector3> _navPath = [];
	int _navIndex;
	Vector3 _lastPathGoal;
	float _nextPathCalcTime;
	Vector3 _avoidDir;
	float _avoidUntil;
	Vector3 _stuckCheckPos;
	float _stuckCheckTime;
	float _nextDoorKickTime;

	// Grounded capsule physics & velocity tracking
	Vector3 _prevWorldPos;
	Vector3 _smoothedVelocity;
	float _smoothedHorizSpeed;
	float _verticalVelocity;
	bool _isGrounded = true;

	// Fallback motion & feedback
	float _walkPhase;
	float _lungeAnim;
	float _hitFlash;
	float _nextAttackTime;
	float _nextVoiceTime;
	Vector3 _wanderGoal;
	float _nextWanderTime;
	bool _hadTarget;
	bool _isDead;

	// SMG TimeBubble temporal slow-mo + Tesla Lightning electrocution VFX state
	float _temporalSlowUntil;
	float _temporalScale = 1f;
	float _electroShockUntil;
	float _nextElectroArcTime;

	public bool IsDead => _isDead;
	public Vector3 HeadWorldPosition => WorldPosition + Vector3.Up * (EyeHeight * 0.96f);
	public float EffectiveTimeScale => Time.Now < _temporalSlowUntil ? _temporalScale : 1f;

	public void ApplyTemporalSlow( float scale = 0.15f, float duration = 0.28f )
	{
		if ( _isDead ) return;
		_temporalScale = Math.Clamp( scale, 0.08f, 1f );
		_temporalSlowUntil = MathF.Max( _temporalSlowUntil, Time.Now + duration );
	}

	public void ApplyElectroShock( float duration = 0.48f )
	{
		if ( _isDead ) return;
		_electroShockUntil = MathF.Max( _electroShockUntil, Time.Now + duration );
	}

	/// <summary>
	/// Alerts nearby civilians when weapons fire or explosions detonate so they scream and flee in panic!
	/// </summary>
	public static void AlertCiviliansNear( Scene scene, Vector3 origin, float radius = 850f )
	{
		if ( !scene.IsValid() ) return;
		foreach ( var npc in scene.GetAllComponents<PostalNpc>() )
		{
			if ( !npc.IsValid() || npc._isDead || npc.Archetype != PostalNpcArchetype.Civilian )
				continue;
			if ( npc.WorldPosition.Distance( origin ) <= radius )
				npc.TriggerPanic( origin );
		}
	}

	public void TriggerPanic( Vector3 threatOrigin )
	{
		bool wasPanicking = Time.Now < _panicUntil;
		_panicOrigin = threatOrigin;
		_panicUntil = Time.Now + Game.Random.Float( 6f, 9f );
		if ( !wasPanicking )
		{
			_nextFleePickTime = 0f;
			if ( Time.Now >= _nextVoiceTime )
			{
				PlayVoice( NoticeSoundPath, 0.92f );
				_nextVoiceTime = Time.Now + 3.5f;
			}
		}
	}

	protected override void OnAwake()
	{
		base.OnAwake();
		EnsureComponents();
	}

	protected override void OnStart()
	{
		base.OnStart();
		EnsureComponents();
		EnsureVisualModel();
		SyncCapsuleMetrics();
		DepenetrateIfStuck();
		SnapToGroundImmediate();
		_prevWorldPos = WorldPosition;
		_stuckCheckPos = WorldPosition;
		_stuckCheckTime = Time.Now + 0.45f;
		_wanderGoal = WorldPosition;
		_fleeGoal = WorldPosition;
		_nextWanderTime = Time.Now + Game.Random.Float( 1.5f, 4.5f );
	}

	void EnsureComponents()
	{
		// Never keep a NavMeshAgent moving WorldPosition (prevents AgentPosition rubber-banding/teleporting).
		var legacyAgent = GetComponent<NavMeshAgent>();
		if ( legacyAgent.IsValid() )
			legacyAgent.Destroy();

		if ( GetComponent<SensesLayer>() is null )
		{
			var senses = GameObject.AddComponent<SensesLayer>();
			senses.SightRange = Archetype == PostalNpcArchetype.RangedEnemy ? 1550f : 1150f;
			senses.HearingRange = 1250f;
			senses.FieldOfView = 155f;
		}
	}

	void SyncCapsuleMetrics()
	{
		var cap = GetComponent<CapsuleCollider>();
		if ( cap.IsValid() )
		{
			_capsuleRadius = Math.Clamp( cap.Radius, 12f, 34f );
			_capsuleHeight = Math.Clamp( cap.End.z + _capsuleRadius, 40f, 136f );
		}
	}

	internal static float ComputeVisualGroundOffsetZ( Model model, float scale )
	{
		if ( model is null || model.IsError ) return 0f;
		var b = model.Bounds;
		if ( model.BoneCount > 0 && MathF.Abs( b.Mins.z ) < 9f )
			return 0f;
		return -b.Mins.z * scale;
	}

	void EnsureVisualModel()
	{
		if ( !_visualPivot.IsValid() )
		{
			_visualPivot = GameObject.Children.FirstOrDefault( c => c.Name == "Visual" );
			if ( _visualPivot.IsValid() )
			{
				_skinnedMr = _visualPivot.GetComponent<SkinnedModelRenderer>();
				_visualMr = _skinnedMr.IsValid() ? _skinnedMr : _visualPivot.GetComponent<ModelRenderer>();
				_loadedModel = _visualMr.IsValid() ? _visualMr.Model : null;
				float sc = ModelScale > 0.05f ? ModelScale : 1f;
				_baseVisualLocalPos = new Vector3( 0f, 0f, ComputeVisualGroundOffsetZ( _loadedModel, sc ) );
				_visualPivot.LocalPosition = _baseVisualLocalPos;
				if ( _skinnedMr.IsValid() )
					ResolveNpcSequences();
			}
		}

		if ( _visualMr.IsValid() && _visualMr.Model is not null && !_visualMr.Model.IsError )
			return;

		if ( string.IsNullOrWhiteSpace( ModelPath ) )
			return;

		_loadedModel = Model.Load( ModelPath );
		if ( _loadedModel is null || _loadedModel.IsError )
			return;

		if ( !_visualPivot.IsValid() )
		{
			_visualPivot = new GameObject( GameObject, true, "Visual" );
			_visualPivot.Flags |= GameObjectFlags.NotSaved;
		}

		if ( _loadedModel.BoneCount > 0 )
		{
			_skinnedMr = _visualPivot.GetOrAddComponent<SkinnedModelRenderer>();
			_skinnedMr.Model = _loadedModel;
			_skinnedMr.UseAnimGraph = false;
			_skinnedMr.RenderType = ModelRenderer.ShadowRenderType.On;
			_visualMr = _skinnedMr;
			ResolveNpcSequences();
		}
		else
		{
			_visualMr = _visualPivot.GetOrAddComponent<ModelRenderer>();
			_visualMr.Model = _loadedModel;
			_visualMr.RenderType = ModelRenderer.ShadowRenderType.On;
		}

		var b = _loadedModel.Bounds;
		float scale = ModelScale > 0.05f ? ModelScale : 1f;
		_visualPivot.LocalScale = new Vector3( scale );
		_baseVisualLocalPos = new Vector3( 0f, 0f, ComputeVisualGroundOffsetZ( _loadedModel, scale ) );
		_visualPivot.LocalPosition = _baseVisualLocalPos;

		float height = Math.Clamp( b.Size.z * scale, 40f, 136f );
		float radius = Math.Clamp( MathF.Max( b.Size.x, b.Size.y ) * 0.24f * scale, 13f, 34f );
		var cap = GetOrAddComponent<CapsuleCollider>();
		cap.Start = Vector3.Up * radius;
		cap.End = Vector3.Up * MathF.Max( radius + 8f, height - radius );
		cap.Radius = radius;
		_capsuleRadius = radius;
		_capsuleHeight = height;
		EyeHeight = height * 0.82f;
	}

	void ResolveNpcSequences()
	{
		if ( !_skinnedMr.IsValid() || !_skinnedMr.SceneModel.IsValid() ) return;
		var names = _skinnedMr.Sequence.SequenceNames?.ToList() ?? [];
		if ( names.Count == 0 ) return;

		_skinnedMr.Sequence.Blending = true;

		string FindSeq( params string[] keys )
		{
			foreach ( var k in keys )
			{
				foreach ( var n in names )
					if ( string.Equals( n, k, StringComparison.OrdinalIgnoreCase ) ) return n;
			}
			foreach ( var k in keys )
			{
				foreach ( var n in names )
					if ( n.Contains( k, StringComparison.OrdinalIgnoreCase ) ) return n;
			}
			return null;
		}

		_seqIdle = FindSeq( "idle", "idle_02", "idle2", "idletarget" ) ?? names[0];
		_seqWalk = FindSeq( "walk", "trot", "chase", "run", "run2" ) ?? _seqIdle;
		_seqMove = FindSeq( "chase", "chase2", "chase_02", "run", "run2", "chasefast", "chasefaster", "walk", "trot" ) ?? _seqIdle;
		_seqAttack = FindSeq( "melee", "attack", "attack1", "fire", "shoot", "shootreload2", "shootreload", "shootseries", "stompfront", "flamethrower_startv2" ) ?? _seqIdle;
		_seqStagger = FindSeq( "stagger", "stagger2", "stagger_02", "flinch" );
		_seqFlee = FindSeq( "terrified", "scared", "run", "run2" ) ?? _seqMove;
		_seqSpecial = FindSeq( "stompfront", "dancea", "idletomelee", "reload2", "reload_02" );

		PlayNpcAnim( _seqIdle, loop: true );
	}

	void PlayNpcAnim( string seq, bool loop, float rate = 1f, bool lockUntilDone = false )
	{
		if ( !_skinnedMr.IsValid() || !_skinnedMr.SceneModel.IsValid() || string.IsNullOrEmpty( seq ) )
			return;

		bool sameLoop = loop && string.Equals( _skinnedMr.Sequence.Name, seq, StringComparison.OrdinalIgnoreCase ) && _skinnedMr.Sequence.Looping;
		if ( sameLoop )
		{
			_skinnedMr.Sequence.PlaybackRate = rate;
			return;
		}

		_skinnedMr.Sequence.Name = seq;
		_skinnedMr.Sequence.Looping = loop;
		_skinnedMr.Sequence.PlaybackRate = rate;
		_skinnedMr.Sequence.Time = 0f;

		if ( lockUntilDone )
		{
			float dur = _skinnedMr.Sequence.Duration > 0.05f ? _skinnedMr.Sequence.Duration / MathF.Max( rate, 0.25f ) : 0.6f;
			_actionAnimEndTime = Time.Now + MathF.Min( dur, 1.4f );
		}
	}

	internal static void AttachGroundedVisual( GameObject parent, Model model, float modelScale, CapsuleCollider cap = null, PostalNpc npc = null )
	{
		if ( model is null || model.IsError ) return;
		var vis = new GameObject( parent, true, "Visual" );

		if ( model.BoneCount > 0 )
		{
			var smr = vis.AddComponent<SkinnedModelRenderer>();
			smr.Model = model;
			smr.UseAnimGraph = false;
			smr.RenderType = ModelRenderer.ShadowRenderType.On;
		}
		else
		{
			var mr = vis.AddComponent<ModelRenderer>();
			mr.Model = model;
			mr.RenderType = ModelRenderer.ShadowRenderType.On;
		}

		var b = model.Bounds;
		float scale = modelScale > 0.05f ? modelScale : 1f;
		vis.LocalScale = new Vector3( scale );
		vis.LocalPosition = new Vector3( 0f, 0f, ComputeVisualGroundOffsetZ( model, scale ) );

		float height = Math.Clamp( b.Size.z * scale, 36f, 136f );
		float radius = Math.Clamp( MathF.Max( b.Size.x, b.Size.y ) * 0.24f * scale, 14f, 36f );
		if ( cap is not null )
		{
			cap.Start = Vector3.Up * radius;
			cap.End = Vector3.Up * MathF.Max( radius + 8f, height - radius );
			cap.Radius = radius;
		}
		if ( npc is not null )
			npc.EyeHeight = height * 0.82f;
	}

	protected override void SetupRelationships()
	{
		if ( Archetype == PostalNpcArchetype.Civilian )
		{
			SetRelationship( Factions.Enemy, Disposition.Fearful );
			SetRelationship( Factions.Monster, Disposition.Fearful );
		}
		else
		{
			SetRelationship( Factions.Player, Disposition.Hostile );
			SetRelationship( Factions.Ally, Disposition.Hostile );
			SetRelationship( Factions.Citizen, Disposition.Hostile );
		}
	}

	protected override BaseSchedule SelectSchedule() => null;

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( _isDead )
			return;

		EnsureVisualModel();
		UpdatePendingMelee();
		UpdatePendingBurst();

		if ( Time.Now >= _staggerUntil )
			UpdateAiBehavior();
		else
			StopMovement();

		UpdateGroundedGravity();

		float dt = MathF.Max( Time.Delta, 0.001f );
		var frameVel = (WorldPosition - _prevWorldPos).WithZ( 0 ) / dt;
		if ( frameVel.Length < 800f )
		{
			float blend = Math.Clamp( dt * 10f, 0f, 1f );
			_smoothedVelocity = Vector3.Lerp( _smoothedVelocity, frameVel, blend );
			_smoothedHorizSpeed = _smoothedVelocity.Length;
		}
		_prevWorldPos = WorldPosition;

		UpdateVisualAnimation();
	}

	Capsule MakeBodyCapsule( float radiusInset = 0.5f, float bottomClearance = 3f )
	{
		float r = MathF.Max( _capsuleRadius - radiusInset, 9f );
		float bottomZ = r + bottomClearance;
		float topZ = MathF.Max( bottomZ + 8f, _capsuleHeight - r );
		return new Capsule( Vector3.Up * bottomZ, Vector3.Up * topZ, r );
	}

	SceneTraceResult TraceBody( Vector3 from, Vector3 to, float radiusInset = 0.5f, float bottomClearance = 3f )
	{
		return Scene.Trace.Capsule( MakeBodyCapsule( radiusInset, bottomClearance ), from, to )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "debris", "ragdoll", "npc", "playercontroller", "player" )
			.Run();
	}

	void DepenetrateIfStuck()
	{
		var check = TraceBody( WorldPosition, WorldPosition, radiusInset: 1.0f, bottomClearance: 4f );
		if ( !check.StartedSolid )
			return;

		// Search outward rings for nearest clear standing spot so NPCs never start embedded in walls
		float[] radii = [8f, 18f, 32f, 48f];
		foreach ( float dist in radii )
		{
			for ( int i = 0; i < 8; i++ )
			{
				float ang = i * (MathF.PI * 0.25f);
				var candidate = WorldPosition + new Vector3( MathF.Cos( ang ) * dist, MathF.Sin( ang ) * dist, 2f );
				var tr = TraceBody( candidate, candidate, radiusInset: 1.0f, bottomClearance: 4f );
				if ( !tr.StartedSolid )
				{
					WorldPosition = candidate;
					return;
				}
			}
		}
	}

	void SnapToGroundImmediate()
	{
		var tr = TraceBody( WorldPosition + Vector3.Up * 36f, WorldPosition - Vector3.Up * 220f, radiusInset: 2f, bottomClearance: 0f );
		if ( tr.Hit && !tr.StartedSolid && tr.Normal.z >= 0.6f )
		{
			WorldPosition = WorldPosition.WithZ( tr.EndPosition.z );
			_verticalVelocity = 0f;
			_isGrounded = true;
			return;
		}

		var ray = Scene.Trace.Ray( WorldPosition + Vector3.Up * 44f, WorldPosition - Vector3.Up * 220f )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "debris", "ragdoll", "npc", "playercontroller", "player" )
			.Run();
		if ( ray.Hit && ray.Normal.z >= 0.6f )
		{
			WorldPosition = WorldPosition.WithZ( ray.HitPosition.z );
			_verticalVelocity = 0f;
			_isGrounded = true;
		}
	}

	void UpdateGroundedGravity()
	{
		float dt = MathF.Min( Time.Delta, 0.05f );
		float probeDown = _isGrounded ? 26f : MathF.Max( 24f, -_verticalVelocity * dt + 14f );

		var tr = TraceBody( WorldPosition + Vector3.Up * 6f, WorldPosition - Vector3.Up * probeDown, radiusInset: 2f, bottomClearance: 0f );

		if ( tr.Hit && !tr.StartedSolid && tr.Normal.z >= 0.65f && _verticalVelocity <= 20f )
		{
			float dz = WorldPosition.z - tr.EndPosition.z;
			if ( dz <= 24f && dz >= -18f )
			{
				WorldPosition = WorldPosition.WithZ( tr.EndPosition.z );
				_verticalVelocity = 0f;
				_isGrounded = true;
				return;
			}
		}

		_isGrounded = false;
		_verticalVelocity = MathF.Max( _verticalVelocity - 800f * dt, -1400f );
		float fallStep = _verticalVelocity * dt;
		var fallTr = TraceBody( WorldPosition, WorldPosition + Vector3.Up * fallStep, radiusInset: 2f, bottomClearance: 0f );

		if ( fallTr.Hit && !fallTr.StartedSolid && fallStep < 0f )
		{
			WorldPosition = WorldPosition.WithZ( fallTr.EndPosition.z );
			if ( fallTr.Normal.z >= 0.65f )
			{
				_verticalVelocity = 0f;
				_isGrounded = true;
			}
		}
		else if ( !fallTr.StartedSolid )
		{
			WorldPosition = WorldPosition.WithZ( WorldPosition.z + fallStep );
		}
	}

	void UpdatePendingMelee()
	{
		if ( !_hasPendingMelee || Time.Now < _pendingMeleeHitAt )
			return;

		_hasPendingMelee = false;
		if ( Time.Now < _staggerUntil )
			return; // Interrupted by stagger!

		var dir = _pendingMeleeDir.LengthSquared > 0.01f ? _pendingMeleeDir.Normal : WorldRotation.Forward;

		if ( AttackStyle == PostalNpcAttackStyle.GoliathSlam )
		{
			var slamPos = WorldPosition + dir * 52f;
			PlayVoice( AttackSoundPath, 1.0f );
			PostalVfx.SpawnShockwave( Scene, slamPos, 195f, new Color( 1f, 0.55f, 0.12f, 1f ), 0.32f, 8f );
			PostalVfx.SpawnExplosion( Scene, slamPos + Vector3.Up * 10f, 125f, new Color( 1f, 0.5f, 0.1f, 1f ) );

			var hitRoots = new HashSet<GameObject> { GameObject.Root };
			foreach ( var go in Scene.FindInPhysics( new Sphere( slamPos, 195f ) ) )
			{
				if ( !go.IsValid() || !hitRoots.Add( go.Root ) || go.Tags.Has( "world" ) ) continue;
				var info = new DamageInfo( AttackDamage, GameObject, GameObject ) { Position = slamPos };
				info.Tags.Add( DamageTags.Melee );
				foreach ( var d in go.Components.GetAll<Component.IDamageable>( FindMode.EverythingInSelfAndAncestors ) )
					d.OnDamage( info );
				var pc = go.Components.GetInAncestorsOrSelf<PlayerController>();
				if ( pc.IsValid() && pc.Body.IsValid() )
					pc.Body.Velocity += ((go.WorldPosition - slamPos).WithZ( 0 ).Normal + Vector3.Up * 0.65f).Normal * 460f;
			}
			return;
		}

		if ( _pendingMeleeStomp )
		{
			var stompPos = WorldPosition + dir * 42f;
			PostalVfx.SpawnShockwave( Scene, stompPos, 120f, new Color( 1f, 0.2f, 0.75f, 1f ), 0.24f, 6f );
		}

		if ( _pendingMeleeTarget.IsValid() && WorldPosition.Distance( _pendingMeleeTarget.WorldPosition ) <= AttackRange * 1.25f )
		{
			var dmgInfo = new DamageInfo( AttackDamage * (_pendingMeleeStomp ? 1.3f : 1f), GameObject, GameObject )
			{
				Position = _pendingMeleeTarget.WorldPosition + Vector3.Up * 36f
			};
			dmgInfo.Tags.Add( DamageTags.Melee );
			foreach ( var d in _pendingMeleeTarget.Components.GetAll<Component.IDamageable>( FindMode.EverythingInSelfAndAncestors ) )
				d.OnDamage( dmgInfo );
		}
	}

	void UpdatePendingBurst()
	{
		if ( _burstRemaining <= 0 || Time.Now < _nextBurstShot )
			return;

		_burstRemaining--;
		_nextBurstShot = Time.Now + 0.16f;
		if ( _burstTarget.IsValid() )
			FireEnemyProjectile( _burstTarget, speed: 880f, spread: 24f, color: new Color( 1f, 0.7f, 0.2f, 1f ) );
	}

	void UpdateAiBehavior()
	{
		if ( !Networking.IsHost )
			return;

		// Panicking civilians pick an open street route away from gunfire/explosions instead of running into walls!
		if ( Archetype == PostalNpcArchetype.Civilian && Time.Now < _panicUntil )
		{
			_hadTarget = true;
			if ( Time.Now >= _nextFleePickTime || WorldPosition.WithZ( 0 ).Distance( _fleeGoal.WithZ( 0 ) ) < 48f )
			{
				_fleeGoal = PickFleeDestination( _panicOrigin );
				_nextFleePickTime = Time.Now + Game.Random.Float( 1.6f, 2.6f );
			}
			var actualMove = MoveGroundedTowards( _fleeGoal, RunSpeed );
			FaceDirection( actualMove.LengthSquared > 0.0004f ? actualMove : (_fleeGoal - WorldPosition).WithZ( 0 ) );
			return;
		}

		GameObject target = FindPrimaryTarget();

		if ( target.IsValid() )
		{
			if ( !_hadTarget && Time.Now >= _nextVoiceTime )
			{
				PlayVoice( NoticeSoundPath, 0.9f );
				_nextVoiceTime = Time.Now + 4.5f;
			}
			_hadTarget = true;

			var toTarget = (target.WorldPosition - WorldPosition).WithZ( 0 );
			float dist = toTarget.Length;
			var dir = dist > 1f ? toTarget / dist : WorldRotation.Forward;

			if ( Archetype == PostalNpcArchetype.Civilian )
			{
				if ( Time.Now >= _nextFleePickTime || WorldPosition.WithZ( 0 ).Distance( _fleeGoal.WithZ( 0 ) ) < 48f )
				{
					_fleeGoal = PickFleeDestination( target.WorldPosition );
					_nextFleePickTime = Time.Now + Game.Random.Float( 1.5f, 2.4f );
				}
				var actualMove = MoveGroundedTowards( _fleeGoal, RunSpeed );
				FaceDirection( actualMove.LengthSquared > 0.0004f ? actualMove : (_fleeGoal - WorldPosition).WithZ( 0 ) );
				return;
			}

			if ( Archetype == PostalNpcArchetype.MeleeEnemy )
			{
				if ( dist > AttackRange * 0.85f )
				{
					var actualMove = MoveGroundedTowards( target.WorldPosition, RunSpeed );
					FaceDirection( actualMove.LengthSquared > 0.0004f ? actualMove : dir );
				}
				else
				{
					StopMovement();
					FaceDirection( dir );
					TryMeleeStrike( target, dir );
				}
			}
			else
			{
				float preferredDist = MathF.Max( AttackRange * 0.62f, 320f );
				if ( dist > AttackRange )
				{
					var actualMove = MoveGroundedTowards( target.WorldPosition, RunSpeed );
					FaceDirection( actualMove.LengthSquared > 0.0004f ? actualMove : dir );
				}
				else if ( dist < preferredDist * 0.55f )
				{
					if ( Time.Now >= _nextFleePickTime )
					{
						_fleeGoal = PickFleeDestination( target.WorldPosition, 200f );
						_nextFleePickTime = Time.Now + 1.1f;
					}
					MoveGroundedTowards( _fleeGoal, MoveSpeed );
					FaceDirection( dir );
				}
				else
				{
					StopMovement();
					FaceDirection( dir );
				}

				// Only fire once actually facing the target!
				if ( dist <= AttackRange * 1.15f && Vector3.Dot( WorldRotation.Forward.WithZ( 0 ).Normal, dir ) > 0.72f )
					TryRangedAttack( target );
			}
		}
		else
		{
			_hadTarget = false;
			float wanderSpeed = MoveSpeed * 0.72f;

			if ( Time.Now >= _nextWanderTime || WorldPosition.WithZ( 0 ).Distance( _wanderGoal.WithZ( 0 ) ) < 48f )
			{
				_wanderGoal = PickWanderDestination();
				_nextWanderTime = Time.Now + Game.Random.Float( 4.5f, 8.5f );
			}

			var toGoal = (_wanderGoal - WorldPosition).WithZ( 0 );
			if ( toGoal.Length > 48f )
			{
				var actualMove = MoveGroundedTowards( _wanderGoal, wanderSpeed );
				FaceDirection( actualMove.LengthSquared > 0.0004f ? actualMove : toGoal );
			}
			else
			{
				StopMovement();
			}
		}
	}

	Vector3 PickFleeDestination( Vector3 threatPos, float desiredDist = 360f )
	{
		var away = (WorldPosition - threatPos).WithZ( 0 );
		var baseDir = away.LengthSquared > 1f ? away.Normal : WorldRotation.Forward;
		float[] anglesDeg = [0f, 35f, -35f, 70f, -70f, 110f, -110f, 150f];

		Vector3 bestPoint = WorldPosition;
		float bestScore = float.MinValue;

		foreach ( float deg in anglesDeg )
		{
			var candDir = (Rotation.FromYaw( deg ) * baseDir).WithZ( 0 ).Normal;
			var tr = TraceBody( WorldPosition, WorldPosition + candDir * desiredDist );
			if ( tr.StartedSolid ) continue;

			float clearDist = tr.Hit ? MathF.Max( 0f, tr.Distance - 20f ) : desiredDist;
			if ( clearDist < 48f ) continue;

			var candPos = WorldPosition + candDir * clearDist;
			if ( Scene.NavMesh.IsEnabled )
			{
				var navPt = Scene.NavMesh.GetClosestPoint( candPos );
				if ( navPt.HasValue && navPt.Value.WithZ( 0 ).Distance( candPos.WithZ( 0 ) ) < 96f )
					candPos = navPt.Value;
			}

			float distFromThreat = candPos.WithZ( 0 ).Distance( threatPos.WithZ( 0 ) );
			float score = clearDist + distFromThreat * 0.65f - MathF.Abs( deg ) * 0.8f;
			if ( score > bestScore )
			{
				bestScore = score;
				bestPoint = candPos;
			}
		}

		return bestPoint;
	}

	Vector3 PickWanderDestination()
	{
		for ( int attempt = 0; attempt < 6; attempt++ )
		{
			float yaw = Game.Random.Float( 0f, 360f );
			float dist = Game.Random.Float( 140f, 300f );
			var dir = Rotation.FromYaw( yaw ).Forward;
			var tr = TraceBody( WorldPosition, WorldPosition + dir * dist );
			if ( tr.StartedSolid ) continue;

			float clear = tr.Hit ? MathF.Max( 0f, tr.Distance - 24f ) : dist;
			if ( clear < 70f ) continue;

			var cand = WorldPosition + dir * clear;
			if ( Scene.NavMesh.IsEnabled )
			{
				var navPt = Scene.NavMesh.GetClosestPoint( cand );
				if ( navPt.HasValue && navPt.Value.WithZ( 0 ).Distance( cand.WithZ( 0 ) ) < 96f )
					return navPt.Value;
			}
			return cand;
		}

		return WorldPosition;
	}

	GameObject FindPrimaryTarget()
	{
		if ( Senses is not null )
		{
			if ( Archetype == PostalNpcArchetype.Civilian )
			{
				var threat = Senses.VisibleThreats?.FirstOrDefault( x => x.IsValid() );
				if ( threat.IsValid() ) return threat;
			}
			else
			{
				var best = Senses.FindBestTarget();
				if ( best.IsValid() ) return best;
			}
		}

		if ( Archetype == PostalNpcArchetype.Civilian )
			return null;

		float maxDetect = Archetype == PostalNpcArchetype.RangedEnemy ? 1300f : 940f;
		GameObject closest = null;
		float bestDist = maxDetect;
		foreach ( var pc in Scene.GetAllComponents<PlayerController>() )
		{
			if ( !pc.IsValid() ) continue;
			float d = WorldPosition.Distance( pc.WorldPosition );
			if ( d >= bestDist ) continue;

			var tr = Scene.Trace.Ray( WorldPosition + Vector3.Up * EyeHeight, pc.WorldPosition + Vector3.Up * 48f )
				.IgnoreGameObjectHierarchy( GameObject )
				.IgnoreGameObjectHierarchy( pc.GameObject )
				.WithoutTags( "trigger", "debris", "ragdoll" )
				.Run();
			if ( !tr.Hit )
			{
				bestDist = d;
				closest = pc.GameObject;
			}
		}
		return closest;
	}

	void StopMovement()
	{
		_navPath.Clear();
		_navIndex = 0;
		_avoidUntil = 0f;
	}

	Vector3 ComputeCrowdSeparation()
	{
		var sep = Vector3.Zero;
		foreach ( var other in Scene.GetAllComponents<PostalNpc>() )
		{
			if ( !other.IsValid() || other == this || other._isDead ) continue;
			var diff = (WorldPosition - other.WorldPosition).WithZ( 0 );
			float d = diff.Length;
			if ( d > 0.1f && d < 46f )
				sep += (diff / d) * ((46f - d) / 46f);
		}
		return sep;
	}

	Vector3 ResolveSteeringDirection( Vector3 targetPos )
	{
		// Active obstacle-avoidance detour overrides straight line briefly so NPCs glide around props/corners
		if ( Time.Now < _avoidUntil && _avoidDir.LengthSquared > 0.01f )
			return _avoidDir;

		Vector3 waypoint = targetPos;

		// Query s&box NavMesh path cleanly (throttled) without letting NavMeshAgent teleport WorldPosition!
		if ( Scene.NavMesh.IsEnabled )
		{
			bool needPath = Time.Now >= _nextPathCalcTime
				|| _navPath.Count == 0
				|| _lastPathGoal.WithZ( 0 ).Distance( targetPos.WithZ( 0 ) ) > 96f;

			if ( needPath )
			{
				_nextPathCalcTime = Time.Now + 0.38f;
				_lastPathGoal = targetPos;
				_navPath.Clear();
				_navIndex = 0;

				var path = Scene.NavMesh.CalculatePath( new Sandbox.Navigation.CalculatePathRequest
				{
					Start = WorldPosition,
					Target = targetPos
				} );

				if ( path.IsValid && path.Points is not null && path.Points.Count >= 2 )
				{
					foreach ( var pt in path.Points )
						_navPath.Add( pt.Position );
					_navIndex = 1;
				}
			}

			while ( _navIndex < _navPath.Count && WorldPosition.WithZ( 0 ).Distance( _navPath[_navIndex].WithZ( 0 ) ) < 34f )
				_navIndex++;

			if ( _navIndex < _navPath.Count )
				waypoint = _navPath[_navIndex];
		}

		var delta = (waypoint - WorldPosition).WithZ( 0 );
		if ( delta.LengthSquared < 1f )
			return Vector3.Zero;

		var desired = delta.Normal + ComputeCrowdSeparation() * 0.45f;
		return desired.LengthSquared > 0.001f ? desired.Normal : delta.Normal;
	}

	void PickObstacleDetour( Vector3 desiredDir )
	{
		float[] anglesDeg = [50f, -50f, 95f, -95f, 140f, -140f];
		Vector3 bestDir = Vector3.Zero;
		float bestClear = 0f;

		foreach ( float deg in anglesDeg )
		{
			var cand = (Rotation.FromYaw( deg ) * desiredDir).WithZ( 0 ).Normal;
			var tr = TraceBody( WorldPosition, WorldPosition + cand * 140f );
			if ( tr.StartedSolid ) continue;

			float clear = tr.Hit ? tr.Distance : 140f;
			if ( clear > bestClear )
			{
				bestClear = clear;
				bestDir = cand;
			}
		}

		if ( bestClear >= 32f )
		{
			_avoidDir = bestDir;
			_avoidUntil = Time.Now + 0.75f;
		}
		else if ( Archetype == PostalNpcArchetype.Civilian )
		{
			_nextFleePickTime = 0f;
			_nextWanderTime = 0f;
		}
	}

	Vector3 MoveGroundedTowards( Vector3 targetPos, float speed )
	{
		float dt = MathF.Min( Time.Delta, 0.05f ) * EffectiveTimeScale;
		var toGoal = (targetPos - WorldPosition).WithZ( 0 );
		if ( toGoal.Length <= 10f )
			return Vector3.Zero;

		var moveDir = ResolveSteeringDirection( targetPos );
		if ( moveDir.LengthSquared < 0.001f )
			return Vector3.Zero;

		float stepDist = MathF.Min( speed * dt, toGoal.Length );
		var startPos = WorldPosition;
		bool blockedByWall = MoveWithCapsule( moveDir * stepDist );
		var actualDelta = (WorldPosition - startPos).WithZ( 0 );

		// Detect genuine stuck state and pick an open side detour instead of vibrating against walls
		if ( EffectiveTimeScale > 0.5f && Time.Now >= _stuckCheckTime )
		{
			float progressed = WorldPosition.WithZ( 0 ).Distance( _stuckCheckPos.WithZ( 0 ) );
			_stuckCheckPos = WorldPosition;
			_stuckCheckTime = Time.Now + 0.42f;
			if ( progressed < 10f && (blockedByWall || actualDelta.Length < stepDist * 0.25f) )
				PickObstacleDetour( moveDir );
		}

		return actualDelta;
	}

	bool MoveWithCapsule( Vector3 wishMove )
	{
		var curPos = WorldPosition;
		var remaining = wishMove.WithZ( 0 );
		if ( remaining.LengthSquared < 1e-6f )
			return false;

		var origDir = remaining.Normal;
		bool hitWall = false;

		for ( int iter = 0; iter < 3; iter++ )
		{
			if ( remaining.LengthSquared < 0.0025f )
				break;

			var tr = TraceBody( curPos, curPos + remaining );

			if ( tr.StartedSolid )
			{
				DepenetrateIfStuck();
				return true;
			}

			if ( !tr.Hit )
			{
				curPos += remaining;
				break;
			}

			hitWall = true;

			// Kick open closed doors automatically while moving!
			if ( tr.GameObject.IsValid() && Time.Now >= _nextDoorKickTime )
			{
				var door = tr.GameObject.Components.GetInAncestorsOrSelf<UnityDoor>();
				if ( door.IsValid() )
				{
					door.OpenAwayFrom( curPos, fastKick: true, syncPartner: true );
					_nextDoorKickTime = Time.Now + 0.45f;
					break;
				}
			}

			// Advance only up to a safe skin distance before the hit surface (never penetrate!)
			float safeFrac = MathF.Max( 0f, tr.Fraction - 0.02f );
			curPos += remaining * safeFrac;
			var leftover = remaining * (1f - safeFrac);

			// Safe step-up for genuine stairs/curbs (<= 20u) using full body capsule + anti-wall-clip ray check
			if ( MathF.Abs( tr.Normal.z ) < 0.35f && leftover.Length > 0.25f )
			{
				if ( TryStepUp( curPos, leftover, out var steppedPos ) )
				{
					curPos = steppedPos;
					hitWall = false;
					break;
				}
			}

			// Project remaining velocity cleanly along wall plane (never reverse direction into jitter)
			var n = tr.Normal.WithZ( 0 );
			if ( n.LengthSquared < 0.001f )
				break;

			n = n.Normal;
			remaining = leftover - n * Vector3.Dot( leftover, n );
			if ( Vector3.Dot( remaining, origDir ) <= 0.05f )
				break;
		}

		WorldPosition = curPos;
		return hitWall;
	}

	bool TryStepUp( Vector3 curPos, Vector3 horizStep, out Vector3 steppedPos )
	{
		steppedPos = curPos;
		const float MaxStepUp = 20f;

		var upTr = TraceBody( curPos, curPos + Vector3.Up * MaxStepUp );
		if ( upTr.StartedSolid )
			return false;

		float lift = MaxStepUp * MathF.Max( 0f, upTr.Fraction - 0.02f );
		if ( lift < 4f )
			return false;

		var raisedPos = curPos + Vector3.Up * lift;
		var fwdTr = TraceBody( raisedPos, raisedPos + horizStep );
		if ( fwdTr.StartedSolid || (fwdTr.Hit && fwdTr.Fraction < 0.5f) )
			return false;

		float movedFrac = fwdTr.Hit ? MathF.Max( 0f, fwdTr.Fraction - 0.02f ) : 1f;
		var afterFwd = raisedPos + horizStep * movedFrac;

		var downTr = TraceBody( afterFwd, afterFwd - Vector3.Up * (lift + 6f), radiusInset: 1.5f, bottomClearance: 0f );
		if ( !downTr.Hit || downTr.StartedSolid || downTr.Normal.z < 0.7f )
			return false;

		float dz = downTr.EndPosition.z - curPos.z;
		if ( dz < 1f || dz > MaxStepUp )
			return false;

		// Ensure no thin wall separates current torso from stepped destination
		var antiClip = Scene.Trace.Ray( curPos + Vector3.Up * (_capsuleHeight * 0.5f), downTr.EndPosition + Vector3.Up * (_capsuleHeight * 0.5f) )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "debris", "ragdoll", "npc", "playercontroller", "player" )
			.Run();
		if ( antiClip.Hit )
			return false;

		steppedPos = downTr.EndPosition;
		return true;
	}

	void FaceDirection( Vector3 dir )
	{
		if ( dir.LengthSquared < 0.001f ) return;
		var desired = Rotation.LookAt( dir.Normal, Vector3.Up );
		WorldRotation = Rotation.Slerp( WorldRotation, desired, Time.Delta * 8f * EffectiveTimeScale );
	}

	void TryMeleeStrike( GameObject target, Vector3 dir )
	{
		if ( Time.Now < _nextAttackTime ) return;
		_nextAttackTime = Time.Now + AttackCooldown;
		_lungeAnim = 1f;

		_hasPendingMelee = true;
		_pendingMeleeTarget = target;
		_pendingMeleeDir = dir;

		if ( AttackStyle == PostalNpcAttackStyle.GoliathSlam )
		{
			PlayNpcAnim( _seqAttack, loop: false, rate: 1.0f, lockUntilDone: true );
			_pendingMeleeHitAt = Time.Now + 0.36f;
			_pendingMeleeStomp = false;
			return;
		}

		PlayVoice( AttackSoundPath, 0.92f );
		if ( AttackStyle == PostalNpcAttackStyle.KarenBrawler && !string.IsNullOrEmpty( _seqSpecial ) && Game.Random.Float() < 0.38f )
		{
			PlayNpcAnim( _seqSpecial, loop: false, rate: 1.1f, lockUntilDone: true );
			_pendingMeleeHitAt = Time.Now + 0.28f;
			_pendingMeleeStomp = true;
		}
		else
		{
			PlayNpcAnim( _seqAttack, loop: false, rate: 1.15f, lockUntilDone: true );
			_pendingMeleeHitAt = Time.Now + 0.22f;
			_pendingMeleeStomp = false;
		}
	}

	void TryRangedAttack( GameObject target )
	{
		if ( Time.Now < _nextAttackTime ) return;
		_nextAttackTime = Time.Now + AttackCooldown;
		_lungeAnim = 0.6f;

		PlayVoice( AttackSoundPath, 0.88f );
		PlayNpcAnim( _seqAttack, loop: false, rate: 1.15f, lockUntilDone: true );

		switch ( AttackStyle )
		{
			case PostalNpcAttackStyle.RangedBurst:
				_burstTarget = target;
				_burstRemaining = 2;
				_nextBurstShot = Time.Now + 0.16f;
				FireEnemyProjectile( target, speed: 880f, spread: 20f, color: new Color( 1f, 0.72f, 0.2f, 1f ) );
				break;

			case PostalNpcAttackStyle.ShotgunSpread:
				for ( int i = 0; i < 6; i++ )
					FireEnemyProjectile( target, speed: 1200f, spread: 75f, color: new Color( 1f, 0.55f, 0.15f, 1f ), dmgScale: 0.4f );
				break;

			case PostalNpcAttackStyle.CrossbowBolt:
				FireEnemyProjectile( target, speed: 1380f, spread: 12f, color: new Color( 0.95f, 0.25f, 0.9f, 1f ) );
				break;

			case PostalNpcAttackStyle.PlasmaSpit:
				FireEnemyProjectile( target, speed: 840f, spread: 16f, color: new Color( 0.25f, 1f, 0.45f, 1f ), radius: 75f );
				break;

			default:
				FireEnemyProjectile( target, speed: 1050f, spread: 18f, color: new Color( 1f, 0.85f, 0.25f, 1f ) );
				break;
		}
	}

	void FireEnemyProjectile( GameObject target, float speed, float spread, Color color, float dmgScale = 1f, float radius = 0f )
	{
		var start = WorldPosition + Vector3.Up * (EyeHeight * 0.84f) + WorldRotation.Forward * 24f;
		var aimTarget = target.WorldPosition + Vector3.Up * 42f + Vector3.Random * spread;
		var dir = (aimTarget - start).Normal;

		PostalVfx.SpawnMuzzleFlash( Scene, start, dir, color, scale: 0.65f, lightRadius: 150f );

		var projGo = new GameObject( true, "EnemyProjectile" );
		projGo.WorldPosition = start;
		projGo.WorldRotation = Rotation.LookAt( dir );

		var proj = projGo.AddComponent<PostalProjectile>();
		proj.Velocity = dir * speed;
		proj.Attacker = GameObject;
		proj.Weapon = GameObject;
		proj.Damage = AttackDamage * dmgScale;
		proj.ExplosionRadius = radius;
		proj.TrailColor = color;
		proj.ImpactSoundPath = "mount://pbd/sounds/player/bow_hit2.vsnd";

		PostalVfx.AttachProjectileVisuals( projGo, PostalProjectileVisualKind.EnemyPlasma, color );
	}

	void UpdateVisualAnimation()
	{
		if ( !_visualPivot.IsValid() )
			return;

		float speed = _smoothedHorizSpeed;
		if ( _isMovingAnim )
			_isMovingAnim = speed > 8f;
		else
			_isMovingAnim = speed > 18f;

		if ( _skinnedMr.IsValid() )
		{
			if ( string.IsNullOrEmpty( _seqIdle ) && _skinnedMr.SceneModel.IsValid() )
				ResolveNpcSequences();
		}

		float ts = EffectiveTimeScale;

		if ( _skinnedMr.IsValid() && !string.IsNullOrEmpty( _seqIdle ) )
		{
			_visualPivot.LocalPosition = _baseVisualLocalPos;
			_visualPivot.LocalRotation = Rotation.Identity;

			if ( Time.Now >= _actionAnimEndTime )
			{
				if ( _isMovingAnim )
				{
					bool isPanicking = Archetype == PostalNpcArchetype.Civilian && (_hadTarget || Time.Now < _panicUntil);
					bool isWalkingSlow = !_hadTarget && !isPanicking && speed < MoveSpeed * 0.92f && !string.IsNullOrEmpty( _seqWalk );
					string moveSeq = isPanicking ? _seqFlee : (isWalkingSlow ? _seqWalk : _seqMove);
					float refSpeed = isWalkingSlow ? MathF.Max( MoveSpeed, 65f ) : MathF.Max( RunSpeed, 115f );
					float rate = Math.Clamp( speed / refSpeed, 0.72f, 1.25f ) * ts;
					PlayNpcAnim( moveSeq, loop: true, rate: MathF.Max( rate, 0.08f ) );
				}
				else
				{
					PlayNpcAnim( _seqIdle, loop: true, rate: MathF.Max( ts, 0.1f ) );
				}
			}
			else if ( ts < 0.99f )
			{
				_skinnedMr.Sequence.PlaybackRate = MathF.Max( ts, 0.1f );
			}
		}
		else
		{
			if ( speed > 10f )
				_walkPhase += Time.Delta * ts * Math.Clamp( speed * 0.06f, 4f, 11f );

			float bobZ = speed > 10f ? MathF.Abs( MathF.Sin( _walkPhase ) ) * 3.5f : MathF.Sin( Time.Now * 2.2f ) * 0.6f;
			float waddleRoll = speed > 10f ? MathF.Sin( _walkPhase ) * 4.5f : 0f;

			float lungeFwd = 0f;
			float lungePitch = 0f;
			if ( _lungeAnim > 0f )
			{
				_lungeAnim = MathF.Max( 0f, _lungeAnim - Time.Delta * ts * 4.2f );
				float s = MathF.Sin( _lungeAnim * MathF.PI );
				lungeFwd = s * 12f;
				lungePitch = s * 14f;
			}

			_visualPivot.LocalPosition = _baseVisualLocalPos + new Vector3( lungeFwd, 0f, bobZ );
			_visualPivot.LocalRotation = Rotation.From( new Angles( lungePitch, 0f, waddleRoll ) );
		}

		// Crackling arcs across the NPC's body while electrocuted by the Tesla Lightning Gun!
		bool isElectro = Time.Now < _electroShockUntil;
		if ( isElectro && Time.Now >= _nextElectroArcTime )
		{
			_nextElectroArcTime = Time.Now + 0.09f;
			var bodyCenter = WorldPosition + Vector3.Up * (EyeHeight * 0.55f);
			var a = bodyCenter + Vector3.Random * 18f;
			var b = bodyCenter + Vector3.Random * 22f;
			PostalVfx.SpawnTracerBeam( Scene, a, b, new Color( 0.4f, 0.92f, 1f, 1f ), width: 2.2f, duration: 0.09f, jagged: true, forks: 1 );
		}

		if ( _visualMr.IsValid() )
		{
			Color desiredTint = Color.White;
			if ( _hitFlash > 0f )
			{
				_hitFlash = MathF.Max( 0f, _hitFlash - Time.Delta * 5f );
				desiredTint = Color.Lerp( Color.White, new Color( 1f, 0.25f, 0.25f, 1f ), _hitFlash );
			}
			else if ( isElectro )
			{
				float flicker = 0.5f + 0.5f * MathF.Sin( Time.Now * 42f );
				desiredTint = Color.Lerp( new Color( 0.55f, 0.92f, 1f, 1f ), Color.White, flicker );
			}
			else if ( ts < 0.95f )
			{
				float pulse = 0.35f + 0.2f * MathF.Sin( Time.Now * 8f );
				desiredTint = Color.Lerp( Color.White, new Color( 0.3f, 0.85f, 1f, 1f ), pulse );
			}
			_visualMr.Tint = desiredTint;
		}
	}

	protected override void OnDamaged( Component.IDamageable.DamageEvent e )
	{
		base.OnDamaged( e );
		if ( e.DamageInfo.Tags.Has( "headshot_bonus" ) )
			return;

		_hitFlash = 1f;

		var hitPos = e.DamageInfo.Position.LengthSquared > 1f
			? e.DamageInfo.Position
			: WorldPosition + Vector3.Up * (EyeHeight * 0.65f);
		var hitDir = ((WorldPosition + Vector3.Up * (EyeHeight * 0.65f)) - hitPos);
		hitDir = hitDir.LengthSquared > 1f ? hitDir.Normal : WorldRotation.Backward;

		// 2x Headshot Critical bonus + distinct crunch sound & heavy blood spray!
		bool isHeadshot = hitPos.z >= WorldPosition.z + EyeHeight * 0.86f && e.DamageInfo.Damage > 0f;
		if ( isHeadshot )
			UnitySoundUtil.PlayAt( "mount://pbd/sounds/master/critical_hit_iter-003.vsnd", hitPos, 0.95f, 1400f );

		if ( Archetype == PostalNpcArchetype.Civilian )
			TriggerPanic( hitPos );

		PostalVfx.SpawnBloodSplatter( Scene, hitPos, hitDir, heavy: isHeadshot || e.DamageInfo.Damage >= 45f );
		UnitySoundUtil.PlayRandomAt( FleshHitSounds, hitPos, 0.85f, 1200f, 0.9f, 1.12f );

		if ( isHeadshot && !_isDead && Health > 0f )
		{
			var bonusTags = new TagSet();
			bonusTags.Add( "headshot_bonus" );
			((Component.IDamageable)this).OnDamage( new DamageInfo
			{
				Damage = e.DamageInfo.Damage * 0.85f,
				Position = hitPos,
				Attacker = e.DamageInfo.Attacker,
				Weapon = e.DamageInfo.Weapon,
				Tags = bonusTags,
			} );
			if ( _isDead || Health <= 0f )
				return;
		}

		if ( !string.IsNullOrEmpty( _seqStagger ) && (isHeadshot || e.DamageInfo.Damage >= 22f) )
		{
			PlayNpcAnim( _seqStagger, loop: false, rate: 1.1f, lockUntilDone: true );
			_staggerUntil = Time.Now + 0.34f;
		}
		if ( Time.Now >= _nextVoiceTime * 0.5f )
		{
			PlayVoice( PainSoundPath, 0.9f );
			_nextVoiceTime = Time.Now + 1.2f;
		}
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent e )
	{
		if ( _isDead ) return;
		_isDead = true;

		bool isExplosiveKill = e.DamageInfo.Tags.Has( DamageTags.Explosion ) || e.DamageInfo.Damage >= 110f;
		var deathCenter = WorldPosition + Vector3.Up * (EyeHeight * 0.6f);
		var hitPos = e.DamageInfo.Position.LengthSquared > 1f ? e.DamageInfo.Position : deathCenter - WorldRotation.Forward * 24f;
		var launchDir = (deathCenter - hitPos);
		launchDir = launchDir.LengthSquared > 1f ? launchDir.Normal : WorldRotation.Backward;

		PlayVoice( string.IsNullOrEmpty( DeathSoundPath ) ? PainSoundPath : DeathSoundPath, 1.0f );
		PostalVfx.SpawnBloodSplatter( Scene, deathCenter, launchDir, heavy: true );
		if ( isExplosiveKill )
			UnitySoundUtil.PlayAt( "mount://pbd/sounds/master/huge_blood_guts_gore_explo.vsnd", deathCenter, 0.95f, 1800f );

		if ( _loadedModel is not null && !_loadedModel.IsError )
		{
			float scale = ModelScale > 0.05f ? ModelScale : 1f;
			var spawnPos = _visualPivot.IsValid() ? _visualPivot.WorldPosition : WorldPosition;
			var spawnRot = _visualPivot.IsValid() ? _visualPivot.WorldRotation : WorldRotation;

			bool hasArticulatedRagdoll = _loadedModel.BoneCount > 0
				&& _loadedModel.Physics is not null
				&& _loadedModel.Physics.Parts.Count >= 2;

			if ( hasArticulatedRagdoll )
			{
				var ragdoll = new GameObject( true, $"{DisplayName}_Ragdoll" );
				ragdoll.WorldPosition = spawnPos;
				ragdoll.WorldRotation = spawnRot;
				ragdoll.WorldScale = new Vector3( scale );
				ragdoll.Tags.Add( "ragdoll", "removable", "debris" );

				var smr = ragdoll.AddComponent<SkinnedModelRenderer>();
				smr.Model = _loadedModel;
				smr.UseAnimGraph = false;
				smr.RenderType = ModelRenderer.ShadowRenderType.On;

				var mp = ragdoll.AddComponent<ModelPhysics>();
				mp.Model = _loadedModel;
				mp.Renderer = smr;
				mp.MotionEnabled = true;

				if ( _skinnedMr.IsValid() && _skinnedMr.SceneModel.IsValid() )
					mp.CopyBonesFrom( _skinnedMr, teleport: true );

				var corpseCtrl = ragdoll.AddComponent<PostalRagdollCorpse>();
				corpseCtrl.Physics = mp;
				corpseCtrl.HitPosition = hitPos;
				corpseCtrl.ImpulseVelocity = (launchDir + Vector3.Up * (isExplosiveKill ? 0.65f : 0.25f)).Normal
					* (isExplosiveKill ? 440f : Math.Clamp( 155f + e.DamageInfo.Damage * 2.5f, 160f, 360f ));
				corpseCtrl.TryApplyImpulse();
			}
			else
			{
				var corpse = new GameObject( true, $"{DisplayName}_Corpse" );
				corpse.WorldPosition = spawnPos + Vector3.Up * 6f;
				corpse.WorldRotation = spawnRot;
				corpse.WorldScale = new Vector3( scale );
				corpse.Tags.Add( "removable", "debris" );

				var mr = corpse.AddComponent<ModelRenderer>();
				mr.Model = _loadedModel;

				var col = corpse.AddComponent<BoxCollider>();
				col.Scale = _loadedModel.Bounds.Size * 0.65f;
				col.Center = _loadedModel.Bounds.Center;

				var rb = corpse.AddComponent<Rigidbody>();
				rb.MassOverride = 55f;
				rb.Velocity = (launchDir + Vector3.Up * 0.45f).Normal * 280f;
				rb.AngularVelocity = Vector3.Random * 6f;

				corpse.AddComponent<PostalRagdollCorpse>();
			}
		}

		GameObject.Destroy();
	}

	public bool CanPress( IPressable.Event e ) => Archetype == PostalNpcArchetype.Civilian;

	public IPressable.Tooltip? GetTooltip( IPressable.Event e )
		=> Archetype == PostalNpcArchetype.Civilian ? new IPressable.Tooltip( $"Talk to {DisplayName}", "record_voice_over", null ) : null;

	public bool Press( IPressable.Event e )
	{
		PlayVoice( NoticeSoundPath, 0.95f );
		if ( !string.IsNullOrEmpty( _seqSpecial ) )
			PlayNpcAnim( _seqSpecial, loop: false, rate: 1f, lockUntilDone: true );
		return true;
	}

	void PlayVoice( string path, float vol )
	{
		if ( !string.IsNullOrEmpty( path ) )
			UnitySoundUtil.PlayAt( path, WorldPosition + Vector3.Up * EyeHeight, vol, 1400f );
	}
}

/// <summary>
/// Applies immediate directional collapse momentum across articulated ragdoll bodies so corpses
/// go instantly limp ("corpo mole") without balancing upright, and cleans up after 22 seconds.
/// </summary>
public sealed class PostalRagdollCorpse : Component
{
	public ModelPhysics Physics { get; set; }
	public Vector3 HitPosition { get; set; }
	public Vector3 ImpulseVelocity { get; set; }

	bool _impulseApplied;
	float _age;

	public void TryApplyImpulse()
	{
		if ( _impulseApplied || !Physics.IsValid() || Physics.Bodies is null || Physics.Bodies.Count == 0 )
			return;

		_impulseApplied = true;

		Rigidbody closestBody = null;
		float closestDist = float.MaxValue;
		var tiltDir = ImpulseVelocity.WithZ( 0 ).LengthSquared > 1f
			? ImpulseVelocity.WithZ( 0 ).Normal
			: Vector3.Random.WithZ( 0 ).Normal;

		int idx = 0;
		foreach ( var b in Physics.Bodies )
		{
			var rb = b.Component;
			if ( !rb.IsValid() ) continue;
			// Upper bodies get stronger push than legs so the ragdoll immediately buckles & topples limp!
			float heightWeight = idx <= 2 ? 0.95f : 0.45f;
			rb.Velocity = ImpulseVelocity * heightWeight + Vector3.Random * 28f;
			rb.AngularVelocity = Vector3.Cross( Vector3.Up, tiltDir ) * 4.5f + Vector3.Random * 3.5f;

			float d = rb.WorldPosition.Distance( HitPosition );
			if ( d < closestDist )
			{
				closestDist = d;
				closestBody = rb;
			}
			idx++;
		}

		if ( closestBody.IsValid() )
		{
			closestBody.Velocity = ImpulseVelocity * 1.45f;
			closestBody.AngularVelocity = Vector3.Random * 8f;
		}
	}

	protected override void OnUpdate()
	{
		if ( !_impulseApplied )
			TryApplyImpulse();

		_age += Time.Delta;
		if ( _age >= 22f )
			GameObject.Destroy();
	}
}
