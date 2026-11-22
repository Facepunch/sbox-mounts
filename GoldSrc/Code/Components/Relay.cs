using Sandbox;

namespace GoldSrc.Maps;

/// <summary>
/// Automatic and manually activated forwarding triggers.
/// </summary>
[Title( "GoldSrc Relay" )]
[Icon( "call_split" )]
public sealed class Relay : MapEntity
{
	/// <summary>
	/// trigger_auto. GoldSrc fires these once, shortly after the map spawns, and that is what
	/// starts scripted sequences like Half-Life's intro tram.
	/// </summary>
	[Property] public bool FireOnSpawn { get; set; }

	/// <summary>
	/// Remove the trigger after its first use.
	/// </summary>
	[Property] public bool FireOnce { get; set; }

	/// <summary>
	/// Whether another output can start while the previous one is running.
	/// </summary>
	[Property] public bool AllowOverlap { get; set; } = true;
	[Property] public bool SelfActivator { get; set; }
	[Property] public string GlobalState { get; set; }
	[Property, Hide] public bool Fired { get; set; }
	[Property, Hide] public bool SpawnChecked { get; set; }

	private float _spawnTime;

	protected override void OnStart()
	{
		_spawnTime = Time.Now + 0.1f;
	}

	protected override void OnFixedUpdate()
	{
		if ( !FireOnSpawn || SpawnChecked || Time.Now < _spawnTime ) return;
		SpawnChecked = true;
		if ( string.IsNullOrEmpty( GlobalState ) || Map.GetGlobal( GlobalState ) == 1 )
			Trigger( GameObject );
	}

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		if ( FireOnce && Fired )
			return;

		if ( !AllowOverlap && IsRunningDoo( OnTrigger ) )
			return;

		Fired = true;
		Fire( OnTrigger, SelfActivator ? GameObject : activator, detached: FireOnce );
		if ( FireOnce ) RemoveAfter( 0.1f );
	}

	public override void TurnOn( GameObject activator = null ) => Trigger( activator );
	public override void TurnOff( GameObject activator = null ) => Trigger( activator );
}
