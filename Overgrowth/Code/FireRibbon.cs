using System;

namespace Overgrowth;

internal sealed class FireRibbon
{
	internal struct Point
	{
		public Vector3 Position, Velocity;
		public float Width, Heat, Born;
	}

	internal readonly Point[] Points = new Point[5];
	internal int Count { get; private set; }
	internal readonly float Seed = Random.Shared.Float( 0f, 100f );
	internal float SpawnFraction => spawnDelay / 0.1f;
	float spawnDelay = Random.Shared.Float( 0f, 0.1f );

	internal void Advance( Vector3 anchor, Vector3 velocity, Vector3 center, bool burning, float time, float dt )
	{
		if ( !burning )
		{
			bool hot = false;
			for ( int i = 0; i < Count; i++ ) hot |= Points[i].Heat > 0f;
			if ( !hot ) { Count = 0; return; }
		}

		spawnDelay -= dt;
		if ( spawnDelay <= 0f )
		{
			if ( Count == Points.Length ) Array.Copy( Points, 1, Points, 0, Count - 1 );
			else Count++;
			Points[Count - 1] = new()
			{
				Position = anchor, Velocity = velocity * 0.1f,
				Width = 0.12f * MathF.Min( 2f, velocity.Length * 0.1f + 1f ),
				Heat = burning ? Random.Shared.Float( 0f, 1.5f ) : 0f, Born = time
			};
			do spawnDelay += 0.1f; while ( spawnDelay <= 0f );
		}

		if ( Count == 0 ) return;
		Points[Count - 1].Position = anchor;
		float damping = MathF.Pow( 0.2f, dt );
		for ( int i = 0; i < Count; i++ )
		{
			ref var p = ref Points[i];
			p.Velocity *= damping;
			p.Position += p.Velocity * dt;
			p.Velocity += (Wind( p.Position * 5f, time ) + Wind( p.Position * 30f, time ) * 2f) * dt;
			if ( i + 1 < Count )
			{
				var toNext = Points[i + 1].Position - p.Position;
				float length = toNext.Length;
				if ( length > 0.2f ) p.Velocity += toNext / length * ((length - 0.2f) * 100f * dt);
				p.Heat -= length * 10f * dt;
			}
			p.Heat -= 3f * dt;
			p.Velocity.y += 12f * dt;
			p.Velocity -= (p.Position - center).WithY( 0f ).ClampLength( 1f ) * (18f * dt);
		}
	}

	static Vector3 Wind( Vector3 point, float time )
	{
		point += new Vector3( 7f, 3f, 5f ) * time;
		return new( MathF.Sin( point.x ) + MathF.Cos( point.y * 1.3f ) + MathF.Sin( point.z * 3f ),
			MathF.Sin( point.x * 1.2f ) + MathF.Cos( point.y * 1.8f ) + MathF.Sin( point.z * 0.8f ),
			MathF.Sin( point.x * 1.6f ) + MathF.Cos( point.y * 0.5f ) + MathF.Sin( point.z * 1.2f ) );
	}
}
