using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>
/// The base of the mounted Source weapons: view kick, the view model placed against the camera, the drop out in
/// front of the holder, the standalone model shown only while nobody holds it, and the shared bullet rules.
/// </summary>
[Alias( "Source1.SourceWeapon", "SourceWeapon" )]
public abstract class Weapon : BaseCombatWeapon
{
	/// <summary>What a mounted weapon's attack2 does. One data-driven choice instead of a flag per behaviour.</summary>
	public enum SecondaryFires
	{
		/// <summary>Launch the projectile the weapon script's secondary ammo names, if any.</summary>
		Default,
		/// <summary>weapon_shotgun: both barrels at once.</summary>
		DoubleShot,
		/// <summary>weapon_crossbow: toggle a narrow field of view. Counter-Strike's scopes step through <see cref="BulletWeapon.ZoomLevels"/>.</summary>
		Zoom,
		/// <summary>weapon_rpg: toggle laser guidance.</summary>
		Guide,
		/// <summary>Counter-Strike's Glock and FAMAS: toggle three-round bursts.</summary>
		Burst,
		/// <summary>Counter-Strike's M4A1 and USP: attach or detach the silencer.</summary>
		Silencer,
		/// <summary>Hold primary fire until the tap refire delay has elapsed after release.</summary>
		DelayPrimary
	}

	/// <summary>Minimum interval between empty primary clicks for weapons that can fire as soon as ammo arrives.</summary>
	[Property, Feature( "Shooting" )] public float EmptyClickInterval { get; set; }
	TimeUntil _nextEmptyClick;
	TimeUntil _deployUntil;
	protected bool IsDeploying => _deployUntil > 0;

	protected override void OnEquipped()
	{
		_deployUntil = DeployTime;
		base.OnEquipped();
	}

	public override bool CanReload() => (!HasOwner || !IsDeploying) && base.CanReload();

	protected bool WantsEmptyClick => HasOwner && EmptyClickInterval > 0 && Input.Down( "attack1" ) && !HasPrimaryAmmo();

	public override void DryFire()
	{
		if ( WantsEmptyClick ) TryPlayEmptyClick( EmptyClickInterval );
		else base.DryFire();
	}

	protected bool TryPlayEmptyClick( float interval )
	{
		if ( _nextEmptyClick > 0 ) return false;
		if ( DryFireSound is { } sound ) GameObject.PlaySound( sound );
		_nextEmptyClick = interval;
		return true;
	}

	/// <summary>Random pitch added to the aim per attack (min, max degrees).</summary>
	[Property, Group( "Recoil" )] public Vector2 RecoilPitch { get; set; } = new( -0.3f, -0.1f );

	/// <summary>Random yaw added to the aim per attack (min, max degrees).</summary>
	[Property, Group( "Recoil" )] public Vector2 RecoilYaw { get; set; } = new( -0.1f, 0.1f );

	/// <summary>Random pitch of the first-person view punch per attack (min, max degrees).</summary>
	[Property, Group( "Camera" )] public Vector2 PunchPitch { get; set; } = new( 0f, 0f );

	/// <summary>Random yaw of the first-person view punch per attack (min, max degrees).</summary>
	[Property, Group( "Camera" )] public Vector2 PunchYaw { get; set; } = new( 0f, 0f );

	/// <summary>Random roll of the first-person view punch per attack (min, max degrees).</summary>
	[Property, Group( "Camera" )] public Vector2 PunchRoll { get; set; } = new( 0.5f, 0.5f );

	public enum PunchResponses { Oscillate, AddImpulse, ResetImpulse }

	[Property, Group( "Camera" )] public PunchResponses PunchResponse { get; set; }

	/// <summary>How many times the view punch oscillates before it lapses.</summary>
	[Property, Group( "Camera" )] public float PunchFrequency { get; set; } = 2f;

	/// <summary>How long the view punch lasts, in seconds.</summary>
	[Property, Group( "Camera" )] public float PunchDuration { get; set; } = 0.3f;

	/// <summary>First-person camera shake strength per attack, in world units. Zero for none.</summary>
	[Property, Group( "Camera" )] public float ShakeAmplitude { get; set; } = 0f;

	/// <summary>New shake offsets per second.</summary>
	[Property, Group( "Camera" )] public float ShakeFrequency { get; set; } = 40f;

	/// <summary>How long the shake takes to settle, in seconds.</summary>
	[Property, Group( "Camera" )] public float ShakeDuration { get; set; } = 0.3f;

	/// <summary>The weapon's own model while it lies in the world, hidden while something holds it.</summary>
	[Property, Feature( "WorldModel" )] public GameObject DroppedGameObject { get; set; }

	/// <summary>True while a player holds this.</summary>
	public bool HasOwner => Owner.IsValid();

	/// <summary>The root GameObject to ignore when tracing from the aim ray.</summary>
	public GameObject AimIgnoreRoot => Holder?.GameObject ?? GameObject;

	internal GameObject AttackInstigator => Attacker;

	// Only a host-local launch can supply captured attribution. A remote owner is credited through the weapon.
	internal GameObject ResolveLaunchAttacker( GameObject attacker ) =>
		Rpc.Caller == Connection.Local && attacker.IsValid() ? attacker : Attacker;

	protected override bool OnAdding( BaseInventoryComponent inventory )
	{
		WeaponSetup.ConfigureAmmo( this );
		return base.OnAdding( inventory );
	}

	protected override void OnAdded( BaseInventoryComponent inventory )
	{
		if ( UsesPrimaryClip && Clip1 > PrimaryClipSize )
		{
			inventory.GiveAmmo( PrimaryAmmoType, Clip1 - PrimaryClipSize );
			Clip1 = PrimaryClipSize;
		}
		if ( UsesAmmo && !UsesClips && PrimaryAmmoType is not null && StartingAmmo > 0 && inventory.GetAmmo( PrimaryAmmoType ) != int.MaxValue )
		{
			inventory.GiveAmmo( PrimaryAmmoType, StartingAmmo );
			StartingAmmo = 0;
		}
		base.OnAdded( inventory );
	}

	protected override void OnEnabled()
	{
		base.OnEnabled();
		ShowStandalone();
	}

	TimeSince _sinceIconAttempt = 10;

	protected override void OnUpdate()
	{
		base.OnUpdate();
		ShowStandalone();

		// The HUD icon comes from the mounted weapon script, which may not be readable until its game has mounted
		if ( DisplayIcon is null && _sinceIconAttempt > 1 )
		{
			_sinceIconAttempt = 0;
			WeaponSetup.Icon( this );
		}
	}

	void ShowStandalone()
	{
		var show = !IsHeld;
		if ( DroppedGameObject.IsValid() && DroppedGameObject.Enabled != show ) DroppedGameObject.Enabled = show;
	}

	/// <summary>
	/// Kicks the owner's aim, then punches and shakes their view if they're the local player in first person.
	/// Returns whether the view was kicked.
	/// </summary>
	protected bool KickView( Angles? punchOverride = null )
	{
		if ( !HasOwner ) return false;

		Owner.EyeAngles += new Angles( Random.Shared.Float( RecoilPitch.x, RecoilPitch.y ), Random.Shared.Float( RecoilYaw.x, RecoilYaw.y ), 0 );

		if ( Owner.ThirdPerson || Owner.IsProxy ) return false;

		var punch = punchOverride ?? new Angles( Random.Shared.Float( PunchPitch.x, PunchPitch.y ), Random.Shared.Float( PunchYaw.x, PunchYaw.y ), Random.Shared.Float( PunchRoll.x, PunchRoll.y ) );
		if ( PunchResponse == PunchResponses.Oscillate ) Scene.Camera?.AddPunch( punch, PunchFrequency, PunchDuration );
		else ViewPunch.Apply( Scene.Camera, punch, PunchResponse == PunchResponses.ResetImpulse );

		if ( ShakeAmplitude > 0f )
			Scene.Camera?.AddShake( ShakeAmplitude, ShakeFrequency, ShakeDuration );

		return true;
	}

	// The view the gun is placed at, captured before the camera bone offsets the view, so authored camera
	// animation moves the camera around the gun, not the gun with it.
	CameraView _viewModelView;

	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		base.ModifyCamera( camera, ref view );

		_viewModelView = view;

		ViewModel?.GetComponentInChildren<ViewModel>()?.UpdateCameraBone( ref view );
	}

	protected override void PlaceViewModel( CameraComponent camera, in CameraView view )
	{
		var model = ViewModel?.GetComponentInChildren<ViewModel>();
		if ( !model.IsValid() ) return;

		var placement = _viewModelView;
		var owner = Owner;
		if ( owner.IsValid() ) owner.GetOrAddComponent<ViewModelMotion>().Apply( ref placement, owner.Velocity, model.Mirrored );
		model.Place( placement );
		model.FollowCameraEffects( camera, view );
		model.ApplyLens( camera, view, camera.GetComponent<Zoom>()?.ViewModelFovOffset ?? 0 );
		OnViewModelPlaced( model );
	}

	/// <summary>
	/// The viewmodel has its final place for the frame. That happens after updates, so effects following its
	/// attachments move here or they trail the view.
	/// </summary>
	protected virtual void OnViewModelPlaced( ViewModel model ) { }

	/// <summary>Bullets go through hitboxes and skip the player controller's own collider.</summary>
	protected override SceneTrace BulletTrace( Ray ray, float distance, float radius )
	{
		return Scene.Trace.Ray( ray, distance )
			.IgnoreGameObjectHierarchy( AimIgnoreRoot )
			.WithCollisionRules( "bullet" )
			.WithoutTags( "playercontroller" )
			.Radius( radius )
			.UseHitboxes();
	}

	TimeSince _timeSinceNoise;

	/// <summary>Gunfire is loud: NPCs hear a player shooting. NPC shooters announce their own bursts.</summary>
	protected void EmitGunfireNoise( ShotEffect shot )
	{
		if ( !Networking.IsHost || shot.NoEvents || _timeSinceNoise < 0.25f ) return;

		var owner = Owner;
		if ( !owner.IsValid() ) return;

		_timeSinceNoise = 0;
		Scene.GetSystem<NpcSystem>()?.EmitStimulus( StimulusKinds.Gunshot, owner.WorldPosition, owner.GameObject, radius: 3000f, lifetime: 1.5f );
	}

	/// <summary>
	/// The host bullet trace already applied its impulse. Client-side ragdolls receive the visual impact locally.
	/// </summary>
	protected void PushRagdoll( GameObject hitObject, Vector3 hitPosition, Vector3 origin, float impactScale, float bodyShare )
	{
		if ( Networking.IsHost || !hitObject.IsValid() || impactScale <= 0 ) return;

		var struck = hitObject.Components.GetInAncestorsOrSelf<Rigidbody>();
		if ( !struck.IsValid() || struck.IsProxy ) return;

		var physics = hitObject.Components.GetInAncestorsOrSelf<ModelPhysics>();
		if ( !physics.IsValid() || physics.Bodies.Count < 2 ) return;

		var impulse = (hitPosition - origin).Normal * (4000f * impactScale);
		struck.ApplyImpulseAt( hitPosition, impulse );
		if ( bodyShare <= 0 ) return;

		foreach ( var body in physics.Bodies )
		{
			var rb = body.Component;
			if ( rb.IsValid() && rb != struck ) rb.ApplyImpulse( impulse * bodyShare );
		}
	}

	/// <summary>Thrown out in front of the player dropping it, carrying their speed; otherwise let go where it is.</summary>
	protected override bool OnDrop()
	{
		var holder = Owner;
		var position = WorldPosition;
		var velocity = Vector3.Up * 50f;

		if ( holder.IsValid() )
		{
			var eye = holder.EyeTransform;
			position = eye.Position + eye.Forward * 48f;
			velocity = holder.Velocity + eye.Forward * 200f + Vector3.Up * 100f;
		}

		Network.DropOwnership();
		GameObject.SetParent( null, true );
		WorldScale = PrefabWorldScale();
		GameObject.Enabled = true;
		WorldPosition = position;

		if ( GetComponent<Rigidbody>( true ) is { } body )
		{
			body.Velocity = velocity;
			body.AngularVelocity = Vector3.Random * 8.0f;
		}

		return true;
	}

	Vector3 PrefabWorldScale()
	{
		if ( !string.IsNullOrEmpty( GameObject.PrefabInstanceSource ) && GameObject.GetPrefab( GameObject.PrefabInstanceSource ) is { } prefab && prefab.IsValid() )
			return prefab.LocalScale;

		return WorldScale;
	}
}
