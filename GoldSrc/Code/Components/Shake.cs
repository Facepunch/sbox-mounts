namespace GoldSrc.Maps;

public sealed class Shake : MapEntity
{
	[Property] public float Amplitude { get; set; }
	[Property] public float Frequency { get; set; }
	[Property] public float Duration { get; set; }
	[Property] public float Radius { get; set; }

	public override void Trigger( GameObject activator = null )
	{
		if ( CanTrigger && Networking.IsHost && Amplitude > 0 )
			HalfLife.GroundShake.Emit( Scene, WorldPosition, Amplitude, Frequency, Duration, Radius );
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );
}
