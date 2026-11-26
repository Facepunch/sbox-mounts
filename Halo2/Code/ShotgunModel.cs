using System;

namespace Halo2;

[Hide]
public partial class ShotgunModel : BaseWeaponModel
{
	internal Shotgun Weapon { get; set; }
	// The visible model receives weapon events. The local arms graph owns the animation cues in either view.
	ShotgunViewModel LocalView => Weapon?.ViewModel?.GetComponent<ShotgunViewModel>();

	public override void OnReloadStart()
	{
		base.OnReloadStart();
		LocalView?.OnReloadStart();
	}

	public override void OnIncrementalReload() => LocalView?.OnIncrementalReload();

	public override void OnReloadFinish()
	{
		base.OnReloadFinish();
		LocalView?.OnReloadFinish();
	}

	public override void OnReloadCancel() => LocalView?.OnReloadCancel();

	ParticleEffect casings;
	Transform ejectionMarker;
	bool firstPersonEjection;
	double shotStart;
	int nextParticleTick;
	uint seed = (uint)Random.Shared.Next();

	ParticleEffect muzzleFlash, shortFlare, longFlare, fierySmoke;
	internal void ConfigureMuzzleFlash( bool firstPerson )
	{
		if ( !MuzzleGameObject.IsValid() ) return;
		var texture = Texture.Load( $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.PrefabPath}/{ShotgunLoader.MuzzleFlashName}" );
		if ( !texture.IsValid() || texture.IsError ) return;

		var go = new GameObject( MuzzleGameObject, false, "Muzzle flash" );
		muzzleFlash = go.AddComponent<ParticleEffect>();
		muzzleFlash.MaxParticles = firstPerson ? 2 : 1;
		muzzleFlash.Lifetime = firstPerson ? 0.125f : 0.2f;
		muzzleFlash.LocalSpace = 1;
		muzzleFlash.ApplyShape = true;
		muzzleFlash.SnapToFrame = true;
		muzzleFlash.InitialScale = firstPerson ? 3 : 12;
		// The 60 x 62 flash frame spans two particle radii vertically.
		muzzleFlash.Stretch = 62f / 60f - 1;
		muzzleFlash.Scale = firstPerson ? new ParticleFloat( 1, 4 ) { Evaluation = ParticleFloat.EvaluationType.Life }
			: LifeCurve( new Curve.Frame( 0, 0.3944444f, 0, 1.132075f ), new Curve.Frame( 1, 0.9055555f, 0.539683f, 0 ) );
		muzzleFlash.ApplyColor = true;
		muzzleFlash.Tint = firstPerson ? Color.FromBytes( 252, 188, 101 ) : Color.FromBytes( 251, 206, 123 );
		muzzleFlash.ApplyAlpha = true;
		muzzleFlash.Alpha = firstPerson ? new ParticleFloat( 1, 0 ) { Evaluation = ParticleFloat.EvaluationType.Life }
			: FadeAfterPlateau( 5.00483f );
		MirrorBillboards( muzzleFlash );
		var renderer = go.AddComponent<ParticleSpriteRenderer>();
		renderer.Sprite = Sprite.FromTexture( texture );
		renderer.Scale = 2 * 60f / 62f;
		renderer.Additive = true;
		renderer.FogStrength = 0;
		renderer.RenderOptions.Game = !firstPerson;
		renderer.RenderOptions.Overlay = firstPerson;
		go.Enabled = true;
		fierySmoke = CreateFierySmoke( firstPerson );
		ConfigureSoftSmoke( firstPerson );
		ConfigureSparks( firstPerson );
		if ( firstPerson ) return;
		var flareTexture = Texture.Load( $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.PrefabPath}/{ShotgunLoader.LongFlashName}" );
		if ( !flareTexture.IsValid() || flareTexture.IsError ) return;
		var flareSprite = Sprite.FromTexture( flareTexture );
		// The long edge starts at the marker and extends along the barrel.
		flareSprite.Animations[0].Origin = new Vector2( 1, 0.5f );
		shortFlare = CreateFlare( flareSprite, true );
		longFlare = CreateFlare( flareSprite, false );
	}

	ParticleEffect CreateFlare( Sprite sprite, bool shortReach )
	{
		var go = new GameObject( MuzzleGameObject, false, shortReach ? "Short muzzle flare" : "Long muzzle flare" );
		if ( shortReach ) go.LocalPosition = Vector3.Backward * 1.2f;
		var flare = go.AddComponent<ParticleEffect>();
		flare.MaxParticles = 1;
		flare.Lifetime = shortReach ? 0.2f : 0.15f;
		flare.ApplyShape = true;
		flare.SnapToFrame = true;
		flare.InitialScale = shortReach ? 12 : 30;
		flare.Scale = shortReach
			? LifeCurve( new Curve.Frame( 0, 0.3888889f, 0, 1.136174f ), new Curve.Frame( 1, 0.777778f, 1.11111f, 0 ) )
			: new ParticleFloat( 0, 1 ) { Evaluation = ParticleFloat.EvaluationType.Life };
		flare.ApplyColor = true;
		flare.Tint = shortReach ? Color.FromBytes( 251, 206, 123 ) : Color.FromBytes( 231, 159, 80 );
		flare.ApplyAlpha = true;
		flare.Alpha = FadeAfterPlateau( shortReach ? 4.896553f : 1.414634f );
		var renderer = go.AddComponent<ParticleSpriteRenderer>();
		renderer.Sprite = sprite;
		renderer.Alignment = ParticleSpriteRenderer.BillboardAlignment.Particle;
		renderer.Additive = true;
		renderer.FogStrength = 0;
		flare.OnParticleCreated += p => OrientFlare( flare, p );
		flare.OnPostStep += _ =>
		{
			foreach ( var p in flare.Particles ) OrientFlare( flare, p );
		};
		go.Enabled = true;
		return flare;
	}

	void OrientFlare( ParticleEffect flare, Particle p )
	{
		if ( !Scene.Camera.IsValid() ) return;
		var axis = flare.WorldRotation.Forward;
		var up = Vector3.Cross( Scene.Camera.View.Position - flare.WorldPosition, axis );
		if ( up.LengthSquared < 0.000001f ) up = flare.WorldRotation.Up;
		up = up.Normal;
		p.Angles = Rotation.LookAt( Vector3.Cross( axis, up ), up ).Angles();
		float radius = p.StartScale * flare.Scale.Evaluate( p.LifeDelta, 0 );
		// The original quad spans four radii along the barrel and 74/49 across it.
		p.Size = new Vector2( 4 * radius, (p.Rand( 2 ) < 0.5f ? -1 : 1) * (74f / 49f) * radius );
		p.Position = flare.WorldPosition + up * (radius / 49f);
	}

	ParticleEffect CreateFierySmoke( bool firstPerson )
	{
		var texture = Texture.Load( $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.PrefabPath}/{ShotgunLoader.FierySmokeName}" );
		if ( !texture.IsValid() || texture.IsError ) return null;
		var go = new GameObject( MuzzleGameObject, false, "Fiery muzzle smoke" );
		var smoke = go.AddComponent<ParticleEffect>();
		smoke.MaxParticles = 1;
		smoke.LocalSpace = 1;
		smoke.Lifetime = 0.2f;
		smoke.ApplyShape = true;
		smoke.InitialScale = firstPerson ? 9 : 18;
		smoke.Scale = firstPerson
			? LifeCurve( new Curve.Frame( 0, 0.3944444f, 0, 0.9005849f ), new Curve.Frame( 1, 0.688889f, 1.414141f, 0 ) )
			: LifeCurve( new Curve.Frame( 0, 0.2166667f, 0, 1.274854f ), new Curve.Frame( 1, 1, -0.20202f, 0 ) );
		smoke.ApplyAlpha = true;
		smoke.Alpha = firstPerson ? new ParticleFloat( 1, 0 ) { Evaluation = ParticleFloat.EvaluationType.Life }
			: FadeAfterPlateau( 2.519775f );
		smoke.ApplyColor = true;
		smoke.Tint = Color.FromBytes( 245, 168, 90 );
		smoke.ApplyRotation = true;
		smoke.InitialRoll = new ParticleFloat( 0, 360 );
		MirrorBillboards( smoke );
		var renderer = go.AddComponent<ParticleSpriteRenderer>();
		renderer.Sprite = Sprite.FromTexture( texture );
		renderer.Scale = 2;
		renderer.Additive = true;
		renderer.FogStrength = 0;
		renderer.RenderOptions.Game = !firstPerson;
		renderer.RenderOptions.Overlay = firstPerson;
		go.Enabled = true;
		return smoke;
	}

	static void MirrorBillboards( ParticleEffect effect )
	{
		effect.OnParticleCreated += OrientFlash;
		effect.OnPostStep += _ =>
		{
			foreach ( var particle in effect.Particles ) OrientFlash( particle );
		};
	}

	static void OrientFlash( Particle particle )
	{
		// A half-turn mirrors both axes. Reflecting X supplies the other two orientations
		// while keeping the taller billboard axis positive for the renderer's bounds.
		bool flipX = particle.Rand( 1 ) < 0.5f;
		bool flipY = particle.Rand( 2 ) < 0.5f;
		particle.Angles.roll = particle.StartAngles.roll + (flipY ? 180 : 0);
		particle.Size.x = (flipX != flipY ? -1 : 1) * System.MathF.Abs( particle.Size.x );
	}

	static ParticleFloat FadeAfterPlateau( float cubic )
	{
		// Clamp the opacity cubic at one until its falling segment crosses it.
		float start = (cubic - 1) / cubic;
		return LifeCurve( new Curve.Frame( 0, 1 ), new Curve.Frame( start, 1, 0, -cubic * start * start ),
			new Curve.Frame( 1, 0, cubic + 2, 0 ) );
	}

	static ParticleFloat LifeCurve( params Curve.Frame[] frames ) => new()
	{
		Type = ParticleFloat.ValueType.Curve,
		Evaluation = ParticleFloat.EvaluationType.Life,
		CurveA = new Curve( frames )
	};

	ParticleEffect CreateCasings()
	{
		var texture = Texture.Load( $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.PrefabPath}/{ShotgunLoader.CasingName}" );
		if ( !texture.IsValid() || texture.IsError ) return null;
		var go = Scene.CreateObject( false );
		go.Name = "Shotgun casings";
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.WorldTransform = ShellEjectGameObject.WorldTransform;
		var casings = go.AddComponent<ParticleEffect>();
		casings.StartDelay = 14f / 30;
		casings.Lifetime = new ParticleFloat( 2, 5 );
		casings.ApplyShape = true;
		casings.InitialScale = 4.8f;
		casings.ApplyRotation = true;
		casings.InitialRoll = new ParticleFloat( 0, 360 );
		casings.SheetSequence = true;
		// The casing cycles five times, spinning fastest just after ejection.
		casings.SequenceTime = new ParticleFloat
		{
			Type = ParticleFloat.ValueType.Curve,
			CurveA = new Curve( new Curve.Frame( 0, 0, 0, 13.513514f ), new Curve.Frame( 1, 5, 0, 0 ) )
		};
		casings.SequenceSpeed = 0;
		var holder = Weapon?.Holder?.GameObject;
		var contacts = ShotgunContacts.For( Weapon );
		casings.OnPreStep += _ =>
		{
			if ( casings.DelayedParticles.Count > 0 ) TrackEjectionMarker();
		};
		casings.OnParticleCreated += EjectCasing;
		casings.OnPostStep += dt => CasingParticles.Advance( casings, holder, contacts, dt );
		var renderer = go.AddComponent<ParticleSpriteRenderer>();
		renderer.Sprite = Sprite.FromTexture( texture );
		renderer.Lighting = true;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0;
		go.Enabled = true;
		return casings;
	}

	void TrackEjectionMarker( ShotgunModel model )
	{
		ejectionMarker = model.ShellEjectGameObject.WorldTransform;
		firstPersonEjection = model is ShotgunViewModel;
	}

	void TrackEjectionMarker()
	{
		if ( !Weapon.IsValid() ) return;
		if ( Weapon.WeaponModel is ShotgunModel model && model.ShellEjectGameObject.IsValid() )
			TrackEjectionMarker( model );
		else if ( !Weapon.IsHeld && Weapon.GetComponent<SkinnedModelRenderer>()?.GetAttachment( "primary_ejection" ) is {} looseMarker )
		{
			ejectionMarker = looseMarker;
			firstPersonEjection = false;
		}
	}

	void EjectCasing( Particle casing )
	{
		TrackEjectionMarker();
		var marker = ejectionMarker;
		casing.StartPosition = casing.Position = firstPersonEjection ? marker.PointToWorld( Vector3.Down * 12 ) : marker.Position;
		var direction = marker.Rotation.Forward;
		if ( !firstPersonEjection )
		{
			// The world emitter samples azimuth uniformly and polar angle from zero to ten degrees.
			const double unit = 1f / 65535f;
			float azimuth = (float)(BlamRandom.Next( ref seed ) * unit * MathF.Tau);
			double polar = BlamRandom.Next( ref seed ) * unit * 10f.DegreeToRadian();
			double radial = Math.Sin( polar );
			direction = marker.Rotation * new Vector3( (float)Math.Cos( polar ),
				(float)(Math.Sin( azimuth ) * radial), (float)(Math.Cos( azimuth ) * radial) ).Normal;
		}
		// Lifetime and launch speed both use the particle's first random value.
		casing.Velocity = direction * casing.LifeTimeRemaining.Remap( 2, 5, 210, 300 );
	}

	public override void OnAttack( Vector3? hitPoint = null, Vector3? origin = null )
	{
		LocalView?.PlayFireAnimation();
		shotStart = Time.NowDouble;
		nextParticleTick = 1;
		if ( ShellEjectGameObject.IsValid() )
		{
			TrackEjectionMarker( this );
			if ( !casings.IsValid() ) casings = CreateCasings();
			casings?.Emit( ShellEjectGameObject.WorldPosition, 0 );
		}
	}

	void EmitMuzzleParticles()
	{
		if ( muzzleFlash.IsValid() )
		{
			for ( int i = 0; i < muzzleFlash.MaxParticles; i++ )
				muzzleFlash.Emit( MuzzleGameObject.WorldPosition, 0 );
		}
		EmitSoftSmoke();
		EmitSparks();
		if ( fierySmoke.IsValid() ) fierySmoke.Emit( fierySmoke.WorldPosition, 0 );
		if ( shortFlare.IsValid() ) shortFlare.Emit( shortFlare.WorldPosition, 0 );
		if ( longFlare.IsValid() ) longFlare.Emit( longFlare.WorldPosition, 0 );
	}

	protected override void OnUpdate()
	{
		if ( !Weapon.IsValid() )
		{
			GameObject.Destroy();
			return;
		}
		if ( nextParticleTick == 0 ) return;
		int due = Math.Min( 13, (int)((Time.NowDouble - shotStart) * 30) );
		if ( nextParticleTick > due ) return;
		var model = Weapon.WeaponModel as ShotgunModel;
		// Each emitter accumulates its rate over the elapsed part of its event.
		// At 30 Hz this gives one flash tick and twelve smoke ticks.
		while ( nextParticleTick <= due )
		{
			if ( nextParticleTick == 1 ) model?.EmitMuzzleParticles();
			else if ( model?.trailingSmoke is {} smoke && smoke.IsValid() ) smoke.Emit( smoke.WorldPosition, 0 );
			nextParticleTick++;
		}
		if ( nextParticleTick > 13 ) nextParticleTick = 0;
	}
}
