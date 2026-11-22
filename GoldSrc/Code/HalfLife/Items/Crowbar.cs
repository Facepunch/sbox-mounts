namespace GoldSrc.HalfLife;

[Title( "Crowbar" ), Category( "Half-Life" )]
[WeaponPickup( "crowbar", null, 0, Bucket = 0, Weight = 0 )]
public sealed class Crowbar : StudioWeapon
{
	static readonly BBox HeadHull = new( new Vector3( -16, -16, -18 ), new Vector3( 16, 16, 18 ) );
	TimeUntil? _retry, _decalDelay;
	TimeUntil _fullDamageAt;
	Vector3 _decalPoint, _decalNormal;
	int _swing, _hit;

	internal override void Configure()
	{
		HoldType = WeaponHolds.Melee;
		PrimaryDelay = 0.5f;
		UsesClips = false;
		UsesAmmo = false;
		AttackDamageTags = ["club"];
		Ballistics = new() { Damage = 10, Range = 32, Radius = 0 };
	}

	public override void PrimaryAttack() => Swing( true );
	public override Ray AimRay => Holder switch
	{
		PlayerController player => player.EyeTransform.ForwardRay,
		{ } holder => holder.CalculateAimRay( this ),
		null => UnheldAimRay
	};

	void Swing( bool first )
	{
		var hit = TraceSwing( AimRay );
		if ( hit.Hit )
		{
			var normalDamage = Ballistics.Damage * (_fullDamageAt <= 0 ? 1 : 0.5f);
			var damage = WeaponDamage.ForTarget( Holder?.GameObject, hit.GameObject, normalDamage, 25 );
			ShootBullet( hit, damage, 0, hitboxTags: false );
			SetNextPrimaryFire( 0.25f );
			_fullDamageAt = 1.25f;
		}
		else if ( first )
		{
			_retry = 0.1f;
			_fullDamageAt = 1 + PrimaryDelay;
		}
		else return;

		ShootEffects( new ShotEffect( hit.EndPosition, hit.Hit, hit.Normal, hit.GameObject, hit.Surface, NoEvents: !first ) );
	}

	SceneTraceResult TraceSwing( Ray aim )
	{
		var trace = BulletTrace( aim, Ballistics.Range, 0 );
		var line = trace.Run();
		if ( line.Hit ) return line;
		var hull = trace.Size( HeadHull ).Run();
		if ( !hull.Hit || hull.GameObject?.GetComponentInParent<IDamageable>() is not null ) return hull;

		// FindHullIntersection resolves a hull hit to a point on the actual surface.
		var end = aim.Position + (hull.EndPosition - aim.Position) * 2;
		line = trace.FromTo( aim.Position, end ).Run();
		if ( line.Hit ) return line;
		var nearest = float.MaxValue;
		foreach ( var corner in HeadHull.Corners )
		{
			line = trace.FromTo( aim.Position, end + corner ).Run();
			if ( !line.Hit || line.Distance >= nearest ) continue;
			nearest = line.Distance;
			hull = line;
		}
		return hull;
	}

	protected override void Think()
	{
		base.Think();
		if ( _retry is not { } due || due > 0 ) return;
		_retry = null;
		Swing( false );
	}

	protected override void OnShootEffects( ShotEffect shot )
	{
		PostWeaponEvent( WeaponEvents.Attack );
		if ( Application.IsDedicatedServer ) return;
		var view = ViewModel?.GetComponent<StudioWeaponView>();
		if ( !shot.NoEvents )
		{
			PlaySample( "weapons/cbar_miss1.wav" );
			view?.Play( (_swing++ % 3) switch { 0 => "attack1miss", 1 => "attack2", _ => "attack3" } );
		}
		if ( !shot.Hit ) return;
		view?.Play( (_hit++ & 1) == 0 ? "attack2hit" : "attack3hit" );
		if ( HitFlesh( shot ) )
		{
			PlaySample( $"weapons/cbar_hitbod{Game.Random.Int( 1, 3 )}.wav" );
			return;
		}
		var volume = PlaySurfaceImpact( shot );
		if ( volume > 0 )
			PlaySample( $"weapons/cbar_hit{Game.Random.Int( 1, 2 )}.wav", volume, Game.Random.Int( 98, 101 ) / 100f );
		_decalPoint = shot.HitPosition;
		_decalNormal = shot.Normal;
		_decalDelay = 0.2f;
	}

	static float PlaySurfaceImpact( ShotEffect shot )
	{
		if ( Networking.MaxPlayers > 1 || Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ) return 1;
		var material = mount.GetMaterialType( shot.Surface );
		if ( material == 'F' ) return 0;
		if ( mount.GetSurfaceSound( material, "bullet" ) is { } impact ) Sound.Play( impact, shot.HitPosition );
		if ( material == 'P' && Game.Random.Int( 0, 1 ) == 1 )
		{
			StudioSparks.Emit( shot.HitPosition, 6 );
			NpcAudio.Play( mount.NpcAudio.Find( $"buttons/spark{Game.Random.Int( 5, 6 )}.wav" ), shot.HitPosition, Game.Random.Float( 0.7f, 1 ) );
		}
		return material switch
		{
			'M' or 'V' => 0.3f,
			'D' => 0.1f,
			'G' => 0.5f,
			'T' or 'W' or 'P' or 'Y' or 'U' => 0.2f,
			'S' => 0,
			_ => 0.6f
		};
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( _decalDelay is not { } due || due > 0 ) return;
		_decalDelay = null;
		ImpactDecal.Place( _decalPoint, _decalNormal, $"shot{Game.Random.Int( 1, 5 )}", 10 );
	}

	protected override void OnHolstered()
	{
		_retry = _decalDelay = null;
		base.OnHolstered();
	}
}
