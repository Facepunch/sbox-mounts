namespace Diesel;

/// <summary>Playable mounted firearm using the engine inventory, ammo, damage and replication paths.</summary>
[Title( "PAYDAY Weapon" ), Group( "PAYDAY" )]
public sealed class PaydayWeapon : BaseCombatWeapon
{
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
		base.PrimaryAttack();
		if ( Owner.IsValid() && !Owner.IsProxy )
		{
			Owner.EyeAngles += new Angles( -0.3f, Random.Shared.Float( -0.7f, 0.7f ), 0 );
			if ( !Owner.ThirdPerson ) Scene.Camera?.AddPunch( new Angles( -0.5f, 0, 0 ), 2, 0.15f );
		}
	}

	protected override void PlaceViewModel( CameraComponent camera, in CameraView view )
	{
		if ( !ViewModel.IsValid() ) return;
		// PDTH's default/M4 shoulder pivot relative to the first-person camera.
		var shoulder = Coordinates.Position( new Vector3( 8.40486f, 33.2872f, -0.76365f ) );
		var head = Coordinates.Position( new Vector3( 7, 16, -0.5f ) );
		var rotation = Rotation.From( -1, 0, 0 ) * Rotation.From( -0.644497f, 0.151694f, -1.1892f ).Inverse;
		ViewModel.WorldPosition = view.Position + view.Rotation * (head - rotation * shoulder);
		ViewModel.WorldRotation = view.Rotation * rotation;
	}
}
