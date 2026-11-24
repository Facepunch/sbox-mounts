using System;

namespace Halo2;

public partial class ShotgunModel
{
	ParticleEffect softSmoke, trailingSmoke;
	CurveRange smokeRadius, smokeAlpha;
	bool viewSmoke;

	void ConfigureSoftSmoke( bool firstPerson )
	{
		var texture = Texture.Load( $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.PrefabPath}/{ShotgunLoader.SmokeName}" );
		if ( !texture.IsValid() || texture.IsError || ShotgunLoader.Mounted is not {} loader ) return;
		(smokeRadius, smokeAlpha) = loader.SmokeCurves( firstPerson );
		viewSmoke = firstPerson;
		var sprite = Sprite.FromTexture( texture );
		softSmoke = CreateSmoke( "Soft muzzle smoke", sprite );
		softSmoke.Lifetime = firstPerson ? new ParticleFloat( 0.25f, 1 ) : new ParticleFloat( 0.5f, 1.5f );
		softSmoke.OnParticleCreated += StartSmoke;
		softSmoke.OnPostStep += dt => AdvanceSmoke( softSmoke, dt, false );

		trailingSmoke = CreateSmoke( "Trailing muzzle smoke", sprite );
		trailingSmoke.Lifetime = new ParticleFloat( 1, 2 );
		trailingSmoke.OnParticleCreated += StartTrailingSmoke;
		trailingSmoke.OnPostStep += dt => AdvanceSmoke( trailingSmoke, dt, true );
	}

	ParticleEffect CreateSmoke( string name, Sprite sprite )
	{
		var go = new GameObject( MuzzleGameObject, false, name );
		var effect = go.AddComponent<ParticleEffect>();
		var renderer = go.AddComponent<ParticleSpriteRenderer>();
		renderer.Sprite = sprite;
		renderer.Scale = 2;
		renderer.RenderOptions.Game = !viewSmoke;
		renderer.RenderOptions.Overlay = viewSmoke;
		go.Enabled = true;
		return effect;
	}

	void EmitSoftSmoke()
	{
		if ( !softSmoke.IsValid() ) return;
		float random = BlamRandom.Next( ref seed ) / 65535f;
		int count = (int)(viewSmoke ? 5 + 2 * random : 10 + 5 * random);
		for ( int i = 0; i < count; i++ ) softSmoke.Emit( softSmoke.WorldPosition, 0 );
	}

	void StartTrailingSmoke( Particle p )
	{
		p.LifeDelta = 0;
		float random = p.Rand( 1, 0 );
		p.StartAngles.roll = random * 360;
		p.Set( "roll_speed", 1 - 2 * (p.LifeTimeRemaining - 1) );
		byte gray = (byte)MathF.Round( 100 - 40 * random );
		p.Color = Color.FromBytes( gray, gray, gray );
		UpdateTrailingSmoke( p );
	}

	static void UpdateTrailingSmoke( Particle p )
	{
		float t = p.LifeDelta;
		float low = 0.1388889f * t + 0.09444445f;
		float high = 0.7f * t + 0.3f;
		p.Size = 3 + 3 * (low + (high - low) * p.Rand( 1, 0 ));
		p.Alpha = 0.35f * (((-0.42384624f * t - 0.21579337f) * t - 0.36036032f) * t + 1);
		OrientFlash( p );
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

	void AdvanceSmoke( ParticleEffect effect, float dt, bool trailing )
	{
		float drag = 1 - (6.212135f * dt).Clamp( 0, 1 );
		float angularDrag = 1 - (29.507639f * dt).Clamp( 0, 1 );
		foreach ( var p in effect.Particles )
		{
			p.Velocity = (p.Velocity + Vector3.Up * (4.1712594f * 120 * 0.047619022f * dt)) * drag;
			float roll = p.Get<float>( "roll_speed" );
			p.StartAngles.roll += roll * dt;
			p.Set( "roll_speed", roll * angularDrag );
			if ( trailing ) UpdateTrailingSmoke( p );
			else UpdateSmokeAppearance( p, SmokeLifetimeFraction( p ), p.Rand( 1, 0 ) );
		}
	}
}
