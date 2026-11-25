namespace Diesel;

/// <summary>
/// A heist's authored ambience from <c>levels/&lt;map&gt;/world_sounds</c>: positioned <c>sound_emitters</c> (vending machines,
/// AC hum, copiers, idling cars…) loop in place, and the level's <c>default_ambience</c> bed loops non-spatially.
/// <c>default_occasional</c> one-shots repeat at random intervals. The sound loads asynchronously from the mount, then plays
/// through a <see cref="SoundPointComponent"/>; each machine plays its own copy (map content exists on every client).
/// </summary>
[Hide]
public sealed class PaydayAmbientSound : Component
{
	[Property] public string SoundPath { get; set; }
	/// <summary>Non-spatial (level bed / occasionals) rather than positioned.</summary>
	[Property] public bool Global { get; set; }
	/// <summary>Seconds between restarts. Loops use a tiny gap (restart as soon as the rendered take ends).</summary>
	[Property] public float MinInterval { get; set; } = 0.05f;
	[Property] public float MaxInterval { get; set; } = 0.05f;

	[ConVar( "payday_ambience", Help = "Play PAYDAY map ambience and sound emitters." )]
	public static bool Enabled { get; set; } = true;

	protected override async Task OnLoad()
	{
		if ( !Enabled || string.IsNullOrEmpty( SoundPath ) ) return;
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
}
