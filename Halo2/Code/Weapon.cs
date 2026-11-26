namespace Halo2;

[Sandbox.ModelEditor.GameData( "weapon_attachment" )]
public sealed class WeaponAttachment
{
	public string PrimaryHand { get; set; }
	public string SecondaryHand { get; set; }
	public string PreferredGrip { get; set; }
}

[Sandbox.ModelEditor.GameData( "player_representation" )]
public sealed class PlayerRepresentation
{
	public int Index { get; set; }
	public float CameraFieldOfView { get; set; }
}

public abstract partial class Weapon : BaseCombatWeapon
{
	protected override async System.Threading.Tasks.Task OnLoad()
	{
		await base.OnLoad();
		await Sandbox.Mounting.Directory.Mount( Halo2Mount.MountIdent );
	}

	[Property] public bool NpcOnly { get; set; }
	[Property] public bool DualWieldable { get; set; }
	[Property] public string AnimationClass { get; set; }
	[Property] public string AnimationType { get; set; }
	[Property] public string AttackSoundMarker { get; set; }
	[Property] public string DryFireMarker { get; set; }
	bool dryTriggerHeld;
	protected double PrimaryReadyAt { get; private set; }

	protected override void OnEquipped()
	{
		if ( Networking.IsHost && AnimationClass is { Length: > 0 } && AnimationType is { Length: > 0 } )
			HolderAnimator?.ConfigureWeapon( AnimationClass, AnimationType );
		base.OnEquipped();
		if ( !IsValid || Holder is not PlayerController || !IsActive ) return;
		if ( FirstPersonAnimations()?.Actions.TryGetValue( "ready", out var clip ) == true )
		{
			PrimaryReadyAt = (System.Math.Floor( Time.NowDouble * 30 ) + ReleaseFrame( clip )) / 30;
			SetNextPrimaryFire( (float)(PrimaryReadyAt - Time.NowDouble) );
		}
	}

	[Property] public Vector3 ViewOffset { get; set; }
	[Property] public float KickDuration { get; set; }
	[Property] public float KickDegrees { get; set; }
	[Property] public float KickDistance { get; set; }
	[Property] public Vector2 KickJitter { get; set; }
	uint kickSeed = (uint)System.Random.Shared.Next();
	Vector3 kickOffset;
	TimeUntil kickEnds;

	internal static float CooldownSeconds( int ticks )
	{
		double seconds = ticks / 30.0;
		// A float timer rounded up would still be active on its authored end tick.
		float delay = (float)seconds;
		return delay > seconds ? System.MathF.BitDecrement( delay ) : delay;
	}

	// Loose weapons retain unclaimed ammunition when the recipient reaches the reserve cap.
	[Sync( SyncFlags.FromHost )] int looseReserve { get; set; } = -1;
	// The inventory exposes one ammo count. Retain the second magazine's share for detachment.
	int? pairedReserve;
	protected int LooseReserve => looseReserve >= 0 ? looseReserve : Clip1 < 0 ? StartingAmmo : 0;
	int LoadedRounds => Clip1 < 0 ? ClipMaxSize : Clip1;

	Weapon MatchingWeapon( BaseInventoryComponent inventory ) =>
		inventory.Items.OfType<Weapon>().FirstOrDefault( w => w != this && w.GetType() == GetType() &&
			w.GameObject.PrefabInstanceSource == GameObject.PrefabInstanceSource );

	protected override bool OnCanPickup( BaseInventoryComponent inventory )
	{
		if ( NpcOnly && inventory.GetComponentInParent<PlayerController>().IsValid() ) return false;
		if ( !UsesClips || !UsesAmmo ) return base.OnCanPickup( inventory );
		if ( LoadedRounds == 0 && LooseReserve == 0 ) return false;
		if ( MatchingWeapon( inventory ) is not {} held ) return base.OnCanPickup( inventory );
		return held.UsesAmmo && held.Ammo1 < held.MaxReserveAmmo;
	}

	int CarriedReserve( BaseInventoryComponent inventory )
	{
		int available = inventory?.GetAmmo( PrimaryAmmoType ) ?? 0;
		if ( available == 0 || available == int.MaxValue ) return 0;
		if ( pairedReserve is int share ) available = System.Math.Min( available, share );
		else if ( MatchingWeapon( inventory )?.pairedReserve is int other ) available = System.Math.Max( 0, available - other );
		return System.Math.Min( available, MaxReserveAmmo - LooseReserve );
	}

	protected override int TakeReserveAmmo( BaseAmmoResource ammoType, int amount )
	{
		int taken = base.TakeReserveAmmo( ammoType, amount );
		if ( taken > 0 && pairedReserve is int share && ammoType == PrimaryAmmoType )
			pairedReserve = System.Math.Max( 0, share - taken );
		else if ( taken > 0 && Offhand.IsValid() && Offhand.PrimaryAmmoType == ammoType && Offhand.pairedReserve is int other )
			Offhand.pairedReserve = System.Math.Min( other, Inventory.GetAmmo( ammoType ) );
		return taken;
	}

	protected override bool OnAdding( BaseInventoryComponent inventory )
	{
		if ( NpcOnly && inventory.GetComponentInParent<PlayerController>().IsValid() ) return false;
		if ( offhandPickup == inventory ) return true;
		if ( !UsesClips || !UsesAmmo ) return base.OnAdding( inventory );
		var held = MatchingWeapon( inventory );
		if ( held is null ) return base.OnAdding( inventory );
		if ( !held.UsesAmmo || held.Ammo1 >= held.MaxReserveAmmo ) return false;

		int loaded = LoadedRounds;
		int reserve = LooseReserve;
		// Pickup(prefab) parents the new item before Add assigns its inventory slot.
		var source = Slot >= 0 ? Inventory : null;
		if ( source == inventory ) return false;
		int carried = CarriedReserve( source );
		int taken = inventory.GiveAmmo( held.PrimaryAmmoType, reserve + carried + loaded );
		if ( taken > 0 )
		{
			int fromReserve = System.Math.Min( reserve, taken );
			looseReserve = reserve - fromReserve;
			int remainder = taken - fromReserve;
			if ( carried > 0 ) remainder -= source.TakeAmmo( PrimaryAmmoType, System.Math.Min( carried, remainder ) );
			Clip1 = loaded - remainder;
			if ( source is null && Clip1 == 0 && looseReserve == 0 ) GameObject.Destroy();
		}
		return false;
	}

	protected override void OnAdded( BaseInventoryComponent inventory )
	{
		if ( !UsesClips || !UsesAmmo )
		{
			base.OnAdded( inventory );
			return;
		}

		int reserve = LooseReserve;
		StartingAmmo = 0;
		base.OnAdded( inventory );
		int credited = inventory.GiveAmmo( PrimaryAmmoType, reserve );
		looseReserve = reserve - credited;
		pairedReserve = offhandPickup == inventory && MatchingWeapon( inventory ).IsValid() ? credited : null;
	}

	protected override void OnRemoved( BaseInventoryComponent inventory )
	{
		base.OnRemoved( inventory );
		if ( inventory.ActiveItem is Weapon primary && primary.Offhand == this ) primary.SetOffhand( null );
		SetOffhand( null );
		// Drop and transfer have already reparented the weapon. Removal for destruction has not.
		if ( Inventory != inventory && UsesClips && UsesAmmo )
			looseReserve = LooseReserve + inventory.TakeAmmo( PrimaryAmmoType, CarriedReserve( inventory ) );
		if ( UsesClips && UsesAmmo && MatchingWeapon( inventory ) is {} remaining ) remaining.pairedReserve = null;
		pairedReserve = null;
	}

	protected override bool OnDrop()
	{
		var ray = AimRay;
		var velocity = Sandbox.Npcs.BaseNpc.GetVelocity( Holder?.GameObject );
		var renderer = HolderRenderer;
		var grip = renderer?.Model?.GetData<WeaponAttachment>()?.PrimaryHand ?? "right_hand";
		var release = renderer?.GetAttachment( "drop" ) ?? renderer?.GetAttachment( grip );
		if ( release is null && renderer is not null && renderer.TryGetBoneTransform( HoldBone, out var hand ) )
			release = hand;
		var transform = release ?? new Transform( ray.Position, Rotation.LookAt( ray.Forward ) );
		if ( !base.OnDrop() ) return false;
		WorldTransform = transform.WithScale( WorldScale );
		if ( GetComponent<Rigidbody>( true ) is {} body )
		{
			// An inventory drop rolls the speed before deviating the holder's facing.
			ref uint randomState = ref BlamRandom.GameplayState( Scene );
			float speed = (0.8f + 0.4f * (BlamRandom.Next( ref randomState ) / 65535f)) * 120;
			var direction = Spread.Deviate( ray.Forward, 0, System.MathF.PI / 8, ref randomState );
			var toss = (velocity + direction * speed).ClampLength( 4 * 120 );
			body.Velocity = toss;
			body.AngularVelocity = DropSpin( toss / 120, ref randomState );
		}
		return true;
	}

	static Vector3 DropSpin( Vector3 velocity, ref uint randomState )
	{
		float speed = velocity.Length;
		if ( speed < 0.0001f ) speed = BlamRandom.Next( ref randomState ) / 65535f;
		var axis = Vector3.Cross( Vector3.Up, velocity );
		float length = axis.Length;
		axis = length < 0.0001f ? Spread.RandomDirection( ref randomState ) : axis / length;
		return axis * ((BlamRandom.Next( ref randomState ) / 65535f) * (System.MathF.PI / 2) * speed);
	}

	Transform cameraAdjustment = global::Transform.Zero;

	static float KickWeight( float remaining )
	{
		// These firing damage effects use 1024 byte samples of the fourth-power curve.
		static float Sample( int index )
		{
			double x = index / 1023.0;
			return (int)(255 * x * x * x * x) * (1f / 255f);
		}

		float position = remaining.Clamp( 0, 1 ) * 1023;
		int index = (int)System.MathF.Round( position - 0.1f );
		if ( index == 1023 ) return 1;
		float fraction = position - (int)position;
		return Sample( index ) * (1 - fraction) + Sample( index + 1 ) * fraction;
	}

	[Property] public float VerticalAimOffset { get; set; } = 0.165f;

	// MCC projects the authored observer offset through the safe camera rectangle.
	// Preserve the player's selected lens and rotate the view so the aim ray meets that pixel.
	Vector2 reticleOffset;
	void ApplyObserverAim( CameraComponent camera, ref CameraView view )
	{
		reticleOffset = Vector2.Zero;
		var viewport = camera.Viewport;
		var rect = camera.ScreenRect;
		var size = rect.Size;
		if ( Owner is { ThirdPerson: false } && !camera.Orthographic && size.x > 0 && size.y > 0 && viewport.z > 0 && viewport.w > 0 )
		{
			float aspect = size.x / size.y;
			float referenceAspect = System.MathF.Min( aspect, 16f / 9f );
			float halfFov = System.MathF.Tan( view.FieldOfView.DegreeToRadian() * 0.5f );
			float horizontal = camera.FovAxis == CameraComponent.Axis.Horizontal ? halfFov : halfFov * referenceAspect;
			float vertical = camera.FovAxis == CameraComponent.Axis.Horizontal ? halfFov / aspect : halfFov;
			const float mccAngleScale = 0.895f;
			float observer = System.MathF.Tan( System.MathF.Atan( horizontal * 0.75f ) * mccAngleScale );
			float projection = System.MathF.Tan( System.MathF.Atan( horizontal / referenceAspect ) * mccAngleScale );
			// Outer edges use the HUD safe area. Interior split seams use a four-pixel inset.
			var hudScale = size / new Vector2( 640 * viewport.z, 480 * viewport.w );
			int left = (int)(rect.Left + (viewport.x == 0 ? 48 : 4) * hudScale.x);
			int right = (int)(rect.Right - (viewport.x + viewport.z >= 1 ? 48 : 4) * hudScale.x);
			int top = (int)(rect.Top + (viewport.y == 0 ? 36 : 4) * hudScale.y);
			int bottom = (int)(rect.Bottom - (viewport.y + viewport.w >= 1 ? 36 : 4) * hudScale.y);
			if ( projection > 0 )
			{
				// The observer truncates safe edges in display pixels before it centers and projects the aim point.
				float y = (top + bottom + (bottom - top) * VerticalAimOffset * observer / projection) * 0.5f;
				var anchor = new Vector2( (left + right) / 2, System.MathF.Floor( y + 0.5f ) );
				reticleOffset = (anchor - rect.Position) / size - new Vector2( 0.5f );
				var aimInView = new Vector3( 1, -2 * reticleOffset.x * vertical * aspect, -2 * reticleOffset.y * vertical );
				view.Rotation *= Rotation.LookAt( aimInView ).Inverse;
			}
		}
	}

	public override void DrawHud( Painter painter, Vector2 crosshair )
	{
		if ( Owner is { ThirdPerson: false } ) crosshair += painter.Bounds.Size * reticleOffset;
		base.DrawHud( painter, crosshair );
	}

	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		ApplyZoomLens( ref view );
		ApplyObserverAim( camera, ref view );

		if ( kickEnds > 0 && KickDuration > 0 )
		{
			float durationTicks = KickDuration * 30;
			float elapsedTicks = kickEnds.Passed * 30;
			// A new impulse has full weight until the first tick. The countdown stores whole ticks.
			float weight = elapsedTicks < 1 ? 1 : KickWeight( ((int)durationTicks - elapsedTicks) / durationTicks );
			view.Position += view.Rotation * (kickOffset * weight);
			view.Rotation *= Rotation.FromPitch( -KickDegrees * weight );
		}

		cameraAdjustment = global::Transform.Zero;
		if ( !ViewModel.IsValid() || Owner is { ThirdPerson: true } ) return;
		var renderer = ViewModel.GetComponent<SkinnedModelRenderer>();
		if ( !renderer.IsValid() || !renderer.TryGetBoneTransform( "camera_control", out var control ) ) return;
		cameraAdjustment = renderer.SceneModel.Transform.ToLocal( control );
		var adjusted = view.Transform.ToWorld( cameraAdjustment );
		view.Position = adjusted.Position;
		view.Rotation = adjusted.Rotation;
	}

	protected override void PlaceViewModel( CameraComponent camera, in CameraView view )
	{
		if ( !ViewModel.IsValid() ) return;
		// The camera already includes the rig's animated camera node.
		var root = view.Transform.ToWorld( cameraAdjustment.ToLocal( global::Transform.Zero ) );
		ViewModel.WorldTransform = root.WithPosition( root.PointToWorld( ViewOffset ) );
		if ( Offhand.IsValid() && Offhand.ViewModel.IsValid() )
			Offhand.ViewModel.WorldTransform = root.WithPosition( root.PointToWorld( Offhand.ViewOffset ) );
	}

	protected Halo2Animator HolderAnimator => HolderRenderer is {} body
		&& body.GetComponentInParent<Halo2Animator>( true ) is {} animator && animator.Renderer == body ? animator : null;

	Halo2Animator equippedAnimator;
	Sandbox.Animation.Graph previousGraph;
	bool previousUseAnimGraph;

	protected Halo2Animator BindHolderAnimator()
	{
		if ( HolderAnimator is {} existing ) return existing;
		var body = HolderRenderer;
		if ( body?.Model?.GetData<WeaponAttachment>() is null || string.IsNullOrEmpty( AnimationClass ) || string.IsNullOrEmpty( AnimationType ) ) return null;

		var animator = body.GameObject.AddComponent<Halo2Animator>();
		animator.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked | ComponentFlags.NotCloned;
		animator.Renderer = body;
		previousGraph = body.Animation.Graph;
		previousUseAnimGraph = body.UseAnimGraph;
		if ( animator.Configure( "combat", AnimationClass, AnimationType ) )
			return equippedAnimator = animator;

		animator.Destroy();
		previousGraph = null;
		return null;
	}

	void ReleaseHolderAnimator()
	{
		if ( equippedAnimator.IsValid() )
		{
			if ( equippedAnimator.Bound )
			{
				var body = equippedAnimator.Renderer;
				body.Animation.Graph = previousGraph;
				body.UseAnimGraph = previousUseAnimGraph;
			}
			equippedAnimator.Destroy();
		}
		equippedAnimator = null;
		previousGraph = null;
	}

	protected override void DestroyWorldModel()
	{
		ReleaseHolderAnimator();
		base.DestroyWorldModel();
	}

	protected override void OnDisabled()
	{
		PrimaryReadyAt = 0;
		ClearPutAway();
		dryTriggerHeld = false;
		SetZoom( 0, audible: false );
		ReleaseHolderAnimator();
		base.OnDisabled();
	}

	protected void PlayHolderOverlay( string name )
	{
		if ( !Application.IsDedicatedServer ) HolderAnimator?.PlayWeaponOverlay( name );
	}

	protected override void OnShootEffects( ShotEffect shot )
	{
		if ( !shot.NoEvents )
		{
			if ( UsesBinoculars ) SetZoom( 0 );
			PlayHolderOverlay( "fire_1" );
		}
		if ( !shot.NoEvents && !IsProxy && KickDuration > 0 )
		{
			float radius = KickJitter.x + (KickJitter.y - KickJitter.x) * (BlamRandom.Next( ref kickSeed ) / 65535f);
			float angle = System.MathF.Tau * (BlamRandom.Next( ref kickSeed ) / 65535f);
			var (sin, cos) = System.MathF.SinCos( angle );
			kickOffset = new Vector3( -KickDistance, radius * cos, -radius * sin );
			kickEnds = KickDuration;
		}

		if ( shot.NoEvents || Application.IsDedicatedServer )
		{
			base.OnShootEffects( shot );
			return;
		}

		base.OnShootEffects( shot with { NoEvents = true } );
		// A joining client can receive the weapon before the mount registers its sounds.
		if ( !AttackSound.IsValid() )
		{
			if ( AttackSound?.ResourcePath is { Length: > 0 } path )
				AttackSound = GameResource.Load<SoundEvent>( path );
			else if ( GameObject.PrefabInstanceSource is { Length: > 0 } prefab )
				AttackSound = GameObject.GetPrefab( prefab )?.GetComponent<Weapon>( true )?.AttackSound;
		}
		var model = WeaponModel;
		PlayFiringSound();
		model?.OnAttack( shot.Hit || shot.HitPosition != default ? shot.HitPosition : null, shot.Origin );
		PostWeaponEvent( WeaponEvents.Attack );
	}

	protected virtual void PlayFiringSound() => PlayFiringSound( AttackSound, AttackSoundMarker );

	protected override void OnControl()
	{
		if ( !Input.Down( TriggerButton ) || Input.Pressed( TriggerButton ) ) dryTriggerHeld = false;
		if ( Offhand.IsValid() && !PuttingAway ) Offhand.OnControl();
		base.OnControl();
		if ( IsOffhand ) return;
		UpdateZoomInput();
		UpdateOffhandPickup();
	}

	public override void DryFire()
	{
		if ( DryFireMarker is null )
		{
			base.DryFire();
			return;
		}
		if ( dryTriggerHeld ) return;
		dryTriggerHeld = true;
		PlayDrySound();
		SetNextPrimaryFire( PrimaryDelay );
		if ( AutoReload && CanReload() ) Reload();
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void PlayDrySound()
	{
		if ( !Application.IsDedicatedServer ) PlayFiringSound( DryFireSound, DryFireMarker );
	}

	protected void PlayFiringSound( SoundEvent cue, string marker )
	{
		var sound = EffectMarker( marker )?.PlaySound( cue );
		if ( sound.IsValid() && Holder is PlayerController player && player.Network.IsOwner ) sound.SpacialBlend = 0;
	}

	protected GameObject EffectMarker( string marker )
	{
		var model = WeaponModel;
		if ( string.IsNullOrEmpty( marker ) ) return model?.MuzzleGameObject ?? GameObject;
		if ( marker == "primary_trigger" ) return model?.MuzzleGameObject;
		if ( model is null ) return null;
		foreach ( var renderer in model.GameObject.GetComponentsInChildren<SkinnedModelRenderer>() )
			if ( renderer.CreateAttachments && renderer.GetAttachmentObject( marker ) is {} attachment ) return attachment;
		return null;
	}

	[Property] public float DamageAccelerationScale { get; set; } = 1;

	protected static bool ApplyLooseItemImpulse( BaseInventoryItem item, Vector3 direction, float force )
	{
		var body = item.GetComponent<Rigidbody>();
		if ( !body.IsValid() || !body.MotionEnabled ) return false;

		// Loose items receive two normalized upward adjustments before the damage impulse.
		direction = ((direction + Vector3.Up * 0.15f).Normal + Vector3.Up * 0.3f).Normal;
		float velocity = force * (item is Weapon weapon ? weapon.DamageAccelerationScale : 1);
		if ( !float.IsFinite( velocity ) || velocity <= 0 ) return true;
		body.ApplyImpulse( direction * (velocity * body.Mass) );

		float speed = velocity / 120;
		ref uint state = ref BlamRandom.GameplayState( body.Scene );
		const float fraction = 1f / 65535;
		if ( speed < 0.0001f ) speed = BlamRandom.Next( ref state ) * fraction;
		var axis = Vector3.Cross( Vector3.Up, direction ).Normal;
		if ( axis == Vector3.Zero ) axis = Spread.RandomDirection( ref state );
		body.AngularVelocity += axis * (BlamRandom.Next( ref state ) * fraction * speed * System.MathF.PI / 2);
		return true;
	}

	protected override float GetHitForce( GameObject target, float force )
	{
		var body = target.GetComponentInParent<Rigidbody>();
		if ( !body.IsValid() || target.GetComponentInParent<Sandbox.Npcs.BaseNpc>() is not null
			|| target.GetComponentInParent<PlayerController>() is not null ) return force;

		// The damage tag supplies a velocity change. ApplyImpulseAt takes momentum.
		return force * body.Mass * (body.GetComponent<Weapon>()?.DamageAccelerationScale ?? 1);
	}

	protected override SceneTrace BulletTrace( Ray ray, float distance, float radius ) =>
		Scene.Trace.Ray( ray, distance ).IgnoreGameObjectHierarchy( Holder?.GameObject ?? GameObject )
			.WithCollisionRules( "bullet" ).WithoutTags( "playercontroller" ).Radius( radius ).UseHitboxes();
}
