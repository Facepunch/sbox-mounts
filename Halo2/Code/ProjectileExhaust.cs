namespace Halo2;

public sealed record ProjectileParticleStage( float Start, float End, MuzzleFlashDefinition[] Effects );

[Hide]
public sealed class ProjectileExhaust : Component
{
	[Property] public MuzzleFlashDefinition[] Effects { get; set; }

	[Property] public ProjectileParticleStage[] Stages { get; set; }
	[Property] public double FiredAt { get; set; }

	[Sync( SyncFlags.FromHost )] public float AccelerationRange { get; internal set; }

	MuzzleFlash particles;
	Vector3 previousPosition;
	uint random = (uint)System.Random.Shared.Next();
	double started;
	int nextTick;
	bool drivenByFlight;

	protected override void OnStart() => InitializeParticles();

	void InitializeParticles()
	{
		if ( Application.IsDedicatedServer || particles.IsValid() ) return;
		particles = GameObject.AddComponent<MuzzleFlash>();
		particles.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
		started = FiredAt > 0 ? FiredAt : Time.NowDouble;
		nextTick = Stages is null ? 0 : System.Math.Max( 1, (int)((Time.NowDouble - started) * 30) );
		previousPosition = WorldPosition;
	}

	internal void StepFlight( Vector3 start )
	{
		if ( Effects is null || Application.IsDedicatedServer ) return;
		drivenByFlight = true;
		if ( !particles.IsValid() ) InitializeParticles();
		Emit( Effects, start, WorldPosition, 1 );
	}

	protected override void OnUpdate()
	{
		if ( drivenByFlight || !particles.IsValid() ) return;
		int due = (int)((Time.NowDouble - started) * 30);
		if ( due < nextTick ) return;
		int first = nextTick;
		var current = WorldPosition;
		var start = previousPosition;
		for ( ; nextTick <= due; nextTick++ )
		{
			var end = Vector3.Lerp( previousPosition, current, (float)(nextTick - first + 1) / (due - first + 1) );
			if ( Stages is null ) Emit( Effects, start, end, 1 );
			else
			{
				// A 30 Hz update can straddle two events. Each emitter accumulates only its own interval.
				float from = (nextTick - 1) / 30f, to = nextTick / 30f;
				foreach ( var stage in Stages )
				{
					float a = System.MathF.Max( from, stage.Start ), b = System.MathF.Min( to, stage.End );
					if ( b <= a ) continue;
					Emit( stage.Effects, Vector3.Lerp( start, end, (a - from) * 30 ), Vector3.Lerp( start, end, (b - from) * 30 ), (b - a) * 30 );
				}
			}
			start = end;
		}
		previousPosition = current;
	}

	void Emit( MuzzleFlashDefinition[] effects, Vector3 start, Vector3 end, float fraction )
	{
		foreach ( var effect in effects )
			particles.Emit( effect, false, GameObject, random, BlamRandom.Next( ref random ) / 65535f, true, AccelerationRange, start, end, fraction );
	}
}
