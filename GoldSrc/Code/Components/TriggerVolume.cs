using Sandbox;
using System.Collections.Generic;

namespace GoldSrc;

/// <summary>
/// Touch/cooldown triggers and periodic trigger_hurt damage, with separate GoldSrc flag rules.
/// </summary>
[Title( "GoldSrc Trigger Volume" )]
[Icon( "sensors" )]
public sealed class TriggerVolume : MapEntity, Component.ITriggerListener
{
	/// <summary>
	/// trigger_once.
	/// </summary>
	[Property] public bool FireOnce { get; set; }

	/// <summary>
	/// The "wait" key: how long a trigger_multiple stays dead after firing.
	/// </summary>
	[Property] public float Wait { get; set; } = 0.2f;

	/// <summary>
	/// SF_TRIGGER_NOCLIENTS - the volume ignores players.
	/// </summary>
	[Property] public bool NoClients { get; set; }
	[Property] public bool AllowMonsters { get; set; }
	[Property] public bool AllowPushables { get; set; }
	[Property] public bool Hurt { get; set; }
	[Property] public float Damage { get; set; }
	[Property] public bool StartOff { get; set; }
	[Property] public bool ClientsOnly { get; set; }
	[Property] public bool ClientsOnlyFire { get; set; }
	[Property] public bool TargetOnce { get; set; }
	[Property] public Vector3 FacingDirection { get; set; }

	private bool _fired;
	private float _nextFire;
	private float _nextDamage;
	private bool _targetFired;
	private Collider _collider;
	private readonly HashSet<IDamageable> _damaged = [];
	private readonly List<Collider> _touching = [];

	protected override void OnStart()
	{
		_collider = GetComponent<Collider>();
		if ( Hurt && StartOff && _collider.IsValid() )
			_collider.Enabled = false;
	}

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || !Hurt || !_collider.IsValid() ) return;
		_collider.Enabled = !_collider.Enabled;
	}

	public override void TurnOn( GameObject activator = null ) => Trigger( activator );
	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	private void Touch( Collider other )
	{
		if ( !CanTrigger || !other.IsValid() || Hurt ) return;
		if ( _fired && (FireOnce || Wait <= 0f) || Time.Now < _nextFire ) return;
		var player = IsPlayer( other );
		var monster = AllowMonsters && other.GetComponentInParent<Monster>().IsValid();
		var pushable = AllowPushables && other.GameObject.Tags.Has( "goldsrc_pushable" );
		if ( !(player && !NoClients || monster || pushable) ) return;
		if ( Vector3.Dot( FacingDirection, other.WorldRotation.Forward ) < 0f ) return;

		_fired = true;
		_nextFire = Time.Now + Wait;
		Fire( OnTrigger, other.GameObject );
	}

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( !Hurt ) Touch( other );
	}

	protected override void OnFixedUpdate()
	{
		if ( !CanTrigger || !_collider.IsValid() || !_collider.Enabled ) return;
		_touching.Clear();
		_touching.AddRange( _collider.Touching );
		if ( !Hurt )
		{
			foreach ( var other in _touching )
			{
				if ( !CanTrigger || !_collider.Enabled ) return;
				Touch( other );
			}
			return;
		}

		if ( Time.Now < _nextDamage ) return;
		_damaged.Clear();
		var touched = false;
		foreach ( var other in _touching )
		{
			if ( !CanTrigger || !_collider.Enabled ) return;
			if ( !other.IsValid() ) continue;
			var player = IsPlayer( other );
			if ( player ? NoClients : ClientsOnly ) continue;
			foreach ( var target in other.GetComponentsInParent<IDamageable>() )
			{
				if ( !_damaged.Add( target ) ) continue;
				touched = true;
				var damage = new DamageInfo
				{
					Damage = Damage * 0.5f,
					Attacker = GameObject,
					Origin = WorldPosition,
					Position = other.WorldPosition
				};
				target.OnDamage( damage );
				if ( !CanTrigger ) return;
				if ( (!ClientsOnlyFire || player) && (!TargetOnce || !_targetFired) )
				{
					_targetFired = true;
					Fire( OnTrigger, other.GameObject );
				}
			}
		}

		if ( touched ) _nextDamage = Time.Now + 0.5f;
	}

	private static bool IsPlayer( Collider other )
	{
		if ( !other.IsValid() )
			return false;

		return other.GameObject.Tags.Has( "player" )
			|| other.GetComponentInParent<PlayerController>().IsValid();
	}
}
