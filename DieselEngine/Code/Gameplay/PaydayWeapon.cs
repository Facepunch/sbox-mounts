namespace Diesel;

/// <summary>Playable mounted firearm using the engine inventory, ammo, damage and replication paths.</summary>
[Title( "PAYDAY Weapon" ), Group( "PAYDAY" )]
public sealed class PaydayWeapon : BaseCombatWeapon
{
	[Property] public string WeaponId { get; set; } = "amcar4";
	internal WeaponDefinition Definition => WeaponDefinition.Get( WeaponId );
	bool reloadCancelled;
	[Property] public GameObject DroppedModel { get; set; }
	[Property] public float EmptyReloadTime { get; set; } = 1.9f;
	[Property] public float TacticalReloadTime { get; set; } = 1.4f;

	public override bool CanPrimaryAttack() => IsHeld && base.CanPrimaryAttack();
	public override bool CanSecondaryAttack() => false;
	public override bool CanReload() => IsHeld && base.CanReload();

	protected override void OnAdded( BaseInventoryComponent inventory )
	{
		base.OnAdded( inventory );
		if ( DroppedModel.IsValid() ) DroppedModel.Enabled = false;
	}

	protected override void OnRemoved( BaseInventoryComponent inventory )
	{
		base.OnRemoved( inventory );
		if ( DroppedModel.IsValid() ) DroppedModel.Enabled = true;
	}

	public override void Reload()
	{
		ReloadTime = Clip1 == 0 ? EmptyReloadTime : TacticalReloadTime;
		base.Reload();
	}
	protected override void OnReloadStarted()
	{
		reloadCancelled = false;
		base.OnReloadStarted();
	}
	protected override void OnReloadCancelled()
	{
		reloadCancelled = true;
		base.OnReloadCancelled();
	}
	protected override void OnReloadFinished()
	{
		if ( Definition.Shotgun ) SetNextPrimaryFire( reloadCancelled ? .3f : 1f );
		base.OnReloadFinished();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( DroppedModel.IsValid() ) DroppedModel.Enabled = !IsHeld;
	}

	protected override SceneTrace BulletTrace( Ray ray, float distance, float radius ) =>
		Scene.Trace.Ray( ray, distance ).IgnoreGameObjectHierarchy( Holder?.GameObject ?? GameObject )
			.WithCollisionRules( "bullet" ).WithoutTags( "playercontroller" ).Radius( radius ).UseHitboxes();

	public override void PrimaryAttack()
	{
		if ( Definition.Grenade )
		{
			if ( !TakePrimaryAmmo() ) return;
			TimeSinceShoot = 0;
			if ( Networking.IsHost ) SpawnGrenade();
			else LaunchGrenade();
			ShootEffects();
		}
		else if ( Definition.Shotgun ) FireShotgun();
		else base.PrimaryAttack();
		if ( Owner.IsValid() && !Owner.IsProxy )
		{
			Owner.EyeAngles += new Angles( -0.3f, Random.Shared.Float( -0.7f, 0.7f ), 0 );
			if ( !Owner.ThirdPerson ) Scene.Camera?.AddPunch( new Angles( -0.5f, 0, 0 ), 2, 0.15f );
		}
	}

	void FireShotgun()
	{
		if ( !TakePrimaryAmmo() ) return;
		var hits = new HashSet<IDamageable>();
		var effects = new ShotEffect[Ballistics.Pellets];
		for ( int i = 0; i < effects.Length; i++ )
		{
			var ray = AimRay with { Forward = AimRay.Forward.WithAimCone( CurrentSpread.x, CurrentSpread.y ) };
			var tr = BulletTrace( ray, Ballistics.Range, 0 ).Run();
			var target = tr.GameObject?.GetComponentInParent<IDamageable>();
			// Diesel shotguns damage each enemy once per shell, not once per pellet.
			if ( target is null || hits.Add( target ) )
			{
				float distance = tr.StartPosition.Distance( tr.EndPosition );
				float falloff = 1 - Math.Clamp( (distance - 500 / 2.54f) / Ballistics.Range, 0, 1 );
				ShootBullet( tr, Ballistics.Damage * falloff, Ballistics.Force );
			}
			effects[i] = new ShotEffect( tr.EndPosition, tr.Hit, tr.Normal, tr.GameObject, tr.Surface, NoEvents: i > 0 );
		}
		TimeSinceShoot = 0;
		ShootEffects( effects );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void LaunchGrenade() => SpawnGrenade();
	void SpawnGrenade()
	{
		if ( !Definition.Grenade || !IsHeld ) return;
		using var scope = Scene.Push();
		var grenade = new GameObject( true, "GL40 grenade" );
		grenade.WorldPosition = AimRay.Position;
		grenade.WorldRotation = Rotation.LookAt( AimRay.Forward );
		grenade.AddComponent<ModelRenderer>().Model = Model.Load( "mount://payday/units/weapons/m79/grenade.unit.vmdl" );
		var projectile = grenade.AddComponent<PaydayGrenade>();
		projectile.Attacker = Attacker;
		projectile.Weapon = GameObject;
		projectile.Velocity = AimRay.Forward * (4000 / 2.54f);
		grenade.NetworkSpawn();
	}

	protected override void PlaceViewModel( CameraComponent camera, in CameraView view )
	{
		if ( !ViewModel.IsValid() ) return;
		var stance = PaydayStance.Get( WeaponId );
		ViewModel.WorldPosition = view.Position + view.Rotation * stance.Position;
		ViewModel.WorldRotation = view.Rotation * stance.Rotation;
	}
}
