namespace Diesel;

/// <summary>
/// A heist's authored ambience from <c>levels/&lt;map&gt;/world_sounds</c>: positioned <c>sound_emitters</c> (vending machines,
/// AC hum, copiers, idling cars…) loop in place, and the level's <c>default_ambience</c> bed loops non-spatially.
/// <c>default_occasional</c> one-shots repeat at random intervals through a <see cref="SoundPointComponent"/>.
/// Loops play as native seamless loops, one per layered sound of the event (each layer loops at its own length, as in
/// Wwise). Each machine plays its own copy (map content exists on every client).
/// </summary>
[Hide]
public sealed class PaydayAmbientSound : Component
{
	[Property] public string SoundPath { get; set; }
	/// <summary>Event name, for native looping (<see cref="Loop"/>).</summary>
	[Property] public string EventName { get; set; }
	/// <summary>Non-spatial (level bed / occasionals) rather than positioned.</summary>
	[Property] public bool Global { get; set; }
	/// <summary>Loop seamlessly instead of repeating at <see cref="MinInterval"/>..<see cref="MaxInterval"/>.</summary>
	[Property] public bool Loop { get; set; } = true;
	[Property] public float MinInterval { get; set; } = 0.05f;
	[Property] public float MaxInterval { get; set; } = 0.05f;

	[ConVar( "payday_ambience", Help = "Play PAYDAY map ambience and sound emitters." )]
	public static bool Enabled { get; set; } = true;

	readonly List<SoundHandle> loops = [];
	List<SoundEvent> loopLayers;

	protected override async Task OnLoad()
	{
		if ( !Enabled || string.IsNullOrEmpty( SoundPath ) ) return;
		if ( Loop && Sandbox.Mounting.Directory.Get( "payday" ) is PaydayMount mount
			&& mount.GetLoopLayers( EventName, 2000 ) is { Count: > 0 } layers )
		{
			loopLayers = layers;
			if ( Active ) StartLoops();
			return;
		}
		SoundEvent sound;
		try { sound = await ResourceLibrary.LoadAsync<SoundEvent>( SoundPath ); }
		catch ( Exception e ) { Log.Warning( $"PAYDAY ambience {SoundPath}: {e.Message}" ); return; }
		if ( sound is null || !this.IsValid() ) return;
		var point = AddComponent<SoundPointComponent>();
		point.SoundEvent = sound;
		point.Repeat = true;
		point.MinRepeatTime = MinInterval;
		point.MaxRepeatTime = MaxInterval;
		if ( Global ) { point.SoundOverride = true; point.Force2d = true; }
		point.StartSound();
	}

	void StartLoops()
	{
		if ( loopLayers is null || loops.Count > 0 ) return;
		foreach ( var layer in loopLayers )
		{
			var handle = GameObject.PlaySound( layer );
			if ( handle is null ) continue;
			if ( Global ) { handle.ListenLocal = true; handle.DistanceAttenuation = false; }
			loops.Add( handle );
		}
	}

	protected override void OnEnabled()
	{
		base.OnEnabled();
		StartLoops();
	}

	protected override void OnDisabled()
	{
		foreach ( var handle in loops ) handle?.Stop();
		loops.Clear();
		base.OnDisabled();
	}
}
