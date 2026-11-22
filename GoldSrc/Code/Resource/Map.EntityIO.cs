using System;
using GoldSrc.Bsp;

namespace GoldSrc.Maps;

/// <summary>
/// GoldSrc entity IO. Every entity fires its "target" by name at runtime; because we know every
/// name up front we resolve those names to direct component references at load time and emit the
/// result as a Doo, so the whole map's logic is visible and editable in the Doo editor.
/// </summary>
partial class MapBuilder
{
	private readonly Dictionary<Entity, MapEntity> _entities = [];
	private static readonly HashSet<string> EntityFields = new( StringComparer.OrdinalIgnoreCase )
	{
		"classname", "globalname", "origin", "oldorigin", "velocity", "basevelocity", "movedir",
		"angle", "angles", "avelocity", "punchangle", "v_angle", "fixangle", "idealpitch", "pitch_speed", "ideal_yaw", "yaw_speed",
		"modelindex", "model", "viewmodel", "weaponmodel", "absmin", "absmax", "mins", "maxs", "size", "ltime", "nextthink",
		"solid", "movetype", "skin", "body", "effects", "gravity", "friction", "light_level", "frame", "scale", "sequence",
		"animtime", "framerate", "controller", "blending", "rendermode", "renderamt", "rendercolor", "renderfx",
		"health", "frags", "weapons", "takedamage", "deadflag", "view_ofs", "button", "impulse", "chain", "dmg_inflictor",
		"enemy", "aiment", "owner", "groundentity", "spawnflags", "flags", "colormap", "team", "max_health",
		"teleport_time", "armortype", "armorvalue", "waterlevel", "watertype", "target", "targetname", "netname", "message",
		"dmg_take", "dmg_save", "dmg", "dmgtime", "noise", "noise1", "noise2", "noise3", "speed",
		"air_finished", "pain_finished", "radsuit_finished"
	};

	/// <summary>
	/// Wiring is deferred until every entity exists, since an entity can target one that appears
	/// later in the lump.
	/// </summary>
	private readonly List<Action> _wiring = [];

	/// <summary>
	/// Make an entity firable under its "targetname".
	/// </summary>
	private void RegisterTarget( Entity entity, MapEntity component, string toggle, string on, string off, string kill = null )
	{
		if ( !_entities.TryAdd( entity, component ) ) return;
		component.Map = _mapState;
		component.Master = entity.GetString( "master" );
		component.Target = entity.Target;
		component.UseMethods = [off, on, toggle is null ? null : nameof( MapEntity.SetValue ), toggle];
		component.KillInput = kill;
		_mapState.Entities.Add( component );
		if ( !string.IsNullOrEmpty( entity.TargetName ) )
			_wiring.Add( () => AddRenderAppearance( entity, component ) );
	}

	private static void AddRenderAppearance( Entity entity, MapEntity component )
	{
		var renderers = component.GetComponentsInChildren<ModelRenderer>( true ).ToList();
		if ( renderers.Count == 0 ) return;
		var appearance = component.GameObject.AddComponent<RenderAppearance>( false );
		appearance.Renderers = renderers;
		appearance.InitialMode = entity.RenderMode;
		appearance.RenderMode = entity.RenderMode;
		appearance.RenderFx = entity.GetInt( "renderfx" );
		appearance.RenderAmount = entity.GetFloat( "renderamt" );
		var color = entity.RenderColor;
		appearance.RenderColor = new Color( color.x / 255f, color.y / 255f, color.z / 255f );
		appearance.CaptureMaterials();
		appearance.Enabled = true;
	}

	private void RegisterTarget( Entity entity, MapEntity component )
		=> RegisterTarget( entity, component, nameof( MapEntity.Trigger ), nameof( MapEntity.TurnOn ), nameof( MapEntity.TurnOff ), nameof( MapEntity.Kill ) );

	/// <summary>
	/// Fill in a <see cref="MapEntity.OnTrigger"/> from the entity's target/delay/killtarget keys
	/// once every name is known.
	/// </summary>
	private void WireTrigger( Entity entity, MapEntity component, UseType use = UseType.Toggle, bool delayed = true )
	{
		component.UsesTargetOutput = true;
		component.OutputUse = use;
		component.OutputDelay = delayed ? entity.GetFloat( "delay" ) : 0f;
		component.OutputKillTarget = delayed ? entity.GetString( "killtarget" ) : null;
		_wiring.Add( () => component.OnTrigger = BuildFire( component.Target, use, component.OutputDelay, component.OutputKillTarget ) );
	}
	/// <summary>
	/// The Doo an entity runs when it fires: an optional "delay" wait, then one call per resolved
	/// killtarget removal, then the targets. GoldSrc applies the delay to both.
	/// </summary>
	private Doo BuildFire( string targetName, UseType use = UseType.Toggle, float delay = 0f, string killName = null )
		=> _mapState.BuildOutput( targetName, use, delay, killName );

	/// <summary>
	/// The same, driven straight off an entity's target/delay/killtarget keys.
	/// </summary>
	private Doo BuildFire( Entity entity, UseType use = UseType.Toggle )
		=> BuildFire( entity.Target, use, entity.GetFloat( "delay" ), entity.GetString( "killtarget" ) );

	/// <summary>
	/// A multi_manager's custom keys are target names mapped to absolute delays.
	/// </summary>
	private static List<(float Delay, string Name)> ManagerEvents( Entity entity )
	{
		var events = new List<(float Delay, string Name)>();

		foreach ( var (key, value) in entity.KeyValues )
		{
			if ( key == "wait" || EntityFields.Contains( key ) )
				continue;

			if ( events.Count == 16 )
				break;

			float.TryParse( value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var delay );

			var hash = key.IndexOf( '#' );
			var name = hash >= 0 ? key[..hash] : key;

			events.Add( (delay, name) );
		}

		return events;
	}

	private static Doo.Block DelayBlock( float seconds ) => new Doo.DelayBlock
	{
		Seconds = new Doo.LiteralExpression { LiteralValue = seconds }
	};

	private static Doo.Block InvokeBlock( Component component, string method, bool activator = true )
		=> MapState.InvokeBlock( component, method, activator );

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
