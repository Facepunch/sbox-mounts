using Diesel;
using System.Globalization;
using System.Text;

/// <summary>
/// Research dump for the authored sun direction (tracker P2). Prints each world's environment
/// values, sun/sky/underlay parameters, the underlay's resources, rig nodes and any matching
/// animation clips. Run with: Diesel.Verify &lt;install&gt; --dump-sun [output.txt]
/// </summary>
static class SunDump
{
	static readonly string[] Types =
	[
		"object", "model", "unit", "scene", "animation", "animation_def", "animation_state_machine",
		"animation_states", "animation_subset", "sequence_manager", "environment", "material_config", "texture", "massunit", "world", "continent",
	];

	/// <summary>Second pass: the shared sky underlay, its sun nodes/controllers and the engine's underlay Lua.</summary>
	public static void RunUnderlay( BundleDatabase db, TextWriter o )
	{
		var names = new Dictionary<ulong, string>();
		foreach ( var name in Encoding.Latin1.GetString( db.Find( "idstring_lookup", "idstring_lookup" ) ).Split( '\0' ) )
			if ( name.Length > 0 ) names.TryAdd( IdString.Hash( name ), name );
		foreach ( var type in Types.Append( "lua" ).Append( "cooked_physics" ) ) names.TryAdd( IdString.Hash( type ), type );
		string Name( ulong hash ) => names.TryGetValue( hash, out var n ) ? n : $"#{hash:x16}";

		foreach ( var lua in new[] { "core/lib/managers/viewport/feeders/coreenvironmentunderlayfeeder", "core/lib/managers/coreunderlayeffect" } )
		{
			var bytes = db.Find( "lua", lua );
			o.WriteLine( $"==================== lua {lua}: {(bytes is null ? "missing" : $"{bytes.Length} bytes")} ====================" );
			if ( bytes is null ) continue;
			bool text = bytes.Take( 256 ).All( b => b is 9 or 10 or 13 || (b >= 32 && b < 127) );
			if ( text ) o.WriteLine( Encoding.UTF8.GetString( bytes ) );
			else
			{
				o.WriteLine( "(binary) first 64 bytes: " + Convert.ToHexString( bytes.AsSpan( 0, Math.Min( 64, bytes.Length ) ) ) );
				// Compiled chunks still carry their string constants.
				var strings = System.Text.RegularExpressions.Regex.Matches( Encoding.Latin1.GetString( bytes ), "[ -~]{4,}" ).Select( m => m.Value ).Distinct();
				o.WriteLine( "strings: " + string.Join( " | ", strings ) );
			}
		}

		const string underlay = "core/environments/skies/default/default";
		Resources( db, o, underlay, Name );
		if ( db.Find( "model", underlay ) is not { } modelBytes ) return;
		var model = new ModelFile( modelBytes );
		var all = model.RigNodes.Select( n => n.Name ).ToHashSet();
		Dictionary<ulong, AnimationFile.Track> controllers;
		try { controllers = model.Controllers( all ); }
		catch ( Exception ex )
		{
			o.WriteLine( $"--- controller read failed: {ex.Message}" );
			controllers = [];
		}
		o.WriteLine( $"--- embedded controllers on {controllers.Count} nodes:" );
		foreach ( var (node, track) in controllers )
		{
			o.WriteLine( $"    {Name( node )}: pos keys {track.PositionTimes.Length} [{Range( track.PositionTimes )}], rot keys {track.RotationTimes.Length} [{Range( track.RotationTimes )}]" );
			for ( int i = 0; i < track.RotationTimes.Length; i += Math.Max( 1, track.RotationTimes.Length / 8 ) )
				o.WriteLine( $"      t={track.RotationTimes[i]:0.###} q={track.Rotations[i]}" );
		}
		foreach ( var (id, node, parent) in model.RigNodes )
			if ( Name( node ).Contains( "sun", StringComparison.OrdinalIgnoreCase ) )
				o.WriteLine( $"    node {Name( node )} id={id} parent={Name( model.RigNodes.FirstOrDefault( n => n.Id == parent ).Name )}" );

		// Sample d_sun's world direction across each controller's time range for a sanity check.
		var sunTracks = controllers.Where( c => Name( c.Key ).StartsWith( "d_sun", StringComparison.Ordinal ) ).ToList();
		float end = sunTracks.SelectMany( c => c.Value.PositionTimes.Concat( c.Value.RotationTimes ) ).DefaultIfEmpty( 0 ).Max();
		o.WriteLine( $"--- d_sun samples over [0, {end}] using all embedded controllers:" );
		for ( int i = 0; i <= 8; i++ )
		{
			float t = end * i / 8f;
			try
			{
				var pose = model.Pose( null, t, controllers );
				var m = model.NodePose( pose, "d_sun" );
				o.WriteLine( $"    t={t:0.###} pos={m.Translation} +X={System.Numerics.Vector3.TransformNormal( System.Numerics.Vector3.UnitX, m )} +Y={System.Numerics.Vector3.TransformNormal( System.Numerics.Vector3.UnitY, m )} +Z={System.Numerics.Vector3.TransformNormal( System.Numerics.Vector3.UnitZ, m )}" );
			}
			catch ( Exception ex ) { o.WriteLine( $"    t={t}: {ex.Message}" ); }
		}
	}

	static string Range( float[] times ) => times.Length == 0 ? "" : $"{times[0]:0.###}..{times[^1]:0.###}";

	public static void Run( BundleDatabase db, TextWriter o )
	{
		var names = new Dictionary<ulong, string>();
		foreach ( var name in Encoding.Latin1.GetString( db.Find( "idstring_lookup", "idstring_lookup" ) ).Split( '\0' ) )
			if ( name.Length > 0 ) names.TryAdd( IdString.Hash( name ), name );
		foreach ( var type in Types ) names.TryAdd( IdString.Hash( type ), type );
		string Name( ulong hash ) => names.TryGetValue( hash, out var n ) ? n : $"#{hash:x16}";

		ulong worldType = IdString.Hash( "world" );
		var worlds = db.Entries.Keys.Where( k => k.Type == worldType ).Select( k => Name( k.Path ) ).Where( n => n.StartsWith( "levels/" ) ).OrderBy( n => n ).ToList();
		o.WriteLine( $"Worlds: {worlds.Count}" );

		var underlays = new SortedSet<string>();
		foreach ( var worldPath in worlds )
		{
			o.WriteLine();
			o.WriteLine( $"==================== {worldPath} ====================" );
			var world = ScriptData.Read( db.Find( "world", worldPath ) );
			o.WriteLine( "-- world top-level keys: " + string.Join( ", ", world.Keys ) );
			var environment = world.Table( "environment" );
			o.WriteLine( "-- world.environment:" );
			Dump( o, environment, "  ", 0, 4 );
			var values = environment?.Table( "environment_values" );
			string envPath = values?.Text( "environment" );
			if ( values?.Text( "underlay" ) is { Length: > 0 } underlay ) underlays.Add( underlay );
			if ( values is not null )
				foreach ( var value in values.Values )
					if ( value is string s && s.Contains( '/' ) ) underlays.Add( s );

			if ( envPath is not null && db.Find( "environment", envPath ) is { } envBytes )
			{
				var data = ScriptData.Read( envBytes );
				o.WriteLine( $"-- environment file {envPath}: top keys = {string.Join( ", ", data.Keys )}" );
				o.WriteLine( "-- environment params mentioning sun/sky/underlay/anim/rot/fog:" );
				Params( o, data, "", 0 );
			}
		}

		o.WriteLine();
		o.WriteLine( "==================== underlay / path candidates ====================" );
		foreach ( var path in underlays ) Resources( db, o, path, Name );

		o.WriteLine();
		o.WriteLine( "==================== lookup names mentioning sun/underlay/sky ====================" );
		foreach ( var name in names.Values.Where( n => n.Contains( "sun", StringComparison.OrdinalIgnoreCase )
			|| n.Contains( "underlay", StringComparison.OrdinalIgnoreCase ) || n.Contains( "/sky", StringComparison.OrdinalIgnoreCase ) ).OrderBy( n => n ).Take( 400 ) )
		{
			ulong h = IdString.Hash( name );
			var types = db.Entries.Keys.Where( k => k.Path == h ).Select( k => Name( k.Type ) ).ToList();
			o.WriteLine( $"  {name}{(types.Count > 0 ? "  [" + string.Join( ", ", types ) + "]" : "")}" );
		}

		o.WriteLine();
		o.WriteLine( "==================== massunit / brush probe (tracker P2) ====================" );
		foreach ( var worldPath in worlds )
		{
			string dir = worldPath[..(worldPath.LastIndexOf( '/' ) + 1)];
			foreach ( var name in names.Values.Where( n => n.StartsWith( dir, StringComparison.Ordinal ) ).OrderBy( n => n ) )
			{
				ulong h = IdString.Hash( name );
				var types = db.Entries.Keys.Where( k => k.Path == h ).Select( k => Name( k.Type ) ).ToList();
				if ( types.Count > 0 ) o.WriteLine( $"  {name}  [{string.Join( ", ", types )}]" );
			}
		}
	}

	static void Resources( BundleDatabase db, TextWriter o, string path, Func<ulong, string> name )
	{
		ulong h = IdString.Hash( path );
		var entries = db.Entries.Values.Where( e => e.Path == h ).ToList();
		o.WriteLine();
		o.WriteLine( $"## {path}: {(entries.Count == 0 ? "no resources" : string.Join( ", ", entries.Select( e => $"{name( e.Type )} ({e.Length} bytes)" ) ))}" );
		foreach ( var e in entries )
		{
			string type = name( e.Type );
			var bytes = db.Read( e );
			if ( type is "object" or "unit" or "scene" or "animation_def" or "animation_state_machine" or "animation_states" or "animation_subset" or "sequence_manager" )
			{
				var text = Encoding.UTF8.GetString( bytes );
				o.WriteLine( $"--- {type} text ({text.Length} chars, first 6000):" );
				o.WriteLine( text.Length > 6000 ? text[..6000] : text );
				foreach ( var reference in System.Text.RegularExpressions.Regex.Matches( text, "(?:file|name|model|animation|anim|unit)=\"([^\"]+/[^\"]+)\"" ).Select( m => m.Groups[1].Value ).Distinct() )
				{
					ulong rh = IdString.Hash( reference );
					var types = db.Entries.Keys.Where( k => k.Path == rh ).Select( k => name( k.Type ) ).ToList();
					o.WriteLine( $"    ref {reference} -> [{string.Join( ", ", types )}]" );
				}
			}
			if ( type == "model" )
			{
				try
				{
					var model = new ModelFile( bytes );
					o.WriteLine( "--- model rig nodes (id, name, parent):" );
					foreach ( var (id, node, parent) in model.RigNodes ) o.WriteLine( $"    {id,6} {name( node )} parent={parent}" );
					var pose = model.Pose( null, 0 );
					foreach ( var candidate in new[] { "d_sun", "sun", "a_sun", "rp_sun" } )
					{
						try
						{
							var m = model.NodePose( pose, candidate );
							var forward = System.Numerics.Vector3.TransformNormal( System.Numerics.Vector3.UnitY, m );
							var down = System.Numerics.Vector3.TransformNormal( -System.Numerics.Vector3.UnitZ, m );
							o.WriteLine( $"    bind {candidate}: pos={m.Translation} +Y={forward} -Z={down}" );
						}
						catch ( InvalidDataException ) { }
					}
				}
				catch ( Exception ex ) { o.WriteLine( $"--- model parse failed: {ex.Message}" ); }
			}
		}
		// Clips are often named after the underlay or stored beside it.
		string dir = path[..(path.LastIndexOf( '/' ) + 1)];
		foreach ( var clip in new[] { "sun_vertical", "sun_horizontal" } )
			foreach ( var candidate in new[] { dir + clip, path + "_" + clip, dir + "anims/" + clip } )
				if ( db.Find( "animation", candidate ) is { } clipBytes )
				{
					o.WriteLine( $"--- animation {candidate} ({clipBytes.Length} bytes)" );
					try
					{
						var anim = new AnimationFile( clipBytes );
						o.WriteLine( $"    tracks: {string.Join( ", ", anim.Tracks.Keys.Select( k => k.ToString( "x16" ) ) )}" );
					}
					catch ( Exception ex ) { o.WriteLine( $"    parse failed: {ex.Message}" ); }
				}
	}

	static void Params( TextWriter o, ScriptTable table, string path, int depth )
	{
		if ( table is null || depth > 12 ) return;
		foreach ( var (key, value) in table )
		{
			if ( value is not ScriptTable child ) continue;
			string here = path + "/" + (child.Meta ?? key);
			if ( child.Meta == "param" )
			{
				string k = child.Text( "key" ) ?? "";
				if ( new[] { "sun", "sky", "underlay", "anim", "rot", "fog", "angle", "dir" }.Any( w => k.Contains( w, StringComparison.OrdinalIgnoreCase ) ) )
					o.WriteLine( $"  {path}/{k} = {Format( child.GetValueOrDefault( "value" ) )}" );
				continue;
			}
			Params( o, child, here, depth + 1 );
		}
	}

	static void Dump( TextWriter o, ScriptTable table, string indent, int depth, int maxDepth )
	{
		if ( table is null ) { o.WriteLine( indent + "(missing)" ); return; }
		foreach ( var (key, value) in table )
		{
			if ( value is ScriptTable child )
			{
				o.WriteLine( $"{indent}{key}{(child.Meta is null ? "" : $" <{child.Meta}>")}:" );
				if ( depth < maxDepth ) Dump( o, child, indent + "  ", depth + 1, maxDepth );
			}
			else o.WriteLine( $"{indent}{key} = {Format( value )}" );
		}
	}

	static string Format( object value ) => value switch
	{
		null => "nil",
		float f => f.ToString( "R", CultureInfo.InvariantCulture ),
		ulong u => $"#{u:x16}",
		_ => Convert.ToString( value, CultureInfo.InvariantCulture ),
	};
}
