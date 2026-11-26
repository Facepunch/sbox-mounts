using System;

namespace NS2;

internal sealed class MarineWeaponAim( float fieldOfView )
{
	internal enum Weapon { Bullets, Minigun, Railgun }

	readonly AlienAim.Acquisition acquisition = new( fieldOfView );
	internal float TurnRate => acquisition.TurnRate;

	static readonly float[] MinigunAccuracy = [23, 25, 28, 30, 35, 39, 43];
	static readonly float[] RailgunAccuracy = [25, 28, 30, 32, 35, 39, 43];
	static readonly float[] BulletAccuracy = [15, 17, 19, 25, 28, 30, 32];
	int bulletHits, bulletSamples;
	Vector3 missOffset;
	TimeSince aimStateAge;

	internal void Record( bool hit )
	{
		bulletSamples++;
		if ( hit ) bulletHits++;
	}

	internal bool Track( Ns2Npc owner, GameObject enemy, Vector3 point, Rotation aim, int tier, Weapon weapon, out Vector3 viewOffset )
	{
		viewOffset = Vector3.Zero;
		var previous = acquisition.Target;
		bool ready = acquisition.TrackView( enemy, owner.EyePosition, point, aim, 1, tier );
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
			var accuracy = weapon switch { Weapon.Minigun => MinigunAccuracy, Weapon.Railgun => RailgunAccuracy, _ => BulletAccuracy };
			float goal = accuracy[Math.Clamp( tier, 1, accuracy.Length ) - 1];
			if ( weapon == Weapon.Bullets && target is Onos ) goal *= 2;
			if ( weapon != Weapon.Railgun )
				goal = Math.Max( 12, goal - owner.WorldPosition.Distance( enemy.WorldPosition ) * (0.25f / MapLoader.Scale) );
			bool shouldMiss = bulletSamples > 0 && bulletHits * 100.0 / bulletSamples >= goal;
			if ( shouldMiss != (missOffset != Vector3.Zero) )
			{
				aimStateAge = 0;
				float spread = weapon switch
				{
					Weapon.Minigun => 5f.DegreeToRadian(),
					Weapon.Bullets => 0.018f + 2.8f.DegreeToRadian(),
					_ => 0
				};
				missOffset = shouldMiss && target.Renderer.IsValid()
					? (target.Renderer.LocalBounds.Size + Vector3.One * (spread * MapLoader.Scale)) * direction : Vector3.Zero;
			}
		}
		viewOffset = missOffset;
		return ready;
	}
}
