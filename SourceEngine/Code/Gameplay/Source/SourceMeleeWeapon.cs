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

	static readonly BBox SwingHull = BBox.FromPositionAndSize( Vector3.Zero, 32 );
	static readonly Vector3[] SwingCorners = SwingHull.Corners.ToArray();

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
		var tr = TraceSwing();

		// Hit and miss recover at different speeds: replace the flat cooldown FirePrimary set.
		SetNextPrimaryFire( tr.Hit ? SwingDelay : MissSwingDelay );

		SwingEffects( new ShotEffect( tr.HitPosition, tr.Hit, tr.Normal, tr.GameObject, tr.Surface ) );

		ShootBullet( tr, Damage, SwingForce, hitboxTags: true );
	}

	SceneTraceResult TraceSwing()
	{
		var aim = AimRay;
		var trace = Scene.Trace.Ray( aim, Range )
			.IgnoreGameObjectHierarchy( AimIgnoreRoot )
			.WithoutTags( "playercontroller" )
			.UseHitboxes();
		var line = trace.Run();
		if ( line.Hit ) return line;

		// Stop the box a corner's length short so the wider swing can't extend the reach.
		var end = aim.Position + aim.Forward * Math.Max( 0, Range - SwingHull.Maxs.Length );
		var hull = trace.FromTo( aim.Position, end ).Size( SwingHull ).Run();
		if ( !hull.Hit || !hull.GameObject.IsValid() ) return line;
		if ( (hull.GameObject.WorldPosition - aim.Position).Normal.Dot( aim.Forward ) < 0.70721f ) return line;

		// Refine the contact with a line, then the closest of the eight corner rays.
		end = aim.Position + (hull.EndPosition - aim.Position) * 2;
		line = trace.FromTo( aim.Position, end ).Run();
		if ( line.Hit ) return line;

		var nearest = float.MaxValue;
		foreach ( var corner in SwingCorners )
		{
			var hit = trace.FromTo( aim.Position, end + corner ).Run();
			if ( !hit.Hit ) continue;
			var distance = (hit.EndPosition - aim.Position).LengthSquared;
			if ( distance >= nearest ) continue;
			hull = hit;
			nearest = distance;
		}
		return hull;
	}

	/// <summary>Lets melee weapons relay extra presentation data alongside the hit.</summary>
	protected virtual void SwingEffects( ShotEffect shot ) => ShootEffects( shot );

	protected override void OnShootEffects( ShotEffect shot )
	{
		if ( Application.IsDedicatedServer ) return;

		// A missed swing has no impact point. The trace endpoint would select a hit animation.
		base.OnShootEffects( shot.Hit ? shot : shot with { HitPosition = default } );
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
