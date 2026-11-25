using System;

namespace Source1.Hl2;

public sealed class SporeCloud : Component
{
	const float EmitDuration = 5;
	static readonly Color Tint = new( 225 / 255f, 140 / 255f, 64 / 255f );
	ParticleEffect spores, dust;
	TimeSince age;
	float nextBurst;

	internal static void Create( Vector3 position, Vector3 direction, string mount )
	{
		var go = new GameObject( false, "Bugbait spores" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;
		go.WorldRotation = Rotation.LookAt( direction );
		var cloud = go.AddComponent<SporeCloud>();
		cloud.spores = Particles.Emitter( go, "particle/fire", mount, true, false, 68 );
		cloud.dust = Particles.Emitter( go, "particle/particle_noisesphere", mount, false, false, 9 );
		if ( cloud.spores.IsValid() ) cloud.spores.Lifetime = 2;
		if ( cloud.dust.IsValid() ) cloud.dust.Lifetime = 1;
		cloud.age = 0;
		go.Enabled = true;
	}

	protected override void OnUpdate()
	{
		if ( age >= EmitDuration + 2 )
		{
			GameObject.Destroy();
			return;
		}

		var distant = Scene.Camera is { } camera && camera.WorldPosition.DistanceSquared( WorldPosition ) >= 512 * 512;
		while ( nextBurst <= age && nextBurst < EmitDuration )
		{
			for ( var i = 0; i < 4; i++ ) Emit( spores, false );
			Emit( dust, true );
			nextBurst += distant ? 0.25f : 0.125f;
		}

		UpdateParticles( spores, false );
		UpdateParticles( dust, true );
	}

	void Emit( ParticleEffect effect, bool smoky )
	{
		if ( !effect.IsValid() ) return;
		var p = effect.Emit( WorldPosition + Particles.InCube( smoky ? 16 : 32 ), 0 );
		if ( p is null ) return;
		p.Velocity = WorldRotation.Forward * (smoky ? Game.Random.Float( 64, 128 ) : Game.Random.Float( 128, 256 ));
		p.Angles = new Angles( 0, 0, smoky ? Game.Random.Float( 0, 360 ).RadianToDegree() : 0 );
	}

	void UpdateParticles( ParticleEffect effect, bool smoky )
	{
		if ( !effect.IsValid() ) return;
		var camera = Scene.Camera?.WorldTransform;
		foreach ( var p in effect.Particles )
		{
			var t = p.LifeDelta.Clamp( 0, 1 );
			var alpha = smoky ? MathX.Lerp( 32, 64, p.Rand() ) : MathX.Lerp( 128, 255, p.Rand() );
			var ramp = alpha / 255 * MathF.Sin( MathF.PI * t );
			p.Color = (Tint * ramp).WithAlpha( 1 );
			var depth = camera is { } view ? (p.Position - view.Position).Dot( view.Rotation.Forward ) : 128;
			p.Alpha = ramp * ((depth - 64) / 64).Clamp( 0, 1 );
			p.Size = smoky ? MathX.Lerp( 64, 128, t ) : MathX.Lerp( p.Rand() < 0.5f ? 2 : 4, 2, t );
			if ( smoky ) p.Angles = p.Angles.WithRoll( p.Angles.roll + MathX.Lerp( -2, 2, p.Rand() ).RadianToDegree() * Time.Delta );
			p.Velocity = (p.Velocity.Normal + Particles.InCube( 0.5f )).Normal * (p.Velocity.Length - 64 * Time.Delta);
		}
	}
}
