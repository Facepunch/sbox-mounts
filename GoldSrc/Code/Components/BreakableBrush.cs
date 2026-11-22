using System;
using Sandbox;

namespace GoldSrc.Maps;

[Title( "GoldSrc Breakable" )]
[Category( "GoldSrc" )]
public sealed class BreakableBrush : MapEntity, Component.IDamageable, Component.ICollisionListener
{
	[Property] public float Health { get; set; }
	[Property] public int MaterialType { get; set; }
	[Property] public bool TriggerOnly { get; set; }
	[Property] public bool BreakOnTouch { get; set; }
	[Property] public bool BreakOnPressure { get; set; }
	[Property] public bool CrowbarSensitive { get; set; }
	[Property] public bool Pushable { get; set; }
	[Property] public bool PushableBreakable { get; set; }
	[Property] public float PressureDelay { get; set; }
	[Property] public float PressureTop { get; set; }
	[Property] public SoundEvent[] DamageSounds { get; set; } = [];
	[Property] public SoundEvent[] MetalDamageSounds { get; set; } = [];
	[Property] public SoundEvent[] BreakSounds { get; set; } = [];
	private bool _broken;
	private float _breakAt = float.PositiveInfinity;
	private bool Breakable => MaterialType != 7 && (!Pushable || PushableBreakable);

	public override void Trigger( GameObject activator = null )
	{
		if ( Pushable && BrushInteraction.IsPlayer( activator ) ) return;
		if ( CanTrigger && !_broken && Breakable ) Break();
	}

	public override void TurnOn( GameObject activator = null ) => Trigger( activator );
	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	void IDamageable.OnDamage( in DamageInfo damage )
	{
		if ( !CanTrigger || _broken || TriggerOnly || !Breakable ) return;
		var amount = damage.Damage;
		if ( damage.Tags.Has( "club" ) )
		{
			if ( CrowbarSensitive && BrushInteraction.IsPlayer( damage.Attacker )
				&& (!damage.Weapon.IsValid() || damage.Weapon == damage.Attacker) )
				amount = Health;
			amount *= 2f;
		}
		if ( damage.Tags.Has( "poison" ) ) amount *= 0.1f;
		TakeDamage( amount );
	}

	private void TakeDamage( float damage )
	{
		Health -= damage;
		if ( Health <= 0f ) Break();
		else PlayDamageSound();
	}

	private void PlayDamageSound()
	{
		var sounds = MaterialType == 6 && Game.Random.Int( 0, 1 ) != 0 ? MetalDamageSounds : DamageSounds;
		var pitch = Game.Random.Int( 0, 2 ) != 0 ? 100 : 95 + Game.Random.Int( 0, 34 );
		Play( sounds, Game.Random.Float( 0.75f, 1f ), pitch / 100f );
	}

	private void Play( SoundEvent[] sounds, float volume, float pitch )
	{
		if ( sounds.Length == 0 ) return;
		var selected = sounds[Game.Random.Int( 0, sounds.Length - 1 )];
		if ( selected is null ) return;
		var handle = Sound.Play( selected, WorldPosition );
		handle.Volume = volume;
		handle.Pitch = pitch;
	}

	private void Break()
	{
		if ( _broken ) return;
		_broken = true;
		_breakAt = float.PositiveInfinity;
		TargetName = null;
		var pitch = 95 + Game.Random.Int( 0, 29 );
		if ( pitch > 97 && pitch < 103 ) pitch = 100;
		Play( BreakSounds, MathF.Min( Game.Random.Float( 0.85f, 1f ) + MathF.Abs( Health ) / 100f, 1f ), pitch / 100f );
		foreach ( var collider in GetComponentsInChildren<Collider>( true ) ) collider.Enabled = false;
		Fire( OnTrigger, detached: true );
		RemoveAfter( 0.1f );
	}

	protected override void OnFixedUpdate()
	{
		if ( CanTrigger && !_broken && Time.Now >= _breakAt ) Break();
	}

	void ICollisionListener.OnCollisionStart( Collision collision ) => Touch( collision.Other.GameObject );
	void ICollisionListener.OnCollisionUpdate( Collision collision ) => Touch( collision.Other.GameObject );

	private void Touch( GameObject other )
	{
		if ( !CanTrigger || _broken || TriggerOnly || Pushable || !Breakable
			|| _breakAt < float.PositiveInfinity || !BrushInteraction.IsPlayer( other ) ) return;
		if ( BreakOnTouch )
		{
			var damage = BrushInteraction.Velocity( other ).Length * 0.01f;
			if ( damage >= Health )
			{
				TakeDamage( damage );
				BrushInteraction.Damage( GameObject, other, damage * 0.25f, "slash" );
			}
		}
		if ( _broken || !BreakOnPressure ) return;
		var collider = other.GetComponentInParent<Collider>();
		if ( !collider.IsValid() || collider.GetWorldBounds().Mins.z < PressureTop - 2f ) return;
		PlayDamageSound();
		_breakAt = Time.Now + (PressureDelay == 0f ? 0.1f : PressureDelay);
	}
}
