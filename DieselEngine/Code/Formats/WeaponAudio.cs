using System.Text;

namespace Diesel;

/// <summary>Animation-state callbacks supply the actual cue names, including shared pistol foley.</summary>
sealed class WeaponAudio
{
	public Dictionary<string, WwiseBank> Events { get; } = [];
	public Dictionary<string, Dictionary<string, string>> Cues { get; } = [];
	public string Fire { get; }
	public string Dry { get; }
	public string Equip { get; }
	public string Stop { get; }
	public WeaponAudio( BundleDatabase db, WeaponDefinition definition, Dictionary<string, WwiseBank> banks )
	{
		WwiseBank Find( string name ) => banks.Values.FirstOrDefault( b => b.HasEvent( name ) );
		string Add( string name )
		{
			var bank = banks[definition.Bank].HasEvent( name ) ? banks[definition.Bank] : Find( name );
			if ( bank is null ) return null;
			Events.TryAdd( name, bank );
			return name;
		}
		Fire = Add( definition.Prefix + "_fire" ) ?? throw new InvalidDataException( "Missing weapon firing sound." );
		Dry = Add( definition.Prefix + "_dryfire" );
		Equip = Add( definition.Prefix + "_equip" );
		Stop = definition.Automatic ? Add( definition.Prefix + "_stop" ) : null;
		if ( definition.Grenade ) Add( "gl_explode" );
		var states = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "animation_states", "anims/fps/fps_spawn" ) ) ).Elements( "state" ).ToArray();
		foreach ( var clip in definition.Clips )
		{
			string state = clip switch {
				"reload_enter" => $"fps/reload/{definition.State}/enter",
				"reload_exit" => $"fps/reload/{definition.State}/exit",
				"reload" when definition.Shotgun => $"fps/reload/{definition.State}/loop",
				"reload" or "reload_not_empty" => $"fps/{clip}/{definition.State}",
				_ => $"fps/{clip}"
			};
			var map = new Dictionary<string, string>();
			var triggers = new AnimationFile( db.Find( "animation", definition.ClipPath( clip ) ) ).Cues.Select( c => c.Name ).ToHashSet();
			var node = states.FirstOrDefault( s => s.Attribute( "name" ) == state );
			foreach ( var key in node?.Element( "keys" )?.Elements( "key" ) ?? [] )
				if ( key.Attribute( "callback" ) == "play_sound" && key.Attribute( "at" ) == "trigger" && triggers.Contains( key.Attribute( "trigger" ) ) && Add( key.Attribute( "param1" ) ) is { } sound )
					map[key.Attribute( "trigger" )] = sound;
			Cues[clip] = map;
		}
	}
}
