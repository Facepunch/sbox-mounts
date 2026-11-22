namespace GoldSrc.HalfLife;

[Title( "Snarks" ), Category( "Half-Life" )]
[WeaponPickup( "squeak", "snarks", 15, ClassName = "snark", PickupModel = "w_sqknest", Bucket = 4, Weight = 5 )]
public sealed class SnarkWeapon : ProjectileWeapon
{
	TimeUntil? _redraw;
	internal override string DeploySequence => "up";
	internal override (string Name, float? Delay) IdleAnimation => Game.Random.Float( 0, 1 ) switch
	{
		<= 0.75f => ("idle1", 60f / 16),
		<= 0.875f => ("fidgetfit", 70f / 16),
		_ => ("fidgetnip", 80f / 16)
	};
	internal override string ShotSequence => "throw";
	internal override string ShotSound => $"squeek/sqk_hunt{Game.Random.Int( 2, 3 )}.wav";
	public override Ray AimRay => Owner.IsValid() ? Owner.EyeTransform.ForwardRay : base.AimRay;

	protected override void OnEquipped()
	{
		base.OnEquipped();
		PlaySample( ShotSound );
	}

	internal override void PlayLaunchSound() => PlaySample( ShotSound, pitch: 1.05f );

	internal override void Configure()
	{
		HoldType = WeaponHolds.Throwable;
		PrimaryDelay = 0.3f;
		UsesClips = false;
		StartingAmmo = 5;
	}

	internal override bool Release( Ray ray )
	{
		var prefab = GameObject.GetPrefab( "mount://hl1/entities/npcs/monster_snark.prefab" );
		if ( !prefab.IsValid() ) return false;
		var aim = Rotation.LookAt( ray.Forward );
		var down = System.Math.Clamp( aim.Angles().pitch / 90, 0, 1 );
		var forward = Vector3.Lerp( ray.Forward, Rotation.FromYaw( aim.Angles().yaw ).Forward, down );
		// GoldSrc traces from the centre of its standing hull, raised when crouching and looking down.
		var origin = Owner.WorldPosition + Vector3.Up * (36 + (Owner.IsDucking ? down * 18 : 0));
		var trace = Scene.Trace.Ray( origin, origin + forward * 24 ).IgnoreGameObjectHierarchy( Owner.GameObject );
		if ( trace.Run().Hit ) return false;
		var clearance = trace.FromTo( origin + forward * 24, origin + ray.Forward * 60 ).Run();
		if ( clearance.StartedSolid || clearance.Fraction <= 0 ) return false;
		var snark = prefab.Clone( new Transform( clearance.EndPosition, Rotation.LookAt( ray.Forward ) ) );
		snark.GetComponent<Snark>().Thrower = Owner.GameObject;
		snark.NetworkSpawn( true, null );
		snark.GetComponent<Snark>().Launch( forward * 200 + Owner.Velocity );
		_redraw = 1;
		return true;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Networking.IsHost || _redraw is not { } due || due > 0 ) return;
		_redraw = null;
		if ( !Inventory.IsValid() ) return;
		if ( Ammo1 == 0 ) Inventory.Remove( this );
		else if ( IsActive ) Redraw();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Redraw() => ViewModel?.GetComponent<StudioWeaponView>()?.Play( DeploySequence, idleAfter: Game.Random.Float( 10, 15 ) );
}
