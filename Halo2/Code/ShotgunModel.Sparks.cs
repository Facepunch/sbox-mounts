using System;

namespace Halo2;

public partial class ShotgunModel
{
	ParticleEffect muzzleSparks;
	bool viewSparks;

	void ConfigureSparks( bool firstPerson )
	{
		var texture = Texture.Load( $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.PrefabPath}/{ShotgunLoader.SparksName}" );
		if ( !texture.IsValid() || texture.IsError ) return;
		viewSparks = firstPerson;
		var go = new GameObject( MuzzleGameObject, false, "Muzzle sparks" );
		muzzleSparks = go.AddComponent<ParticleEffect>();
		muzzleSparks.Lifetime = new ParticleFloat( 0.15f, firstPerson ? 0.2f : 0.25f );
		muzzleSparks.LocalSpace = firstPerson ? 1 : 0;
		muzzleSparks.SnapToFrame = true;
		muzzleSparks.OnParticleCreated += StartSpark;
		muzzleSparks.OnPostStep += _ =>
		{
			foreach ( var p in muzzleSparks.Particles ) UpdateSpark( p );
		};
		var renderer = go.AddComponent<ParticleSpriteRenderer>();
		renderer.Sprite = Sprite.FromTexture( texture );
		renderer.Sprite.Animations[0].Origin = new Vector2( 1 - 48f / 56f, 4f / 9f );
		renderer.Alignment = ParticleSpriteRenderer.BillboardAlignment.Particle;
		renderer.Additive = true;
		renderer.FogStrength = 0;
		renderer.RenderOptions.Game = !firstPerson;
		renderer.RenderOptions.Overlay = firstPerson;
		go.Enabled = true;
	}

	void EmitSparks()
	{
		if ( !muzzleSparks.IsValid() ) return;
		float random = BlamRandom.Next( ref seed ) / 65535f;
		int count = (int)((viewSparks ? 15 : 10) + 5 * random + 0.0001f);
		for ( int i = 0; i < count; i++ ) muzzleSparks.Emit( muzzleSparks.WorldPosition, 0 );
	}

	void StartSpark( Particle p )
	{
		p.LifeDelta = 0;
		float speed = viewSparks ? 120 + 120 * p.Rand( 0 ) : 240 + 360 * p.Rand( 0 );
		float angle = viewSparks ? 45 - 15 * p.Rand( 3 ) : 50 - 35 * p.Rand( 3 );
		float polar = angle.DegreeToRadian();
		float azimuth = MathF.Tau * (BlamRandom.Next( ref seed ) / 65535f);
		var axis = new Vector3( MathF.Cos( polar ), MathF.Sin( azimuth ) * MathF.Sin( polar ), MathF.Cos( azimuth ) * MathF.Sin( polar ) );
		p.Velocity = muzzleSparks.WorldRotation * axis.Normal * speed;
		p.Color = Color.FromBytes( 128, 0, 0 );
		UpdateSpark( p );
	}

	void UpdateSpark( Particle p )
	{
		float t = p.LifeDelta;
		float radius = (viewSparks ? 3 : 4.2f) * SparkScale( t );
		p.Alpha = (1 + t * t * (10.333334f - 11.333334f * t)).Clamp( 0, 1 );
		OrientSpark( p, radius, Scene.Camera, muzzleSparks.WorldRotation.Up );
	}

	internal static float SparkScale( float age ) => 0.25f + 0.75f * (((5.142857f * age - 11.285714f) * age + 7.142857f) * age).Clamp( 0, 1 );

	internal static void OrientSpark( Particle p, float radius, CameraComponent camera, Vector3 fallbackUp, bool fiery = false )
	{
		float t = p.LifeDelta;
		// The original spline crosses atlas frames 1 and 2 at these normalized ages.
		var (second, third) = fiery ? (0.2476984f, 0.5027956f) : (0.2419855f, 0.5006184f);
		int frame = t < second ? 0 : t < third ? 1 : 2;
		p.SequenceTime = new Vector3( (frame + 0.5f) / 3, 0, 0 );
		float height = 9f / (frame == 2 ? 55 : 56);
		p.Size = new Vector2( 2 * radius, (p.Rand( 1 ) < 0.5f ? -2 : 2) * height * radius );
		AlignToVelocity( p, camera, fallbackUp, false );
	}

	internal static void AlignToVelocity( Particle p, CameraComponent camera, Vector3 fallbackUp, bool vertical )
	{
		if ( !camera.IsValid() ) return;
		var axis = p.Velocity.Normal;
		var up = Vector3.Cross( camera.View.Position - p.Position, axis );
		if ( up.LengthSquared < 0.000001f ) up = fallbackUp;
		p.Angles = Rotation.LookAt( Vector3.Cross( axis, up ), vertical ? axis : up ).Angles();
	}
}
