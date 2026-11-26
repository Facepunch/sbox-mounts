using System;

namespace Source1;

/// <summary>A mounted weapon that is swung: a short trace from the aim, then a fatter one when that misses.</summary>
[Title( "Source Melee Weapon" )]
[Category( "Source 1" )]
[Alias( "Source1.SourceMeleeWeapon", "SourceMeleeWeapon" )]
public class MeleeWeapon : Weapon
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

	/// <summary>Each point of melee damage pushes a 75 kg body by four units per second.</summary>
	const float ImpulsePerDamage = 75 * 4;

	[Property] public SoundEvent HitSound { get; set; }

	public MeleeWeapon()
	{
		// Melee doesn't use ammo: the engine default is on, which would dry-fire an empty clip.
		UsesAmmo = false;
		AttackDamageTags = [DamageTags.Melee];
	}

	// The secondary input consumes the weapon's idle frame, even though it has no attack.
	internal override bool ShouldPlayIdle => !Input.Down( "attack2" );

	public bool CanAttack() => NextPrimaryFire <= 0;

	protected override void OnEquipped()
	{
		base.OnEquipped();
		_ = WeaponSetup.ConfigureWeapon( this );
	}

	public override bool CanSecondaryAttack() => false;

	/// <summary>
	/// The swing. Runs once on the owning client: our trace decides what we hit, the engine claims it to the host,
	/// and the host applies the damage.
	/// </summary>
	public override void PrimaryAttack()
	{
		var aim = AimRay;
		var tr = TraceSwing( aim );

		// Hit and miss recover at different speeds: replace the flat cooldown FirePrimary set.
		SetNextPrimaryFire( tr.Hit ? SwingDelay : MissSwingDelay );

		SwingEffects( new ShotEffect( tr.Hit ? tr.HitPosition : aim.Project( Range ), tr.Hit, tr.Normal, tr.GameObject, ImpactSurface( tr ), aim.Position ) );

		tr.Direction = aim.Forward;
		var damage = Damage;
		if ( tr.GameObject.IsValid() && tr.GameObject.GetComponentInParent<IDamageable>() is Prop prop )
			damage *= prop.Model?.GetData<PropDamageModifiers>()?.Club ?? 1;
		ShootBullet( tr, damage, Damage * ImpulsePerDamage, hitboxTags: false );
	}

	SceneTraceResult TraceSwing( Ray aim )
	{
		// Source swings use MASK_SHOT_HULL, which hits the player hull without hitgroups.
		var trace = Scene.Trace.Ray( aim, Range ).IgnoreGameObjectHierarchy( AimIgnoreRoot );
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
		if ( Application.IsDedicatedServer )
		{
			base.OnShootEffects( shot );
			return;
		}

		if ( !shot.NoEvents && AttackSound is not null )
		{
			var swing = Sounds.PlayWeaponSound( this, AttackSound );
			if ( swing.IsValid() && Network.IsOwner ) swing.SpacialBlend = 0;
		}

		if ( shot.Hit )
		{
			if ( HitSound is not null ) Sounds.PlayWeaponSound( this, HitSound );
			// A peer can receive the hit before it knows the mounted surface. Resolve it on the struck object.
			if ( !shot.Surface.IsValid() && shot.HitObject.IsValid() )
			{
				var start = shot.Origin ?? shot.HitPosition + shot.Normal * 8;
				var end = shot.HitPosition + (shot.HitPosition - start).Normal * 8;
				var contact = Scene.Trace.Ray( start, end ).IgnoreGameObjectHierarchy( AimIgnoreRoot ).Run();
				if ( contact.GameObject == shot.HitObject ) shot.Surface = ImpactSurface( contact );
			}
			OnShootImpact( shot );
		}
		else WaterImpact( shot );

		if ( shot.NoEvents ) return;
		WeaponModel?.OnAttack( shot.Hit ? shot.HitPosition : null, shot.Origin );
		PostWeaponEvent( WeaponEvents.Attack );
	}

	bool WaterImpact( in ShotEffect shot )
	{
		if ( shot.Origin is not { } start ) return false;
		foreach ( var map in Scene.GetAll<SourceWaterMap>() )
		{
			if ( !map.Active || map.FindWater( shot.HitPosition ) < 0 || map.FindWater( start ) >= 0 ) continue;
			var entry = Scene.Trace.Ray( start, shot.HitPosition ).WithTag( "water" ).HitTriggersOnly().Run();
			if ( !entry.Hit ) continue;
			WeaponEffects.WaterSplash( entry.HitPosition, MountContent.OfWeapon( this ), entry.GameObject.Tags.Has( "slime" ) );
			return true;
		}
		return false;
	}

	protected override void OnShootImpact( in ShotEffect shot )
	{
		if ( !WaterImpact( shot ) ) SurfaceImpact( shot, MountContent.OfWeapon( this ) );
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
