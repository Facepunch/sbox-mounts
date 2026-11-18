namespace Renderware;

/// <summary>
/// The whole map as one scene: static geometry batched into cells with texture arrays, dynamic props, collision,
/// 2dfx lights, and the time cycle's sun, sky and fog, plus water and ambience.
/// </summary>
sealed partial class WorldLoader : SceneLoader<GTAMount>
{
	/// <summary>Time of day the world is built at (0-24, fractions blend between timecyc rows).</summary>
	public float Hour => 13f;

	/// <summary>Time cycle weather by name (SUNNY, EXTRASUNNY_LA...), null for the game's first.</summary>
	public string Weather => "SUNNY";

	/// <summary>Whether to spawn a corona sprite for each 2dfx light.</summary>
	public bool Coronas => false;

	protected override void BuildScene()
	{
		var timeCycle = Host.TimeCycle;
		int weather = timeCycle?.FindWeather( Weather ?? "" ) ?? 0;
		var environment = timeCycle?.Get( weather, Hour ) ?? TimeCycle.Entry.Default;
		var weatherName = timeCycle?.Weathers[weather] ?? "CLEAR";

		BuildEnvironment( environment, weatherName );

		if ( Host.Water is { } water )
			BuildWater( water, environment.Water );

		BuildAmbience( Host.Water );
		BuildMap();
	}
}
