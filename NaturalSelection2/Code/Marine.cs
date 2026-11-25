using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NS2;

[Title( "Marine" ), Category( "Natural Selection 2" )]
public sealed class Marine : BaseNpc, Component.IDamageable
{
	const float Scale = MapLoader.Scale;
	internal const string PistolSound = "marine/pistol/fire";
	internal const string RifleSound = "marine/rifle/fire_loop_1_upgrade_0";
	[Sync( SyncFlags.FromHost )] float armor { get; set; } = 30;
	float IDamageable.Armour { get => armor; set => armor = Math.Clamp( value, 0, 30 ); }
	float IDamageable.MaxArmour => 30;
	bool wantsFire;
	[Sync( SyncFlags.FromHost )] bool trigger { get; set; }
	[Sync( SyncFlags.FromHost )] bool reloading { get; set; }
	sealed class Magazine
	{
		internal readonly int Capacity;
		internal int Clip, Reserve;

		internal Magazine( int capacity )
		{
			Clip = Capacity = capacity;
			Reserve = capacity * 4;
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
	TimeSince age, lastShot, lastHit, lastRequest;
	TimeUntil combatUntil, blindFireUntil;
	Vector3 lastAimPoint;
	GameObject firingTarget;
	readonly AlienAim.Acquisition acquisition = new( 90 );
	TimeUntil repath, nextJuke, nextDodge;
	int strafeSide = 1;
	Rotation aim;
	readonly AlienTargeting targeting = new();
	SparkAnimationGraph.Playback graph, weapon;
	Action<string> weaponTag;
	SoundHandle rifleLoop;
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
		if ( Scene.PhysicsWorld.Gravity.z < 0 )
			GetComponent<Rigidbody>().GravityScale = 21.5f * Scale / -Scene.PhysicsWorld.Gravity.z;
		age = 0;
		lastRequest = lastShot = lastHit = 1;
		aim = WorldRotation;
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader body )
			graph = new( body.PropGraph, body );
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
		var enemy = targeting.FindEnemy( this, out var memory );
		var goal = memory ?? enemy?.WorldPosition;
		bool visible = enemy.IsValid() && !memory.HasValue;
		Animation.SetLookTarget( visible ? enemy : null );
		if ( visible && enemy.GetComponentInParent<IDamageable>() is { } target )
		{
			bool usePistol;
			if ( target is Babbler )
			{
				usePistol = false;
			}
			else if ( target is AlienStructure )
			{
				float durability = target.Health + 2 * target.Armour;
				float maxDurability = target.MaxHealth + 2 * target.MaxArmour;
				usePistol = rifleAmmo.Reserve == 0 && (pistol || durability <= 0.15f * maxDurability && pistolAmmo.Reserve > 0);
			}
			else
			{
				usePistol = rifleAmmo.Clip == 0 && pistolAmmo.Reserve > 0
					&& (rifleAmmo.Reserve == 0 || target.Health <= target.MaxHealth * 0.7f);
			}
			SelectWeapon( usePistol );
		}
		else if ( !enemy.IsValid() )
		{
			MaintainWeapons();
		}
		if ( deployed && Ammo.Clip == 0 && Ammo.Reserve > 0 ) reloading = true;
		var aimPoint = visible ? CalculateAimPoint( enemy ) : lastAimPoint;
		float distance = 0;
		if ( visible )
		{
			var size = enemy.GetComponentInChildren<Collider>()?.LocalBounds.Size ?? Vector3.Zero;
			distance = Math.Max( 0, EyePosition.Distance( enemy.WorldPosition ) - (size * enemy.WorldScale).WithZ( 0 ).Length * 0.5f );
		}
		bool mobile = visible && enemy.GetComponentInParent<AlienStructure>() is null;
		bool clearShot = visible && (mobile ? distance < 32.5f * Scale : distance <= BulletRange);
		bool babbler = visible && enemy.Tags.Has( "ns2_babbler" );
		bool aimed = clearShot && (babbler || acquisition.Ready( enemy, aimPoint - EyePosition, aim ));
		if ( aimed )
		{
			lastAimPoint = aimPoint;
			firingTarget = enemy;
			blindFireUntil = mobile ? 0.5f : 0;
		}
		wantsFire = !reloading && Ammo.Clip > 0 && (aimed || enemy.IsValid() && enemy == firingTarget && blindFireUntil > 0);
		if ( mobile && clearShot )
		{
			if ( babbler ) Movement.Stop();
			else Strafe( enemy, aimPoint, distance );
			return;
		}

		if ( repath > 0 ) return;
		repath = 0.2f;
		if ( goal is { } p && (!visible || distance > 22 * Scale) )
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

	void MaintainWeapons()
	{
		if ( reloading ) return;
		bool inCombat = combatUntil > 0;
		bool reloadRifle = rifleAmmo.Reserve > 0 && rifleAmmo.Clip < rifleAmmo.Capacity
			&& (!inCombat || rifleAmmo.Clip < rifleAmmo.Capacity * 0.4f);
		bool reloadPistol = pistolAmmo.Reserve > 0 && pistolAmmo.Clip < pistolAmmo.Capacity
			&& (inCombat ? rifleAmmo.Reserve == 0 : rifleAmmo.Clip == rifleAmmo.Capacity);

		if ( reloadRifle || reloadPistol )
		{
			SelectWeapon( !reloadRifle );
			reloading = deployed;
		}
		else if ( rifleAmmo.Clip > 0 )
		{
			SelectWeapon( false );
		}
	}

	protected override void OnUpdate()
	{
		if ( GameObject.IsDestroyed ) return;
		base.OnUpdate();
		if ( Health <= 0 ) return;
		if ( equippedPistol != pistol ) EquipWeapon();
		if ( Networking.IsHost )
		{
			bool press = wantsFire && NpcConVars.Enabled && (!pistol || !previousPress);
			previousPress = press;
			trigger = press && deployed && (!pistol || lastShot >= 0.1f);

			if ( Animation.LookObject.IsValid() )
				aim = AlienAim.Turn( aim, CalculateAimPoint( Animation.LookObject ) - EyePosition, 5 );
			else if ( wantsFire )
				aim = AlienAim.Turn( aim, lastAimPoint - EyePosition, 5 );
		}
		var activity = reloading ? "reload" : trigger ? "primary" : "none";
		var move = Animation.MoveVelocity.LengthSquared > 1 ? "run" : "idle";
		UpdateInputs( graph, move, activity );
		graph?.Update( age, Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null );
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

	void OnWeaponTag( string tag )
	{
		if ( tag == "shoot" && lastRequest < 0.1f && !reloading && Ammo.Clip > 0 && NpcConVars.Enabled )
		{
			FireBullet();
			if ( --Ammo.Clip == 0 && deployed && Ammo.Reserve > 0 ) reloading = true;
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
		for ( int i = 0; i < 20; i++ )
		{
			var hit = ray.Run();
			if ( box && (!hit.Hit || hit.GameObject?.Tags.Has( "world" ) == true) )
			{
				var extents = new Vector3( d.WithX( 0 ).Length, d.WithY( 0 ).Length, d.WithZ( 0 ).Length ) * (0.018f * Scale);
				hit = ray.FromTo( eye, hit.EndPosition ).Size( -extents, extents ).Run();
			}
			var hp = hit.GameObject?.GetComponentInParent<IDamageable>();
			var corpse = hit.GameObject?.GetComponentInParent<AlienCorpse>();
			var obj = (hp as Component)?.GameObject ?? corpse?.GameObject ?? hit.GameObject;
			bool pass = corpse.IsValid() || obj.IsValid() && (hp is { MaxHealth: > 0, Health: <= 0 } || obj.Tags.HasAny( "ragdoll", "ns2_mac", "ns2_drifter", "ns2_hydra", "ns2_hallucination" ));
			if ( hp is { Health: > 0 } && GetDisposition( obj ) == Disposition.Hostile )
			{
				var info = new DamageInfo { Damage = pistol ? 20 : 10, Origin = eye, Position = hit.HitPosition, Attacker = GameObject, Weapon = GameObject };
				info.Tags.Add( "bullet" );
				hp.OnDamage( info );
				combatUntil = 3;
				lastHit = 0;
			}
			if ( !pass ) break;
			ray = ray.IgnoreGameObjectHierarchy( obj );
		}
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
		StopRifleSound();
		base.OnDisabled();
	}

	protected override void OnDamaging( IDamageable.DamageEvent hit ) => AlienArmor.Absorb( hit, armor );

	protected override void OnDamaged( IDamageable.DamageEvent hit )
	{
		if ( hit.AcceptedDamage > 0 ) combatUntil = 3;
		float ratio = AlienArmor.HealthPerArmor( hit.DamageInfo );
		if ( ratio > 0 && hit.AcceptedDamage > 0 ) armor = Math.Max( 0, armor - hit.AcceptedDamage * (0.7f / 0.3f) / ratio );
	}
}
