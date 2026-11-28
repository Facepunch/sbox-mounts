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

	/// <summary>Clips the behaviour may layer over the creature: additive or bone-masked.</summary>
	public virtual IEnumerable<string> GestureChoices => [];

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

	[Rpc.Broadcast( NetFlags.HostOnly )]
	protected void PlaySound( string name )
	{
		if ( Sounds.Sound( name, MountContent.Of( GameObject ) ) is { } sound )
			Sound.Play( sound, WorldPosition );
	}
}

/// <summary>
/// Shared mounted creature: idles, pursues and attacks with the model's own activities, timing hits from its
/// animation events. Distinct behaviours (leaps, zombie headcrabs, grenades, projectiles) are separate components.
/// </summary>
[Title( "Source Creature NPC" )]
[Category( "Source 1" )]
[Alias( "Source1.SourceCreatureNpc", "SourceCreatureNpc" )]
public class CreatureNpc : BaseNpc, Component.IPressable
{
	/// <summary>How a creature gets around.</summary>
	public enum Locomotions { Ground, Flying, Stationary }

	/// <summary>Discard a pending movement goal if the creature never reaches it.</summary>
	const float CommandTimeout = 30;

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
	internal Dictionary<string, float> DamageByEvent { get; set; }
	internal float? EventDamage( string name ) => name is not null && DamageByEvent?.TryGetValue( name, out var damage ) == true ? damage : null;
	internal float? PlayerAttackDamage { get; set; }
	[Property] public float AttackInterval { get; set; } = 1.2f;
	[Property] public float AttackDelay { get; set; } = 0.3f;
	[Property] public float AttackHeight { get; set; } = 32;

	/// <summary>A shooter with a melee attack that holds its distance while it can shoot, as npc_hunter does, instead of closing in between shots.</summary>
	[Property] public bool StandOff { get; set; }

	/// <summary>The NPC class's sound script prefix, such as NPC_Strider, for its <see cref="NpcVoice"/> lines.</summary>
	[Property] public string Voice { get; set; }

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
	AnimationPlayer _animation = new() { UseBlendGrids = true };
	LinkAnimation _link;
	CreatureBehaviour[] _behaviours = [];
	Hl2.Manhack _manhack;
	Hl2.NpcProjectileAttack _projectile;
	protected NpcRangedEffects RangedEffects { get; private set; }
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
		{
			if ( Aquatic ) AddComponent<SwimLayer>();
			else AddComponent<FlyingLayer>();
		}

		base.OnAwake();
	}

	protected override void OnStart()
	{
		base.OnStart();
		_body = GetComponent<Rigidbody>( true );
		_behaviours = GetComponents<CreatureBehaviour>().ToArray();
		_manhack = GetComponent<Hl2.Manhack>();
		_projectile = GetComponent<Hl2.NpcProjectileAttack>();
		RangedEffects = GetComponent<NpcRangedEffects>();
		_photo = GetComponent<Hl2.ScannerPhoto>();
		_home = WorldPosition;
		if ( IsProxy )
		{
			_observedAttack = AttackSerial;
			_observedFlinch = FlinchSerial;
		}
		else if ( Locomotion == Locomotions.Flying && !Aquatic )
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
		_sequences = ModelSequences.Of( model );
		_idle = _sequences.FindActivity( "ACT_IDLE", Idle );
		_walk = _sequences.FindActivity( Locomotion == Locomotions.Flying && !Aquatic ? "ACT_FLY" : "ACT_WALK", Move );
		_run = _sequences.FindActivity( "ACT_RUN", _walk );
		_melee = _sequences.FindActivity( "ACT_MELEE_ATTACK1" );
		// Leaps and crab throws are ACT_RANGE_ATTACK1 too; those belong to their components.
		_ranged = HasRangedAttack ? _sequences.FindActivity( "ACT_RANGE_ATTACK1" ) : null;
		_meleeChoices = _sequences.All.Where( s => !s.Delta && s.Activity == "ACT_MELEE_ATTACK1" && model.AnimationClips().ContainsKey( s.Name ) ).Select( s => s.Name ).ToArray();
		// Masked ACT_GESTURE_FLINCH_* sequences (zombies) play over whatever the creature is doing, as Source's gesture layer does.
		_flinches = _sequences.All.Where( s => !s.Delta && s.BoneMask is not null && s.Activity?.StartsWith( "ACT_GESTURE_FLINCH", StringComparison.Ordinal ) == true && model.AnimationClips().ContainsKey( s.Name ) )
			.Select( s => s.Name ).ToArray();
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
			.. _behaviours.SelectMany( x => x.AnimationChoices ), .. _link.Choices];
	}

	float GroundSpeed( string clip ) => _sequences?.Get( clip )?.GroundSpeed ?? 0;

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

		var velocity = Animation.MoveVelocity;
		if ( Locomotion == Locomotions.Ground )
			_animation.SetMoveYaw( velocity, WorldRotation );

		var speed = Aquatic ? velocity.Length : Locomotion == Locomotions.Ground ? velocity.WithZ( 0 ).Length : MoveSpeed;
		var moving = Aquatic ? speed > 2 : Locomotion == Locomotions.Flying || speed > 10;
		string forced = null;
		foreach ( var behaviour in _behaviours )
		{
			forced = behaviour.Animation;
			if ( forced is not null )
				break;
		}

		if ( forced is not null )
		{
			_animation.Play( forced );
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
			if ( Aquatic ) running = speed > Movement.WalkSpeed;
			var clip = !moving ? idle : running ? run : walk;
			// Feet match the ground: rate is our speed over the clip's ground speed, or over the configured pace without one.
			var ground = _animation.GroundSpeed( _sequences.Get( clip ) );
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
		if ( Aquatic && !IsUnderwater( WorldPosition ) )
		{
			Movement?.Stop();
			_strikeTarget = null;
			return;
		}

		var canEngage = _melee is not null || HasRangedAttack || _behaviours.Length > 0 || _manhack.IsValid();
		var target = !Passive && canEngage ? SelectCombatTarget() : null;
		if ( target.IsValid() && !_target.IsValid() )
			NpcVoice.Say( this, Voice, VoiceLine.Alert, target );
		_target = target;
		if ( !target.IsValid() && _nextIdleLine )
		{
			_nextIdleLine = Game.Random.Float( 10, 30 );
			NpcVoice.Say( this, Voice, VoiceLine.Idle );
		}

		foreach ( var behaviour in _behaviours )
		{
			if ( !behaviour.Think( target ) )
				continue;

			_strikeTarget = null;
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

		var squadGoal = _squadTarget.IsValid() ? _squadTarget.WorldPosition : _squadGoal;
		var goal = target.IsValid() ? target.WorldPosition : _command ?? squadGoal ?? (Leader.IsValid() ? Leader.WorldPosition : _home);
		var distance = target.IsValid() ? WorldPosition.Distance( target.WorldPosition ) : WorldPosition.Distance( goal );
		var ranged = HasRangedAttack;
		var meleeRange = !ranged ? AttackRange : _melee is null ? 0 : Math.Min( AttackRange, MeleeRange );
		// Approximation of Source's chase between attacks: hostile shooters with a melee attack (antlion workers)
		// close in, other shooters keep half their range.
		var engage = ranged && (Friendly || StandOff || _melee is null) ? AttackRange * 0.5f : meleeRange * 0.7f;
		var stop = target.IsValid() ? engage : Leader.IsValid() && !_command.HasValue && !squadGoal.HasValue ? 110 : 20;
		if ( _command.HasValue && !target.IsValid() && WorldPosition.Distance( _command.Value ) < 80 )
			_command = null;

		if ( Locomotion == Locomotions.Ground && !IsAirborne )
		{
			if ( Attacking || _strikeTarget.IsValid() )
			{
				if ( Movement?.MoveTarget is not null )
					Movement.Stop();
			}
			else if ( _lastPath > 0.4f )
			{
				if ( distance > stop )
					Movement?.MoveTo( goal, stop, PaceFor( target.IsValid() || _command.HasValue || squadGoal.HasValue ), target );
				else
					Movement?.Stop();
				_lastPath = 0;
			}
		}
		else if ( Movement is SwimLayer swimmer )
		{
			swimmer.MoveTo( target.IsValid() ? Npcs.CapturePoint( target ) : goal, stop, MoveSpeed );
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

		UpdateAttack( target, distance, meleeRange );
	}

	protected virtual GameObject SelectCombatTarget() => Senses?.FindBestTarget();

	protected virtual void UpdateAttack( GameObject target, float distance, float meleeRange )
	{
		var ranged = HasRangedAttack;
		if ( !target.IsValid() || Passive || !ranged && _melee is null || _strikeTarget.IsValid() || LastAttack < Math.Max( AttackInterval, _attackRecovery ) || distance > AttackRange )
			return;

		if ( Locomotion == Locomotions.Ground )
			WorldRotation = Rotation.LookAt( (target.WorldPosition - WorldPosition).WithZ( 0 ) );

		_rangedStrike = ranged && distance > meleeRange;
		var clip = _rangedStrike ? _ranged ?? Attack : Translate( "ACT_MELEE_ATTACK1", null ) ?? PickMelee() ?? Attack;
		_strikes = Npcs.AttackCues( Renderer.Model, clip, AttackDelay );
		_strikeIndex = 0;
		// A real attack activity holds the creature for its length; a stand-in clip (stalker, strider) only for the interval.
		var attackActivity = _attackActivities.Contains( clip ?? "" );
		var duration = Renderer.Model.AnimationClips().TryGetValue( clip ?? "", out var animation ) ? animation.Duration : 0;
		_attackHolds = attackActivity && duration > 0;
		_attackRecovery = _attackHolds ? duration : AttackInterval;
		LastAttack = 0;
		AttackClip = clip;
		AttackSerial++;
		_strikeTarget = target;
		if ( _rangedStrike && _strikes.Length > 0 )
			RangedEffects?.PresentCharge( _strikes[0].Time );
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
		return ground > 1 ? ground : hurry ? MoveSpeed * 1.6f : MoveSpeed;
	}

	bool HasRangedAttack => _projectile.IsValid() || RangedEffects.IsValid() || _photo.IsValid();


	void Strike( GameObject target, string hitEvent )
	{
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

		var presentation = _rangedStrike ? RangedEffects : null;
		var start = presentation?.Muzzle?.Position ?? WorldPosition + Vector3.Up * AttackHeight;
		var end = Npcs.CapturePoint( target );
		var range = _rangedStrike ? AttackRange : Math.Min( AttackRange, MeleeRange );
		if ( start.Distance( end ) > range + 40 )
			return;

		var tr = Scene.Trace.Ray( start, end ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
		presentation?.PresentShot( new( tr.EndPosition, tr.Hit, tr.Normal, tr.GameObject, tr.Surface, start ) );
		if ( tr.Hit && tr.GameObject?.Root != target.Root )
			return;

		Damage( target, start, tr.EndPosition, presentation?.DamageTag ?? (_rangedStrike ? "bullet" : "melee"), EventDamage( hitEvent ) );
	}

	internal void Damage( GameObject target, Vector3 origin, Vector3 position, string tag, float? amount = null )
	{
		amount ??= Npcs.IsPlayer( target ) ? PlayerAttackDamage ?? AttackDamage : AttackDamage;
		var damage = new DamageInfo { Damage = amount.Value, Attacker = GameObject, Weapon = GameObject, Origin = origin, Position = position };
		damage.Tags.Add( tag );
		target.GetComponentInParent<Component.IDamageable>()?.OnDamage( damage );
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

	bool IsUnderwater( Vector3 point ) => SourceWaterMap.IsUnderwater( Scene, point );

	protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
	{
		INpcPart.Hurt( this, damage );

		var attacker = damage.Instigator;
		if ( _flinches.Length > 0 && _lastFlinch > 1 && Health > 0 )
		{
			_lastFlinch = 0;
			Flinch = Game.Random.FromArray( _flinches );
			FlinchSerial++;
		}
		if ( attacker.IsValid() && !Passive && GetDisposition( attacker ) != Disposition.Friendly )
			SetRelationship( attacker, Disposition.Hostile, priority: 10 );
		if ( Health >= 1 )
			NpcVoice.Say( this, Voice, VoiceLine.Pain );
		if ( !Ambient || !attacker.IsValid() || attacker == GameObject )
			return;

		_scaredOf = attacker;
		_sinceScared = 0;
		EndCurrentSchedule();
	}

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

	protected override void OnDamaging( Component.IDamageable.DamageEvent damage )
	{
		INpcPart.PreDamage( this, damage );
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

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e ) => Npcs.FollowTooltip( Friendly || Passive, Leader );

	bool IPressable.CanPress( IPressable.Event e ) => Friendly || Passive;

	bool IPressable.Press( IPressable.Event e )
	{
		Follow( e.Source.GameObject );
		return true;
	}

	[Rpc.Host]
	void Follow( GameObject source )
	{
		if ( !(Friendly || Passive) || Npcs.FollowPlayer( source, WorldPosition ) is not { } player )
			return;

		Leader = Leader == player ? null : player;
		NpcVoice.Say( this, Voice, Leader.IsValid() ? VoiceLine.Follow : VoiceLine.Unfollow, player );
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
