using System;
using Sandbox.Npcs;

namespace GoldSrc.HalfLife;

/// <summary>An NPC using mounted GoldSrc sequences and the shared Sandbox NPC layers.</summary>
[Hide]
[Alias( "GoldSrc.HalfLifeNpc" )]
public partial class Npc : BaseNpc
{
	// GoldSrc studio activity IDs used to select authored sequences.
	protected const int Idle = 1, Walk = 3, Run = 4, Fly = 5, Swim = 6, Flinch = 26, BigFlinch = 27, Ranged = 28, Melee = 30;

	[Property] public string ClassName { get; set; }
	[Property] public string ModelPath { get; set; }
	[Property] public string ProjectilePath { get; set; }
	[Property] public int Body { get; set; }
	[Property] public int Skin { get; set; }
	[Sync( SyncFlags.FromHost )] protected string Action { get; private set; }
	[Sync( SyncFlags.FromHost )] protected int ActionSerial { get; private set; }
	[Sync( SyncFlags.FromHost )] TimeSince ActionStart { get; set; }
	[Sync( SyncFlags.FromHost )] bool ActionReversed { get; set; }
	[Sync( SyncFlags.FromHost )] float ActionRate { get; set; } = 1;
	protected float ActionTime => (float)ActionStart * ActionRate;
	internal virtual float PlaybackRate => 1;
	[Sync( SyncFlags.FromHost )] byte AimBlend { get; set; }

	internal void SetAimBlend( StudioSequence sequence, Vector3 direction )
	{
		if ( sequence?.PitchFrames is null ) return;
		var start = sequence.Description.BlendStart[0];
		var end = sequence.Description.BlendEnd[0];
		if ( end == start ) return;
		var pitch = -direction.EulerAngles.Normal.pitch;
		if ( end < start ) pitch = -pitch;
		AimBlend = (byte)(255 * ((pitch - start) / (end - start)).Clamp( 0, 1 ));
	}

	internal NpcDefinitionAttribute Definition => _definition ??= Game.TypeLibrary.GetType( GetType() ).GetAttribute<NpcDefinitionAttribute>();
	NpcDefinitionAttribute _definition;
	StudioSequence _idle, _walk, _run, _attack, _action, _death;
	Dictionary<string, StudioSequence> _sequences;
	NpcAudio _audio;
	BoxCollider _collisionHull;
	internal virtual string VoiceSample( string sample ) => sample;
	internal virtual float VoiceVolume => 1;
	internal virtual float VoicePitch => 1;
	internal virtual NpcSounds Sounds { get; } = new();
	readonly StudioSequenceEvents _sequenceEvents = new();
	GameObject _target, _leapTarget;
	protected GameObject LastHostile { get; private set; }
	protected bool InCombat => LastHostile.IsValid();
	float[] _hitTimes = [];
	StudioSequence _shownSequence;
	float _clipTime;
	int _nextHit, _shownAction;
	TimeSince _shownActionStart;
	float _shownActionRate;
	bool _shownReversed;
	protected TimeUntil NextWander { get; set; }
	TimeUntil _nextMove, _nextAttack, _nextVoice, _leapExpires;
	Vector3 _home;
	bool _loaded;
	ModelLoader _modelLoader;
	Sandbox.Animation.Graph _defaultGraph;

	protected override async void OnStart()
	{
		base.OnStart();
		_home = WorldPosition;
		if ( this is ICollisionListener && Definition?.Movement != NpcMovement.Stationary && GetComponent<Rigidbody>() is { } body )
			body.CollisionUpdateEventsEnabled = true;
		_collisionHull = GetComponentInChildren<BoxCollider>();
		if ( Definition is null || !Renderer.IsValid() || string.IsNullOrWhiteSpace( ModelPath ) )
			return;

		try
		{
			var mount = await Sandbox.Mounting.Directory.Mount( "hl1" );
			if ( mount is not HalfLifeMount halfLife || mount.GetByPath( ModelPath ) is not ModelLoader loader )
				return;

			var model = await Model.LoadAsync( ModelPath );
			if ( !IsValid || !Renderer.IsValid() || !model.IsValid() || model.IsError )
				return;

			loader.EnsureNpcAnimations();
			_audio = halfLife.NpcAudio;
			Renderer.Model = model;
			OnModelLoaded( loader );
			ApplyAppearance( model );
			ApplyEquipment();
			_sequences = new( StringComparer.OrdinalIgnoreCase );
			foreach ( var sequence in loader.Sequences )
				_sequences.TryAdd( sequence.Name, sequence );
			_idle = Find( Definition.IdleSequence ) ?? Activity( Idle ) ?? loader.Sequences.FirstOrDefault();
			var movementActivity = Definition.Movement == NpcMovement.Swimming ? Swim : Fly;
			_walk = Definition.Movement == NpcMovement.Ground ? Activity( Walk ) : Activity( movementActivity );
			_run = Definition.Movement == NpcMovement.Ground ? Activity( Run ) ?? _walk : _walk;
			_attack = Find( Definition.AttackSequence ) ?? Activity( Definition.Attack == NpcAttack.Melee ? Melee : Ranged );
			SetUpCaptureAnimations();
			Renderer.UseAnimGraph = true;
			_modelLoader = loader;
			Renderer.Animation.Graph = _defaultGraph = loader.AnimationGraphs.GetGraph( [ _idle, _walk, _run, _attack, .. FlinchSequences, .. ExtraSequences, .. _captureAnimations ], loader.Controllers, ControllerParameters );
			_loaded = true;
			if ( Networking.IsHost ) PlaceAtSpawn();
			UpdateSequence();
		}
		catch ( Exception e )
		{
			Log.Warning( $"Could not load Half-Life NPC '{ClassName}': {e.Message}" );
		}
	}

	internal void ApplyAppearance( Model model )
	{
		var body = Math.Max( EffectiveBody, 0 );
		foreach ( var part in model.Parts.All )
		{
			var count = part.Choices.Count;
			if ( count == 0 )
				continue;

			Renderer.SetBodyGroup( part.Name, body % count );
			body /= count;
		}

		var skin = EffectiveSkin;
		if ( skin > 0 )
			Renderer.MaterialGroup = $"Skin {skin}";
	}

	internal virtual string[] ControllerParameters => [];
	internal virtual void OnModelLoaded( ModelLoader loader ) { }
	internal virtual int EffectiveBody => Body;
	internal virtual int EffectiveSkin => Skin;
	internal virtual string[] ExtraActions => [];
	internal virtual IEnumerable<StudioSequence> ExtraSequences => ExtraActions.Select( Find );
	internal IEnumerable<StudioSequence> Activities( int activity ) => _sequences.Values.Where( sequence => sequence.Description.Activity == activity );
	internal virtual bool FollowLeader() => false;
	internal virtual void FinishAction( string name ) { }
	internal virtual void FinishMovementCycle() { }
	internal virtual void PlaySequenceSound( NpcAudio audio, int id ) { }
	internal virtual void HandleActionEvent( Mdl.Event e ) => HandleActionEvent( e.Index );
	internal virtual void HandleActionEvent( int id ) { }
	internal virtual void ApplyEquipment() { }
	[Sync( SyncFlags.FromHost ), Change( nameof( EquipmentChanged ) )]
	protected bool EquipmentDrawn { get; set; }
	void EquipmentChanged( bool before, bool after )
	{
		if ( Renderer.IsValid() && Renderer.Model.IsValid() )
			ApplyEquipment();
	}
	int _nextEvent;

	internal StudioSequence Find( string name ) => name is null ? null : _sequences?.GetValueOrDefault( name );

	internal StudioSequence Activity( int activity ) => _sequences.Values
		.Where( x => x.Description.Activity == activity )
		.OrderByDescending( x => x.Description.ActWeight )
		.FirstOrDefault();

	internal StudioSequence WeightedActivity( int activity )
	{
		StudioSequence selected = null;
		int total = 0;
		foreach ( var clip in _sequences.Values )
		{
			if ( clip.Description.Activity != activity ) continue;
			var weight = clip.Description.ActWeight;
			total += weight;
			if ( total == 0 || Game.Random.Int( 0, total - 1 ) < weight ) selected = clip;
		}
		return selected;
	}

	internal virtual float ModelOriginHeight => 0;
	internal virtual float HoverClearance => MathF.Max( Definition.Height, 48 );

	internal virtual void PlaceAtSpawn()
	{
		if ( Definition.Movement != NpcMovement.Flying ) return;
		var floor = Scene.Trace.Ray( WorldPosition + Vector3.Up * 8, WorldPosition - Vector3.Up * 48 )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "npc", "player", "trigger" ).Run();
		if ( floor.Hit )
			WorldPosition = floor.EndPosition + Vector3.Up * HoverClearance;
	}

	protected override void OnUpdate()
	{
		if ( ScriptedIdle is null && !_script.IsValid() ) base.OnUpdate();
		if ( MapNavigation.IsValid() ) MapNavigation.UpdatePosition = Movement?.MoveTarget.HasValue == true;
		if ( !_loaded || Health <= 0 )
			return;

		if ( Networking.IsHost )
		{
			if ( _script.IsValid() ) UpdateScript();
			else if ( ScriptedIdle is null ) UpdateLeap();
		}
		UpdateSequence();
	}

	protected override void Think()
	{
		if ( !_loaded )
			return;

		if ( AdvanceAction() ) return;

		var hostile = Definition.Attack == NpcAttack.None ? null : FindVisibleEnemy();
		if ( hostile.IsValid() )
		{
			if ( hostile != LastHostile )
				OnEnemySpotted( hostile );
			LastHostile = hostile;
			if ( Action is null ) Engage( hostile );
			return;
		}
		LastHostile = null;

		if ( _nextMove > 0 )
			return;
		_nextMove = 0.25f;
		Animation.ClearLookTarget();

		var threat = FindVisibleThreat() ?? (Definition.Attack == NpcAttack.None ? FindVisibleEnemy() : null);
		if ( threat.IsValid() )
		{
			OnThreatSeen( threat );
			var away = (WorldPosition - threat.WorldPosition).WithZ( 0 ).Normal;
			MoveToward( WorldPosition + away * 200, 16 );
		}
		else if ( !FollowLeader() ) IdleMovement();
	}

	internal virtual void IdleMovement()
	{
		if ( !Movement.IsValid() || NextWander > 0 ) return;
		NextWander = Game.Random.Float( 3, 7 );
		var direction = Rotation.FromYaw( Game.Random.Float( 0, 360 ) ).Forward;
		MoveToward( _home + direction * Game.Random.Float( 52, 160 ), 10, 0.6f );
		IdleSound();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void MuzzleFlash( Vector3 position, string sprite, float size )
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount || mount.GetIndexedSprite( sprite ) is not { } flash )
			return;
		var go = new GameObject( false, "Muzzle flash" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;
		var renderer = go.AddComponent<SpriteRenderer>();
		renderer.Sprite = flash;
		renderer.Size = size;
		renderer.Additive = true;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0.06f;
		go.Enabled = true;
	}

	internal bool MoveToward( Vector3 point, float stop, float pace = 1 )
	{
		if ( !Movement.IsValid() )
			return false;

		if ( Definition.Movement == NpcMovement.Ground )
		{
			var projected = Movement.ProjectPoint( point );
			if ( projected is null )
				return false;
			point = projected.Value;
		}

		if ( MapNavigation.IsValid() && !MapNavigation.UpdatePosition )
		{
			// A scripted teleport can move the actor while the nav agent is parked.
			MapNavigation.SetAgentPosition( WorldPosition );
			MapNavigation.UpdatePosition = true;
		}
		Movement.MoveTo( point, stop, Definition.Speed * pace );
		return true;
	}

	/// <summary>Play a named sequence from this actor's mounted studio model once.</summary>
	public bool PlayAnimation( string name )
	{
		if ( !Networking.IsHost || !_loaded || Health <= 0 || Find( name ) is not { } sequence ) return false;
		PlayAction( sequence );
		return true;
	}

	internal virtual StudioSequence RestSequence => _idle;
	internal virtual StudioSequence MovementSequence( float speed ) => speed > 12 ? (speed >= Definition.Speed * 0.8f ? _run : _walk) : null;

	internal virtual float MovementPlaybackRate( StudioSequence sequence, float speed ) => speed > 12 && sequence.GroundSpeed > 1
		? Math.Clamp( speed / sequence.GroundSpeed, 0.5f, 2f ) : 1;

	void UpdateSequence()
	{
		var captured = CaptureSequence( out var captureTime );
		var sequence = captured ?? Find( Action );
		var loop = sequence is null;
		var speed = Animation.MoveVelocity.Length;
		sequence ??= Find( ScriptedIdle ) ?? ScriptGait ?? MovementSequence( speed ) ?? RestSequence;
		if ( sequence is null || !Renderer.Animation.IsValid )
			return;

		var changed = _shownSequence != sequence || _shownAction != ActionSerial;
		var rate = loop ? MovementPlaybackRate( sequence, speed ) : 1f;
		var nextTime = _clipTime + Time.Delta * rate;
		if ( ScriptedIdle is null && loop && !changed && Networking.IsHost && nextTime >= sequence.Duration ) FinishMovementCycle();
		_clipTime = loop
			? changed ? 0f : nextTime % sequence.Duration
			: Math.Clamp( captured is null ? ActionTime : captureTime, 0, sequence.Duration );

		if ( changed )
		{
			// AI can finish or interrupt an action before this frame's renderer update.
			if ( _shownActionRate > 0 )
			{
				var end = Math.Min( (float)_shownActionStart * _shownActionRate, _shownSequence.Duration );
				_sequenceEvents.Update( _audio, Renderer, end, false, this, _shownReversed, IsOnGround );
			}
			_shownActionStart = ActionStart;
			_shownActionRate = !loop && captured is null ? ActionRate : 0;
			_shownReversed = ActionReversed;
			// A scripted sequence outside the usual AI states gets a shared single-clip graph on demand.
			if ( Renderer.Animation.Graph != _defaultGraph )
				Renderer.Animation.Graph = _defaultGraph;
			if ( !Renderer.Animation.Parameters.TryGetFloat( sequence.TimeParameter, out _ ) )
				Renderer.Animation.Graph = _modelLoader.AnimationGraphs.GetGraph( [sequence], _modelLoader.Controllers, ControllerParameters );
		}

		var parameters = Renderer.Animation.Parameters;
		if ( sequence.PitchFrames is not null ) parameters.Set( "aim_pitch", AimBlend / 255f );
		if ( changed )
		{
			_shownSequence = sequence;
			_shownAction = ActionSerial;
			parameters.Set( StudioAnimationGraphs.StateParameter, sequence.Name );
			_sequenceEvents.Reset( sequence, _clipTime, _clipTime <= 0.1f );
		}
		parameters.Set( sequence.TimeParameter, !loop && captured is null && ActionReversed ? 1 - _clipTime / sequence.Duration : _clipTime / sequence.Duration );
		_sequenceEvents.Update( _audio, Renderer, _clipTime, loop, this, !loop && captured is null && ActionReversed, IsOnGround );
	}

	internal virtual void PainSound() => Speak( Sounds.Pain );

	internal virtual void IdleSound() => Speak( Sounds.Idle );
	internal virtual void OnEnemySpotted( GameObject target ) => Speak( Sounds.Alert );
	internal virtual void OnThreatSeen( GameObject threat ) => Speak( Sounds.Alert );

	internal void Speak( string sample, float attenuation = GoldSrcSoundEvents.Normal )
	{
		if ( _nextVoice <= 0 && _audio.Emit( VoiceSample( sample ), WorldPosition, VoiceVolume, VoicePitch, this as TalkingNpc, attenuation ) )
			_nextVoice = Game.Random.Float( 2, 4 );
	}
}
