namespace Source1.Hl2;

/// <summary>A map hint where an antlion maker can spawn a child.</summary>
public sealed class AntlionBurrow : Component
{
	[Property] public string Group { get; set; }
	[Property] public bool Available { get; set; } = true;

	internal TimeUntil ReuseDelay { get; set; }

	public void EnableHint() => Available = true;
	public void DisableHint() => Available = false;
}
