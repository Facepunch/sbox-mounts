using System;
using GoldSrc.Bsp;

namespace GoldSrc;

/// <summary>
/// GoldSrc entity IO. Every entity fires its "target" by name at runtime; because we know every
/// name up front we resolve those names to direct component references at load time and emit the
/// result as a Doo, so the whole map's logic is visible and editable in the Doo editor.
/// </summary>
partial class MapLoader
{
	/// <summary>
	/// CBaseEntity::Use's USE_TYPE. USE_SET only exists for trains under player control.
	/// </summary>
	private enum UseType
	{
		Off,
		On,
		Toggle
	}

	/// <summary>
	/// One entity that can be fired by name, and the method to call for each use type.
	/// </summary>
	private sealed class Target
	{
		public Component Component;
		public string Toggle;
		public string On;
		public string Off;
		public string Kill;

		public string Method( UseType use ) => use switch
		{
			UseType.On => On,
			UseType.Off => Off,
			_ => Toggle
		};
	}

	private readonly Dictionary<string, List<Target>> _targets = new( StringComparer.Ordinal );

	/// <summary>
	/// Wiring is deferred until every entity exists, since an entity can target one that appears
	/// later in the lump.
	/// </summary>
	private readonly List<Action> _wiring = [];

	/// <summary>
	/// Make an entity firable under its "targetname".
	/// </summary>
	private void RegisterTarget( Entity entity, Component component, string toggle, string on, string off, string kill = null )
	{
		var name = entity.TargetName;
		if ( string.IsNullOrEmpty( name ) || !component.IsValid() )
			return;

		if ( !_targets.TryGetValue( name, out var list ) )
			_targets[name] = list = [];

		list.Add( new Target { Component = component, Toggle = toggle, On = on, Off = off, Kill = kill } );
	}

	private void RegisterTarget( Entity entity, MapEntity component )
		=> RegisterTarget( entity, component, nameof( MapEntity.Trigger ), nameof( MapEntity.TurnOn ), nameof( MapEntity.TurnOff ), nameof( MapEntity.Kill ) );

	/// <summary>
	/// Fill in a <see cref="MapEntity.OnTrigger"/> from the entity's target/delay/killtarget keys
	/// once every name is known.
	/// </summary>
	private void WireTrigger( Entity entity, MapEntity component, UseType use = UseType.Toggle )
	{
		_wiring.Add( () => component.OnTrigger = BuildFire( entity, use ) );
	}
	/// <summary>
	/// The Doo an entity runs when it fires: an optional "delay" wait, then one call per resolved
	/// killtarget removal, then the targets. GoldSrc applies the delay to both.
	/// </summary>
	private Doo BuildFire( string targetName, UseType use = UseType.Toggle, float delay = 0f, string killName = null )
	{
		var body = new List<Doo.Block>();

		if ( delay > 0f )
			body.Add( DelayBlock( delay ) );

		foreach ( var target in Resolve( killName ) )
		{
			if ( target.Kill is not null )
				body.Add( InvokeBlock( target.Component, target.Kill, activator: false ) );
		}

		foreach ( var target in Resolve( targetName ) )
		{
			var method = target.Method( use );
			if ( method is not null )
				body.Add( InvokeBlock( target.Component, method ) );
		}

		return body.Count > 0 ? new Doo { Body = body } : null;
	}

	/// <summary>
	/// The same, driven straight off an entity's target/delay/killtarget keys.
	/// </summary>
	private Doo BuildFire( Entity entity, UseType use = UseType.Toggle )
		=> BuildFire( entity.Target, use, entity.GetFloat( "delay" ), entity.GetString( "killtarget" ) );

	/// <summary>
	/// A multi_manager's keys are arbitrary target names mapped to the delay they fire at, so the
	/// Doo is the targets sorted by delay with the gaps between them as Delay blocks. Valve repeats
	/// a target at several delays by suffixing "#N", which is not part of the name.
	/// </summary>
	private Doo BuildManager( Entity entity )
	{
		var events = new List<(float Delay, string Name)>();

		foreach ( var (key, value) in entity.KeyValues )
		{
			if ( key is "classname" or "targetname" or "origin" or "angles" or "angle" or "spawnflags"
				or "wait" or "globalname" or "target" or "delay" or "killtarget" or "model"
				or "rendermode" or "renderamt" or "rendercolor" or "health" or "speed" )
				continue;

			if ( events.Count == 16 )
				break;

			float.TryParse( value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var delay );

			var hash = key.IndexOf( '#' );
			var name = hash >= 0 ? key[..hash] : key;

			events.Add( (MathF.Max( delay, 0f ), name) );
		}

		if ( events.Count == 0 )
			return null;

		var body = new List<Doo.Block>();
		var elapsed = 0f;

		foreach ( var (delay, name) in events.OrderBy( x => x.Delay ) )
		{
			// Doo delays are sequential, so each block only waits for the gap since the last one.
			var gap = delay - elapsed;
			if ( gap > 0f )
			{
				body.Add( DelayBlock( gap ) );
				elapsed = delay;
			}

			foreach ( var target in Resolve( name ) )
				if ( target.Toggle is not null )
					body.Add( InvokeBlock( target.Component, target.Toggle ) );
		}

		return new Doo { Body = body };
	}

	private IEnumerable<Target> Resolve( string name )
	{
		if ( string.IsNullOrEmpty( name ) || !_targets.TryGetValue( name, out var list ) )
			return [];

		return list;
	}

	private static Doo.Block DelayBlock( float seconds ) => new Doo.DelayBlock
	{
		Seconds = new Doo.LiteralExpression { LiteralValue = seconds }
	};

	private static Doo.Block InvokeBlock( Component component, string method, bool activator = true ) => new Doo.InvokeBlock
	{
		InvokeType = Doo.InvokeType.Member,
		TargetComponent = new Doo.TargetComponent
		{
			Type = Doo.TargetComponent.TargetType.Direct,
			ComponentValue = component
		},
		Member = $"{component.GetType().FullName}.{method}",
		Arguments = activator ? [new Doo.VariableExpression { VariableName = "activator" }] : null
	};

	/// <summary>
	/// GoldSrc's "triggerstate": 0 is off, 2 is toggle, anything else is on.
	/// </summary>
	private static UseType TriggerState( Entity entity ) => entity.GetInt( "triggerstate" ) switch
	{
		0 => UseType.Off,
		2 => UseType.Toggle,
		_ => UseType.On
	};
}
