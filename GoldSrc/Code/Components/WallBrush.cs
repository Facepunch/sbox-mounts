using Sandbox;

namespace GoldSrc.Maps;

[Title( "GoldSrc Wall" )]
[Category( "GoldSrc" )]
public sealed class WallBrush : MapEntity
{
	[Property] public BrushAppearance Appearance { get; set; }
	[Property] public bool Conveyor { get; set; }
	[Property] public bool VisualOnly { get; set; }
	[Property] public Vector3 Direction { get; set; }
	[Property] public float Speed { get; set; } = 100f;

	protected override void OnStart() => UpdateConveyor();

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		if ( Conveyor )
		{
			Speed = -Speed;
			UpdateConveyor();
		}
		else if ( Appearance.IsValid() )
			Appearance.Frame = 1 - Appearance.Frame;
	}

	public override void TurnOn( GameObject activator = null )
	{
		if ( Conveyor || Appearance.IsValid() && Appearance.Frame == 0 ) Trigger( activator );
	}

	public override void TurnOff( GameObject activator = null )
	{
		if ( Conveyor || Appearance.IsValid() && Appearance.Frame != 0 ) Trigger( activator );
	}

	private void UpdateConveyor()
	{
		if ( !Conveyor ) return;
		if ( Appearance.IsValid() ) Appearance.ConveyorSpeed = Speed;
		foreach ( var collider in GetComponentsInChildren<Collider>( true ) )
			collider.SurfaceVelocity = VisualOnly ? Vector3.Zero : Direction * Speed;
	}
}
