using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NS2;

[Title( "Marine" ), Category( "Natural Selection 2" )]
public sealed partial class Marine : Ns2Npc, Component.IDamageable
{
	const float Scale = MapLoader.Scale;
	internal const string PistolSound = "marine/pistol/fire";
	internal const string RifleSound = "marine/rifle/fire_loop_1_upgrade_0";
	[Sync( SyncFlags.FromHost )] float armor { get; set; } = 30;
	float IDamageable.Armour { get => armor; set => armor = Math.Clamp( value, 0, 30 ); }
	float IDamageable.MaxArmour => 30;
	readonly AlienWounds wounds = new();
	bool lastHitWasGas;
	bool railgunDeath;
	bool wantsFire;
	Rigidbody body;
	float fallSpeed;
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
	[Sync( SyncFlags.FromHost )] bool pistol { get; set; }
	Magazine Ammo => pistol ? pistolAmmo : rifleAmmo;
	float BulletRange => (pistol ? 45 : 41) * Scale;
	string WeaponName => pistol ? "pistol" : "rifle";
	bool? equippedPistol;
	bool previousPress, skipDraw = true;
	TimeSince weaponAge;
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
	readonly MarineWeaponAim acquisition = new( 90 );
	TimeUntil repath, nextJuke, nextDodge;
	int strafeSide = 1;
	Rotation aim;
	internal Vector3 AimDirection => aim.Forward;
	SparkAnimationGraph.Playback graph, weapon;
	Action<string> worldTag, weaponTag;
	bool leftFoot = true;
	SoundHandle rifleLoop, reloadSound;
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
		root.AddComponent<NavigationLayer>().RunSpeed = 5 * Scale;
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
		if ( Scene.PhysicsWorld.Gravity.z < 0 )
			body.GravityScale = 21.5f * Scale / -Scene.PhysicsWorld.Gravity.z;
		ignoreTarget = IsIgnoredTarget;
		lastRequest = lastShot = lastHit = 1;
		aim = WorldRotation;
		if ( Networking.IsHost )
		{
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
		weaponAge = 0;
		deployed = reloaded = previousPress = false;
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
		var enemy = AlienTargeting.ForTeam( this ).FindEnemy( this, out var memory, ignoreUntil > 0 ? ignoreTarget : null );
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
		var target = visible ? enemy.GetComponentInParent<IDamageable>() : null;
		bool structureOrBabbler = enemy.IsValid() && enemy.Tags.HasAny( "ns2_harvester", "ns2_hive", "ns2_crag", "ns2_babbler" );
		if ( !structureOrBabbler && TryMaintainWeapons( goal ) )
		{
			assignedEnemy = null;
			wantsFire = false;
			Movement.Stop();
			return;
		}
		if ( target is not null )
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
		var aimPoint = visible ? CalculateAimPoint( enemy ) : default;
		if ( visible ) viewTarget = aimPoint;
		// NS2 uses the structure attack routine for Drifters even though they can move.
		bool useEntityAttack = visible && target is not (AlienStructure or Drifter);
		float distance = visible ? EyePosition.Distance( enemy.WorldPosition ) : 0;
		if ( useEntityAttack )
		{
			var size = enemy.GetComponentInChildren<Collider>()?.LocalBounds.Size ?? Vector3.Zero;
			distance = Math.Max( 0, distance - (size * enemy.WorldScale).WithZ( 0 ).Length * 0.5f );
		}
		bool clearShot = visible && distance < (useEntityAttack ? 32.5f : 22) * Scale;
		bool babbler = visible && enemy.Tags.Has( "ns2_babbler" );
		bool aimed = clearShot && (babbler || acquisition.Track( this, enemy, aimPoint, aim, AimSkillTier, MarineWeaponAim.Weapon.Bullets, out viewTarget ));
		if ( aimed )
		{
			lastAimPoint = aimPoint;
			firingTarget = enemy;
			blindFireUntil = useEntityAttack ? 0.5f : 0;
		}
		else if ( useEntityAttack )
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
		wantsFire = !reloading && Ammo.Clip > 0 && (aimed || enemy.IsValid() && enemy == firingTarget && blindFireUntil > 0);
		if ( useEntityAttack && clearShot )
		{
			if ( babbler ) Movement.Stop();
			else Strafe( enemy, aimPoint, distance );
			return;
		}

		if ( repath > 0 ) return;
		repath = 0.2f;
		if ( goal is { } unseen && trackedEnemy == enemy && unreachable && !clearShot )
		{
			ApproachUnreachable( unseen );
			return;
		}
		if ( goal is { } p && !clearShot )
			Movement.MoveTo( p, Scale, faceTarget: visible ? enemy : null );
		else Movement.Stop();
	}

	void Strafe( GameObject enemy, Vector3 aimPoint, float distance )
	{
		if ( repath > 0 ) return;
		repath = 0.1f;
		var toward = (enemy.WorldPosition - WorldPosition).WithZ( 0 ).Normal;
		var velocity = enemy.GetComponent<BaseNpc>()?.Animation?.MoveVelocity ?? enemy.GetComponent<Rigidbody>()?.Velocity ?? Vector3.Zero;
		if ( distance > 22 * Scale && Vector3.Dot( toward, velocity.WithZ( 0 ).Normal ) < -0.5f )
		{
			Movement.Stop();
			return;
		}

		if ( nextJuke <= 0 )
		{
			strafeSide = -strafeSide;
			nextJuke = Random.Shared.Float( 0.35f, 1 );
		}
		var side = Vector3.Cross( EyePosition - aimPoint, Vector3.Up ).Normal * strafeSide;
		if ( distance < 6 * Scale && nextDodge <= 0 && IsOnGround )
		{
			nextDodge = 2;
			Launch( Animation.MoveVelocity.WithZ( MathF.Sqrt( 2 * 21.5f * 1.25f ) * Scale ) );
			jumping = true;
			PlayWorldSound( SurfaceLoader.SoundPath( "marine/common/jump" ) );
			return;
		}
		if ( IsAirborne ) return;
		var destination = WorldPosition + side * Scale;
		if ( Movement.ProjectPoint( destination ) is { } point )
			Movement.MoveTo( point, 4, 5 * Scale, enemy );
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
		if ( !IsOnGround )
		{
			fallSpeed = -body.Velocity.z;
			return;
		}

		jumping = false;
		if ( fallSpeed > 5 * Scale )
			PlayWorldSound( SurfaceLoader.SoundPath( $"materials/{SurfaceLoader.FootstepMaterial( GroundSurface )}/fall" ) );
		fallSpeed = 0;
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
		base.OnUpdate();
		if ( Health <= 0 ) return;
		if ( equippedPistol != pistol ) EquipWeapon();
		if ( Networking.IsHost )
		{
			if ( wounds.Update() is { } cue )
				PlayWorldSound( $"sounds/marine/common/{(lastHitWasGas ? "spore_wound" : cue)}.sound" );
			bool press = wantsFire && NpcConVars.Enabled && (!pistol || !previousPress);
			previousPress = press;
			trigger = press && deployed && (!pistol || lastShot >= 0.1f);
			if ( trigger && !pistol && !reloading && Ammo.Clip > 0 )
				NpcTeamMemory.Get( Scene ).ReportRifleFire( this );

			if ( (viewTarget ?? (wantsFire ? lastAimPoint : null)) is { } point )
				aim = AlienAim.Turn( aim, point - EyePosition, acquisition.TurnRate );
		}
		var activity = reloading ? "reload" : trigger ? "primary" : "none";
		var move = jumping ? "jump" : Animation.MoveVelocity.Length > 0.1f * Scale ? "run" : "idle";
		var output = PlayerGait.UpdatePose( this, 5.75f );
		if ( graph is not null )
		{
			UpdateInputs( graph, move, activity );
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
			UpdateInputs( weapon, move, activity );
			weapon.Update( weaponAge, tag: weaponTag );
		}
		if ( !pistol && trigger && !reloading && !Application.IsDedicatedServer )
		{
			if ( rifleLoop is null && GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{RifleSound}.sound" ) is { } sound )
				rifleLoop = GameObject.PlaySound( sound );
		}
		else StopRifleSound();
	}

	void UpdateInputs( SparkAnimationGraph.Playback playback, string move, string activity )
	{
		if ( playback is null ) return;
		playback.Set( "move", move );
		playback.Set( "activity", activity );
		playback.Set( "alive", 1 );
		playback.Set( "weapon", WeaponName );
		playback.Set( "catalyst_speed", 1 );
		playback.Set( "reload_speed", 1 );
		playback.Set( "skip_draw", skipDraw ? 1 : 0 );
		playback.Set( "idleName", "idle" );
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
		if ( tag == "shoot" && lastRequest < 0.1f && !reloading && Ammo.Clip > 0 && NpcConVars.Enabled )
		{
			FireBullet();
			if ( --Ammo.Clip == 0 ) BeginReload();
		}
		else if ( tag == "deploy_end" ) deployed = true;
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
		var ray = Scene.Trace.Ray( eye, eye + d * BulletRange ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).UseHitboxes();
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
			var hp = hit.GameObject?.GetComponentInParent<IDamageable>();
			var corpse = hit.GameObject?.GetComponentInParent<NpcCorpse>();
			var obj = (hp as Component)?.GameObject ?? corpse?.GameObject ?? hit.GameObject;
			bool pass = corpse.IsValid() || obj.IsValid() && (hp is { MaxHealth: > 0, Health: <= 0 } || obj.Tags.HasAny( "ragdoll", "ns2_mac", "ns2_drifter", "ns2_hydra", "ns2_hallucination" ));
			if ( obj.IsValid() && !obj.Tags.Has( "world" ) )
			{
				hitEntity = true;
				acquisition.Record( hp is not null );
			}
			if ( hp is { Health: > 0 } && GetDisposition( obj ) == Disposition.Hostile )
			{
				var info = new DamageInfo { Damage = pistol ? 20 : 10, Origin = eye, Position = hit.HitPosition, Attacker = GameObject, Weapon = GameObject };
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

	void StopRifleSound()
	{
		rifleLoop?.Stop();
		rifleLoop = null;
	}

	protected override void OnDisabled()
	{
		assignedEnemy = null;
		StopRifleSound();
		reloadSound?.Stop();
		base.OnDisabled();
	}

	protected override void OnDamaging( IDamageable.DamageEvent hit )
	{
		hit.AcceptedDamage *= AlienArmor.UmbraScale( hit );
		AlienArmor.ApplyPlayerRules( hit, this, armor );
		if ( hit.Cancelled ) return;
		AlienArmor.Absorb( hit, armor );
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
		armor -= wounds.ApplyDamage( hit, armor, MaxHealth, 30, armorOnlyDamage );
	}

	protected override void OnDeath( IDamageable.DeathEvent e )
	{
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
