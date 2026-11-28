namespace Source1.Hl2;

public sealed class BugbaitSensor : Component, IMapInput, IMapOutputs
{
	Doo.Block IMapInput.BuildInput( string input, string parameter, bool hasUser ) => input.ToLowerInvariant() switch
	{
		"enable" when parameter.Length == 0 => this.Call( nameof( StartListening ) ),
		"disable" when parameter.Length == 0 => this.Call( nameof( StopListening ) ),
		"toggle" when parameter.Length == 0 => this.Call( nameof( ToggleListening ) ),
		_ => null
	};

	void IMapOutputs.Wire( System.Func<string, bool, Doo> output ) => OnBaited = output( "OnBaited", true );

	[Property] public bool Listening { get; set; }
	[ConVar( "bugbait_radius", ConVarFlags.Server, Help = "Detection radius for bugbait sensors without a map override." )]
	public static float DefaultRadius { get; set; } = 512;

	[Property] public float Radius { get; set; }
	[Property] public bool DetectThrows { get; set; } = true;
	[Property] public bool DetectSqueezes { get; set; } = true;
	[Property] public bool SuppressCall { get; set; }
	[Property, Doo.ArgumentHint<GameObject>( "user" )] public Doo OnBaited { get; set; }

	public void StartListening() => Listening = true;
	public void StopListening() => Listening = false;
	public void ToggleListening() => Listening = !Listening;

	internal static bool Activate( Scene scene, Vector3 position, GameObject thrower, bool squeezed )
	{
		var suppress = false;
		foreach ( var sensor in scene.GetAllComponents<BugbaitSensor>() )
		{
			if ( !sensor.Listening || !(squeezed ? sensor.DetectSqueezes : sensor.DetectThrows) )
				continue;
			if ( sensor.WorldPosition.Distance( position ) >= (sensor.Radius == 0 ? DefaultRadius : sensor.Radius) )
				continue;

			sensor.RunDoo( sensor.OnBaited, context => context.SetArgument( "user", thrower ) );
			suppress |= sensor.SuppressCall;
		}
		return suppress;
	}
}
