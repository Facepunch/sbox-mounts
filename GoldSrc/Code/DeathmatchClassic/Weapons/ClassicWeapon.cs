using GoldSrc.HalfLife;

namespace GoldSrc.DeathmatchClassic;

[Hide]
public abstract class ClassicWeapon : ProjectileWeapon
{
	internal override string DeploySequence => "idle";
	internal override (string Name, float? Delay) IdleAnimation => (DeploySequence, null);
	internal override bool SwitchWhenSpent => UsesAmmo;
	internal override void Configure()
	{
		UsesClips = false;
		UsesAmmo = Definition.Ammo is not null;
		HoldType = WeaponHolds.Rifle;
		Ballistics = new() { Range = 2048, Radius = 0 };
	}

	protected SceneTraceResult Trace( Vector3 start, Vector3 end ) =>
		Scene.Trace.Ray( start, end ).IgnoreGameObjectHierarchy( Owner.GameObject )
			.WithCollisionRules( "bullet" ).WithoutTags( "playercontroller", "trigger" ).UseHitboxes().Run();

	protected void Hit( SceneTraceResult hit, float amount, string tag )
	{
		if ( hit.GameObject?.GetComponentInParent<IDamageable>() is not { } victim ) return;
		var damage = new DamageInfo { Damage = amount, Origin = Owner.EyePosition, Position = hit.EndPosition,
			Attacker = Owner.GameObject, Weapon = GameObject, Hitbox = hit.Hitbox };
		damage.Tags.Add( tag );
		victim.OnDamage( damage );
	}

	protected void LaunchMissile<T>( string model, Ray ray, float speed, float damage ) where T : NpcProjectile, new() =>
		NpcProjectile.Fire<T>( Owner.GameObject, $"mount://dmc/dmc/models/{model}.mdl.vmdl", damage, null, ray.Position, ray.Forward, speed );
}
