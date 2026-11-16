using System;

namespace Doom;

/// <summary>
/// A damaging sector floor (nukage, lava, blood).
/// </summary>
[Title( "Doom Damage Floor" )]
[Category( "Doom" )]
[Icon( "local_fire_department" )]
public sealed class DoomDamageFloor : Component, Component.ITriggerListener
{
	[Property] public float Damage { get; set; } = 10f;

	/// <summary>Seconds between hits: 32 tics.</summary>
	[Property] public float Interval { get; set; } = 32f / DoomSector.TicRate;

	// refcounted: so we can have multiple triggers on the same sector and not worry about event order
	private readonly Dictionary<IDamageable, int> _inside = new();

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		var target = other.GetComponentInParent<IDamageable>();
		if ( target is null ) return;

		_inside[target] = _inside.GetValueOrDefault( target ) + 1;
	}

	void ITriggerListener.OnTriggerExit( Collider other )
	{
		var target = other.GetComponentInParent<IDamageable>();
		if ( target is null || !_inside.TryGetValue( target, out var count ) ) return;

		if ( count <= 1 ) _inside.Remove( target );
		else _inside[target] = count - 1;
	}

	protected override void OnDisabled() => _inside.Clear();

	private long CurrentTick() => (long)MathF.Floor( Time.Now / Interval );

	private long _lastTick;
	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;

		var tick = CurrentTick();
		if ( tick == _lastTick ) return;
		_lastTick = tick;

		foreach ( var target in _inside.Keys.ToArray() )
		{
			target.OnDamage( new DamageInfo
			{
				Attacker = GameObject,
				Origin = WorldPosition,
				Position = WorldPosition,
				Damage = Damage,
			} );
		}
	}
}
