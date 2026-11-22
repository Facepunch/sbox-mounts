using Sandbox;
using System.Collections.Generic;

namespace GoldSrc.Maps;

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
	[Property] public int DamageTypes { get; set; }
	[Property] public bool StartOff { get; set; }
	[Property] public bool ClientsOnly { get; set; }
	[Property] public bool ClientsOnlyFire { get; set; }
	[Property] public bool TargetOnce { get; set; }

	private bool _fired;
	private float _nextFire;
	private float _nextDamage;
	private float _lastDamageTime = -1f;
	private float _damageFrame = -1f;
	private bool _targetFired;
	private bool _warnedHealing;
	private Collider _collider;
	private readonly HashSet<IDamageable> _damaged = [];
	private readonly HashSet<GameObject> _windowPlayers = [];
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
		var controller = other.GetComponentInParent<PlayerController>();
		var activator = controller.IsValid() ? controller.GameObject : other.GameObject;
		if ( !IsMasterTriggered( activator ) ) return;

		_fired = true;
		_nextFire = Time.Now + Wait;
		bool remove = FireOnce || Wait <= 0f;
		Fire( OnTrigger, activator, detached: remove );
		if ( remove ) RemoveAfter( 0.1f );
	}

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( Hurt ) HurtTouch( other );
		else Touch( other );
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

		foreach ( var other in _touching )
		{
			if ( !CanTrigger || !_collider.Enabled ) return;
			HurtTouch( other );
		}
	}

	private void HurtTouch( Collider other )
	{
		if ( !CanTrigger || !other.IsValid() ) return;
		var player = IsPlayer( other );
		if ( player ? NoClients : ClientsOnly ) return;
		var targets = other.GetComponentsInParent<IDamageable>().ToArray();
		if ( targets.Length == 0 ) return;
		if ( Damage < 0f && !_warnedHealing )
		{
			_warnedHealing = true;
			Log.Warning( $"GoldSrc: healing trigger '{TargetName}' requires game-provided health integration." );
		}
		var controller = other.GetComponentInParent<PlayerController>();
		var activator = controller.IsValid() ? controller.GameObject : other.GameObject;
		if ( Time.Now < _nextDamage )
		{
			if ( Time.Now != _lastDamageTime
				&& (!Networking.IsActive || !player || !_windowPlayers.Add( activator )) ) return;
		}
		else
		{
			_windowPlayers.Clear();
			if ( player ) _windowPlayers.Add( activator );
		}
		if ( _damageFrame != Time.Now )
		{
			_damageFrame = Time.Now;
			_damaged.Clear();
		}
		var touched = false;
		foreach ( var target in targets )
		{
			if ( !_damaged.Add( target ) ) continue;
			touched = true;
			if ( Damage >= 0f )
				target.OnDamage( new EntityDamage( Damage * 0.5f, DamageTypes, GameObject, other.WorldPosition ) );
			if ( !CanTrigger ) return;
		}
		if ( !touched ) return;
		_lastDamageTime = Time.Now;
		_nextDamage = Time.Now + 0.5f;
		if ( (!ClientsOnlyFire || player) && (!TargetOnce || !_targetFired) )
		{
			_targetFired = true;
			Fire( OnTrigger, activator );
		}
	}

	private static bool IsPlayer( Collider other )
	{
		if ( !other.IsValid() )
			return false;

		return other.GameObject.Tags.Has( "player" )
			|| other.GetComponentInParent<PlayerController>().IsValid();
	}
}
