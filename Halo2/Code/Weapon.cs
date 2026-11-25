namespace Halo2;

[Sandbox.ModelEditor.GameData( "weapon_attachment" )]
public sealed class WeaponAttachment
{
	public string PrimaryHand { get; set; }
}

public abstract class Weapon : BaseCombatWeapon
{
	[Property] public Vector3 ViewOffset { get; set; }
	[Property] public float KickDuration { get; set; }
	[Property] public float KickDegrees { get; set; }
	[Property] public float KickDistance { get; set; }
	TimeUntil kickEnds;

	// Loose weapons retain unclaimed ammunition when the recipient reaches the reserve cap.
	[Sync( SyncFlags.FromHost )] int looseReserve { get; set; } = -1;
	int LooseReserve => looseReserve >= 0 ? looseReserve : Clip1 < 0 ? StartingAmmo : 0;
	int LoadedRounds => Clip1 < 0 ? ClipMaxSize : Clip1;

	Weapon MatchingWeapon( BaseInventoryComponent inventory ) =>
		inventory.Items.OfType<Weapon>().FirstOrDefault( w => w != this && w.GetType() == GetType() &&
			w.GameObject.PrefabInstanceSource == GameObject.PrefabInstanceSource );

	protected override bool OnCanPickup( BaseInventoryComponent inventory )
	{
		if ( !UsesClips || !UsesAmmo ) return base.OnCanPickup( inventory );
		if ( LoadedRounds == 0 && LooseReserve == 0 ) return false;
		if ( MatchingWeapon( inventory ) is not {} held ) return base.OnCanPickup( inventory );
		return held.UsesAmmo && held.Ammo1 < held.MaxReserveAmmo;
	}

	int CarriedReserve( BaseInventoryComponent inventory )
	{
		int available = inventory?.GetAmmo( PrimaryAmmoType ) ?? 0;
		return available == int.MaxValue ? 0 : System.Math.Min( available, MaxReserveAmmo - LooseReserve );
	}

	protected override bool OnAdding( BaseInventoryComponent inventory )
	{
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
		looseReserve = reserve - inventory.GiveAmmo( PrimaryAmmoType, reserve );
	}

	protected override void OnRemoved( BaseInventoryComponent inventory )
	{
		base.OnRemoved( inventory );
		// Drop and transfer have already reparented the weapon. Removal for destruction has not.
		if ( Inventory != inventory && UsesClips && UsesAmmo )
			looseReserve = LooseReserve + inventory.TakeAmmo( PrimaryAmmoType, CarriedReserve( inventory ) );
	}

	Transform cameraAdjustment = global::Transform.Zero;

	static float KickWeight( float remaining )
	{
		// The shotgun's fade uses 1024 byte samples of the fourth-power curve.
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

	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( kickEnds > 0 && KickDuration > 0 )
		{
			float durationTicks = KickDuration * 30;
			float elapsedTicks = kickEnds.Passed * 30;
			// A new impulse has full weight until the first tick. The countdown stores whole ticks.
			float weight = elapsedTicks < 1 ? 1 : KickWeight( ((int)durationTicks - elapsedTicks) / durationTicks );
			view.Position -= view.Rotation.Forward * (KickDistance * weight);
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
	}

	protected Halo2Animator HolderAnimator => HolderRenderer is {} body
		&& body.GetComponentInParent<Halo2Animator>( true ) is {} animator && animator.Renderer == body ? animator : null;

	protected void PlayHolderOverlay( string name )
	{
		if ( !Application.IsDedicatedServer ) HolderAnimator?.PlayWeaponOverlay( name );
	}

	protected override void OnShootEffects( ShotEffect shot )
	{
		if ( !shot.NoEvents ) PlayHolderOverlay( "fire_1" );
		if ( !shot.NoEvents && !IsProxy && KickDuration > 0 ) kickEnds = KickDuration;

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
		var muzzle = model?.MuzzleGameObject;
		var emitter = muzzle.IsValid() ? muzzle : GameObject;
		var sound = emitter.PlaySound( AttackSound );
		if ( sound.IsValid() && Holder is PlayerController player && player.Network.IsOwner ) sound.SpacialBlend = 0;
		model?.OnAttack( shot.Hit || shot.HitPosition != default ? shot.HitPosition : null, shot.Origin );
		PostWeaponEvent( WeaponEvents.Attack );
	}

	protected override float GetHitForce( GameObject target, float force )
	{
		var body = target.GetComponentInParent<Rigidbody>();
		if ( !body.IsValid() || target.GetComponentInParent<Sandbox.Npcs.BaseNpc>() is not null
			|| target.GetComponentInParent<PlayerController>() is not null ) return force;

		// The damage tag supplies a velocity change. ApplyImpulseAt takes momentum.
		return force * body.Mass;
	}

	protected override SceneTrace BulletTrace( Ray ray, float distance, float radius ) =>
		Scene.Trace.Ray( ray, distance ).IgnoreGameObjectHierarchy( Holder?.GameObject ?? GameObject )
			.WithCollisionRules( "bullet" ).WithoutTags( "playercontroller" ).Radius( radius ).UseHitboxes();
}
