namespace GoldSrc.Maps;

[Title( "GoldSrc Render Controller" )]
[Icon( "palette" )]
public sealed class RenderController : MapEntity
{
	[Property] public int Mask { get; set; }
	[Property] public int RenderMode { get; set; }
	[Property] public int RenderFx { get; set; }
	[Property] public float RenderAmount { get; set; }
	[Property] public Color RenderColor { get; set; } = Color.Black;

	private bool _warnedFx;
	private bool _warnedBeamMode;

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		foreach ( var target in Map.FindAll( Target ) )
		{
			if ( target is SpriteEffect sprite )
			{
				if ( (Mask & 2) == 0 ) sprite.RenderAmount = RenderAmount;
				if ( (Mask & 4) == 0 ) sprite.RenderMode = RenderMode;
				if ( (Mask & 8) == 0 ) sprite.RenderColor = RenderColor;
				sprite.UpdateRenderer();
			}
			else if ( target is BeamEffect beam )
			{
				if ( (Mask & 2) == 0 ) beam.RenderAmount = RenderAmount;
				if ( (Mask & 8) == 0 ) beam.RenderColor = RenderColor;
				beam.UpdateRenderer();
				if ( (Mask & 4) == 0 && !_warnedBeamMode )
				{
					_warnedBeamMode = true;
					Log.Warning( $"GoldSrc: env_render '{TargetName}' cannot change packed beam endpoint flags." );
				}
			}
			else if ( target.GetComponent<RenderAppearance>() is { } appearance )
			{
				if ( (Mask & 1) == 0 ) appearance.RenderFx = RenderFx;
				if ( (Mask & 2) == 0 ) appearance.RenderAmount = RenderAmount;
				if ( (Mask & 4) == 0 ) appearance.RenderMode = RenderMode;
				if ( (Mask & 8) == 0 ) appearance.RenderColor = RenderColor;
				appearance.UpdateRenderer();
			}
		}
		if ( (Mask & 1) == 0 && RenderFx != 0 && !_warnedFx )
		{
			_warnedFx = true;
			Log.Warning( $"GoldSrc: env_render '{TargetName}' requests unsupported renderfx {RenderFx}." );
		}
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );
}
