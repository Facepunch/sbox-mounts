using System;

namespace NS2;

internal static class AlienAim
{
	internal sealed class Acquisition( float fieldOfView )
	{
		// BotAim passes its class FOV directly to the cone cosine.
		readonly float cone = MathF.Cos( fieldOfView );
		GameObject target;
		TimeSince lastTracked;
		TimeUntil acquired;
		List<(GameObject Target, float Time)> recentTargets;

		Vector3 samplePosition;
		TimeUntil sample, penalty;
		float turnModifier = 1, penaltyScale = 1;

		internal float TurnRate => 5 * turnModifier * (penalty >= 0 ? penaltyScale : 1);

		internal bool TrackView( GameObject enemy, Vector3 eye, Vector3 point, Rotation view, float modifier, int skillTier )
		{
			if ( !Track( enemy, point - eye, view, skillTier ) ) return false;
			turnModifier = modifier;
			if ( sample < 0 )
			{
				samplePosition = enemy.WorldPosition;
				sample = 0.25f;
			}
			else
			{
				bool high = view.Forward.z > 0.4f;
				// BotAim takes acos of the unnormalized offsets in metres.
				float dot = Vector3.Dot( samplePosition - eye, enemy.WorldPosition - eye ) / (MapLoader.Scale * MapLoader.Scale);
				if ( high || dot is >= -1 and < -0.5f )
				{
					penaltyScale = high ? 0.5f : 0.7f;
					penalty = 0.4f - 0.3f * Math.Clamp( skillTier, 1, 7 ) / 7f;
					samplePosition = enemy.WorldPosition;
					sample = 0.25f;
				}
			}
			return acquired <= 0;
		}

		bool Track( GameObject enemy, Vector3 offset, Rotation view, int skillTier )
		{
			const float panicDistance = 2 * MapLoader.Scale;
			if ( offset.LengthSquared > panicDistance * panicDistance && Vector3.Dot( offset.Normal, view.Forward ) <= cone )
				return false;

			if ( target != enemy || lastTracked > 0.5f )
			{
				if ( target.IsValid() && lastTracked < 1.5f )
					(recentTargets ??= []).Add( (target, Time.Now - lastTracked) );

				bool reacquiring = false;
				for ( int i = (recentTargets?.Count ?? 0) - 1; i >= 0; i-- )
				{
					var entry = recentTargets[i];
					if ( Time.Now - entry.Time < 1.5f && entry.Target.IsValid() )
					{
						if ( entry.Target != enemy ) continue;
						reacquiring = true;
					}
					recentTargets.RemoveAt( i );
				}

				float tier = Math.Clamp( skillTier, 1, 7 ) / 7f;
				acquired = (0.325f - 0.14f * tier) * (reacquiring ? 0.3f : 1);
				target = enemy;
			}
			lastTracked = 0;
			return true;
		}
	}

	// Spark's SlerpRadians advances by a fixed number of radians along the shortest arc.
	internal static float ApproachAngle( float from, float to, float radians )
	{
		float step = radians.RadianToDegree();
		return from + Math.Clamp( Angles.NormalizeAngle( to - from ), -step, step );
	}

	internal static Rotation Turn( Rotation view, Vector3 aim, float turnRate )
	{
		if ( aim.IsNearlyZero() ) return view;
		var from = view.Angles();
		var to = Rotation.LookAt( aim ).Angles();
		// BotMotion scales turn speed when fighting or tracking difficult targets.
		float dt = Time.Delta is > 0 and <= 0.2f ? Time.Delta : 1f / 30;
		float step = turnRate * dt;
		float yaw = ApproachAngle( from.yaw, to.yaw, step );
		float pitch = ApproachAngle( from.pitch, to.pitch, step ).DegreeToRadian();
		// BotMotion keeps the desired direction's horizontal length while easing its vertical component.
		float horizontal = aim.Normal.WithZ( 0 ).Length;
		return Rotation.From( MathF.Atan2( MathF.Sin( pitch ), horizontal ).RadianToDegree(), yaw, 0 );
	}
}
