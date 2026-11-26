using System;

namespace Source1.Hl2;

public sealed class SporeCloud : Component
{
	static readonly Color Tint = new( 225 / 255f, 140 / 255f, 64 / 255f );
	ParticleEffect spores, dust;
	TimeSince age;
	float nextBurst;
	float emitDuration;
	bool trail;

	internal static SporeCloud Create( Vector3 position, Vector3 direction, string mount, float duration, GameObject parent = null )
	{
		var go = new GameObject( parent, false, "Bugbait spores" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;
		go.WorldRotation = Rotation.LookAt( direction );
		var cloud = go.AddComponent<SporeCloud>();
		cloud.trail = parent.IsValid();
		cloud.emitDuration = duration;
		cloud.spores = Particles.Emitter( go, "particle/fire", mount, true, false, cloud.trail ? 132 : 68 );
		cloud.dust = Particles.Emitter( go, "particle/particle_noisesphere", mount, false, false, cloud.trail ? 17 : 9 );
		Configure( cloud.spores, 2 );
		Configure( cloud.dust, 1 );
		cloud.age = 0;
		go.Enabled = true;
		return cloud;
	}

	static void Configure( ParticleEffect effect, float lifetime )
	{
		if ( !effect.IsValid() ) return;
		effect.Lifetime = lifetime;
		var renderer = effect.GetComponent<ParticleSpriteRenderer>( true );
		renderer.CameraFadeNear = 64;
		renderer.CameraFadeFar = 128;
	}

	internal void StopEmitting()
	{
		emitDuration = age;
		GameObject.SetParent( null, true );
	}

	protected override void OnUpdate()
	{
		if ( age >= emitDuration + 2 )
		{
			GameObject.Destroy();
			return;
		}

		var distant = Scene.Camera is { } camera && camera.WorldPosition.DistanceSquared( WorldPosition ) >= 512 * 512;
		while ( nextBurst <= age && nextBurst < emitDuration )
		{
			for ( var i = 0; i < 4; i++ ) Emit( spores, false );
			Emit( dust, true );
			nextBurst += (distant ? 2f : 1f) / (trail ? 16 : 8);
		}

		UpdateParticles( spores, false );
		UpdateParticles( dust, true );
	}

	void Emit( ParticleEffect effect, bool smoky )
	{
		if ( !effect.IsValid() ) return;
		var radius = trail ? 4 : 32;
		var p = effect.Emit( WorldPosition + Particles.InCube( smoky ? radius * 0.5f : radius ), 0 );
		if ( p is null ) return;
		p.Velocity = WorldRotation.Forward * (smoky ? Game.Random.Float( 64, 128 ) : Game.Random.Float( 128, 256 ));
		p.Angles = new Angles( 0, 0, smoky ? Game.Random.Float( 0, 360 ).RadianToDegree() : 0 );
	}

	void UpdateParticles( ParticleEffect effect, bool smoky )
	{
		if ( !effect.IsValid() ) return;
		foreach ( var p in effect.Particles )
		{
			var t = p.LifeDelta.Clamp( 0, 1 );
			var alpha = smoky ? MathX.Lerp( 32, 64, p.Rand() ) : MathX.Lerp( 128, 255, p.Rand() );
			var ramp = alpha / 255 * MathF.Sin( MathF.PI * t );
			p.Color = (Tint * ramp).WithAlpha( 1 );
			p.Alpha = ramp;
			p.Size = smoky ? MathX.Lerp( 64, trail ? 96 : 128, t ) : MathX.Lerp( p.Rand() < 0.5f ? 2 : 4, 2, t );
			if ( smoky ) p.Angles = p.Angles.WithRoll( p.Angles.roll + MathX.Lerp( -2, 2, p.Rand() ).RadianToDegree() * Time.Delta );
			p.Velocity = (p.Velocity.Normal + Particles.InCube( 0.5f )).Normal * (p.Velocity.Length - 64 * Time.Delta);
		}
	}
}
