using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NS2;

[Title( "Marine" ), Category( "Natural Selection 2" )]
public sealed partial class Marine : Ns2Npc, Component.IDamageable
{
	const float Scale = MapLoader.Scale;
	internal const string PistolSound = "marine/pistol/fire";
	[Property, Range( 0, 3 ), Sync( SyncFlags.FromHost )] public int WeaponsLevel { get; set; }

	internal static float UpgradedWeaponDamage( float damage, int level, GameObject target )
	{
		float bonus = target.Tags.HasAny( "structure", "ns2_mac" ) ? 0.15f : 0.1f;
		return damage * (1 + Math.Clamp( level, 0, 3 ) * bonus);
	}
	[Property, Range( 0, 3 ), Sync( SyncFlags.FromHost )] public int ArmorLevel { get; set; }
	[Sync( SyncFlags.FromHost )] float armorFraction { get; set; } = 1;
	float ArmorCapacity => 30 + 20 * Math.Clamp( ArmorLevel, 0, 3 );
	float Armor
	{
		get => armorFraction * ArmorCapacity;
		set => armorFraction = Math.Clamp( value / ArmorCapacity, 0, 1 );
	}
	float IDamageable.Armour { get => Armor; set => Armor = value; }
	float IDamageable.MaxArmour => ArmorCapacity;
	readonly AlienWounds wounds = new();
	bool lastHitWasGas;
	bool railgunDeath;
	bool wantsFire;
	Rigidbody body;
	Vector3 airVelocity, airPosition;
	float slowAmount;
	internal float SlowSpeedModifier => 1 - 0.7f * slowAmount;
	NavMeshAgent agent;
	bool sprintRequested, sprintHeld, sprintLocked, requireSprintPress;
	TimeSince sprintPressAge, sprintAge, sinceSprint, sinceAttackEnd;
	[Sync( SyncFlags.FromHost )] bool sprinting { get; set; }
	[Sync( SyncFlags.FromHost )] bool jumping { get; set; }
	[Sync( SyncFlags.FromHost )] bool trigger { get; set; }
	[Sync( SyncFlags.FromHost )] bool reloading { get; set; }
	sealed class Magazine
	{
		internal readonly int Capacity;
		internal readonly int MaxReserve;
		internal int Clip, Reserve;

		internal Magazine( int capacity )
		{
			Clip = Capacity = capacity;
			Reserve = MaxReserve = capacity * 4;
		}
	}

	readonly Magazine rifleAmmo = new( 50 ), pistolAmmo = new( 10 );
	internal bool ResupplyAmmo()
	{
		var magazine = rifleAmmo.Reserve < rifleAmmo.MaxReserve ? rifleAmmo : pistolAmmo;
		if ( magazine.Reserve >= magazine.MaxReserve ) return false;
		magazine.Reserve = Math.Min( magazine.MaxReserve, magazine.Reserve + magazine.Capacity );
		return true;
	}

	[Sync( SyncFlags.FromHost )] bool pistol { get; set; }
	// The rifle weighs 0.13 equipped and 70% of that when stowed.
	float InventorySpeed => 1.17f - 0.13f * (pistol ? 0.7f : 1);
	Magazine Ammo => pistol ? pistolAmmo : rifleAmmo;
	float BulletRange => (pistol ? 45 : 41) * Scale;
	string WeaponName => pistol ? "pistol" : "rifle";
	bool? equippedPistol;
	bool previousPress, skipDraw = true;
	TimeSince weaponAge, idleAge;
	bool deployed, reloaded;
	static readonly SparkAnimationGraph.PoseParameters Locomotion = new( 0 ), ActionLayer = new( 1 );
	[Sync( SyncFlags.FromHost )] TimeSince age { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose movePose { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose attackPose { get; set; }
	TimeSince lastShot, lastHit, lastRequest;
	TimeUntil combatUntil, blindFireUntil;
	Vector3 modelOrigin;
	Vector3? lastAimPoint;
	TimeUntil refreshCloseAim;
	protected override Vector3 GetEngagementPoint() => Renderer.WorldTransform.PointToWorld( modelOrigin );
	GameObject firingTarget;
	[Property, Range( 1, 7 )] public int AimSkillTier { get; set; } = 1;
	Vector3? viewTarget;
	readonly BotWeaponAim acquisition = new( 90 );
	TimeUntil repath, nextJuke, nextDodge;
	int strafeSide = 1;
	Vector3 dodgeDirection;
	Rotation aim;
	internal Vector3 AimDirection => aim.Forward;
	SparkAnimationGraph.Playback graph, weapon;
	Action<string> worldTag, weaponTag;
	bool leftFoot = true;
	SoundHandle reloadSound;
	ModelRenderer heldModel;

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer )
	{
		renderer.CreateAttachments = true;
		var capsule = root.AddComponent<CapsuleCollider>();
		capsule.Start = Vector3.Up * (0.35f * Scale);
		capsule.End = Vector3.Up * (1.55f * Scale);
		capsule.Radius = 0.35f * Scale;
		root.AddComponent<Rigidbody>().EnableImpactDamage = false;
		var agent = root.AddComponent<NavMeshAgent>();
		agent.Radius = capsule.Radius;
		agent.Height = 1.9f * Scale;
		agent.Enabled = false;
		root.AddComponent<MarineMovement>();
		root.AddComponent<SensesLayer>().SightRange = 45 * Scale;
		root.AddComponent<AnimationLayer>().EnableFootstepSounds = false;
		var npc = root.AddComponent<Marine>();
		npc.DisplayName = "Marine";
		npc.Faction = Factions.Ally;
		npc.MaxHealth = npc.Health = 100;
		npc.EyeHeight = 1.7f * Scale;
		npc.Renderer = renderer;
	}

	protected override void OnStart()
	{
		base.OnStart();
		body = GetComponent<Rigidbody>();
		agent = GetComponent<NavMeshAgent>( true );
		sinceSprint = sinceAttackEnd = 1;
		if ( Scene.PhysicsWorld.Gravity.z < 0 )
			body.GravityScale = 21.5f * Scale / -Scene.PhysicsWorld.Gravity.z;
		ignoreTarget = IsIgnoredTarget;
		lastRequest = lastShot = lastHit = 1;
		aim = WorldRotation;
		if ( Networking.IsHost )
		{
			rifleVariant = Random.Shared.Next( 3 );
			age = 0;
			var mount = Sandbox.Mounting.Directory.Get( "ns2" );
			if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model )
			{
				modelOrigin = model.BoundsCenter;
				graph = new( model.PropGraph, model );
			}
			worldTag = OnWorldTag;
		}
		weaponTag = OnWeaponTag;
		if ( Renderer.GetAttachmentObject( "RHand_Weapon" ) is { } hand )
		{
			var obj = new GameObject( hand, true, "Weapon" ) { NetworkMode = NetworkMode.Never };
			heldModel = obj.AddComponent<ModelRenderer>();
			heldModel.CreateAttachments = true;
		}
		EquipWeapon();
	}

	void EquipWeapon()
	{
		if ( Networking.IsHost && reloading ) CancelReloadSound();
		skipDraw = equippedPistol is null;
		equippedPistol = pistol;
		weaponAge = idleAge = 0;
		deployed = skipDraw && !pistol;
		reloaded = previousPress = false;
		lastRequest = lastShot = 1;
		var directory = $"mount://ns2/ns2/models/marine/{WeaponName}/{WeaponName}";
		if ( heldModel.IsValid() ) heldModel.Model = Model.Load( $"{directory}.model.vmdl" );
		if ( Networking.IsHost )
		{
			var view = Sandbox.Mounting.Directory.Get( "ns2" )?.GetByPath( $"{directory}_view.model.vmdl" ) as ModelLoader;
			weapon = view?.PropGraph is { } g ? new( g, view ) : null;
			reloading = false;
		}
	}

	protected override void Think()
	{
		viewTarget = null;
		var targeting = AlienTargeting.ForTeam( this );
		var enemy = targeting.FindEnemy( this, out var memory, ignoreUntil > 0 ? ignoreTarget : null );
		assignedEnemy = enemy;
		var goal = memory ?? enemy?.WorldPosition;
		if ( enemy.IsValid() && TrackReachability( enemy, goal.Value ) && sinceProgress > 5 )
		{
			ignoredEnemy = enemy;
			ignoredPosition = memory ?? enemy.WorldPosition;
			ignoreUntil = 15;
			trackedEnemy = null;
			assignedEnemy = null;
			wantsFire = false;
			Animation.SetLookTarget( null );
			Movement.Stop();
			return;
		}
		bool visible = enemy.IsValid() && !memory.HasValue;
		Animation.SetLookTarget( visible ? enemy : null );
		var target = enemy.IsValid() ? enemy.GetComponentInParent<IDamageable>() : null;
		bool structureOrBabbler = enemy.IsValid() && enemy.Tags.HasAny( "ns2_harvester", "ns2_hive", "ns2_crag", "ns2_babbler" );
		if ( !structureOrBabbler && TryMaintainWeapons( goal ) )
		{
			assignedEnemy = null;
			wantsFire = false;
			Movement.Stop();
			return;
		}
		if ( visible && target is not null )
		{
			bool usePistol = !structureOrBabbler && rifleAmmo.Clip == 0 && pistolAmmo.Reserve > 0
				&& (rifleAmmo.Reserve == 0 || target.Health <= target.MaxHealth * 0.7f);
			SelectWeapon( usePistol );
		}
		else if ( !enemy.IsValid() && !reloading && rifleAmmo.Clip > 0 )
		{
			SelectWeapon( false );
		}
		if ( Ammo.Clip == 0 ) BeginReload();
		if ( target is AlienStructure or Drifter )
		{
			AttackStructure( enemy, memory );
			return;
		}

		var aimPoint = visible ? CalculateAimPoint( enemy ) : default;
		if ( visible ) viewTarget = aimPoint;
		float distance = visible ? targeting.DistanceToTouch( this, enemy ) : 0;
		bool clearShot = visible && distance < 32.5f * Scale;
		bool babbler = visible && enemy.Tags.Has( "ns2_babbler" );
		bool aimed = clearShot && (babbler || acquisition.Track( this, enemy, aimPoint, aim, AimSkillTier, BotWeaponAim.Weapon.Bullets, out viewTarget ));
		if ( aimed )
		{
			lastAimPoint = aimPoint;
			firingTarget = enemy;
			blindFireUntil = 0.5f;
		}
		else if ( visible )
		{
			if ( blindFireUntil > 0 ) viewTarget = lastAimPoint;
			else if ( distance < 15 * Scale )
			{
				if ( refreshCloseAim <= 0 )
				{
					lastAimPoint = aimPoint;
					refreshCloseAim = 0.5f;
				}
				if ( lastAimPoint.HasValue ) viewTarget = lastAimPoint;
			}
			else lastAimPoint = null;
		}
		wantsFire = aimed || enemy.IsValid() && enemy == firingTarget && blindFireUntil > 0;
		if ( enemy.IsValid() && RetreatUnderFire( enemy, visible, visible ? aimPoint : goal.Value + Vector3.Up * (0.1f * Scale), !aimed && wantsFire ) )
			return;
		if ( clearShot )
		{
			if ( babbler ) Movement.Stop();
			else Strafe( enemy, aimPoint, distance );
			return;
		}

		if ( !enemy.IsValid() && VisitArmory() ) return;
		if ( repath > 0 ) return;
		repath = 0.2f;
		if ( goal is { } unseen && trackedEnemy == enemy && unreachable && !clearShot )
		{
			ApproachUnreachable( unseen );
			return;
		}
		if ( goal is { } p && !clearShot )
			MoveAlongRoute( p, visible ? enemy : null );
		else Movement.Stop();
	}

	void MoveAlongRoute( Vector3 point, GameObject faceTarget = null )
	{
		sprintRequested = WalkDistance( point ) > 10 * Scale;
		Movement.MoveTo( point, Scale, faceTarget: faceTarget );
	}

	void UpdateSprint( bool pressed, bool grounded, Vector3 velocity, Vector3 wish )
	{
		if ( !reloading && pressed != sprintHeld )
		{
			if ( pressed )
			{
				sprintPressAge = 0;
				requireSprintPress = false;
			}
			else if ( sprintPressAge < 0.2f ) sprintLocked = !sprintLocked;
			sprintHeld = pressed;
		}

		bool moving = velocity.LengthSquared > Scale * Scale;
		var direction = wish.WithZ( 0 ).Normal;
		bool next = (pressed || sprintLocked) && !requireSprintPress && grounded && moving && !reloading && !wantsFire && sinceAttackEnd > 0.2f
			&& Vector3.Dot( direction, aim.Forward.WithZ( 0 ).Normal ) >= 0.2f && Vector3.Dot( velocity.WithZ( 0 ).Normal, direction ) >= 0.3f;
		if ( sprintLocked && (wantsFire || !moving || !grounded) )
		{
			sprintLocked = false;
			requireSprintPress = wantsFire;
		}
		if ( sprinting != next )
		{
			if ( next || sprintAge > 5 )
				PlayWorldSound( next ? "sounds/marine/common/sprint_start.sound" : "sounds/marine/common/sprint_tired.sound" );
			sprinting = next;
			sprintAge = 0;
		}
		if ( sprinting ) sinceSprint = 0;
		float speed = 5 + (sprinting ? 0.75f * Math.Clamp( (float)sprintAge / 1.5f, 0, 1 ) : 0);
		Movement.WalkSpeed = Movement.RunSpeed = speed * InventorySpeed * SlowSpeedModifier * Scale;
	}

	void ApproachPoint( Vector3 point )
	{
		// While moving the ground bot looks along its path at eye height.
		var velocity = Animation.MoveVelocity.WithZ( 0 );
		viewTarget = velocity.LengthSquared > 1 ? EyePosition + velocity : point.WithZ( EyePosition.z );
		if ( repath > 0 ) return;
		repath = 0.2f;
		MoveAlongRoute( point );
	}

	void AttackStructure( GameObject enemy, Vector3? lastSeen )
	{
		blindFireUntil = 0;
		var point = lastSeen.HasValue ? lastSeen.Value + Vector3.Up * (0.1f * Scale) : CalculateAimPoint( enemy );
		// The rifle and pistol share a 22 m engagement range, measured to the structure origin.
		if ( lastSeen.HasValue || EyePosition.DistanceSquared( enemy.WorldPosition ) >= 22 * 22 * Scale * Scale )
		{
			wantsFire = false;
			ApproachPoint( point );
			return;
		}

		if ( Movement.MoveTarget.HasValue ) Movement.Stop();
		wantsFire = acquisition.Track( this, enemy, point, aim, AimSkillTier, BotWeaponAim.Weapon.Bullets, out viewTarget );
		if ( wantsFire ) lastAimPoint = point;
	}

	void Strafe( GameObject enemy, Vector3 aimPoint, float distance )
	{
		sprintRequested = false;
		if ( distance > 22 * Scale )
		{
			var toward = (enemy.WorldPosition - WorldPosition).WithZ( 0 ).Normal;
			var velocity = enemy.GetComponent<BaseNpc>()?.Animation?.MoveVelocity ?? enemy.GetComponent<Rigidbody>()?.Velocity ?? Vector3.Zero;
			if ( Vector3.Dot( toward, velocity.WithZ( 0 ).Normal ) < -0.5f )
			{
				if ( Movement.MoveTarget.HasValue ) Movement.Stop();
				return;
			}
		}

		if ( nextJuke <= 0 )
		{
			strafeSide = -strafeSide;
			nextJuke = Random.Shared.Float( 0.35f, 1 );
		}
		var side = Vector3.Cross( EyePosition - aimPoint, Vector3.Up ).Normal * strafeSide;
		if ( repath > 0 ) return;
		repath = 0.1f;
		if ( distance < 6 * Scale && nextDodge <= 0 && IsOnGround )
		{
			nextDodge = 2;
			dodgeDirection = side;
			Launch( Animation.MoveVelocity.WithZ( 8.22f * Scale ) );
			jumping = true;
			PlayWorldSound( SurfaceLoader.SoundPath( "marine/common/jump" ) );
			return;
		}
		if ( IsAirborne ) return;
		var destination = WorldPosition + side * Scale;
		if ( Movement.ProjectPoint( destination ) is { } point )
			Movement.MoveTo( point, 4, Movement.RunSpeed, enemy );
		else Movement.Stop();
	}

	void SelectWeapon( bool usePistol )
	{
		if ( pistol == usePistol ) return;
		pistol = usePistol;
		EquipWeapon();
	}

	void BeginReload()
	{
		if ( !deployed || reloading || Ammo.Clip == Ammo.Capacity || Ammo.Reserve <= 0 ) return;
		reloading = true;
		PlayReloadSound( pistol );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayReloadSound( bool isPistol )
	{
		var weaponName = isPistol ? "pistol" : "rifle";
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/marine/{weaponName}/reload.sound" ) is { } sound )
			reloadSound = GameObject.PlaySound( sound );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void CancelReloadSound() => reloadSound?.Stop();

	bool IsInResponseRange( Vector3 position )
	{
		const float range = 35 * Scale;
		if ( WorldPosition.DistanceSquared( position ) > range * range ) return false;
		return WalkDistance( position ) <= range;
	}

	internal float WalkDistance( Vector3 position )
	{
		if ( !locations.IsValid() ) locations = Scene.GetAllComponents<MapLocations>().FirstOrDefault();
		return locations.IsValid() ? locations.WalkDistance( WorldPosition, position ) : WorldPosition.Distance( position );
	}

	bool TryMaintainWeapons( Vector3? threat )
	{
		if ( reloading && !threat.HasValue ) return true;
		bool inCombat = combatUntil > 0;
		bool reloadRifle = rifleAmmo.Reserve > 0 && rifleAmmo.Clip < rifleAmmo.Capacity
			&& ((!threat.HasValue && !inCombat) || rifleAmmo.Clip < rifleAmmo.Capacity * 0.4f);
		// With a full rifle outside combat, the original bot tops off its pistol even with a threat nearby.
		bool reloadPistol = pistolAmmo.Reserve > 0 && pistolAmmo.Clip < pistolAmmo.Capacity
			&& (inCombat ? rifleAmmo.Reserve == 0 : rifleAmmo.Clip == rifleAmmo.Capacity);
		if ( !reloadRifle && !reloadPistol ) return false;
		if ( threat is { } position && (reloadRifle || inCombat) && IsInResponseRange( position ) ) return false;

		SelectWeapon( !reloadRifle );
		BeginReload();
		return true;
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( !Networking.IsHost || Health <= 0 ) return;
		if ( !IsOnGround ) airVelocity = body.Velocity;
		if ( jumping && !PhysicsCarrier.IsValid() && NpcConVars.Enabled )
		{
			// The ground probe returns control to navigation before the last air step.
			var velocity = airVelocity / Scale;
			velocity.z += Scene.PhysicsWorld.Gravity.z * body.GravityScale * Time.Delta / Scale;
			airVelocity = DodgeVelocity( velocity, aim, dodgeDirection, Time.Delta ) * Scale;
			if ( !IsOnGround )
			{
				body.Gravity = false;
				body.Velocity = airVelocity;
				return;
			}

			if ( agent.Enabled ) FinishDodgeStep();
		}
		else if ( !IsOnGround )
		{
			EndDodge();
			return;
		}

		EndDodge();
		float fallSpeed = -airVelocity.z;
		if ( fallSpeed > 4 * Scale ) Land( fallSpeed );
		airVelocity = Vector3.Zero;
		slowAmount = Math.Max( 0, slowAmount - Scene.FixedDelta );
	}

	void FinishDodgeStep()
	{
		var planar = airVelocity.WithZ( 0 );
		var end = airPosition + planar * Scene.FixedDelta;
		if ( planar.LengthSquared > 0 )
		{
			var trace = Scene.Trace.Body( body.PhysicsBody, new Transform( airPosition, WorldRotation ), end )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).WithCollisionRules( GameObject.Tags ).Run();
			end = airPosition.LerpTo( end, trace.Fraction );
			if ( trace.Hit ) planar -= trace.Normal * Math.Min( 0, planar.Dot( trace.Normal ) );
		}
		agent.SetAgentPosition( end.WithZ( agent.AgentPosition.z ) );
		agent.Velocity = planar.WithZ( 0 );
	}

	void EndDodge()
	{
		if ( !jumping ) return;
		jumping = false;
		if ( body.IsValid() ) body.Gravity = true;
	}

	Vector3 DodgeVelocity( Vector3 velocity, Rotation view, Vector3 direction, float dt )
	{
		float speed = MarineMovement.DirectionalSpeed( view, direction, 5 * InventorySpeed * SlowSpeedModifier );
		return SparkAirMovement.Steer( velocity, view, direction, speed, 11 * SlowSpeedModifier, 6 * SlowSpeedModifier, dt );
	}

	void Land( float impactSpeed )
	{
		float loss = Math.Clamp( (impactSpeed / Scale - 4) / 18, 0, 1 );
		slowAmount = Math.Min( 1, slowAmount + loss );
		if ( agent.Enabled ) agent.Velocity *= 1 - loss;
		if ( impactSpeed > 5 * Scale )
			PlayWorldSound( SurfaceLoader.SoundPath( $"materials/{SurfaceLoader.FootstepMaterial( GroundSurface )}/fall" ) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayWorldSound( string path )
	{
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/{path}" ) is { } sound )
			Sound.Play( sound, WorldPosition );
	}

	protected override void OnUpdate()
	{
		if ( GameObject.IsDestroyed ) return;
		if ( Networking.IsHost && jumping && IsAirborne && body.IsValid() )
		{
			// Categorizing the ground disables the body and starts navigation at the displayed transform.
			airPosition = body.PhysicsBody.Position;
			airVelocity = body.Velocity;
		}
		base.OnUpdate();
		if ( Health <= 0 ) return;
		if ( equippedPistol != pistol ) EquipWeapon();
		if ( Networking.IsHost )
		{
			UpdateSprint( sprintRequested && Movement.MoveTarget.HasValue, IsOnGround, Animation.MoveVelocity, agent.WishVelocity );
			if ( wounds.Update() is { } cue )
				PlayWorldSound( $"sounds/marine/common/{(lastHitWasGas ? "spore_wound" : cue)}.sound" );
			bool press = wantsFire && NpcConVars.Enabled && (!pistol || !previousPress);
			previousPress = press;
			bool attack = press && sinceSprint >= 0.2f && deployed && !reloading && (!pistol || lastShot >= 0.1f);
			bool wasAttacking = trigger;
			trigger = attack && Ammo.Clip > 0;
			if ( wasAttacking && !trigger ) idleAge = sinceAttackEnd = 0;
			if ( attack && Ammo.Clip == 0 )
				BeginReload();
			if ( trigger && !pistol )
				NpcTeamMemory.Get( Scene ).ReportRifleFire( this );

			if ( (viewTarget ?? (wantsFire ? lastAimPoint : null)) is { } point )
				aim = AlienAim.Turn( aim, point - EyePosition, acquisition.TurnRate );
		}
		var activity = reloading ? "reload" : trigger ? "primary" : "none";
		bool idle = IsMovementIdle;
		var move = jumping ? "jump" : sprinting ? "sprint" : idle ? "idle" : "run";
		var output = PlayerGait.UpdatePose( this, 5.75f );
		if ( graph is not null )
		{
			UpdateInputs( graph, move, activity, idle );
			graph.Update( age, output, worldTag );
			movePose = graph.GetPose( 0 );
			attackPose = graph.GetPose( 1 );
		}
		else if ( !Networking.IsHost && output is not null )
		{
			movePose.Write( Locomotion, output, age );
			attackPose.Write( ActionLayer, output, age );
		}
		if ( weapon is not null )
		{
			if ( trigger ) lastRequest = 0;
			UpdateInputs( weapon, move, activity, idle );
			weapon.Update( weaponAge, tag: weaponTag );
		}
		if ( !Application.IsDedicatedServer ) UpdateRifleSound();
	}

	bool IsMovementIdle => Animation.MoveVelocity.LengthSquared < (0.1f * Scale) * (0.1f * Scale)
		&& Movement.Status != MovementLayer.MovementStatus.Moving;

	void UpdateInputs( SparkAnimationGraph.Playback playback, string move, string activity, bool idle )
	{
		if ( playback is null ) return;
		playback.Set( "move", move );
		playback.Set( "activity", activity );
		playback.Set( "alive", 1 );
		playback.Set( "weapon", WeaponName );
		playback.Set( "catalyst_speed", 1 );
		playback.Set( "reload_speed", 1 );
		playback.Set( "skip_draw", skipDraw ? 1 : 0 );
		playback.Set( "empty", Ammo.Clip == 0 && Ammo.Reserve == 0 ? 1 : 0 );
		if ( !idle )
			idleAge = 0;
		if ( idleAge >= 29.5f )
			idleAge = 0;
		else
			playback.Set( "idleName", (float)idleAge switch
			{
				>= 19.5f => pistol ? "idle_gangster" : "idle_lower",
				>= 9.5f => pistol ? "idle_spin" : "idle_brush",
				_ => "idle"
			} );
	}

	void OnWorldTag( string tag )
	{
		if ( tag is not ("step" or "step_run" or "step_sprint") || !PlayerGait.CanPlayStep( this ) ) return;
		leftFoot = !leftFoot;
		bool forward = Vector3.Dot( Animation.MoveVelocity, WorldRotation.Forward ) > -0.1f * Scale;
		PlayFootstep( SurfaceLoader.FootstepMaterial( GroundSurface ), forward, leftFoot );
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	void PlayFootstep( string material, bool forward, bool left )
	{
		if ( Application.IsHeadless ) return;
		bool enemy = forward && PlayerGait.IsEnemyListener( this );
		var cue = $"{(forward ? "footstep" : "backpedal")}_{(left ? "left" : "right")}{(enemy ? "_for_enemy" : "")}";
		var path = SurfaceLoader.SoundPath( $"materials/{material}/{cue}" );
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/{path}" ) is { } sound ) Sound.Play( sound, WorldPosition );
	}

	void OnWeaponTag( string tag )
	{
		if ( tag is "idle_spin_start" or "idle_gangster_start" )
		{
			PlayWorldSound( tag == "idle_spin_start" ? "sounds/marine/pistol/idle_spin.sound" : "sounds/marine/pistol/idle_gangster.sound" );
			return;
		}
		if ( tag == "shoot" && lastRequest < 0.1f && !reloading && Ammo.Clip > 0 && NpcConVars.Enabled )
		{
			FireBullet();
			if ( --Ammo.Clip == 0 ) BeginReload();
		}
		else if ( tag == "deploy_end" )
		{
			deployed = true;
			skipDraw = false;
		}
		else if ( tag == "reload" )
		{
			int rounds = Math.Min( Ammo.Capacity - Ammo.Clip, Ammo.Reserve );
			Ammo.Clip += rounds;
			Ammo.Reserve -= rounds;
			reloaded = true;
		}
		else if ( tag == "reload_end" && reloaded )
		{
			reloaded = false;
			reloading = false;
		}
	}

	void FireBullet()
	{
		float theta = Random.Shared.NextSingle() * MathF.Tau;
		float radius = Random.Shared.NextSingle() * MathF.Tan( (pistol ? 0.2f : 1.4f).DegreeToRadian() );
		var d = (aim.Forward + (aim.Left * MathF.Cos( theta ) + aim.Up * MathF.Sin( theta )) * radius).Normal;
		var eye = EyePosition;
		var ray = Scene.Trace.Ray( eye, eye + d * BulletRange ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).UseDamageHitboxes();
		bool box = lastShot >= 0.2f || lastHit < 0.5f;
		lastShot = 0;
		bool hitEntity = false;
		for ( int i = 0; i < 20; i++ )
		{
			var hit = ray.Run();
			if ( box && (!hit.Hit || hit.GameObject?.Tags.Has( "world" ) == true) )
			{
				var extents = new Vector3( d.WithX( 0 ).Length, d.WithY( 0 ).Length, d.WithZ( 0 ).Length ) * (0.018f * Scale);
				hit = ray.FromTo( eye, hit.EndPosition ).Size( -extents, extents ).Run();
			}
			var obj = hit.GetBulletTarget( out var hp, out bool pass );
			if ( obj.IsValid() && !obj.Tags.Has( "world" ) )
			{
				hitEntity = true;
				acquisition.Record( hp is not null );
			}
			if ( hp is { Health: > 0 } && GetDisposition( obj ) == Disposition.Hostile )
			{
				var info = new DamageInfo { Damage = UpgradedWeaponDamage( pistol ? 20 : 10, WeaponsLevel, obj ), Origin = eye, Position = hit.HitPosition, Attacker = GameObject, Weapon = GameObject };
				info.Tags.Add( "bullet" );
				info.Tags.Add( AlienWounds.ExtraFlinch );
				hp.OnDamage( info );
				combatUntil = 3;
				lastHit = 0;
			}
			if ( !pass ) break;
			ray = ray.IgnoreGameObjectHierarchy( obj );
		}
		if ( !hitEntity ) acquisition.Record( false );
		if ( pistol && Networking.IsHost ) NpcTeamMemory.Get( Scene ).ReportPistolShot( this );
		ShowMuzzle( pistol );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowMuzzle( bool isPistol )
	{
		if ( isPistol && GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{PistolSound}.sound" ) is { } sound )
			GameObject.PlaySound( sound );
		if ( heldModel?.GetAttachmentObject( isPistol ? "fxnode_pistolmuzzle" : "fxnode_riflemuzzle" ) is { } muzzle )
			CinematicLoader.Play( isPistol ? "cinematics/marine/pistol/muzzle_flash.cinematic" : "cinematics/marine/rifle/muzzle_flash.cinematic", muzzle.WorldPosition, muzzle.WorldRotation );
	}

	protected override void OnDisabled()
	{
		if ( Networking.IsHost ) EndDodge();
		assignedEnemy = null;
		StopRifleSound();
		reloadSound?.Stop();
		base.OnDisabled();
	}

	protected override void OnDamaging( IDamageable.DamageEvent hit )
	{
		hit.AcceptedDamage *= AlienArmor.UmbraScale( hit );
		AlienArmor.ApplyPlayerRules( hit, this, Armor );
		if ( hit.Cancelled ) return;
		AlienArmor.Absorb( hit, Armor );
	}

	protected override void OnDamaged( IDamageable.DamageEvent hit )
	{
		float armorOnlyDamage = AlienArmor.ArmorOnlyDamage( hit );
		if ( hit.AcceptedDamage <= 0 && armorOnlyDamage <= 0 ) return;
		combatUntil = 3;
		if ( hit.Instigator.IsValid() )
		{
			lastAttacker = hit.Instigator;
			attackerExpires = 3;
		}
		lastHitWasGas = hit.DamageInfo.Tags.Has( "gas" );
		Armor -= wounds.ApplyDamage( hit, Armor, MaxHealth, ArmorCapacity, armorOnlyDamage );
	}

	protected override void OnDeath( IDamageable.DeathEvent e )
	{
		EndDodge();
		railgunDeath = e.DamageInfo.Tags.Has( "railgun" );
		PlayWorldSound( "sounds/marine/common/death.sound" );
		base.OnDeath( e );
	}

	protected override GameObject CreateRagdoll()
	{
		if ( graph is null ) return railgunDeath ? null : NpcCorpse.Attach( base.CreateRagdoll(), 3f );
		return NpcCorpse.AnimatePlayer( Renderer, graph, age, railgunDeath );
	}
}
