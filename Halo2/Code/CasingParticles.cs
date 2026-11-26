namespace Halo2;

public class CasingSounds
{
	public CuePlayback[] Cues { get; set; }
	public Dictionary<string, int> Casings { get; set; }
	protected uint Seed = (uint)System.Random.Shared.Next();

	protected CuePlayback Cue( int index ) => index > 0 && index <= Cues.Length ? Cues[index - 1] : null;

	internal SoundHandle PlayCasing( Surface surface, Vector3 position, float gain )
	{
		if ( gain <= 0 ) return null;
		var handle = Cue( ContactMaterial.Lookup( Casings, surface, true ) )?.Play( position, ref Seed, null );
		if ( handle.IsValid() ) handle.Volume *= gain;
		return handle;
	}
}

internal static class CasingParticles
{
	internal static void Advance( ParticleEffect casings, GameObject holder, CasingSounds contacts, float dt )
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
			var trace = casings.Scene.Trace.Ray( start, start + 12 * step );
			if ( holder.IsValid() ) trace = trace.IgnoreGameObjectHierarchy( holder );
			var hit = PelletImpactEffect.TraceParticleStructure( trace );
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
				contacts?.PlayCasing( hit.Surface, hit.HitPosition, gain );
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
}
