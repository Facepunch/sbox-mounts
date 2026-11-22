namespace GoldSrc.HalfLife;

[Title( "Gluon Gun" ), Category( "Half-Life" )]
[WeaponPickup( "egon", "uranium", 100, Bucket = 3, Weight = 20 )]
public sealed partial class GluonGun : StudioWeapon
{
	internal override string ShotSequence => $"fire{Game.Random.Int( 1, 4 )}";
	internal override bool CanIdle => !_firing && _idleAfterShutdown <= 0;
	internal override bool SwitchWhenSpent => !_firing;
	internal override (string Name, float? Delay) IdleAnimation =>
		Game.Random.Int( 0, 1 ) == 0 ? ("idle1", Game.Random.Float( 10, 15 )) : ("fidget1", 3);
	SoundHandle _beamSound;
	GluonBeam _beam;
	bool _firing, _running, _firstImpact;
	TimeUntil _nextPulseDeadline, _windupEnds, _idleAfterShutdown;
	TimeUntil _burstExpires, _nextCell, _nextShake;
	internal override Vector3 WorldMuzzleOffset( bool hd ) => hd ? new( 29, -5.2f, 0.5f ) : new( 28.4f, -5.1f, 0 );

	protected override void Think()
	{
		base.Think();
		if ( _burstExpires > 0 && Input.Released( "attack1" ) ) EndAttack( 0.5f );
	}

	public override void PrimaryAttack()
	{
		if ( Owner.IsValid() && WaterBody.Find( Scene, Owner.EyePosition ).IsValid() )
		{
			if ( _burstExpires > 0 ) EndAttack( 0.5f );
			else DryFire();
			return;
		}

		var start = _burstExpires <= 0;
		_burstExpires = PrimaryDelay + 0.15f;
		var hit = BulletTrace( AimRay, Ballistics.Range, 0 ).Run();
		var multiplayer = Networking.MaxPlayers > 1;
		if ( start )
		{
			_nextCell = 0;
			_firstImpact = true;
		}
		else if ( !hit.StartedSolid )
		{
			if ( hit.Hit )
			{
				var damage = WeaponDamage.ForTarget( Holder?.GameObject, hit.GameObject, 14, 20 );
				ShootBullet( hit, damage, 0 );
				// Keep the blast's visibility traces outside the surface, including sloped ceilings.
				ImpactPulse( hit.EndPosition + hit.Normal * 2, _firstImpact );
				_firstImpact = false;
			}
			if ( IsHeld && _nextCell <= 0 )
			{
				TakePrimaryAmmo();
				_nextCell = multiplayer ? 0.2f : 0.1f;
			}
		}
		ShootEffects( new ShotEffect( hit.EndPosition, hit.Hit, hit.Normal, hit.GameObject, hit.Surface, NoEvents: !start ) );
		if ( IsHeld && !HasPrimaryAmmo() ) EndAttack( 1 );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void ImpactPulse( Vector3 point, bool firstImpact )
	{
		if ( !Owner.IsValid() || !IsActive ) return;
		if ( Networking.MaxPlayers > 1 )
			Blast.DamageInRadius( GameObject, Owner.GameObject, point, 0, 128, 5, "energybeam", "blast", "alwaysgib" );
		if ( !firstImpact && _nextShake > 0 ) return;
		GroundShake.Emit( Scene, point, 5, 150, 0.75f, 250 );
		_nextShake = 1.5f;
	}

	public override void DryFire()
	{
		PlaySample( "weapons/357_cock1.wav", 0.8f );
		SetNextFire( 0.25f );
	}

	void EndAttack( float delay )
	{
		_burstExpires = 0;
		SetNextFire( delay );
		EndBeamEffects();
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void EndBeamEffects() => StopBeam();

	protected override void OnShootEffects( ShotEffect shot )
	{
		base.OnShootEffects( shot );
		if ( Application.IsDedicatedServer ) return;
		_nextPulseDeadline = PrimaryDelay + 0.15f;
		if ( _firing ) return;
		_firing = true;
		_windupEnds = 2;
		_beamSound?.Stop();
		_beamSound = PlaySample( "weapons/egon_windup2.wav", 0.98f, 1.25f );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !_firing ) return;
		if ( _nextPulseDeadline <= 0 ) { StopBeam(); return; }
		if ( !_running && _windupEnds <= 0 )
		{
			_beamSound?.Stop();
			_running = true;
			_beamSound = PlaySample( "weapons/egon_run3.wav", 0.98f, 1.25f );
			ViewModel?.GetComponent<StudioWeaponView>()?.Play( ShotSequence );
		}
		if ( _beamSound.IsValid() ) _beamSound.Position = GetMuzzleTransform().Position;
	}

	protected override void OnHolstered()
	{
		_burstExpires = 0;
		StopBeam();
		base.OnHolstered();
	}

	protected override void OnDisabled()
	{
		_burstExpires = 0;
		StopBeam();
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		_beamSound?.Stop();
		base.OnDestroy();
	}

	async void StopBeam()
	{
		StopBeamVisual();
		if ( !_firing ) return;
		_firing = _running = false;
		_idleAfterShutdown = 2;
		_beamSound?.Stop();
		if ( GameObject.IsDestroyed || Scene.IsDestroyed ) return;
		var shutdown = _beamSound = PlaySample( "weapons/egon_off1.wav", 0.98f );
		if ( !shutdown.IsValid() ) return;
		// A muted mixer can stop advancing the voice. Release it even while holstered.
		var file = shutdown.SoundFile;
		if ( await file.LoadAsync() && shutdown == _beamSound )
			await GameTask.DelayRealtimeSeconds( file.Duration );
		if ( shutdown != _beamSound ) return;
		shutdown.Stop();
		_beamSound = null;
	}

	void StopBeamVisual()
	{
		if ( _beam.IsValid() ) _beam.Stop();
		_beam = null;
	}

	internal override void DrawShotTrail( Vector3 start, Vector3 end )
	{
		if ( !_beam.IsValid() ) _beam = GluonBeam.Start( this, end );
	}

	protected override void OnShootImpact( in ShotEffect shot )
	{
		var showFlare = Networking.MaxPlayers <= 1 || (shot.HitObject.IsValid() && shot.HitObject.GetComponentInParent<Component.IDamageable>() is not null);
		_beam?.Aim( shot.HitPosition, showFlare );
	}

	internal override void Configure()
	{
		HoldType = WeaponHolds.Rifle;
		PrimaryDelay = 0.1f;
		UsesClips = false;
		StartingAmmo = 20;
		Ballistics = new() { Damage = 14, Range = 2048 };
		AttackDamageTags = ["energybeam", "alwaysgib"];
	}
}
