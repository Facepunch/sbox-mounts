using Sandbox;

namespace GoldSrc;

/// <summary>
/// The pure-IO entities: trigger_relay, trigger_auto and multi_manager. All three do nothing but
/// forward a trigger, so they differ only in when they fire and whether they fire more than once.
/// A multi_manager's staggered delays live in <see cref="MapEntity.OnTrigger"/> as interleaved
/// Delay blocks, which is exactly how the Doo editor draws them.
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
	/// SF_RELAY_FIREONCE / SF_AUTO_FIREONCE. GoldSrc removes the entity; we just latch, so any
	/// delayed blocks already queued still get to run.
	/// </summary>
	[Property] public bool FireOnce { get; set; }

	/// <summary>
	/// A multi_manager only accepts a retrigger while a batch is still running if it is
	/// multithreaded (SF_MULTIMAN_THREAD). Otherwise the retrigger is dropped.
	/// </summary>
	[Property] public bool AllowOverlap { get; set; } = true;

	private bool _fired;
	private float _spawnTime;

	protected override void OnStart()
	{
		_spawnTime = Time.Now + 0.1f;
	}

	protected override void OnFixedUpdate()
	{
		if ( !FireOnSpawn || _fired || Time.Now < _spawnTime ) return;
		Trigger();
	}

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		if ( FireOnce && _fired )
			return;

		if ( !AllowOverlap && IsRunningDoo( OnTrigger ) )
			return;

		_fired = true;
		base.Trigger( activator );
	}

	// Neither CTriggerRelay::Use nor CMultiManager::ManagerUse look at the use type they were
	// called with - they always fire, and pick their own type for what they fire in turn.
	public override void TurnOn( GameObject activator = null ) => Trigger( activator );
	public override void TurnOff( GameObject activator = null ) => Trigger( activator );
}
