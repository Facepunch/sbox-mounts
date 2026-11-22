using Sandbox;

namespace GoldSrc.Maps;

[Title( "GoldSrc Brush Appearance" )]
[Category( "GoldSrc" )]
public sealed class BrushAppearance : Component
{
	[Property] public int Frame { get; set; }
	[Property] public float ConveyorSpeed { get; set; }
}
