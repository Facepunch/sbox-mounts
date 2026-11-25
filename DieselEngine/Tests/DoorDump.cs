using Diesel;
using System.Globalization;
using System.Text;

/// <summary>
/// Research dump for doors on one level: every door/gate placement with its mesh variation and how the mount classifies it,
/// then per distinct door unit the sequence manager (open/close sequences, animation groups, sounds), the hinge rig
/// (bind pose vs. controller start/end, s&amp;box coordinates in the orientation object's space) and the leaf's bounds.
/// Run with: Diesel.Verify &lt;install&gt; --dump-door [level] [output.txt]   (default level: apartment)
/// </summary>
static class DoorDump
{
	public static void Run( BundleDatabase db, string level, TextWriter o )
	{
		var placements = new List<(string Unit, string Variation, Vector3 Position, float Yaw, int Id)>();
		void Layer( ScriptTable layer )
		{
			foreach ( var category in new[] { "statics", "dynamics" } )
				foreach ( var entry in layer.Table( category )?.Children ?? [] )
					if ( entry.Table( "unit_data" ) is { } unit && unit.Text( "name" ) is { } name
						&& (name.Contains( "door", StringComparison.OrdinalIgnoreCase ) || name.Contains( "gate", StringComparison.OrdinalIgnoreCase )) )
					{
						var position = unit.GetValueOrDefault( "position" ) is Vector3 p ? Coordinates.Position( p ) : Vector3.Zero;
						float yaw = unit.GetValueOrDefault( "rotation" ) is Rotation r ? Coordinates.Rotation( r ).Yaw() : 0;
						string variation = unit.Text( "mesh_variation" ) ?? "";
						if ( variation == "default" ) variation = "";
						placements.Add( (name, variation, position, yaw, unit.GetValueOrDefault( "unit_id" ) is float id ? (int)id : 0) );
					}
		}
		var world = ScriptData.Read( db.Find( "world", $"levels/{level}/world" ) );
		Layer( world );
		if ( world.Table( "world_data" )?.Text( "continents_file" ) is { } continents && db.Find( "continents", $"levels/{level}/{continents}" ) is { } bytes )
			foreach ( var (name, value) in ScriptData.Read( bytes ) )
				if ( value is ScriptTable continent && !continent.Flag( "editor_only" ) && continent.Flag( "enabled", true )
					&& db.Find( "continent", $"levels/{level}/{name}/{name}" ) is { } layerBytes )
					Layer( ScriptData.Read( layerBytes ) );

		o.WriteLine( $"==================== {level}: {placements.Count} door/gate placements ====================" );
		foreach ( var group in placements.GroupBy( p => (p.Unit, p.Variation) ).OrderBy( g => g.Key.Unit ).ThenBy( g => g.Key.Variation ) )
		{
			string kind = MapInteractionDefinition.IsApartmentDoor( group.Key.Unit, group.Key.Variation ) ? "apartment"
				: MapInteractionDefinition.IsFramedDoor( group.Key.Unit, group.Key.Variation ) ? "framed"
				: MapInteractionDefinition.IsSwingDoor( group.Key.Unit, group.Key.Variation ) ? "swing"
				: ArticulatedDoorDefinition.Find( group.Key.Unit, group.Key.Variation ) is not null ? "articulated" : "STATIC";
			o.WriteLine( $"  {group.Key.Unit} [{(group.Key.Variation.Length == 0 ? "default" : group.Key.Variation)}] ×{group.Count()} -> {kind}" );
			foreach ( var p in group.Take( 4 ) ) o.WriteLine( $"      at {V( p.Position )} yaw {F( p.Yaw )} id {p.Id}" );
		}

		foreach ( var unitPath in placements.Select( p => p.Unit ).Distinct().OrderBy( u => u ) )
		{
			o.WriteLine();
			o.WriteLine( $"==================== {unitPath} ====================" );
			try { Unit( db, unitPath, o ); }
			catch ( Exception e ) { o.WriteLine( $"  ERROR {e.Message}" ); }
		}
	}

	static void Unit( BundleDatabase db, string unitPath, TextWriter o )
	{
		var unitXml = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "unit", unitPath ) ) );
		string objectPath = unitXml.Element( "object" )?.Attribute( "file" );
		var objXml = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "object", objectPath ) ) );
		string orientation = objXml.Element( "diesel" )?.Attribute( "orientation_object" );
		o.WriteLine( $"  object {objectPath}, orientation {orientation}" );
		var graphics = UnitGeometry.GraphicsObjects( objXml ).Select( g => g.Attribute( "name" ) ).ToList();
		o.WriteLine( $"  graphics: {string.Join( ", ", graphics )}" );
		if ( objXml.Element( "sequence_manager" )?.Attribute( "file" ) is string smPath && db.Find( "sequence_manager", smPath ) is { } smBytes )
		{
			var manager = ScriptData.Read( smBytes );
			var (open, close) = SequenceSounds.DoorSounds( manager );
			o.WriteLine( $"  door sounds: open={open ?? "-"} close={close ?? "-"}" );
			foreach ( var name in SequenceSounds.SequenceNames( manager ) )
			{
				var sounds = SequenceSounds.Read( manager, name );
				o.WriteLine( $"  sequence {name}{(sounds.Count > 0 ? $"  sounds: {string.Join( ", ", sounds )}" : "")}" );
			}
			o.WriteLine( "  sequence_manager tree:" );
			SequenceDump.Tree( o, manager, "    ", [] );
		}
		else o.WriteLine( "  (no sequence_manager)" );

		if ( db.Find( "model", objectPath ) is not { } modelBytes || orientation is null ) return;
		var file = new ModelFile( modelBytes );
		var rest = file.Pose( null, 0 );
		System.Numerics.Matrix4x4.Invert( file.NodePose( rest, orientation ), out var root );
		// Hinge-like nodes: anything named a_* (anchors) among the object XML's graphics/animation references.
		var hinges = new SortedSet<string>( StringComparer.Ordinal );
		foreach ( var name in new[] { "a_in", "a_out", "a_door", "a_door_a", "a_door_b", "a_door_c", "a_door_left", "a_door_right", "a_left", "a_right", "a_hinge", "a_gate" } )
			try { file.NodePose( rest, name ); hinges.Add( name ); } catch ( InvalidDataException ) { }
		foreach ( var hinge in hinges )
		{
			ulong hash = IdString.Hash( hinge );
			var bind = ModelFile.Convert( file.NodePose( rest, hinge ) * root );
			o.Write( $"  hinge {hinge}: bind pos {V( bind.Position )} yaw {F( bind.Rotation.Yaw() )}" );
			Dictionary<ulong, AnimationFile.Track> tracks;
			try { tracks = file.Controllers( [hash] ).Where( p => p.Key == hash ).ToDictionary( p => p.Key, p => p.Value ); }
			catch ( Exception e ) { o.WriteLine( $"  controllers: {e.Message}" ); continue; }
			if ( !tracks.TryGetValue( hash, out var track ) ) { o.WriteLine( "  (no controller)" ); continue; }
			var times = track.RotationTimes.Length > 0 ? track.RotationTimes : track.PositionTimes;
			o.WriteLine( $"  controller keys {times.Length}, t {F( times.FirstOrDefault() )}..{F( times.LastOrDefault() )}" );
			foreach ( float t in new[] { times.FirstOrDefault(), times.Length > 0 ? times[times.Length / 2] : 0, times.LastOrDefault() } )
			{
				var at = ModelFile.Convert( file.NodePose( file.Pose( null, t, tracks ), hinge ) * root );
				o.WriteLine( $"      t={F( t )}: pos {V( at.Position )} yaw {F( at.Rotation.Yaw() )} (Δyaw from bind {F( (at.Rotation * bind.Rotation.Inverse).Yaw() )})" );
			}
		}
		foreach ( var part in graphics.Where( g => g.Contains( "door" ) || g.Contains( "hinge" ) || g.Contains( "handle" ) || g.Contains( "gate" ) ) )
		{
			var geometry = file.ReadGeometry( [IdString.Hash( part )], IdString.Hash( orientation ) ).SelectMany( g => g.Positions ).ToList();
			if ( geometry.Count == 0 ) continue;
			var min = geometry.Aggregate( ( a, b ) => new Vector3( MathF.Min( a.x, b.x ), MathF.Min( a.y, b.y ), MathF.Min( a.z, b.z ) ) );
			var max = geometry.Aggregate( ( a, b ) => new Vector3( MathF.Max( a.x, b.x ), MathF.Max( a.y, b.y ), MathF.Max( a.z, b.z ) ) );
			o.WriteLine( $"  part {part}: bounds {V( min )} .. {V( max )}" );
		}
	}

	static string F( float f ) => f.ToString( "0.##", CultureInfo.InvariantCulture );
	static string V( Vector3 v ) => $"({F( v.x )}, {F( v.y )}, {F( v.z )})";
}
