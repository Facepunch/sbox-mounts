namespace Diesel;

/// <summary>
/// A level's compiled mission scripts. <c>levels/&lt;map&gt;/mission.mission</c> indexes one file per continent
/// (<c>{ world = { file = "world/world" } }</c>). Each <c>levels/&lt;map&gt;/&lt;file&gt;.mission</c> holds named scripts
/// <c>{ activate_on_parsed, elements[] }</c>. Every element is <c>{ class, editor_name, id, module?, values }</c>, and
/// <c>values</c> always carries <c>enabled</c>, <c>trigger_times</c>, <c>base_delay</c>, <c>execute_on_startup</c>,
/// <c>position</c>, <c>rotation</c> and <c>on_executed[] = { id, delay }</c>, plus class-specific keys.
/// </summary>
sealed class MissionData
{
	public sealed record Link( int Id, float Delay );

	public sealed class Element
	{
		public int Id { get; init; }
		public string Class { get; init; }
		public string Name { get; init; }
		public string Script { get; init; }
		public ScriptTable Values { get; init; }
		public bool Enabled { get; init; }
		public int TriggerTimes { get; init; }
		public float BaseDelay { get; init; }
		public bool ExecuteOnStartup { get; init; }
		public Link[] OnExecuted { get; init; }
		/// <summary>Diesel-space position (cm).</summary>
		public Vector3 Position { get; init; }
		public Rotation Rotation { get; init; }

		public string Text( string key ) => Values.Text( key );
		public float Number( string key, float fallback = 0 ) => Values.Number( key, fallback );
		public bool Flag( string key, bool fallback = false ) => Values.Flag( key, fallback );
		/// <summary>A list of element/unit ids such as <c>elements</c> or <c>unit_ids</c>.</summary>
		public int[] Ids( string key ) => Values.Table( key )?.Values.OfType<float>().Select( f => (int)f ).ToArray() ?? [];
	}

	public sealed record Script( string Name, bool ActivateOnParsed, List<Element> Elements );

	public List<Script> Scripts { get; } = [];
	public Dictionary<int, Element> Elements { get; } = [];

	/// <param name="level">Level directory such as <c>levels/bank</c>.</param>
	public static MissionData Read( BundleDatabase database, string level )
	{
		level = level.TrimEnd( '/' );
		var data = new MissionData();
		var index = database.Find( "mission", $"{level}/mission" );
		if ( index is null ) return data;
		foreach ( var continent in ScriptData.Read( index ).Children )
		{
			if ( continent.Text( "file" ) is not { Length: > 0 } file ) continue;
			var bytes = database.Find( "mission", $"{level}/{file}" );
			if ( bytes is null ) continue;
			foreach ( var (name, value) in ScriptData.Read( bytes ) )
			{
				if ( value is not ScriptTable table ) continue;
				var script = new Script( name, table.Flag( "activate_on_parsed" ), [] );
				foreach ( var entry in table.Table( "elements" )?.Children ?? [] )
				{
					var values = entry.Table( "values" );
					if ( values is null || entry.Text( "class" ) is not { Length: > 0 } cls || entry.GetValueOrDefault( "id" ) is not float id ) continue;
					var element = new Element
					{
						Id = (int)id,
						Class = cls,
						Name = entry.Text( "editor_name" ) ?? "",
						Script = name,
						Values = values,
						Enabled = values.Flag( "enabled", true ),
						TriggerTimes = (int)values.Number( "trigger_times" ),
						BaseDelay = values.Number( "base_delay" ),
						ExecuteOnStartup = values.Flag( "execute_on_startup" ),
						OnExecuted = values.Table( "on_executed" )?.Children
							.Where( l => l.GetValueOrDefault( "id" ) is float )
							.Select( l => new Link( (int)l.Number( "id" ), l.Number( "delay" ) ) ).ToArray() ?? [],
						Position = values.GetValueOrDefault( "position" ) is Vector3 p ? p : Vector3.Zero,
						Rotation = values.GetValueOrDefault( "rotation" ) is Rotation r ? r : Rotation.Identity,
					};
					script.Elements.Add( element );
					data.Elements.TryAdd( element.Id, element );
				}
				data.Scripts.Add( script );
			}
		}
		return data;
	}

	/// <summary>Unit ids (world placements' <c>unit_data.unit_id</c>) the mission refers to: disables, sequences, sequence triggers.</summary>
	public HashSet<int> ReferencedUnits()
	{
		var units = new HashSet<int>();
		foreach ( var element in Elements.Values )
		{
			units.UnionWith( element.Ids( "unit_ids" ) );
			foreach ( var entry in element.Values.Table( "trigger_list" )?.Children ?? [] )
				if ( entry.GetValueOrDefault( "notify_unit_id" ) is float unit ) units.Add( (int)unit );
			foreach ( var entry in element.Values.Table( "sequence_list" )?.Children ?? [] )
				if ( entry.GetValueOrDefault( "unit_id" ) is float unit ) units.Add( (int)unit );
		}
		return units;
	}
}
