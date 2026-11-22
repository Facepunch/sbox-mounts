using System;
using Sandbox;

namespace GoldSrc.Maps;

public enum UseType
{
	Off,
	On,
	Set,
	Toggle
}

/// <summary>
/// Base for every GoldSrc entity that can be fired by another entity's "target" key.
/// </summary>
[Category( "GoldSrc" )]
[Icon( "share" )]
public abstract class MapEntity : Component
{
	[Property, Hide] public int EntityIndex { get; set; } = -1;
	[Property] public MapState Map { get; set; }
	[Property] public string Master { get; set; }
	[Property] public string Target { get; set; }
	[Property, Hide] public string[] UseMethods { get; set; } = [nameof( TurnOff ), nameof( TurnOn ), nameof( SetValue ), nameof( Trigger )];
	[Property, Hide] public string KillInput { get; set; } = nameof( Kill );
	[Property, Hide] public bool UsesTargetOutput { get; set; }
	[Property, Hide] public UseType OutputUse { get; set; } = UseType.Toggle;
	[Property, Hide] public float OutputDelay { get; set; }
	[Property, Hide] public string OutputKillTarget { get; set; }

	public virtual void ChangeTarget( string target )
	{
		Target = target;
		if ( UsesTargetOutput )
			OnTrigger = Map.BuildOutput( Target, OutputUse, OutputDelay, OutputKillTarget );
	}

	protected MapEntity Caller { get; private set; }

	public void Use( GameObject activator, MapEntity caller, int type, float value )
	{
		if ( !CanTrigger ) return;
		var previous = Caller;
		Caller = caller;
		try
		{
			switch ( (UseType)type )
			{
				case UseType.Off: TurnOff( activator ); break;
				case UseType.On: TurnOn( activator ); break;
				case UseType.Set: SetValue( value, activator ); break;
				case UseType.Toggle: Trigger( activator ); break;
				default: throw new ArgumentOutOfRangeException( nameof( type ) );
			}
		}
		finally
		{
			Caller = previous;
		}
	}

	public virtual void SetValue( float value, GameObject activator = null ) => Trigger( activator );

	protected bool IsMasterTriggered( GameObject activator ) =>
		string.IsNullOrEmpty( Master ) || Map.IsMasterTriggered( Master, activator );

	/// <summary>
	/// The entity's "targetname".
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
	protected void Fire( Doo doo, GameObject activator = null, float value = 0f, bool detached = false )
	{
		if ( doo is null || !CanTrigger )
			return;

		if ( UsesTargetOutput && OutputDelay != 0f && ReferenceEquals( doo, OnTrigger ) )
		{
			var go = new GameObject( true, "DelayedUse" ) { Parent = Map.GameObject };
			var pending = go.AddComponent<DelayedUse>();
			pending.Map = Map;
			pending.Target = Target;
			Map.Entities.Add( pending );
			var player = activator.IsValid() ? activator.GetComponentInParent<PlayerController>() : null;
			pending.Begin( doo, player.IsValid() ? player.GameObject : null );
			return;
		}

		if ( _depth >= MaxDepth )
		{
			Log.Warning( $"GoldSrc: IO chain exceeded {MaxDepth} nested fires at '{TargetName}' ({GetType().Name}) - the map has a zero-delay loop." );
			return;
		}

		_depth++;

		try
		{
			Component host = detached && Map.IsValid() ? Map : this;
			host.RunDoo( doo, c =>
			{
				c.SetArgument( "activator", activator );
				c.SetArgument( "caller", this );
				c.SetArgument( "value", value );
			} );
		}
		finally
		{
			_depth--;
		}
	}

	protected void RemoveAfter( float seconds )
	{
		Map.ScheduleRemoval( this, seconds );
	}

	private const int MaxDepth = 32;

	[ThreadStatic] private static int _depth;
}
