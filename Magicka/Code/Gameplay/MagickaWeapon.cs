using System;

namespace Magicka;

/// <summary>
/// Base class for spawnable Magicka player weapons. Uses the Wizard character model with
/// non-arm bones collapsed as the first-person viewmodel, and attaches the item model to
/// the holder's hand in third-person.
/// </summary>
[Hide]
public abstract class MagickaWeapon : BaseCombatWeapon
{
	internal const string WizardViewModelPath = "mount://magicka/models/characters/wizard/avatar_wizard1_mesh_0.vmdl";

	[Property] public string WeaponModelPath { get; set; }
	[Property] public string AttachBone { get; set; } = "LeftAttach";
	[Property] public string IdleSequence { get; set; } = "idle";
	[Property] public string DeploySequence { get; set; } = "pickup_staff";
	[Property] public Vector3 ViewOffset { get; set; } = new( 7f, 2f, -36f );
	[Property] public Angles ViewAngles { get; set; } = Angles.Zero;
	[Property] public Vector3 HeldLocalOffset { get; set; } = Vector3.Zero;
	[Property] public Angles HeldLocalAngles { get; set; } = Angles.Zero;
	[Property] public float HeldScale { get; set; } = 1f;
	[Property] public Vector3 WorldModelOffset { get; set; } = Vector3.Zero;
	[Property] public Angles WorldModelAngles { get; set; } = Angles.Zero;

	private static readonly Dictionary<string, Texture> IconCache = new( StringComparer.OrdinalIgnoreCase );

	private SoundHandle _channelSound;
	private string _channelCue;
	private TimeUntil _channelSoundHold;

	protected MagickaWeaponView ViewController => ViewModel.IsValid() ? ViewModel.GetComponent<MagickaWeaponView>() : null;

	internal virtual void Configure()
	{
		UsesAmmo = false;
		UsesClips = false;
		HoldType = WeaponHolds.Melee;
	}

	internal void SetupPickup( string title, string modelPath )
	{
		DisplayName = title;
		WeaponModelPath = modelPath;
		Configure();
	}

	protected override void OnStart()
	{
		base.OnStart();
		EnsureDisplayIcon();
	}

	protected override void OnAdded( BaseInventoryComponent inventory )
	{
		base.OnAdded( inventory );
		EnsureDisplayIcon();
	}

	protected override void OnEquipped()
	{
		base.OnEquipped();
		EnsureDisplayIcon();
	}

	private void EnsureDisplayIcon()
	{
		if ( Application.IsDedicatedServer || DisplayIcon.IsValid() || string.IsNullOrEmpty( WeaponModelPath ) )
			return;

		DisplayIcon = GetOrRenderIcon( WeaponModelPath );
	}

	private static Texture GetOrRenderIcon( string modelPath )
	{
		if ( IconCache.TryGetValue( modelPath, out var cached ) && cached.IsValid() )
			return cached;

		var model = Model.Load( modelPath );
		if ( !model.IsValid() || model.IsError )
			return null;

		SceneWorld world = null;
		try
		{
			world = new SceneWorld();

			// Tilt tall staves/swords diagonally so they fill the wide weapon-bar icon nicely.
			var bounds = model.RenderBounds;
			var rot = bounds.Size.z > bounds.Size.x * 1.4f
				? Rotation.From( 0f, 25f, 52f )
				: Rotation.From( 12f, 35f, 0f );

			var rotatedCenter = rot * bounds.Center;
			var tx = new Transform( -rotatedCenter, rot, 1f );
			var sceneModel = new SceneModel( world, model, tx );
			sceneModel.Update( 0.01f );

			_ = new SceneDirectionalLight( world, Rotation.From( 35f, -45f, 0f ), Color.White * 2.4f );
			_ = new SceneDirectionalLight( world, Rotation.From( -20f, 135f, 0f ), new Color( 0.65f, 0.75f, 1.0f ) * 1.1f );

			var radius = MathF.Max( bounds.Size.Length * 0.5f, 8f );
			var camera = new SceneCamera
			{
				World = world,
				FieldOfView = 32f,
				ZNear = 1f,
				ZFar = radius * 12f,
				BackgroundColor = Color.Transparent,
				AmbientLightColor = Color.White * 0.45f,
				Position = new Vector3( -radius * 2.8f, 0f, 0f ),
				Rotation = Rotation.LookAt( Vector3.Forward ),
			};

			var rt = Texture.CreateRenderTarget()
				.WithSize( 256, 128 )
				.WithFormat( ImageFormat.RGBA8888 )
				.Create();

#pragma warning disable CS0618
			Graphics.RenderToTexture( camera, rt );
#pragma warning restore CS0618
			IconCache[modelPath] = rt;
			return rt;
		}
		catch
		{
			return null;
		}
		finally
		{
			world?.Delete();
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( GetComponent<ModelRenderer>( true ) is { } loose )
			loose.Enabled = !IsHeld;

		if ( _channelSound is not null && _channelSoundHold )
			StopChannelCue();
	}

	protected override void OnHolstered()
	{
		StopChannelCue();
		base.OnHolstered();
	}

	protected override void PlaceViewModel( CameraComponent camera, in CameraView view )
	{
		base.PlaceViewModel( camera, in view );
		if ( !ViewModel.IsValid() )
			return;

		var camTransform = new Transform( view.Position, view.Rotation );
		var local = new Transform( ViewOffset, ViewAngles.ToRotation(), 1f );
		ViewModel.WorldTransform = camTransform.ToWorld( local );
		ViewController?.ApplyBoneMask();
	}

	protected override void CreateViewModel()
	{
		if ( ViewModel.IsValid() || IsProxy || Holder is not PlayerController )
			return;

		var wizardModel = Model.Load( WizardViewModelPath );
		if ( !wizardModel.IsValid() || wizardModel.IsError )
			return;

		var go = new GameObject( GameObject, false, $"{DisplayName}_viewmodel" );
		go.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved | GameObjectFlags.Absolute;
		go.Tags.Add( "firstperson", "viewmodel" );

		var renderer = go.AddComponent<SkinnedModelRenderer>();
		renderer.Model = wizardModel;
		renderer.UseAnimGraph = false;
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		renderer.RenderOptions.Game = false;
		renderer.RenderOptions.Overlay = true;

		var view = go.AddComponent<MagickaWeaponView>();
		view.Renderer = renderer;
		view.TracerEffect = null;
		view.ViewOffset = ViewOffset;
		view.ViewAngles = ViewAngles;
		view.HeldLocalOffset = HeldLocalOffset;
		view.HeldLocalAngles = HeldLocalAngles;
		view.HeldScale = HeldScale;

		go.Enabled = true;
		view.Initialize( WeaponModelPath, AttachBone, IdleSequence, DeploySequence );
		ViewModel = go;
	}

	protected override void CreateWorldModel()
	{
		DestroyWorldModel();
		if ( !HolderRenderer.IsValid() || string.IsNullOrEmpty( WeaponModelPath ) )
			return;

		var model = Model.Load( WeaponModelPath );
		if ( !model.IsValid() || model.IsError )
			return;

		var parent = HolderRenderer.GetBoneObject( HoldBone ) ?? GameObject;
		var go = new GameObject( parent, false, $"{DisplayName}_worldmodel" );
		go.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		go.LocalPosition = WorldModelOffset;
		go.LocalRotation = WorldModelAngles.ToRotation();
		go.LocalScale = Vector3.One * HeldScale;

		var renderer = go.AddComponent<ModelRenderer>();
		renderer.Model = model;

		go.Enabled = true;
		WorldModel = go;
	}

	protected override bool OnDrop()
	{
		var player = Owner;
		if ( !player.IsValid() )
			return base.OnDrop();

		var eye = player.EyeTransform;
		var velocity = player.Velocity + eye.Forward * 200f + Vector3.Up * 100f;
		Network.DropOwnership();
		GameObject.SetParent( null, true );
		GameObject.Enabled = true;
		WorldPosition = eye.Position + eye.Forward * 48f;
		Slot = -1;

		if ( GetComponent<Rigidbody>( true ) is { } body )
		{
			body.Velocity = velocity;
			body.AngularVelocity = Vector3.Random * 6f;
		}

		return true;
	}

	protected override void OnDestroy()
	{
		StopChannelCue();
		DestroyWorldModel();
		base.OnDestroy();
	}

	protected void PlayCue( string cueName, Vector3? position = null, float volume = 1f, float pitch = 1f ) =>
		MagickaSpellEffects.PlayCue( cueName, position ?? WorldPosition, volume * 0.45f, pitch );

	/// <summary>
	/// Keeps a single continuous spell sound playing without overlapping, and fades it out
	/// immediately when the player stops firing.
	/// </summary>
	protected void MaintainChannelCue( string cueName, Vector3 position, float volume = 0.8f, float holdFor = 0.18f )
	{
		_channelSoundHold = holdFor;
		if ( _channelSound is not null && _channelSound.IsPlaying && string.Equals( _channelCue, cueName, StringComparison.OrdinalIgnoreCase ) )
		{
			_channelSound.Position = position;
			return;
		}

		StopChannelCue();
		_channelCue = cueName;
		_channelSound = MagickaSpellEffects.PlayCue( cueName, position, volume * 0.4f, Game.Random.Float( 0.96f, 1.04f ) );
	}

	protected void StopChannelCue()
	{
		if ( _channelSound is not null )
		{
			_channelSound.Stop( 0.08f );
			_channelSound = null;
			_channelCue = null;
		}
	}

	[Rpc.Broadcast]
	protected void BroadcastBeam( Vector3 start, Vector3 end, Color color, float width, float duration, bool jagged, string texturePath ) =>
		MagickaSpellEffects.SpawnBeam( start, end, color, width, duration, jagged, texturePath );

	[Rpc.Broadcast]
	protected void BroadcastImpact( Vector3 position, Color color, float radius, float duration, string cueName, string effectPath ) =>
		MagickaSpellEffects.SpawnImpact( position, color, radius, duration, cueName, effectPath );

	[Rpc.Broadcast]
	protected void BroadcastShockwave( Vector3 center, Color color, float radius, float duration ) =>
		MagickaSpellEffects.SpawnShockwave( center, color, radius, duration );

	[Rpc.Broadcast]
	protected void BroadcastEffect( string effectPath, Vector3 position, Rotation rotation, float scale ) =>
		MagickaEffectPlayer.PlayOnce( effectPath, position, rotation, scale );

	protected Ray GetCastRay() => Owner.IsValid() ? Owner.EyeTransform.ForwardRay : AimRay;

	protected Vector3 GetCastOrigin( Ray ray ) => ray.Position + ray.Forward * 34f - Vector3.Up * 6f;

	protected MagickaProjectile FireProjectile(
		string missilePath,
		Color tint,
		float damage,
		float speed,
		string hitCue,
		float spread = 0f,
		float gravity = 0f,
		float splashRadius = 0f,
		float knockback = 0f,
		float healAmount = 0f,
		MagickaElementStatus status = MagickaElementStatus.None,
		string trailEffect = null,
		string hitEffect = null )
	{
		if ( !Networking.IsHost )
			return null;

		var ray = GetCastRay();
		var dir = ray.Forward;
		if ( spread > 0f )
			dir = (dir + Vector3.Random * spread).Normal;

		var spawnPos = GetCastOrigin( ray );
		var model = Model.Load( missilePath );
		var proj = MagickaProjectile.Spawn( Owner?.GameObject ?? GameObject, model, spawnPos, dir, damage, tint );
		if ( proj.IsValid() )
		{
			proj.Speed = speed;
			proj.Gravity = gravity;
			proj.SplashRadius = splashRadius;
			proj.KnockbackForce = knockback;
			proj.HealAmount = healAmount;
			proj.ImpactCue = hitCue;
			proj.TrailEffect = trailEffect;
			proj.HitEffect = hitEffect;
			proj.StatusEffect = status;
		}

		return proj;
	}

	protected void CastAreaPulse(
		float radius,
		float damage,
		Color color,
		string cueName,
		string animSequence,
		float knockback = 280f,
		MagickaElementStatus status = MagickaElementStatus.None,
		string areaEffect = null )
	{
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( animSequence, loop: false, playbackRate: 1.2f );

		var ray = GetCastRay();
		var center = Owner.IsValid() ? Owner.WorldPosition + Vector3.Up * 36f : WorldPosition;
		var yaw = Rotation.FromYaw( ray.Forward.EulerAngles.yaw );
		PlayCue( cueName, center, 1f, Game.Random.Float( 0.94f, 1.06f ) );
		if ( !string.IsNullOrEmpty( areaEffect ) )
			BroadcastEffect( areaEffect, center + yaw.Forward * 24f, yaw, 1f );
		BroadcastShockwave( center - Vector3.Up * 16f, color, radius, 0.36f );

		if ( !Networking.IsHost )
			return;

		var ownerRoot = Owner?.GameObject?.Root ?? GameObject.Root;
		var seen = new HashSet<GameObject>();
		foreach ( var obj in Scene.FindInPhysics( new Sandbox.Sphere( center, radius ) ) )
		{
			var root = obj.Root;
			if ( !root.IsValid() || root == ownerRoot || !seen.Add( root ) )
				continue;

			var delta = root.WorldPosition + Vector3.Up * 24f - center;
			var dist = delta.Length;
			var falloff = Math.Clamp( 1f - dist / radius, 0.3f, 1f );

			if ( damage > 0f )
			{
				var info = new DamageInfo
				{
					Damage = damage * falloff,
					Attacker = Owner?.GameObject ?? GameObject,
					Weapon = GameObject,
					Position = root.WorldPosition + Vector3.Up * 24f,
					Origin = center,
				};
				info.Tags.Add( "magic" );
				root.GetComponentInChildren<Component.IDamageable>()?.OnDamage( info );
			}

			if ( status != MagickaElementStatus.None )
				MagickaSpellEffects.ApplyStatus( root, Owner?.GameObject ?? GameObject, status );

			if ( knockback > 0f )
			{
				var dir = delta.LengthSquared > 1f ? delta.Normal : Vector3.Up;
				MagickaSpellEffects.ApplyKnockback( root, dir * (knockback * falloff) + Vector3.Up * (knockback * 0.45f) );
			}
		}
	}
}

/// <summary>
/// The iconic Magicka Wizard's Basic Staff: casts Fire Bolts on primary fire and Frost Bolts
/// on secondary fire using the Wizard's spell-casting animations.
/// </summary>
[Title( "Basic Staff" )]
[Category( "Magicka" )]
public sealed class MagickaStaffWeapon : MagickaWeapon
{
	private const string FireMissileModel = "mount://magicka/models/missiles/fire1.vmdl";
	private const string ColdMissileModel = "mount://magicka/models/missiles/cold0.vmdl";

	internal override void Configure()
	{
		base.Configure();
		AttachBone = "LeftAttach";
		IdleSequence = "idle";
		DeploySequence = "pickup_staff";
		PrimaryDelay = 0.5f;
		SecondaryDelay = 0.5f;
		HoldType = WeaponHolds.Melee;
		PreferredSlot = 0;
		SlotOrder = 20;
	}

	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay );
		SetNextSecondaryFire( PrimaryDelay * 0.6f );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "cast_force_projectile", loop: false, playbackRate: 1.25f );
		PlayCue( "spell_fire_projectile", GetCastRay().Position, 0.9f, Game.Random.Float( 0.94f, 1.06f ) );
		FireProjectile(
			FireMissileModel,
			MagickaMount.ColorForElement( 0x8 ),
			28f,
			1150f,
			"spell_fire_hit",
			status: MagickaElementStatus.Burn,
			trailEffect: "Spells/Projectile/ProjectileTrail_Fire.xml",
			hitEffect: "Spells/Projectile/ProjectileHit_Fire.xml" );
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		SetNextPrimaryFire( SecondaryDelay * 0.6f );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "cast_force_push", loop: false, playbackRate: 1.25f );
		PlayCue( "spell_cold_projectile", GetCastRay().Position, 0.9f, Game.Random.Float( 0.94f, 1.06f ) );
		FireProjectile(
			ColdMissileModel,
			MagickaMount.ColorForElement( 0x4 ),
			22f,
			1300f,
			"spell_cold_hit",
			status: MagickaElementStatus.Chill,
			trailEffect: "Spells/Projectile/ProjectileTrail_Cold.xml",
			hitEffect: "Spells/Projectile/ProjectileHit_Cold.xml" );
	}
}
