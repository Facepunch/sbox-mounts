using System;
using Sandbox.Npcs.Layers;

namespace Source1;

/// <summary>
/// Animates an armed mounted NPC from its model's sequences: the weapon's full-body activities, the layers a player model plays
/// over its legs, flinch and shot gestures, the aim matrix, and the one-shot actions its behaviour starts.
/// </summary>
[Title( "Source NPC Animator" )]
[Category( "Source 1" )]
public sealed class SourceNpcAnimator : Component, BaseCombatWeapon.IEvents
{
	const string UpperLayer = "upper";
	const string ReloadLayer = "reload";

	static readonly string[] FlinchGestures = ["ACT_GESTURE_FLINCH_HEAD", "ACT_GESTURE_SMALL_FLINCH", "ACT_GESTURE_FLINCH_STOMACH", "ACT_GESTURE_FLINCH_CHEST", "ACT_GESTURE_FLINCH_LEFTARM", "ACT_GESTURE_FLINCH_RIGHTARM"];

	static readonly string[] ActionActivities = ["ACT_FLINCH_HEAD", "ACT_BIG_FLINCH", "ACT_SMALL_FLINCH", "ACT_FLINCH_STOMACH", "ACT_CIT_HEAL", "ACT_COMBINE_THROW_GRENADE", "ACT_COMBINE_AR2_ALTFIRE", "ACT_MELEE_ATTACK_SWING", "ACT_METROPOLICE_DEPLOY_MANHACK", "ACT_ACTIVATE_BATON", "ACT_DEACTIVATE_BATON"];

	SourceCombatNpc _npc;
	SourceCombatNpc Npc => _npc.IsValid() ? _npc : _npc = GetComponent<SourceCombatNpc>();
	SkinnedModelRenderer Renderer => Npc?.Renderer;

	SourceNpcWeaponLayers _layers;
	(Model, Model, SourceModelWeaponData, string, string) _resolvedFor;
	SourceModelSequences _sequences;
	SourceLinkAnimation _link;
	(string, bool)[] _choices = [];
	string _aimLayer;
	string _aimYawParameter;
	string _aimPitchParameter;
	string _reloadClip;
	int _shotCount;
	SourceAnimationPlayer _animation;
	TimeSince _lastShot = 10;
	bool _shotPending;
	bool _wasReloading;
	int _shownAction;
	int _shownGesture;
	float _aimYaw, _aimPitch, _aimWeight;

	/// <summary>The reload clip resolved for the current weapon, used to time the NPC reload. Empty for a looped reload, timed by the weapon.</summary>
	public string Reload => _layers?.Clips.Reload ?? (_layers?.ReloadLoops == true ? null : _reloadClip) ?? "";

	/// <summary>How long reloading <paramref name="rounds"/> takes: the reload clip, or one pass of a looped shotgun insert per round. Null without either.</summary>
	public float? ReloadDuration( int rounds )
	{
		var clips = Renderer?.Model?.AnimationClips();
		if ( clips is null )
			return null;

		if ( clips.TryGetValue( Reload, out var reload ) )
			return reload.Duration;

		var loops = _layers?.ReloadLoops == true;
		return loops && clips.TryGetValue( _reloadClip ?? "", out var insert ) ? insert.Duration * Math.Max( rounds, 1 ) : null;
	}

	/// <summary>True while the behaviour's action clip is the one playing.</summary>
	public bool PlayingAction => Npc?.Action is { } action && _animation?.Current == action;

	void BaseCombatWeapon.IEvents.OnWeaponEvent( BaseCombatWeapon weapon, string name )
	{
		if ( !string.Equals( name, WeaponEvents.Attack, StringComparison.OrdinalIgnoreCase ) )
			return;

		_lastShot = 0;
		_shotPending = true;
	}

	protected override void OnStart()
	{
		if ( IsProxy && Npc.IsValid() )
		{
			_shownAction = Npc.ActionSerial;
			_shownGesture = Npc.GestureSerial;
		}
	}

	Model WeaponModel => Npc.Weapon?.WorldModel?.GetComponentInChildren<SkinnedModelRenderer>()?.Model;

	/// <summary>
	/// Resolves the weapon layers once per body model and what the weapon says about how it is held: its model, script and hold.
	/// The HL2 acttable, or a Counter-Strike player model's own layers over its legs.
	/// </summary>
	bool ResolveLayers()
	{
		var model = Renderer.Model;
		var weapon = Npc.Weapon;
		var key = (model, WeaponModel, SourceWeaponSetup.WeaponData( weapon ), weapon?.HoldType, weapon?.HoldFallback);
		if ( _layers is not null && _resolvedFor == key )
			return false;

		_resolvedFor = key;
		_sequences = SourceModelSequences.Of( model );
		_link = new SourceLinkAnimation( _sequences );
		var weaponClass = Hl2ActTable.WeaponClass( _sequences, weapon );
		var acttable = Hl2ActTable.Resolve( _sequences, weaponClass );
		_layers = CssPlayerAnimState.Resolve( model, _sequences, weapon, key.Item2, acttable ) ?? acttable;
		_reloadClip = _layers.Reloads[0];
		_aimLayer = FindAimLayer( weaponClass );

		// The aim matrix: the HL2 one picked by name, else the one a weapon pose names as its additive layer.
		var aim = _sequences.Get( _aimLayer ) ?? _layers.Upper.Where( x => x is not null ).SelectMany( x => _sequences.Get( x ).AdditiveLayers )
			.Select( _sequences.Get ).FirstOrDefault( x => x is { Delta: true, BlendAxes.Length: 2 } );
		_aimYawParameter = aim?.BlendAxes.FirstOrDefault( a => a.Parameter.EndsWith( "yaw", StringComparison.OrdinalIgnoreCase ) )?.Parameter;
		_aimPitchParameter = aim?.BlendAxes.FirstOrDefault( a => a.Parameter.EndsWith( "pitch", StringComparison.OrdinalIgnoreCase ) )?.Parameter;
		return true;
	}

	/// <summary>
	/// The model's additive aim matrix (an aim_yaw by aim_pitch delta grid). Source autolayers it on the weapon
	/// sequences, which the mount does not export, so the standing matrix for the weapon is picked by name.
	/// </summary>
	string FindAimLayer( string weaponClass )
	{
		var aims = _sequences.All.Where( s => s.Delta && s.BlendAxes.Length == 2
			&& s.BlendAxes.Any( a => a.Parameter == "aim_yaw" ) && s.BlendAxes.Any( a => a.Parameter == "aim_pitch" ) ).Select( s => s.Name ).ToArray();
		var shotgun = weaponClass == "weapon_shotgun";
		return aims.Where( n => n.EndsWith( "_SG", StringComparison.OrdinalIgnoreCase ) == shotgun )
			.OrderByDescending( n => n.Contains( "stand", StringComparison.OrdinalIgnoreCase ) )
			.ThenByDescending( n => n.Contains( weaponClass["weapon_".Length..], StringComparison.OrdinalIgnoreCase ) )
			.ThenBy( n => n.Contains( "crouch", StringComparison.OrdinalIgnoreCase ) )
			.ThenBy( n => n.Length ).FirstOrDefault() ?? aims.FirstOrDefault();
	}

	/// <summary>A locomotion clip's ground speed at its normal rate; for a grid that starts from standing (Source's 9-way blend), its fastest cell's.</summary>
	float GroundSpeed( string clip ) => _sequences?.Get( clip ) is { } sequence
		? sequence.GroundSpeed > 0 ? sequence.GroundSpeed : sequence.BlendGridSpeeds.DefaultIfEmpty().Max()
		: 0;

	/// <summary>A 9-way grid holds a standing cell, so its pose carries the speed: move_x and move_y run from 0 at rest to 1 at full stride.</summary>
	bool ScalesStride( string clip ) => _sequences?.Get( clip ) is { GroundSpeed: <= 0 } sequence
		&& sequence.BlendGridSpeeds.Any( x => x < 1 ) && sequence.BlendGridSpeeds.Any( x => x > 1 );

	/// <summary>Playback rate that makes a locomotion clip's feet match how fast we actually move.</summary>
	float MoveRate( string clip, float speed, float fallback )
	{
		var ground = GroundSpeed( clip );
		if ( ground <= 1 )
			return Math.Clamp( speed / fallback, 0.5f, 1.5f );

		// Below full stride a 9-way grid slows by its pose, so only faster movement speeds the clip up.
		return Math.Clamp( speed / ground, ScalesStride( clip ) ? 1 : 0.3f, 2.5f );
	}

	void CreatePlayer()
	{
		_animation = new();
		_animation.EventFired += OnAnimationEvent;
		_animation.UseBlendGrids = true;
		_animation.Gestures = FlinchGestures.Select( _sequences.FindGesture ).Concat( _layers.Shots.SelectMany( x => x ) ).Where( x => x is not null ).Distinct().ToArray();
		_animation.AdditiveLayers = _aimLayer is null ? [] : [_aimLayer];
		// The weapon sequences hold bones with IK, such as the hands on the weapon bone; the mount names each held
		// bone and a target bone its clips animate. One weight drives every lock, so only holds all stances share are locked.
		var holds = _layers.Upper.Where( x => x is not null ).Distinct()
			.Select( x => _sequences.Get( x ).IkTargets.Select( t => (t.Bone, t.TargetBone) ).ToArray() ).ToArray();
		_animation.BoneLocks = holds.Length == 0 ? [] : holds.Skip( 1 ).Aggregate( holds[0], ( shared, next ) => shared.Intersect( next ).ToArray() );
		_animation.OverlayLayers = new Dictionary<string, IReadOnlyList<string>>
		{
			[UpperLayer] = _layers.Upper.Where( x => x is not null ).Distinct().ToArray(),
			[ReloadLayer] = _layers.Reloads.Where( x => x is not null ).Distinct().ToArray()
		};
		_choices = [.. _layers.Clips.All.Select( x => (x, true) ), .. ActionActivities.Select( x => (_sequences.FindActivity( x ), false) ), .. _link.Choices];
	}

	protected override void OnUpdate()
	{
		var npc = Npc;
		if ( !npc.IsValid() || !Renderer.IsValid() || !Renderer.SceneModel.IsValid() )
			return;

		var weapon = npc.Weapon;
		if ( weapon?.WorldModel.IsValid() == true )
		{
			var weaponRenderer = weapon.WorldModel.GetComponentInChildren<SkinnedModelRenderer>();
			if ( weaponRenderer.IsValid() && weaponRenderer.BoneMergeTarget != Renderer )
			{
				weapon.WorldModel.Parent = Renderer.GameObject;
				weapon.WorldModel.LocalTransform = global::Transform.Zero;
				weaponRenderer.BoneMergeTarget = Renderer;
			}
		}

		if ( ResolveLayers() )
		{
			_animation?.Dispose();
			_animation = null;
		}

		if ( _animation is null )
			CreatePlayer();

		if ( !_animation.Ensure( Renderer, _choices ) )
			return;

		if ( npc.GestureSerial != _shownGesture )
		{
			_shownGesture = npc.GestureSerial;
			_animation.PlayGesture( npc.Gesture );
		}

		UpdatePoseParameters();

		if ( npc.ActionSerial != _shownAction )
		{
			_shownAction = npc.ActionSerial;
			_animation.Play( npc.Action, restart: true );
		}

		if ( PlayingAction && !_animation.Finished )
		{
			_animation.Tick();
			return;
		}

		if ( _link.Clip( npc.Animation, _animation, out var restart ) is { } linkClip )
		{
			_animation.Play( linkClip, restart: restart );
			_animation.StopOverlay( UpperLayer );
			_animation.SetLayerWeight( SourceAnimationPlayer.BoneLockLayer, _animation.OverlayWeight( UpperLayer ) * (1 - _animation.OverlayWeight( ReloadLayer )) );
			_animation.Tick();
			return;
		}

		UpdateLocomotion( npc, weapon );
	}

	void UpdateLocomotion( SourceCombatNpc npc, BaseCombatWeapon weapon )
	{
		var clips = _layers.Clips;
		var reloading = weapon?.IsReloading == true;
		if ( reloading && !_wasReloading && _sequences.Get( Reload )?.Events.Any( SourceSounds.IsSoundEvent ) != true
			&& SourceSounds.WeaponSound( weapon, "reload" ) is { } reloadSound )
			Sound.Play( reloadSound, WorldPosition );
		_wasReloading = reloading;

		var aiming = npc.Animation.WeaponStance == AnimationLayer.WeaponStances.Ready;
		var speed = npc.Animation.MoveVelocity.WithZ( 0 ).Length;
		// Run once we are closer to the run clip's ground speed than the walk clip's, as Source picks by speed.
		var walkSpeed = GroundSpeed( clips.Walking( aiming ) );
		var runSpeed = GroundSpeed( clips.Running( aiming ) );
		var running = walkSpeed > 1 && runSpeed > walkSpeed ? speed > (walkSpeed + runSpeed) * 0.5f : speed > ((npc.Movement?.WalkSpeed ?? 0) + (npc.Movement?.RunSpeed ?? 0)) * 0.5f;
		var stance = running ? 2 : speed > 10 ? 1 : 0;
		// The stance a reload starts in picks its clip, as CCSPlayerAnimState does.
		if ( !reloading )
			_reloadClip = _layers.Reloads[stance];

		// Moving, already part way through it, or without a full-body reload, the reload stays on the upper body and the legs keep going.
		var upperReload = reloading && _reloadClip is not null && (speed > 10 || clips.Reload is null || _animation.OverlayClip( ReloadLayer ) == _reloadClip);
		var reloadTime = weapon is SourceBulletWeapon { NpcReloadAnimationTime: > 0 } mountedWeapon
			? mountedWeapon.NpcReloadAnimationTime
			: MathF.Max( weapon?.ReloadTime ?? 1, 0.1f );
		if ( upperReload && Renderer.Model.AnimationClips().TryGetValue( _reloadClip, out var gestureClip ) )
			_animation.PlayOverlay( ReloadLayer, _reloadClip, _layers.ReloadLoops ? 1 : gestureClip.Duration / reloadTime, loop: _layers.ReloadLoops );
		else if ( !reloading )
			_animation.StopOverlay( ReloadLayer );

		var name = reloading && !upperReload && !string.IsNullOrEmpty( Reload ) ? Reload
			: running ? clips.Running( aiming )
			: speed > 10 ? clips.Walking( aiming )
			: _lastShot < 0.2f && clips.Attack is not null ? clips.Attack
			: clips.Standing( aiming );

		var rate = name == Reload && Renderer.Model.AnimationClips().TryGetValue( Reload ?? "", out var clip )
			? clip.Duration / reloadTime
			: running ? MoveRate( name, speed, npc.Movement?.RunSpeed ?? 0 )
			: speed > 10 ? MoveRate( name, speed, npc.Movement?.WalkSpeed ?? 0 )
			: 1;
		_animation.Play( name, rate, restart: name == clips.Attack && _shotPending );

		// Source's 9-way blend: move_x and move_y are the walk direction in the body's frame, scaled by speed over the grid's full stride.
		var local = WorldRotation.Inverse * npc.Animation.MoveVelocity.WithZ( 0 );
		var stride = speed > 10 && ScalesStride( name ) ? MathF.Min( 1, speed / GroundSpeed( name ) ) / speed : 0;
		_animation.SetPoseParameter( "move_x", local.x * stride );
		_animation.SetPoseParameter( "move_y", local.y * stride );

		// The weapon pose runs on the legs' cycle, as CCSPlayerAnimState's aim layer does.
		if ( _layers.Upper[stance] is { } upper && Renderer.Model.AnimationClips().TryGetValue( upper, out var upperClip )
			&& Renderer.Model.AnimationClips().TryGetValue( name ?? "", out var legs ) && legs.Duration > 0 )
			_animation.PlayOverlay( UpperLayer, upper, upperClip.Duration / legs.Duration * rate, loop: true );
		else
			_animation.StopOverlay( UpperLayer );

		// The hands hold the weapon pose's grips, except while a reload moves them.
		_animation.SetLayerWeight( SourceAnimationPlayer.BoneLockLayer, _animation.OverlayWeight( UpperLayer ) * (1 - _animation.OverlayWeight( ReloadLayer )) );

		var shots = _layers.Shots[stance];
		if ( _shotPending && shots.Length > 0 )
			_animation.PlayGesture( shots[_shotCount++ % shots.Length] );
		_shotPending = false;
		_animation.Tick();
	}

	/// <summary>move_yaw follows the walk direction relative to the body, the aim matrix's yaw and pitch the synced look target.</summary>
	void UpdatePoseParameters()
	{
		var npc = Npc;
		var velocity = npc.Animation.MoveVelocity.WithZ( 0 );
		if ( velocity.Length > 10 )
			_animation.SetPoseParameter( "move_yaw", Angles.NormalizeAngle( Rotation.LookAt( velocity ).Yaw() - WorldRotation.Yaw() ) );
		else
			_animation.SetPoseParameter( "move_yaw", 0 );

		if ( _aimYawParameter is null )
			return;

		var aiming = npc.Animation.WeaponStance == AnimationLayer.WeaponStances.Ready && npc.Animation.IsLooking && (npc.Action is null || _animation.Current != npc.Action)
			&& npc.Animation.Traversal is null;
		var aim = Rotation.LookAt( npc.Animation.CalculateLookPosition() - npc.EyePosition ).Angles();
		_aimYaw = MathX.Lerp( _aimYaw, aiming ? Angles.NormalizeAngle( aim.yaw - WorldRotation.Yaw() ) : 0, Time.Delta * 8 );
		// Source aim and body pitch are the pitch of the aim direction, positive down like ours (CAI_BaseNPC::SetAim, CBasePlayerAnimState).
		_aimPitch = MathX.Lerp( _aimPitch, aiming ? aim.pitch : 0, Time.Delta * 8 );
		_aimWeight = MathX.Lerp( _aimWeight, aiming ? 1 : 0, Time.Delta * 6 );
		_animation.SetPoseParameter( _aimYawParameter, _aimYaw );
		if ( _aimPitchParameter is not null )
			_animation.SetPoseParameter( _aimPitchParameter, _aimPitch );
		if ( _aimLayer is not null )
			_animation.SetLayerWeight( _aimLayer, _aimWeight );
	}

	void OnAnimationEvent( SourceModelSequenceData.Event e )
	{
		if ( Networking.IsHost && PlayingAction )
			Npc.OnActionEvent( e );
	}

	protected override void OnDestroy() => _animation?.Dispose();
}
