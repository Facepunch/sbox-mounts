namespace Source1.Hl2;

/// <summary>A map hint where an antlion maker can spawn a child.</summary>
public sealed class AntlionBurrow : Component, IMapInput
{
	Doo.Block IMapInput.BuildInput( string input, string parameter, bool hasUser ) => input.ToLowerInvariant() switch
	{
		"enablehint" when parameter.Length == 0 => this.Call( nameof( EnableHint ) ),
		"disablehint" when parameter.Length == 0 => this.Call( nameof( DisableHint ) ),
		_ => null
	};

	[Property] public string Group { get; set; }
	[Property] public bool Available { get; set; } = true;

	internal TimeUntil ReuseDelay { get; set; }

	public void EnableHint() => Available = true;
	public void DisableHint() => Available = false;
}
