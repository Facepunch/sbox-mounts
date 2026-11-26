using System;

namespace NS2;

public sealed partial class Marine
{
	static readonly float[] BulletAccuracy = [15, 17, 19, 25, 28, 30, 32];
	int bulletHits, bulletSamples;
	Vector3 missOffset, viewOffset;
	TimeSince aimStateAge;

	bool TrackBulletAim( GameObject enemy, Vector3 point )
	{
		var previous = acquisition.Target;
		bool ready = acquisition.TrackView( enemy, EyePosition, point, aim, 1, AimSkillTier );
		if ( previous != acquisition.Target ) bulletHits = bulletSamples = 0;
		if ( !acquisition.HasSample ) return ready;

		var target = enemy.GetComponent<Ns2Npc>();
		var direction = target switch
		{
			Skulk or Gorge => Vector3.Up,
			Lerk => Vector3.Down,
			Onos => Vector3.Left * 0.5f,
			Fade or Marine or Exo => Vector3.Left,
			_ => Vector3.Zero
		};
		if ( direction == Vector3.Zero ) return ready;
		if ( aimStateAge > 0.125f )
		{
			float distance = WorldPosition.Distance( enemy.WorldPosition ) / Scale;
			float goal = BulletAccuracy[Math.Clamp( AimSkillTier, 1, BulletAccuracy.Length ) - 1];
			if ( target is Onos ) goal *= 2;
			goal = Math.Max( 12, goal - distance * 0.25f );
			bool shouldMiss = bulletSamples > 0 && bulletHits * 100.0 / bulletSamples >= goal;
			if ( shouldMiss != (missOffset != Vector3.Zero) )
			{
				aimStateAge = 0;
				const float spread = (0.018f + 2.8f * MathF.PI / 180) * Scale;
				missOffset = shouldMiss && target.Renderer.IsValid()
					? (target.Renderer.LocalBounds.Size + Vector3.One * spread) * direction : Vector3.Zero;
			}
		}
		viewOffset = missOffset;
		return ready;
	}
}
