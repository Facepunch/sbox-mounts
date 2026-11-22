namespace Doom;

/// <summary>Marks the root of a thing prefab with the thing type it was built from.</summary>
[Title( "Doom Thing" )]
[Category( "Doom" )]
[Icon( "category" )]
public sealed class DoomThing : Component
{
	/// <summary>Editor number (doomednum).</summary>
	[Property] public int Type { get; set; }

	[Property] public ThingCategory Category { get; set; }
}
