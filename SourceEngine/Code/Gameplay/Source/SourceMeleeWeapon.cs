using System;

namespace Source1;

/// <summary>A mounted weapon that is swung: a short trace from the aim, then a fatter one when that misses.</summary>
[Title( "Source Melee Weapon" )]
[Category( "Source 1" )]
public class SourceMeleeWeapon : SourceWeapon
{
	/// <summary>Cooldown after a hit connects.</summary>
	[Property] public float SwingDelay { get; set; } = 0.5f;

	/// <summary>Cooldown after a swing misses.</summary>
	[Property] public float MissSwingDelay { get; set; } = 0.75f;

	/// <summary>Damage dealt per hit.</summary>
	[Property] public float Damage { get; set; } = 12f;

	/// <summary>Reach of the swing trace.</summary>
	[Property] public float Range { get; set; } = 128f;

	/// <summary>Radius of the swing trace sphere.</summary>
	[Property] public float SwingRadius { get; set; } = 10f;

	/// <summary>Physics impulse magnitude applied to hit objects.</summary>
	[Property] public float SwingForce { get; set; } = 1000f;

	[Property] public SoundEvent HitSound { get; set; }

	public SourceMeleeWeapon()
	{
		// Melee doesn't use ammo: the engine default is on, which would dry-fire an empty clip.
		UsesAmmo = false;
		AttackDamageTags = [SourceDamageTags.Melee];
	}

	public bool CanAttack() => NextPrimaryFire <= 0;

	public override bool CanSecondaryAttack() => false;

	/// <summary>
	/// The swing. Runs once on the owning client: our trace decides what we hit, the engine claims it to the host,
	/// and the host applies the damage.
	/// </summary>
	public override void PrimaryAttack()
	{
		var trace = Scene.Trace.Ray( AimRay, Range )
			.IgnoreGameObjectHierarchy( AimIgnoreRoot )
			.WithoutTags( "playercontroller" )
			.UseHitboxes();

		var tr = trace.Run();
		if ( !tr.Hit )
			tr = trace.Radius( SwingRadius ).Run();

		// Hit and miss recover at different speeds: replace the flat cooldown FirePrimary set.
		SetNextPrimaryFire( tr.GameObject.IsValid() ? SwingDelay : MissSwingDelay );

		SwingEffects( new ShotEffect( tr.HitPosition, tr.Hit, tr.Normal, tr.GameObject, tr.Surface ) );

		ShootBullet( tr, Damage, SwingForce, hitboxTags: true );

		KickView();
	}

	/// <summary>Lets melee weapons relay extra presentation data alongside the hit.</summary>
	protected virtual void SwingEffects( ShotEffect shot ) => ShootEffects( shot );

	protected override void OnShootEffects( ShotEffect shot )
	{
		if ( Application.IsDedicatedServer ) return;

		base.OnShootEffects( shot );
	}

	protected override void OnShootImpact( in ShotEffect shot )
	{
		if ( !shot.Hit || !shot.HitObject.IsValid() )
			return;

		// A melee thunk instead of the surface's bullet ricochet, so the impact prefab is spawned soundless.
		shot.HitObject.PlaySound(
			shot.Surface.SoundCollection.ImpactHard ?? shot.Surface.GetBaseSurface()?.SoundCollection.ImpactHard ?? HitSound,
			shot.HitObject.WorldTransform.PointToLocal( shot.HitPosition ) );

		ImpactPrefab( shot.HitObject, shot.Surface, shot.HitPosition, shot.Normal, GetShotDirection( shot ) );
	}

	/// <summary>Draws the aiming dot with the current attack availability.</summary>
	public override void DrawCrosshair( Painter painter, Vector2 center )
	{
		using var scope = painter.Scope();
		painter.BlendMode = BlendMode.Lighten;
		painter.Fill = CanAttack() ? Color.White : Color.Red;
		painter.Stroke = Stroke.None;
		painter.Circle( center, 3f );
	}
}
