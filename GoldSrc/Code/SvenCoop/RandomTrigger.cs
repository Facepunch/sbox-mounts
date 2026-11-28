using GoldSrc.Maps;

namespace GoldSrc.SvenCoop;

public sealed class RandomTrigger : MapEntity
{
	[Property] public List<string> Targets { get; set; } = [];
	[Property] public bool Timed { get; set; }
	[Property] public bool Unique { get; set; }
	[Property] public bool Reusable { get; set; }
	[Property] public bool StartOn { get; set; }
	[Property] public bool Once { get; set; }
	[Property] public float MinDelay { get; set; }
	[Property] public float MaxDelay { get; set; }

	TimeUntil? _next;
	GameObject _activator;
	int _used;

	protected override void OnStart()
	{
		if ( Timed && StartOn && Networking.IsHost ) Schedule();
	}

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || !Networking.IsHost ) return;
		if ( !Timed ) Pick( activator );
		else if ( _next.HasValue ) _next = null;
		else
		{
			_activator = activator;
			Schedule();
		}
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	void Schedule() => _next = Game.Random.Float( MinDelay, System.Math.Max( MinDelay, MaxDelay ) );

	protected override void OnFixedUpdate()
	{
		if ( _next is not { } remaining || remaining > 0 ) return;
		if ( Once ) _next = null;
		else Schedule();
		Pick( _activator );
	}

	void Pick( GameObject activator )
	{
		if ( Targets.Count == 0 || _used >= Targets.Count ) return;
		var index = Game.Random.Int( _used, Targets.Count - 1 );
		var name = Targets[index];
		if ( Unique )
		{
			(Targets[index], Targets[_used]) = (Targets[_used], Targets[index]);
			if ( ++_used == Targets.Count )
			{
				if ( Reusable ) _used = 0;
				else _next = null;
			}
		}
		Fire( Map.BuildOutput( name ), activator );
	}
}
