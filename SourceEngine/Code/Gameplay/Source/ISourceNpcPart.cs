using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>
/// A component on a Source NPC that reacts to what happens to that NPC, such as a zombie's headcrab or a
/// manhack's sparks. The NPC calls these itself, on the components of its own GameObject.
/// </summary>
public interface ISourceNpcPart
{
	/// <summary>
	/// The NPC is about to take a hit. Called on the host from its OnDamaging, before the game's rules see the
	/// hit: change the damage or cancel it.
	/// </summary>
	void OnNpcDamaging( Component.IDamageable.DamageEvent damage ) { }

	/// <summary>
	/// The NPC took a hit. Called on the host from its OnDamaged, with the settled hit.
	/// </summary>
	void OnNpcHurt( Component.IDamageable.DamageEvent damage ) { }

	/// <summary>
	/// The NPC is dead. Called on the host from its Die, before it leaves its ragdoll.
	/// </summary>
	void OnNpcDeath( Component.IDamageable.DeathEvent death ) { }

	/// <summary>
	/// Let the parts of an NPC shape a hit it is about to take.
	/// </summary>
	public static void PreDamage( BaseNpc npc, Component.IDamageable.DamageEvent damage )
	{
		foreach ( var part in npc.GetComponents<ISourceNpcPart>() )
			part.OnNpcDamaging( damage );
	}

	/// <summary>
	/// Tell the parts of an NPC that it took a hit.
	/// </summary>
	public static void Hurt( BaseNpc npc, Component.IDamageable.DamageEvent damage )
	{
		foreach ( var part in npc.GetComponents<ISourceNpcPart>() )
			part.OnNpcHurt( damage );
	}

	/// <summary>
	/// Tell the parts of an NPC that it died.
	/// </summary>
	public static void Death( BaseNpc npc, Component.IDamageable.DeathEvent death )
	{
		foreach ( var part in npc.GetComponents<ISourceNpcPart>() )
			part.OnNpcDeath( death );
	}
}
