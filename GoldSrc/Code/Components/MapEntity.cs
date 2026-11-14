using System;
using Sandbox;

namespace GoldSrc;

/// <summary>
/// Base for every GoldSrc entity that can be fired by another entity's "target" key.
///
/// GoldSrc IO is a single call - CBaseEntity::Use( pActivator, pCaller, USE_TYPE, value ) - where
/// the caller picks the USE_TYPE. Rather than pass a use type as an argument (Doo has no way to
/// branch on it) each use type is its own method, so the wiring stays readable in the Doo editor.
/// </summary>
[Category( "GoldSrc" )]
[Icon( "share" )]
public abstract class MapEntity : Component
{
	/// <summary>
	/// The entity's "targetname". Only kept so the graph is readable; all wiring is resolved to
	/// direct component references when the map loads.
	/// </summary>
	[Property] public string TargetName { get; set; }

	/// <summary>
	/// Everything this entity's "target" key pointed at, with the "delay" key baked in as a
	/// leading Delay block.
	/// </summary>
	[Property, Group( "Events" )] public Doo OnTrigger { get; set; }

	/// <summary>USE_TOGGLE - the default for almost every GoldSrc trigger.</summary>
	public virtual void Trigger( GameObject activator = null ) => Fire( OnTrigger, activator );

	/// <summary>USE_ON.</summary>
	public virtual void TurnOn( GameObject activator = null ) => Trigger( activator );

	/// <summary>USE_OFF.</summary>
	public virtual void TurnOff( GameObject activator = null ) { }

	/// <summary>
	/// The receiving end of a "killtarget" key. GoldSrc removes the entity outright.
	/// </summary>
	public void Kill()
	{
		if ( !CanTrigger ) return;
		_killed = true;
		GameObject.Destroy();
	}

	protected bool CanTrigger => IsValid && !_killed && !GameObject.IsDestroyed;
	private bool _killed;

	/// <summary>
	/// Runs a Doo with a re-entrancy guard.
	///
	/// A zero-delay cycle in a map's IO graph - a pair of entities that target each other with no
	/// delay between them - recurses until the stack runs out. Real maps contain these (Half-Life's
	/// looping scripted_sequence walk cycles are one), so a mount cannot assume the data is acyclic.
	/// </summary>
	protected void Fire( Doo doo, GameObject activator = null )
	{
		if ( doo is null || !CanTrigger )
			return;

		if ( _depth >= MaxDepth )
		{
			Log.Warning( $"GoldSrc: IO chain exceeded {MaxDepth} nested fires at '{TargetName}' ({GetType().Name}) - the map has a zero-delay loop." );
			return;
		}

		_depth++;

		try
		{
			RunDoo( doo, c => c.SetArgument( "activator", activator ) );
		}
		finally
		{
			_depth--;
		}
	}

	private const int MaxDepth = 32;

	[ThreadStatic] private static int _depth;
}
