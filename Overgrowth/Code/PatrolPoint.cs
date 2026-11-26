namespace Overgrowth;

public sealed class PatrolPoint : Component
{
	[Property] public List<PatrolPoint> Connections { get; set => field = value ?? []; } = [];
	[Property] public float Wait { get; set; }
	[Property] public string Pose { get; set; } = "Stand";
}
