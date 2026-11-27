using System;
using Sandbox.Npcs.Schedules;
using Sandbox.Npcs;

namespace Source1;

/// <summary>Shared armed mounted NPC behaviour: actions, flinches, medic, grenades, manhacks, and the model and team weapon picked at spawn. <see cref="NpcAnimator"/> animates it.</summary>
[Title( "Source Combat NPC" )]
[Category( "Source 1" )]
[Alias( "Source1.SourceCombatNpc", "SourceCombatNpc" )]
public sealed class CombatNpc : BaseCombatNpc, Component.IDamageable.IEvents, Component.IPressable, BaseCombatWeapon.IEvents
{
	/// <summary>Each shot counts as an attack for the kick's warning.</summary>
	void BaseCombatWeapon.IEvents.OnWeaponEvent( BaseCombatWeapon weapon, string name )
	{
		if ( string.Equals( name, WeaponEvents.Attack, StringComparison.OrdinalIgnoreCase ) )
			_lastAttack = 0;
	}

	/// <summary>
	/// When true, this NPC is friendly to players and will follow them, engaging hostile NPCs.
	/// When false, this NPC targets players and friendly NPCs.
	/// </summary>
	[Property, Title( "#entity.property.npc_friendly" ), Description( "#entity.description.npc_friendly" ), ClientEditable]
	public bool Friendly
	{
		get => Faction == Factions.Ally;
		set
		{
			// Faction is restored first when both properties are saved.
			if ( value == Friendly )
				return;

			Faction = value ? Factions.Ally : Factions.Enemy;

			if ( !value )
				Leader = null;
		}
	}

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e )
	{
		if ( !Friendly )
			return null;

		return Leader.IsValid()
			? new IPressable.Tooltip( "#npc.action.stop_following", "person_off", null )
			: new IPressable.Tooltip( "#npc.action.follow", "follow_the_signs", null );
	}

	bool IPressable.CanPress( IPressable.Event e ) => Friendly;

	bool IPressable.Press( IPressable.Event e )
	{
		ToggleFollow( e.Source.GameObject );
		return true;
	}

	[Rpc.Host]
	private void ToggleFollow( GameObject presserObject )
	{
		if ( !Friendly || !presserObject.IsValid() )
			return;

		var player = presserObject.Root.GetComponent<PlayerController>();
		if ( !player.IsValid() )
			return;

		if ( Rpc.Caller is not null && player.Network.Owner != Rpc.Caller )
			return;

		Leader = Leader == player.GameObject ? null : player.GameObject;
	}

	/// <summary>
	/// An enemy inside the weapon's minimum range is too close to shoot. The NPC runs this far straight away from where the
	/// enemy was, or failing that at least two and up to four hull widths, then shoots again once the enemy is out of it.
	/// </summary>
	const float BackAwayDistance = 120;
	const float BackAwayNearWidths = 2;
	const float BackAwayFarWidths = 4;

	/// <summary>How far round the NPC the back-away looks for a walkable spot farther from the enemy when the straight run is blocked.</summary>
	const float BackAwaySearchRadius = 256;

	/// <summary>A soldier stepping in towards a too-close enemy stops this close to where it saw it.</summary>
	const float StepInDistance = 48;

	/// <summary>
	/// The Combine kick (a rifle-butt strike): chosen within 64 units, about 45 degrees of facing and 64 units of height; it
	/// lands at animation event 3, sweeping a 32 x 32 x 36 box 70 units ahead, pushing a struck player 100 back and 50 up
	/// and jolting the view 12 degrees up and 7 to the side.
	/// </summary>
	const float KickRange = 64;
	const float KickCone = 0.7f;
	const float KickHeight = 64;
	const int KickHitEvent = 3;
	const float KickReach = 70;
	static readonly Vector3 KickBox = new( 32, 32, 36 );
	static readonly Vector2 KickPush = new( 100, 50 );
	static readonly Angles KickViewPunch = new( -12, -7, 0 );

	/// <summary>A player not looking at a soldier that hasn't attacked (shot or kicked) for this long is warned before the kick, which then waits up to <see cref="KickWarnWait"/> for them to look.</summary>
	const float KickWarnAfter = 3;
	const float KickWarnWait = 2;

	/// <summary>npc_combine's grenade toss animation event, COMBINE_AE_GREN_TOSS, and the toss speed of its throw arc.</summary>
	const int CombineAeGrenToss = 7;
	const float ThrowSpeed = 650;

	/// <summary>
	/// EVENT_WEAPON_MELEE_HIT, and weapon_stunstick's swing: a 32 unit hull trace from the shoot position, at an enemy within 48 units
	/// or about to be (sk_crowbar_lead_time), no more than 70 units above or below.
	/// </summary>
	const int EventWeaponMeleeHit = 3001;
	const float MeleeReach = 32;
	const float MeleeRange = 48;
	const float MeleeLeadTime = 0.35f;
	const float MeleeHeight = 70;

	/// <summary>
	/// How long a call-out waits for the speech around to let it through. Source only keeps a squad from talking over itself,
	/// so a line held back by the other side's speech is said a moment later rather than dropped.
	/// </summary>
	const float CalloutPatience = 4;

	/// <summary>weapon_ar2's NPC alt fire launches the combine ball at 1000 units per second; DelaySquadAltFireAttack holds the squad's next one for 10 seconds.</summary>
	const float CombineBallSpeed = 1000;
	const float AltFireDelay = 10;

	/// <summary>CBasePlayer's m_flFieldOfView: the player is looking at what lies within this cosine, in 2D.</summary>
	const float PlayerViewCone = 0.766f;

	/// <summary>A friendly medic heals players with the citizen heal animation.</summary>
	[Property] public bool Medic { get; set; }

	/// <summary>Frag grenades this soldier can throw, like npc_combine's NumGrenades keyvalue.</summary>
	[Property] public int Grenades { get; set; }

	/// <summary>npc_combine_s on the super soldier model: fires its weapon's combine ball at players and their allies instead of throwing grenades, while it has <see cref="Grenades"/>.</summary>
	[Property] public bool Elite { get; set; }

	[Property] public bool DistractedByBugbait { get; set; }

	/// <summary>Never backs away: with an enemy inside its weapon's minimum range it steps in towards it instead, as Combine soldiers do.</summary>
	[Property] public bool StepsIn { get; set; }

	/// <summary>Strikes an enemy right in front of it with its weapon's butt before doing anything else, as Combine soldiers do.</summary>
	[Property] public bool Kicks { get; set; }

	/// <summary>Manhacks this metrocop can deploy, like npc_metropolice's manhacks keyvalue.</summary>
	[Property] public int Manhacks { get; set; }

	/// <summary>The NPC class's sound script prefix, such as NPC_MetroPolice, for its <see cref="NpcVoice"/> lines.</summary>
	[Property] public string Voice { get; set; }

	/// <summary>The one-shot clip the behaviour plays, such as a flinch or a grenade throw, restarted on every client when <see cref="ActionSerial"/> changes.</summary>
	[Sync( SyncFlags.FromHost )] public string Action { get; private set; }
	[Sync( SyncFlags.FromHost )] public int ActionSerial { get; private set; }

	/// <summary>An additive flinch layered over whatever plays, restarted when <see cref="GestureSerial"/> changes.</summary>
	[Sync( SyncFlags.FromHost )] public string Gesture { get; private set; }
	[Sync( SyncFlags.FromHost )] public int GestureSerial { get; private set; }

	[RequireComponent] public NpcAnimator Animator { get; set; }

	TimeSince _actionTime;
	float _actionDuration;
	Func<SourceModelSequenceData.Event, bool> _actionRelease;
	TimeUntil? _bugbaitReaction;
	string _queuedAction;
	GameObject _actionTarget;
	GameObject _heldManhack;
	TimeSince _lastFlinch = 10;
	TimeSince _lastAttack = 10;
	TimeSince _lastHeal = 1000;
	TimeSince _lastPatientCheck;
	TimeSince _lastGrenadeCheck = 10;
	float _nextGrenadeCheck;
	TimeUntil _nextAltFire;
	TimeUntil _nextSwing;
	TimeUntil _batonDebounce;
	GameObject _enemy;
	(VoiceLine Line, GameObject LookAt, TimeUntil Expires)? _pendingCallout;
	bool _wasReloading;
	TimeUntil _nextIdleLine = 10;
	IReadOnlyDictionary<string, float> _skill = new Dictionary<string, float>();

	ModelSequences Sequences => Renderer?.Model is { } model ? ModelSequences.Of( model ) : null;

	string Clip( string activity ) => Sequences?.FindActivity( activity );

	string GestureClip( string activity ) => Sequences?.FindGesture( activity );

	protected override async void OnStart()
	{
		base.OnStart();
		if ( Weapon.IsValid() && Senses.IsValid() )
			Senses.SightRange = MathF.Max( Senses.SightRange, Weapon.Npc.MaxRange );
		_skill = await SkillData.Read( MountContent.Of( GameObject ) );
	}

	/// <summary>
	/// Plays a one-shot activity on every client. <paramref name="release"/> sees its animation events on the host and returns true
	/// once it has acted, such as spawning the grenade at the toss event; it gets null at the clip's end if that event never came.
	/// </summary>
	internal void StartAction( string clip, Func<SourceModelSequenceData.Event, bool> release )
	{
		Action = clip;
		ActionSerial++;
		_actionTime = 0;
		_actionDuration = Renderer.Model.AnimationClips().TryGetValue( clip ?? "", out var animation ) ? animation.Duration : 0.5f;
		_actionRelease = release;
		Movement?.Stop();
	}

	/// <summary>True while the action plays, facing <paramref name="faceTarget"/>.</summary>
	internal bool UpdateAction( GameObject faceTarget )
	{
		if ( faceTarget.IsValid() )
			WorldRotation = Rotation.Lerp( WorldRotation, Rotation.LookAt( (faceTarget.WorldPosition - WorldPosition).WithZ( 0 ) ), Time.Delta * 8 );

		if ( _actionTime < _actionDuration )
			return true;

		Release( null );
		return false;
	}

	/// <summary>Ends the action, finished or interrupted: an unreleased throw is dropped and the clip stops on every client.</summary>
	internal void EndAction()
	{
		_actionRelease = null;
		Action = null;
		ReleaseManhack();
	}

	/// <summary>An animation event of the playing action, on the host.</summary>
	internal void OnActionEvent( SourceModelSequenceData.Event e ) => Release( e );

	void Release( SourceModelSequenceData.Event e )
	{
		if ( _actionRelease?.Invoke( e ) == true )
			_actionRelease = null;
	}

	protected override BaseSchedule SelectSchedule()
	{
		if ( _queuedAction is { } queued )
		{
			_queuedAction = null;
			return ActionSchedule( queued, _actionTarget, null );
		}

		if ( Medic && Friendly && FindPatient() is { } patient )
		{
			var heal = GetSchedule<HealSchedule>();
			heal.Patient = patient;
			return heal;
		}

		var enemy = Senses.FindBestTarget();
		if ( !enemy.IsValid() )
		{
			// npc_metropolice: SCHED_METROPOLICE_DEACTIVATE_BATON once calm again and m_flBatonDebounceTime has passed.
			var calm = base.SelectSchedule();
			if ( calm is { Priority: < SchedulePriorities.Alert } && Weapon is Hl2.Stunstick { StunActive: true } activeStick && _batonDebounce && Clip( "ACT_DEACTIVATE_BATON" ) is { } offClip )
				return BatonSchedule( activeStick, offClip, false, null );
			return calm;
		}

		if ( Manhacks > 0 && Clip( "ACT_METROPOLICE_DEPLOY_MANHACK" ) is { } deployClip && MountContent.Prefab( MountContent.Of( GameObject ), "manhack" ) is { } manhack )
			return ActionSchedule( deployClip, enemy, e => DeployManhack( e, manhack ) );

		// npc_metropolice switches the baton on (SCHED_METROPOLICE_ACTIVATE_BATON) before anything else it does with it.
		if ( Weapon is Hl2.Stunstick { StunActive: false } stick && Clip( "ACT_ACTIVATE_BATON" ) is { } onClip )
		{
			_batonDebounce = Game.Random.Float( 2.5f, 4 );
			return BatonSchedule( stick, onClip, true, enemy );
		}

		// npc_metropolice with a baton: swing once in reach and m_BatonSwingTimer (1 to 1.75 seconds) allows, else close in.
		if ( Weapon is MeleeWeapon melee && Clip( "ACT_MELEE_ATTACK_SWING" ) is { } swingClip )
		{
			var reach = InMeleeReach( enemy );
			if ( reach && _nextSwing )
			{
				_nextSwing = Game.Random.Float( 1, 1.75f );
				return ActionSchedule( swingClip, enemy, e => MeleeHit( e, melee, enemy ) );
			}

			var chase = GetSchedule<ChaseSchedule>();
			chase.Target = enemy;
			chase.Speed = Movement?.RunSpeed ?? 0;
			chase.InReach = reach;
			chase.Reach = () => InMeleeReach( enemy );
			return chase;
		}

		// A possible kick comes before every other attack, whatever the weapon and its ammo.
		if ( CanKick( enemy ) && Clip( "ACT_MELEE_ATTACK1" ) is { } kickClip )
		{
			// A player who isn't looking, and hasn't been attacked for a while, is warned first and given a moment to look.
			// The warning opens the attack, so the kick that follows it isn't warned again.
			var unwarned = _lastAttack > KickWarnAfter;
			_lastAttack = 0;
			if ( unwarned && enemy.GetComponent<PlayerController>() is { } player && !LookedAtBy( player ) )
			{
				CallOut( VoiceLine.Announce, enemy );
				var warn = GetSchedule<WarnSchedule>();
				warn.Target = enemy;
				warn.Duration = KickWarnWait;
				warn.Over = () => LookedAtBy( player ) || KickInterrupted( enemy );
				return warn;
			}

			return ActionSchedule( kickClip, enemy, e => Kick( e ), () => KickInterrupted( enemy ) );
		}

		if ( TooClose( enemy ) )
		{
			if ( StepsIn )
			{
				AnnounceStepIn( enemy );
				var close = GetSchedule<StepInSchedule>();
				close.Target = enemy;
				close.Goal = enemy.WorldPosition;
				close.StopDistance = StepInDistance;
				close.Speed = Movement?.RunSpeed ?? 0;
				close.TooClose = () => TooClose( enemy );
				return close;
			}

			var away = GetSchedule<BackAwaySchedule>();
			away.Target = enemy;
			away.From = enemy.WorldPosition;
			away.Spot = BackAwaySpot( enemy.WorldPosition );
			away.Speed = Movement?.RunSpeed ?? 0;
			away.TooClose = () => TooClose( enemy );
			return away;
		}

		if ( Weapon is BulletWeapon { AlternateProjectile: Projectile.Kinds.EnergyBall } ar2 && CanAltFire( enemy, out var ballTarget ) && Clip( "ACT_COMBINE_AR2_ALTFIRE" ) is { } altFireClip )
		{
			// TASK_ANNOUNCE_ATTACK: a player not looking this way is warned.
			if ( enemy.GetComponent<PlayerController>() is { } player && !LookedAtBy( player ) )
				CallOut( VoiceLine.Announce, enemy );
			return ActionSchedule( altFireClip, enemy, e => AltFire( e, ar2, ballTarget ) );
		}

		if ( !Elite && Grenades > 0 && CanThrowGrenade( enemy, out var toss ) && Clip( "ACT_COMBINE_THROW_GRENADE" ) is { } throwClip )
		{
			_nextGrenadeCheck = 6;
			CallOut( VoiceLine.Grenade, enemy );
			return ActionSchedule( throwClip, enemy, e => ThrowGrenade( e, enemy, toss ) );
		}

		return base.SelectSchedule();
	}

	ActionSchedule ActionSchedule( string clip, GameObject faceTarget, Func<SourceModelSequenceData.Event, bool> release, Func<bool> interrupted = null )
	{
		var schedule = GetSchedule<ActionSchedule>();
		schedule.Clip = clip;
		schedule.FaceTarget = faceTarget;
		schedule.Release = release;
		schedule.Interrupted = interrupted;
		return schedule;
	}

	/// <summary>The enemy is inside the minimum range of the weapon in hand, so the NPC can't shoot it, and it can't kick it either.</summary>
	internal bool TooClose( GameObject enemy )
		=> enemy.IsValid() && Weapon.IsValid() && Weapon.Npc.MinRange > 0 && Weapon.IsActive && Weapon.Holder == this
			&& CloseRangeDistance( GameObject, enemy ) < Weapon.Npc.MinRange && !CanKick( enemy );

	/// <summary>Why a kick can't reach an enemy, or <see cref="KickCheck.Ok"/>.</summary>
	internal enum KickCheck { Ok, Unable, Headcrab, Far, NotFacing, Height, Blocked }

	internal bool CanKick( GameObject enemy ) => CheckKick( enemy ) == KickCheck.Ok;

	/// <summary>
	/// A kicking soldier can strike an enemy within <see cref="KickRange"/> (the close-range distance), within about 45 degrees
	/// of its body's facing, with feet no more than <see cref="KickHeight"/> above or below its own, in a clear line from body
	/// centre to body centre, and that isn't a headcrab.
	/// </summary>
	internal KickCheck CheckKick( GameObject enemy )
	{
		if ( !Kicks || !enemy.IsValid() )
			return KickCheck.Unable;
		if ( NpcVoice.ClassOf( enemy )?.StartsWith( "npc_headcrab", StringComparison.Ordinal ) == true )
			return KickCheck.Headcrab;
		if ( CloseRangeDistance( GameObject, enemy ) > KickRange )
			return KickCheck.Far;
		if ( (enemy.WorldPosition - WorldPosition).WithZ( 0 ).Normal.Dot( WorldRotation.Forward.WithZ( 0 ).Normal ) < KickCone )
			return KickCheck.NotFacing;
		if ( MathF.Abs( enemy.WorldPosition.z - WorldPosition.z ) > KickHeight )
			return KickCheck.Height;

		var tr = Scene.Trace.Ray( Body( GameObject ).Center, Body( enemy ).Center ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ragdoll", "debris" ).Run();
		return !tr.Hit || tr.GameObject?.Root == enemy.Root ? KickCheck.Ok : KickCheck.Blocked;
	}

	/// <summary>A kick is cut short, before or after it lands, by a new enemy, the enemy's death or losing sight of it. Damage ends it too.</summary>
	bool KickInterrupted( GameObject enemy )
		=> Npcs.HealthOf( enemy ) is not { Health: > 0 } || Senses.IsValid() && (Senses.FindBestTarget() is { } best && best != enemy || !Senses.VisibleHostiles.Contains( enemy ));

	/// <summary>
	/// The kick lands at its animation event: a box swept forward along the body's facing from half its height, then from
	/// just above its head, strikes the first character it hates. World geometry stops it; friends and props don't.
	/// A struck player is pushed back and up and has the view jolted. The soldier grunts, hit or miss.
	/// </summary>
	bool Kick( SourceModelSequenceData.Event e )
	{
		// A clip that ends without the strike event strikes nothing.
		if ( e is null )
			return true;
		if ( e.Id != KickHitEvent )
			return false;

		NpcVoice.Say( this, Voice, VoiceLine.Kick );
		var body = Body( GameObject );
		var forward = WorldRotation.Forward.WithZ( 0 ).Normal;
		var start = WorldPosition + Vector3.Up * body.Size.z * 0.5f;
		var end = start + forward * KickReach;
		if ( (KickSweep( start, end ) ?? KickSweep( WorldPosition.WithZ( body.Maxs.z + 1 ), end )) is not { } hit )
			return true;

		var victim = hit.GameObject;
		var damage = new DamageInfo
		{
			Damage = Elite ? _skill.Get( "sk_combine_guard_kick", 15 ) : _skill.Get( "sk_combine_s_kick", 10 ),
			Attacker = GameObject,
			Weapon = Weapon?.GameObject,
			Origin = start,
			Position = hit.Position
		};
		damage.Tags.Add( DamageTags.Melee );
		Npcs.HealthOf( victim )?.OnDamage( damage );
		PlayMountedSound( "NPC_Combine.WeaponBash", WorldPosition );

		if ( Npcs.IsPlayer( victim ) )
		{
			Npcs.Knockback( victim, forward * KickPush.x + Vector3.Up * KickPush.y );
			PunchView( victim, KickViewPunch );
		}
		return true;
	}

	/// <summary>The first hated character the kick's box meets between <paramref name="from"/> and <paramref name="to"/>, unless world geometry is in the way.</summary>
	(GameObject GameObject, Vector3 Position)? KickSweep( Vector3 from, Vector3 to )
	{
		var box = BBox.FromPositionAndSize( Vector3.Zero, KickBox );
		foreach ( var tr in Scene.Trace.Box( box, from, to ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ragdoll", "debris" ).RunAll().OrderBy( x => x.Fraction ) )
		{
			var hit = tr.GameObject;
			var character = hit?.GetComponentInParent<BaseNpc>()?.GameObject ?? hit?.GetComponentInParent<PlayerController>()?.GameObject;
			if ( character.IsValid() )
			{
				if ( GetDisposition( character ) == Disposition.Hostile && Npcs.HealthOf( character ) is { Health: > 0 } )
					return (character, tr.HitPosition);
				continue;
			}

			// Props and other damageable or loose objects let the kick through; anything fixed stops it.
			if ( hit?.GetComponentInParent<Component.IDamageable>() is not null || hit?.GetComponentInParent<Rigidbody>() is { MotionEnabled: true } )
				continue;
			return null;
		}
		return null;
	}

	/// <summary>Jolts a struck player's view. The owner simulates its own view.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void PunchView( GameObject victim, Angles punch )
	{
		var player = victim?.GetComponent<PlayerController>();
		if ( player.IsValid() && !player.IsProxy )
			ViewPunch.Apply( player, punch, reset: false );
	}

	/// <summary>
	/// The distance a weapon's minimum range is measured over: between the centres of the two bodies, leaving out any height
	/// they share. Side by side on a floor that is the flat distance; one above the other adds only the gap between them.
	/// </summary>
	internal static float CloseRangeDistance( GameObject from, GameObject to )
	{
		var a = Body( from );
		var b = Body( to );
		var gap = b.Mins.z > a.Maxs.z ? b.Mins.z - a.Maxs.z : a.Mins.z > b.Maxs.z ? a.Mins.z - b.Maxs.z : 0;
		return MathF.Sqrt( a.Center.WithZ( 0 ).DistanceSquared( b.Center.WithZ( 0 ) ) + gap * gap );
	}

	/// <summary>A body's hull in the world: a player's, the collision box a mounted NPC's prefab gives it, else its bounds.</summary>
	internal static BBox Body( GameObject go )
	{
		if ( go.GetComponent<PlayerController>() is { } player && player.IsValid() )
		{
			var radius = new Vector3( player.BodyRadius, player.BodyRadius, 0 );
			return new BBox( player.WorldPosition - radius, player.WorldPosition + radius + Vector3.Up * player.CurrentHeight );
		}

		if ( go.Children.FirstOrDefault( x => x.Name == "collider" )?.GetComponent<BoxCollider>() is { } box && box.IsValid() )
			return BBox.FromPositionAndSize( box.WorldPosition + box.WorldRotation * box.Center, box.Scale );

		return go.GetBounds();
	}

	/// <summary>
	/// Where to back away to from an enemy at <paramref name="threat"/>: the full run straight away (sliding along a wall it
	/// meets), else the nearest walkable spot farther from the enemy on this side of it, else a shorter run straight away.
	/// Null when cornered.
	/// </summary>
	Vector3? BackAwaySpot( Vector3 threat )
	{
		var away = (WorldPosition - threat).WithZ( 0 );
		away = away.Length > 1 ? away.Normal : -WorldRotation.Forward.WithZ( 0 ).Normal;
		var width = Body( GameObject ).Size.x;

		if ( RunAway( away, BackAwayDistance, out var spot ) >= BackAwayDistance - 1 )
			return spot;
		if ( FartherSpot( threat, away, width ) is { } point )
			return point;
		if ( RunAway( away, width * BackAwayFarWidths, out spot ) >= width * BackAwayNearWidths )
			return spot;
		return null;
	}

	/// <summary>
	/// How far the NPC's hull gets along <paramref name="direction"/>, sliding along the first thing it meets for the rest of
	/// <paramref name="distance"/>, and the walkable spot it ends on. Zero when that spot is off the navmesh.
	/// </summary>
	float RunAway( Vector3 direction, float distance, out Vector3 end )
	{
		var hull = MoveHull();
		var start = WorldPosition;
		var covered = 0f;
		for ( var leg = 0; leg < 2 && covered < distance - 1; leg++ )
		{
			var tr = MoveTrace( hull, start, start + direction * (distance - covered) );
			if ( tr.StartedSolid )
				break;

			covered += tr.Distance;
			start = tr.EndPosition;
			if ( !tr.Hit )
				break;

			direction = (direction - tr.Normal * direction.Dot( tr.Normal )).WithZ( 0 );
			if ( direction.Length < 0.1f )
				break;
			direction = direction.Normal;
		}

		end = start;
		if ( Scene.NavMesh.GetClosestPoint( start, 32 ) is not { } ground || ground.WithZ( 0 ).Distance( start.WithZ( 0 ) ) > 16 )
			return 0;

		end = ground;
		return covered;
	}

	/// <summary>The nearest walkable spot round the NPC that is farther from the enemy, on the far side from it and in a clear line.</summary>
	Vector3? FartherSpot( Vector3 threat, Vector3 away, float width )
	{
		var hull = MoveHull();
		var range = WorldPosition.WithZ( 0 ).Distance( threat.WithZ( 0 ) );
		Vector3? best = null;
		var bestDistance = float.MaxValue;
		for ( var i = 0; i < 12; i++ )
		{
			if ( Scene.NavMesh.GetRandomPoint( WorldPosition, BackAwaySearchRadius ) is not { } point )
				continue;

			var offset = (point - WorldPosition).WithZ( 0 );
			if ( offset.Length < width * BackAwayNearWidths || offset.Length >= bestDistance || offset.Normal.Dot( away ) <= 0
				|| point.WithZ( 0 ).Distance( threat.WithZ( 0 ) ) <= range || MoveTrace( hull, WorldPosition, point.WithZ( WorldPosition.z ) ).Hit )
				continue;

			best = point;
			bestDistance = offset.Length;
		}
		return best;
	}

	/// <summary>The NPC's hull from step height up, relative to its feet, for checking where it can move.</summary>
	BBox MoveHull()
	{
		var body = Body( GameObject );
		var step = MathF.Min( Scene.NavMesh.AgentStepSize, body.Size.z * 0.5f );
		return new BBox( (body.Mins - WorldPosition).WithZ( step ), body.Maxs - WorldPosition );
	}

	SceneTraceResult MoveTrace( BBox hull, Vector3 from, Vector3 to )
		=> Scene.Trace.Box( hull, from, to ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "player", "playercontroller", "npc", "debris" ).Run();

	/// <summary>One time in three a soldier stepping in calls it, if the enemy is within 2000 units and can see it.</summary>
	void AnnounceStepIn( GameObject enemy )
	{
		if ( Game.Random.Int( 0, 2 ) != 0 || enemy.WorldPosition.Distance( WorldPosition ) > 2000 )
			return;

		var seen = enemy.GetComponent<PlayerController>() is { } player && player.IsValid()
			? LookedAtBy( player )
			: enemy.GetComponent<BaseNpc>() is { } npc && npc.Senses.IsValid() && npc.Senses.VisibleHostiles.Contains( GameObject );
		if ( seen )
			NpcVoice.Say( this, Voice, VoiceLine.Assault, enemy );
	}

	GameObject FindPatient()
	{
		// npc_citizen17: heal a player below sk_citizen_heal_player_min_pct of their health, once every sk_citizen_heal_player_delay.
		if ( _lastHeal < _skill.Get( "sk_citizen_heal_player_delay", 25 ) || Clip( "ACT_CIT_HEAL" ) is null )
			return null;

		var threshold = _skill.Get( "sk_citizen_heal_player_min_pct", 0.6f );
		GameObject nearest = null;
		var nearestDistance = HealSchedule.MoveRange;
		foreach ( var player in Scene.GetAllComponents<PlayerController>() )
		{
			var candidate = player.GameObject;
			if ( Npcs.HealthOf( candidate ) is { Health: > 0 } health && health.Health <= health.MaxHealth * threshold )
			{
				var distance = candidate.WorldPosition.Distance( WorldPosition );
				if ( distance < nearestDistance )
				{
					nearest = candidate;
					nearestDistance = distance;
				}
			}
		}
		return nearest;
	}

	internal void BeginHeal( GameObject patient )
	{
		_lastHeal = 0;
		StartAction( Clip( "ACT_CIT_HEAL" ), e =>
		{
			if ( e is not null && e.Name != "AE_CITIZEN_HEAL" )
				return false;

			if ( Npcs.HealthOf( patient ) is { Health: > 0 } health )
			{
				health.Health = Math.Min( health.MaxHealth, health.Health + _skill.Get( "sk_citizen_heal_player", 25 ) );
				PlayMountedSound( "HealthKit.Touch", patient.WorldPosition );
			}
			return true;
		} );
	}

	/// <summary>npc_combine's grenade check: 128 to 1024 units away, standing still, a throw path that clears.</summary>
	bool CanThrowGrenade( GameObject enemy, out Vector3 toss )
	{
		toss = default;
		if ( _lastGrenadeCheck < _nextGrenadeCheck || Movement?.IsMoving == true )
			return false;

		_lastGrenadeCheck = 0;
		_nextGrenadeCheck = 1;
		var target = enemy.WorldPosition;
		var distance = WorldPosition.Distance( target );
		if ( distance is > 1024 or < 128 )
			return false;

		var eye = EyePosition;
		var time = eye.Distance( target ) / ThrowSpeed;
		toss = Toss( eye, target );
		var apex = (eye + target) * 0.5f + Vector3.Up * (0.5f * Gravity * time * time * 0.25f);
		var clear = !Scene.Trace.Ray( eye, apex ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "player", "npc" ).Run().Hit
			&& !Scene.Trace.Ray( apex, target + Vector3.Up * 8 ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "player", "npc" ).Run().Hit;
		return clear;
	}

	float Gravity => MathF.Max( 1, Scene.PhysicsWorld.Gravity.Length );

	Vector3 Toss( Vector3 from, Vector3 target )
	{
		var time = from.Distance( target ) / ThrowSpeed;
		return (target - from) / time + Vector3.Up * (Gravity * time * 0.5f);
	}

	/// <summary>Throws at the toss event, aimed where the enemy is by then, or along the arc checked at the start once it is gone.</summary>
	bool ThrowGrenade( SourceModelSequenceData.Event e, GameObject enemy, Vector3 toss )
	{
		if ( e is not null && e.Id != CombineAeGrenToss )
			return false;

		if ( enemy.IsValid() )
			toss = Toss( EyePosition, enemy.WorldPosition );
		Hl2.NpcGrenade.Spawn( this, MountContent.Of( GameObject ), Hl2.NpcGrenade.FragFuse, _skill.Get( "sk_npc_dmg_fraggrenade", 75 ), _skill.Get( "sk_fraggrenade_radius", 250 ), toss );
		Grenades--;
		return true;
	}

	/// <summary>
	/// CNPC_Combine::CanAltFireEnemy: an elite with grenades left, standing, off its squad's cooldown, shooting at a player or a player ally
	/// whose chest a combine-ball-sized hull reaches at least 65% of the way to and over 128 units out.
	/// </summary>
	bool CanAltFire( GameObject enemy, out Vector3 target )
	{
		target = default;
		if ( !Elite || Grenades < 1 || !_nextAltFire || _lastGrenadeCheck < _nextGrenadeCheck || Movement?.IsMoving == true )
			return false;

		if ( !Npcs.IsPlayer( enemy ) && enemy.GetComponent<BaseNpc>() is not { Faction: Factions.Ally or Factions.Citizen } )
			return false;

		target = Vector3.Lerp( enemy.WorldPosition, CalculateEyePosition( enemy ), 0.75f );
		var muzzle = Projectile.LaunchOrigin( Weapon );
		var tr = Scene.Trace.Box( BBox.FromPositionAndSize( Vector3.Zero, 24 ), muzzle, target ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		if ( tr.Fraction >= 0.65f && muzzle.Distance( target ) * tr.Fraction > 128 )
			return true;

		_lastGrenadeCheck = 0;
		_nextGrenadeCheck = 1;
		return false;
	}

	/// <summary>COMBINE_AE_BEGIN_ALTFIRE charges with Weapon_CombineGuard.Special1; at COMBINE_AE_ALTFIRE the weapon launches its ball at the target.</summary>
	bool AltFire( SourceModelSequenceData.Event e, BulletWeapon weapon, Vector3 target )
	{
		if ( e?.Name == "COMBINE_AE_BEGIN_ALTFIRE" )
			PlayMountedSound( "Weapon_CombineGuard.Special1", WorldPosition );

		if ( e is not null && e.Name != "COMBINE_AE_ALTFIRE" )
			return false;

		if ( !weapon.IsValid() )
			return true;

		var origin = Projectile.LaunchOrigin( weapon );
		Projectile.Launch( weapon, Projectile.Kinds.EnergyBall, origin, (target - origin).Normal * CombineBallSpeed, _skill.Get( "sk_npc_dmg_combineball", 15 ), 0 );
		PlayWeaponSound( weapon, "double_shot" );
		foreach ( var elite in Scene.GetAllComponents<CombatNpc>().Where( x => x.Elite && x.Faction == Faction ) )
			elite._nextAltFire = AltFireDelay;
		return true;
	}

	/// <summary>The player's FInViewCone of this NPC.</summary>
	bool LookedAtBy( PlayerController player )
	{
		var eye = player.EyeTransform;
		var toward = (WorldPosition - eye.Position).WithZ( 0 ).Normal;
		return toward.Dot( eye.Rotation.Forward.WithZ( 0 ).Normal ) > PlayerViewCone;
	}

	/// <summary>TASK_METROPOLICE_ACTIVATE_BATON: says so, and the stick switches at AE_METROPOLICE_BATON_ON or _OFF in the animation.</summary>
	ActionSchedule BatonSchedule( Hl2.Stunstick stick, string clip, bool on, GameObject faceTarget )
	{
		NpcVoice.Say( this, Voice, on ? VoiceLine.BatonOn : VoiceLine.BatonOff );
		return ActionSchedule( clip, faceTarget, e =>
		{
			if ( e is not null && e.Name != (on ? "AE_METROPOLICE_BATON_ON" : "AE_METROPOLICE_BATON_OFF") )
				return false;

			if ( stick.IsValid() )
				stick.SetStunState( on );
			return true;
		} );
	}

	/// <summary>CWeaponStunStick::WeaponMeleeAttack1Condition: the enemy within reach now or where it is heading.</summary>
	bool InMeleeReach( GameObject enemy )
	{
		if ( !enemy.IsValid() || MathF.Abs( enemy.WorldPosition.z - WorldPosition.z ) > MeleeHeight )
			return false;

		var ahead = enemy.WorldPosition + BaseNpc.GetVelocity( enemy ) * MeleeLeadTime;
		return enemy.WorldPosition.WithZ( 0 ).Distance( WorldPosition.WithZ( 0 ) ) <= MeleeRange || ahead.WithZ( 0 ).Distance( WorldPosition.WithZ( 0 ) ) <= MeleeRange;
	}

	/// <summary>weapon_stunstick's EVENT_WEAPON_MELEE_HIT: a hull swept 32 units from the shoot position, toward the enemy when it is ahead.</summary>
	bool MeleeHit( SourceModelSequenceData.Event e, BaseCombatWeapon weapon, GameObject enemy )
	{
		if ( e is not null && e.Id != EventWeaponMeleeHit )
			return false;

		if ( !weapon.IsValid() )
			return true;

		var start = EyePosition;
		var direction = WorldRotation.Forward;
		if ( enemy.IsValid() )
		{
			var delta = (enemy.WorldPosition + Vector3.Up * EyeHeight * 0.5f - start).Normal;
			if ( delta.WithZ( 0 ).Normal.Dot( direction ) > 0.8f )
				direction = delta;
		}

		var hull = new BBox( new Vector3( -16, -16, -40 ), new Vector3( 16, 16, 16 ) );
		var tr = Scene.Trace.Box( hull, start, start + direction * MeleeReach ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		var victim = tr.GameObject?.GetComponentInParent<Component.IDamageable>();
		if ( victim is null )
		{
			PlayWeaponSound( weapon, "melee_miss" );
			return true;
		}

		var damage = new DamageInfo
		{
			Damage = WeaponSetup.Of( weapon )?.Data?.NpcDamage ?? _skill.Get( "sk_npc_dmg_stunstick", 40 ),
			Attacker = GameObject,
			Weapon = weapon.GameObject,
			Origin = start,
			Position = tr.HitPosition
		};
		damage.Tags.Add( "melee" );
		victim.OnDamage( damage );
		PlayWeaponSound( weapon, "melee_hit" );
		StunstickImpact( tr.HitPosition, tr.Normal );
		return true;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void StunstickImpact( Vector3 position, Vector3 normal ) => WeaponEffects.StunstickImpact( position, normal, MountContent.Of( GameObject ) );

	/// <summary>npc_metropolice: the manhack appears in the hand at AE_METROPOLICE_START_DEPLOY and is let go at AE_METROPOLICE_DEPLOY_MANHACK.</summary>
	bool DeployManhack( SourceModelSequenceData.Event e, PrefabFile prefab )
	{
		if ( e?.Name == "AE_METROPOLICE_START_DEPLOY" && !_heldManhack.IsValid() )
			_heldManhack = HoldManhack( prefab );

		if ( e is not null && e.Name != "AE_METROPOLICE_DEPLOY_MANHACK" )
			return false;

		if ( !_heldManhack.IsValid() )
			_heldManhack = HoldManhack( prefab );
		ReleaseManhack();
		return true;
	}

	Transform ManhackHand => Renderer.GetAttachment( "LHand" ) ?? new Transform( WorldPosition + Vector3.Up * 50, WorldRotation );

	/// <summary>A manhack carried by this NPC does not think until it is let go.</summary>
	GameObject HoldManhack( PrefabFile prefab )
	{
		Manhacks--;
		var manhack = GameObject.GetPrefab( prefab.ResourcePath )?.Clone( ManhackHand );
		if ( manhack?.GetComponent<BaseNpc>() is { } npc )
			npc.StartCarry( GameObject );
		manhack?.NetworkSpawn();
		return manhack;
	}

	void ReleaseManhack()
	{
		if ( _heldManhack?.GetComponent<BaseNpc>() is { } npc && npc.IsValid() )
			npc.StopCarry( GameObject );
		_heldManhack = null;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayMountedSound( string name, Vector3 position )
	{
		if ( Sounds.Sound( name, MountContent.Of( GameObject ) ) is { } sound )
			Sound.Play( sound, position );
	}

	/// <summary>One of the weapon script's sounds, such as melee_hit, from the weapon.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void PlayWeaponSound( BaseCombatWeapon weapon, string role )
	{
		if ( weapon.IsValid() && Sounds.WeaponSound( weapon, role ) is { } sound )
			weapon.GameObject.PlaySound( sound );
	}

	protected override void OnDamaging( Component.IDamageable.DamageEvent damage )
	{
		base.OnDamaging( damage );
		INpcPart.PreDamage( this, damage );
	}

	protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
	{
		base.OnDamaged( damage );
		INpcPart.Hurt( this, damage );

		// A heavy hit is called out as taking cover, as COMBINE_COVER and METROPOLICE_COVER_HEAVY_DAMAGE are.
		var heavy = damage.AcceptedDamage >= Health * 0.3f;
		if ( Health >= 1 && heavy )
			CallOut( VoiceLine.Cover, damage.Instigator );
		else if ( Health >= 1 )
			NpcVoice.Say( this, Voice, VoiceLine.Pain );
		if ( Health < 1 || _lastFlinch < 2 )
			return;

		// Source layers a flinch gesture for ordinary hits and plays a full-body flinch for heavy ones.
		var gesture = heavy ? null : damage.DamageInfo.Tags.Contains( DamageTags.Headshot ) ? GestureClip( "ACT_GESTURE_FLINCH_HEAD" ) : null;
		gesture ??= heavy ? null : GestureClip( "ACT_GESTURE_SMALL_FLINCH" ) ?? GestureClip( "ACT_GESTURE_FLINCH_STOMACH" );
		if ( gesture is not null )
		{
			_lastFlinch = 0;
			Gesture = gesture;
			GestureSerial++;
			return;
		}

		var clip = damage.DamageInfo.Tags.Contains( DamageTags.Headshot ) ? Clip( "ACT_FLINCH_HEAD" ) : null;
		clip ??= Clip( heavy ? "ACT_BIG_FLINCH" : "ACT_SMALL_FLINCH" ) ?? Clip( "ACT_FLINCH_STOMACH" );
		if ( clip is null )
			return;

		_lastFlinch = 0;
		_queuedAction = clip;
		_actionTarget = damage.Instigator;
	}

	internal void HearBugbaitImpact()
	{
		if ( !_bugbaitReaction.HasValue ) _bugbaitReaction = Game.Random.Float( 0, 0.5f );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( Networking.IsHost )
		{
			UpdateVoice();
			if ( _bugbaitReaction is { } delay && delay <= 0 )
			{
				_bugbaitReaction = null;
				if ( Health > 0 && Clip( "ACT_COMBINE_BUGBAIT" ) is { } clip )
				{
					EndCurrentSchedule();
					_queuedAction = clip;
					_actionTarget = null;
				}
			}
		}

		// Ambient schedules can run indefinitely; a medic drops them once someone needs healing.
		if ( Networking.IsHost && Medic && Friendly && _lastPatientCheck > 1 && ActiveSchedule is { Priority: < SchedulePriorities.Alert } )
		{
			_lastPatientCheck = 0;
			if ( FindPatient() is not null )
				EndCurrentSchedule();
		}

		// A kick becoming possible, or an enemy coming inside the weapon's minimum range, cuts the engagement short, burst
		// included, so the NPC kicks, backs away or steps in at once.
		if ( Networking.IsHost && ActiveSchedule is CombatEngageSchedule && (Kicks || Weapon.IsValid() && Weapon.Npc.MinRange > 0) && Senses.IsValid()
			&& Senses.FindBestTarget() is { } engaged && (CanKick( engaged ) || TooClose( engaged )) )
			EndCurrentSchedule();

		// COND_METROPOLICE_CHANGE_BATON_STATE: an ambient schedule gives way to switching the baton off.
		if ( Networking.IsHost && Weapon is Hl2.Stunstick { StunActive: true } && _batonDebounce && ActiveSchedule is { Priority: < SchedulePriorities.Alert } && Clip( "ACT_DEACTIVATE_BATON" ) is not null )
			EndCurrentSchedule();
		if ( _heldManhack.IsValid() && Renderer.IsValid() )
			_heldManhack.WorldTransform = ManhackHand;
	}

	/// <summary>Says a call-out now, or once the speech around lets it within <see cref="CalloutPatience"/>.</summary>
	void CallOut( VoiceLine line, GameObject lookAt = null )
	{
		if ( !NpcVoice.Say( this, Voice, line, lookAt ) )
			_pendingCallout = (line, lookAt, CalloutPatience);
	}

	/// <summary>Calls out a new enemy, a reload under fire, and idle chatter when nothing is going on.</summary>
	void UpdateVoice()
	{
		if ( _pendingCallout is { } pending && (pending.Expires || pending.LookAt is not null && !pending.LookAt.IsValid() || NpcVoice.Say( this, Voice, pending.Line, pending.LookAt )) )
			_pendingCallout = null;

		var enemy = Senses.IsValid() ? Senses.FindBestTarget() : null;
		if ( enemy.IsValid() && !_enemy.IsValid() )
			CallOut( VoiceLine.Alert, enemy );
		_enemy = enemy;

		var reloading = Weapon?.IsReloading == true;
		if ( reloading && !_wasReloading && enemy.IsValid() )
			CallOut( VoiceLine.Reload );
		_wasReloading = reloading;

		if ( enemy.IsValid() || !_nextIdleLine )
			return;

		_nextIdleLine = Game.Random.Float( 10, 30 );
		NpcVoice.Say( this, Voice, VoiceLine.Idle );
	}

	void IDamageable.IEvents.OnDied( IDamageable.DeathEvent e )
	{
		if ( e.Victim != GameObject && e.Instigator.IsValid() && e.Instigator.Root == GameObject )
			CallOut( VoiceLine.Kill );
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		INpcPart.Death( this, death );

		NpcVoice.SayDeath( this, Voice );
		if ( Weapon is Hl2.Stunstick stick )
			stick.SetStunState( false );
		base.OnDeath( death );
	}

	protected override void OnDestroy()
	{
		ReleaseManhack();
		base.OnDestroy();
	}
}
