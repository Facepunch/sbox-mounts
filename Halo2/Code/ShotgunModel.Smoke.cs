using System;

namespace Halo2;

public partial class ShotgunModel
{
	ParticleEffect softSmoke;
	CurveRange smokeRadius, smokeAlpha;
	bool viewSmoke;

	void ConfigureSoftSmoke( bool firstPerson )
	{
		var texture = Texture.Load( $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.PrefabPath}/{ShotgunLoader.SmokeName}" );
		if ( !texture.IsValid() || texture.IsError || ShotgunLoader.Mounted is not {} loader ) return;
		(smokeRadius, smokeAlpha) = loader.SmokeCurves( firstPerson );
		viewSmoke = firstPerson;
		var go = new GameObject( MuzzleGameObject, false, "Soft muzzle smoke" );
		softSmoke = go.AddComponent<ParticleEffect>();
		softSmoke.Lifetime = firstPerson ? new ParticleFloat( 0.25f, 1 ) : new ParticleFloat( 0.5f, 1.5f );
		softSmoke.OnParticleCreated += StartSmoke;
		softSmoke.OnPostStep += AdvanceSmoke;
		var renderer = go.AddComponent<ParticleSpriteRenderer>();
		renderer.Sprite = Sprite.FromTexture( texture );
		renderer.Scale = 2;
		renderer.RenderOptions.Game = !firstPerson;
		renderer.RenderOptions.Overlay = firstPerson;
		go.Enabled = true;
	}

	void EmitSoftSmoke()
	{
		if ( !softSmoke.IsValid() ) return;
		float random = BlamRandom.Next( ref seed ) / 65535f;
		int count = (int)(viewSmoke ? 5 + 2 * random : 10 + 5 * random);
		for ( int i = 0; i < count; i++ ) softSmoke.Emit( softSmoke.WorldPosition, 0 );
	}

	void StartSmoke( Particle p )
	{
		p.LifeDelta = 0;
		float r = SmokeLifetimeFraction( p );
		float speedFraction = p.Rand( 1, 0 );
		float speed = viewSmoke ? 60 + 480 * speedFraction : 180 + 420 * speedFraction;
		float cone = 30 * (viewSmoke ? r : 1 - r);
		float azimuth = MathF.Tau * (BlamRandom.Next( ref seed ) / 65535f);
		float polar = cone.DegreeToRadian() * (BlamRandom.Next( ref seed ) / 65535f);
		var direction = new Vector3( MathF.Cos( polar ), MathF.Sin( azimuth ) * MathF.Sin( polar ), MathF.Cos( azimuth ) * MathF.Sin( polar ) );
		p.Velocity = softSmoke.WorldRotation * direction * speed;
		p.StartAngles.roll = speedFraction * 360;
		p.Set( "roll_speed", 20 - 40 * r );
		byte gray = (byte)MathF.Round( 100 - 40 * r );
		p.Color = Color.FromBytes( gray, gray, gray );
		UpdateSmokeAppearance( p, r, speedFraction );
	}

	float SmokeLifetimeFraction( Particle p ) => viewSmoke
		? p.LifeTimeRemaining.Remap( 0.25f, 1, 0, 1 ) : p.LifeTimeRemaining.Remap( 0.5f, 1.5f, 0, 1 );

	void UpdateSmokeAppearance( Particle p, float lifetimeFraction, float speedFraction )
	{
		p.Size = smokeRadius.Evaluate( p.LifeDelta, lifetimeFraction ).Clamp( 6, viewSmoke ? 24 : 36 );
		p.Alpha = smokeAlpha.Evaluate( p.LifeDelta, speedFraction ).Clamp( 0, 1 );
		OrientFlash( p );
	}

	void AdvanceSmoke( float dt )
	{
		float drag = 1 - (6.212135f * dt).Clamp( 0, 1 );
		float angularDrag = 1 - (29.507639f * dt).Clamp( 0, 1 );
		foreach ( var p in softSmoke.Particles )
		{
			p.Velocity = (p.Velocity + Vector3.Up * (4.1712594f * 120 * 0.047619022f * dt)) * drag;
			float roll = p.Get<float>( "roll_speed" );
			p.StartAngles.roll += roll * dt;
			p.Set( "roll_speed", roll * angularDrag );
			UpdateSmokeAppearance( p, SmokeLifetimeFraction( p ), p.Rand( 1, 0 ) );
		}
	}
}
