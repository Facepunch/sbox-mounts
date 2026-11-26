namespace Halo2;

[Hide]
public sealed class ProjectileExhaust : Component
{
	[Property] public MuzzleFlashDefinition[] Effects { get; set; }

	[Sync( SyncFlags.FromHost )] public float AccelerationRange { get; internal set; }

	MuzzleFlash particles;
	Vector3 previousPosition;
	uint random = (uint)System.Random.Shared.Next();
	double nextTick;

	protected override void OnStart()
	{
		if ( Application.IsDedicatedServer ) return;
		particles = GameObject.AddComponent<MuzzleFlash>();
		particles.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
		nextTick = Time.NowDouble;
		previousPosition = WorldPosition;
	}

	protected override void OnUpdate()
	{
		if ( !particles.IsValid() || Time.NowDouble < nextTick ) return;
		int ticks = (int)System.Math.Floor( (Time.NowDouble - nextTick) * 30 ) + 1;
		nextTick += ticks / 30.0;
		var current = WorldPosition;
		var start = previousPosition;
		for ( int tick = 1; tick <= ticks; tick++ )
		{
			var end = Vector3.Lerp( previousPosition, current, (float)tick / ticks );
			foreach ( var effect in Effects )
				particles.Emit( effect, false, GameObject, random, BlamRandom.Next( ref random ) / 65535f, true, AccelerationRange, start, end );
			start = end;
		}
		previousPosition = current;
	}
}
