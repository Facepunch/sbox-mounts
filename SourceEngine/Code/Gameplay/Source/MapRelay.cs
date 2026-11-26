using System;

namespace Source1;

public sealed class MapRelay : Component
{
	[Property] public bool Disabled { get; set; }
	[Property] public bool Once { get; set; }
	[Property] public bool FastRetrigger { get; set; }
	[Property] public List<MapOutputConnection> OnTrigger { get; set; } = [];

	TimeUntil _refire;
	bool _firing;

	public void Trigger( GameObject user )
	{
		if ( Disabled || _refire > 0 ) return;
		if ( _firing )
		{
			if ( FastRetrigger && !Once ) _ = Retrigger( user );
			return;
		}

		_firing = true;
		var delay = 0f;
		foreach ( var connection in OnTrigger )
		{
			connection.Fire( this, user );
			if ( connection.Remaining != 0 ) delay = Math.Max( delay, connection.Delay );
		}
		_firing = false;
		if ( Once ) GameObject.Destroy();
		else if ( !FastRetrigger ) _refire = delay + 0.001f;
	}

	// Source queues inputs. A relay loop must unwind before its next trigger runs.
	async Task Retrigger( GameObject user )
	{
		await GameTask.Yield();
		if ( IsValid ) Trigger( user );
	}

	public void TurnOn() => Disabled = false;
	public void TurnOff() => Disabled = true;
	public void Toggle() => Disabled = !Disabled;
	public void CancelPending()
	{
		StopAllDoo();
		_refire = 0;
	}
}
