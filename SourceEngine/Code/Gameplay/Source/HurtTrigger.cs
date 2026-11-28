using System;

namespace Source1;

/// <summary>Hurts what stands in it once per cycle, like Source's trigger_hurt.</summary>
[Hide]
public sealed class HurtTrigger : Component, Component.ITriggerListener
{
	public const int HurtsPlayers = 1;
	public const int HurtsOthers = 2 | 4;

	[Property] public float Damage { get; set; } = 10;
	[Property] public float Cycle { get; set; } = 1;
	[Property] public int SpawnFlags { get; set; } = HurtsPlayers;

	readonly HashSet<GameObject> _touching = [];
	TimeUntil _next;

	public void TurnOn() => SetListening( true );
	public void TurnOff() => SetListening( false );
	public void Toggle() => SetListening( !GetComponentsInChildren<Collider>( true ).Any( c => c.Enabled ) );

	void SetListening( bool enabled )
	{
		foreach ( var collider in GetComponentsInChildren<Collider>( true ) ) collider.Enabled = enabled;
		if ( !enabled ) _touching.Clear();
	}

	void ITriggerListener.OnTriggerEnter( GameObject other )
	{
		if ( Networking.IsHost ) _touching.Add( other.Root );
	}

	void ITriggerListener.OnTriggerExit( GameObject other )
	{
		if ( Networking.IsHost ) _touching.Remove( other.Root );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || Damage <= 0 || _next > 0 ) return;

		_touching.RemoveWhere( x => !x.IsValid() );
		var hurt = false;
		foreach ( var target in _touching.ToArray() )
		{
			if ( target.GetComponentInChildren<Component.IDamageable>() is not { } victim ) continue;
			if ( (SpawnFlags & (target.GetComponent<PlayerController>().IsValid() ? HurtsPlayers : HurtsOthers)) == 0 ) continue;

			hurt = true;
			victim.OnDamage( new DamageInfo { Damage = Damage, Attacker = GameObject, Origin = WorldPosition, Position = target.WorldPosition } );
		}

		if ( hurt ) _next = Math.Max( Cycle, 0.05f );
	}
}
