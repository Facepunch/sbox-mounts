using System;

namespace Halo2;

[Hide]
public class ShotgunModel : BaseWeaponModel
{
	internal Shotgun Weapon { get; set; }
	ParticleEffect casings;
	TimeUntil? ejectAt;
	bool viewCasing;
	uint seed = (uint)Random.Shared.Next();

	internal void ConfigureCasings( bool firstPerson )
	{
		viewCasing = firstPerson;
		if ( !ShellEjectGameObject.IsValid() ) return;
		var texture = Texture.Load( $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.PrefabPath}/{ShotgunLoader.CasingName}" );
		if ( !texture.IsValid() || texture.IsError ) return;
		var go = new GameObject( ShellEjectGameObject, false, "Shotgun casings" );
		casings = go.AddComponent<ParticleEffect>();
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
		casings.OnPostStep += AdvanceCasings;
		var renderer = go.AddComponent<ParticleSpriteRenderer>();
		renderer.Sprite = Sprite.FromTexture( texture );
		renderer.Lighting = true;
		go.Enabled = true;
	}

	void AdvanceCasings( float dt )
	{
		if ( dt <= 0 ) return;
		// The stones particle controller advances position, applies gravity, then damps velocity.
		const float gravity = 4.1712594f * 120 * 0.99908376f;
		float retained = 1 - (0.05058453f * dt).Clamp( 0, 1 );
		foreach ( var casing in casings.Particles )
		{
			casing.Alpha = CasingAlpha( casing.LifeDelta );
			casing.Velocity = (casing.Velocity + Vector3.Down * (gravity * dt)) * retained;
			if ( casing.Frame < casing.Get<int>( "next_collision_frame" ) ) continue;

			// Halo probes twelve steps ahead and accepts contacts within the first two.
			var step = casing.Velocity * dt;
			var start = casing.Position - step;
			var hit = Scene.Trace.Ray( start, start + 12 * step )
				.IgnoreGameObjectHierarchy( Weapon?.Holder?.GameObject ?? GameObject ).Run();
			if ( !hit.Hit )
			{
				casing.HitNormal = Vector3.Zero;
				// A short rebound can return to the floor inside the five skipped probes.
				if ( casing.LastHitTime < 0 )
					casing.Set( "next_collision_frame", casing.Frame + 6 );
				continue;
			}
			if ( hit.StartedSolid || hit.Fraction >= 1f / 6 ) continue;
			var normalVelocity = hit.Normal * Vector3.Dot( casing.Velocity, hit.Normal );
			bool firstContact = casing.LastHitTime < 0 || casing.HitNormal == Vector3.Zero;
			casing.LastHitTime = Time.Now;
			casing.HitPos = hit.HitPosition;
			casing.HitNormal = hit.Normal;
			casing.Position = hit.HitPosition + hit.Normal * 0.6f;
			casing.Velocity = (casing.Velocity - normalVelocity) * 0.9f - normalVelocity * 0.35f;
			if ( firstContact )
			{
				float gain = (casing.Velocity.Length / 120 - 0.5f).Clamp( 0, 1 );
				ShotgunLoader.Mounted?.PlayCasingImpact( hit.Surface, casing.Position, gain );
			}
		}
	}

	static float CasingAlpha( float age )
	{
		// Halo samples its quadratic transition from a 1024-entry byte table.
		const int last = 1023;
		static float Sample( int i ) => (255 * i * i / (last * last)) * (1f / 255);
		float time = age.Clamp( 0, 1 ) * last;
		int index = (int)System.MathF.Round( time - 0.1f );
		if ( index == last ) return 0;
		float fraction = time - (int)time;
		float fade = Sample( index ) * (1 - fraction) + Sample( index + 1 ) * fraction;
		return System.MathF.Round( (1 - fade) * 255 ) / 255;
	}

	void EjectCasing()
	{
		ejectAt = null;
		if ( !casings.IsValid() || !ShellEjectGameObject.IsValid() ) return;
		var marker = ShellEjectGameObject.WorldTransform;
		var position = viewCasing ? marker.PointToWorld( Vector3.Down * 12 ) : marker.Position;
		if ( casings.Emit( position, 0 ) is not {} casing ) return;
		var direction = marker.Rotation.Forward;
		if ( !viewCasing )
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
		ejectAt = (TimeUntil)(13f / 30f);
	}

	protected override void OnUpdate()
	{
		if ( ejectAt is {} due && due <= 0 ) EjectCasing();
	}
}
