using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal void Stab( Vector3 origin, Vector3 direction, bool light ) => Renderer.GetOrAddComponent<CutMarks>().Stab( origin, direction, light );

	internal static Vector3 ParticleScatter() => new( Random.Shared.Float( -1f, 1f ), Random.Shared.Float( -1f, 1f ), Random.Shared.Float( -1f, 1f ) );
}
