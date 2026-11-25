using Diesel;
using System.Globalization;
using System.Text;

/// <summary>
/// Research dump for skies, fog, exposure and colour grading (tracker P2): the full environment tree of a
/// level (each table once), the sky underlay's scene/material config, and a cross-level table of every
/// numeric/vector parameter so per-map differences stand out.
/// Run with: Diesel.Verify &lt;install&gt; --dump-env [level] [output.txt]   (default level: bank)
/// </summary>
static class EnvDump
{
	static readonly string[] Levels = ["apartment", "bank", "bridge", "diamondheist", "l4d", "secret_stash", "slaughterhouse", "street", "suburbia"];

	public static void Run( BundleDatabase db, string level, TextWriter o )
	{
		var data = Data( db, level, out string envPath );
		o.WriteLine( $"== {level}: {envPath} ==" );
		Tree( o, data, "", [] );

		o.WriteLine();
		o.WriteLine( "== sky underlay ==" );
		string underlay = Param( data?.Table( "others" ), "underlay" ) as string;
		if ( underlay is not null )
		{
			foreach ( var type in new[] { "scene", "material_config" } )
				if ( db.Find( type, underlay ) is { } bytes )
				{
					o.WriteLine( $"-- {type} {underlay}:" );
					o.WriteLine( Encoding.UTF8.GetString( bytes ) );
				}
			// How do the sky's three colours and the clouds' channels combine? Sample the textures.
			foreach ( var texture in new[] { "core/environments/skies/default/background_gradient", "core/environments/skies/default/cloud_normal_df", "core/environments/skies/default/sun" } )
			{
				var image = DdsProbe.Decode( db.Find( "texture", texture ) );
				o.WriteLine( $"-- texture {texture}: {(image is null ? "missing/unsupported" : "")}" );
				if ( image is not null ) DdsProbe.Grid( o, image, 8, 12 );
			}
			// Dome geometry: UV ranges and vertical extent of g_sky/g_clouds/g_sun relative to the dome root.
			if ( db.Find( "model", underlay ) is { } modelBytes )
			{
				var model = new ModelFile( modelBytes );
				var parts = new HashSet<ulong> { IdString.Hash( "g_sky" ), IdString.Hash( "g_clouds" ), IdString.Hash( "g_sun" ) };
				foreach ( var part in model.ReadGeometry( parts, IdString.Hash( "rp_skydome" ) ) )
				{
					string partName = part.Name == IdString.Hash( "g_sky" ) ? "g_sky" : part.Name == IdString.Hash( "g_clouds" ) ? "g_clouds" : "g_sun";
					var bounds = BBox.FromPoints( part.Positions );
					o.WriteLine( $"-- mesh {partName}: {part.Positions.Length} vertices, {part.Indices.Length / 3} triangles, bounds {bounds.Mins} .. {bounds.Maxs}" );
					o.WriteLine( $"    uv u {part.UVs.Min( u => u.x ):0.###}..{part.UVs.Max( u => u.x ):0.###}, v {part.UVs.Min( u => u.y ):0.###}..{part.UVs.Max( u => u.y ):0.###}; vertex colour alpha {part.Colors.Min( c => c.w ):0.###}..{part.Colors.Max( c => c.w ):0.###}" );
					// UV v against height: does v run horizon→zenith?
					foreach ( var i in Enumerable.Range( 0, 8 ).Select( k => k * Math.Max( 1, part.Positions.Length / 8 ) ).Where( k => k < part.Positions.Length ) )
						o.WriteLine( $"    v{i}: pos {part.Positions[i]} uv ({part.UVs[i].x:0.###}, {part.UVs[i].y:0.###}) colour {part.Colors[i]}" );
				}
			}
		}

		o.WriteLine();
		o.WriteLine( "== parameters across levels (path = value per level) ==" );
		var table = new SortedDictionary<string, Dictionary<string, string>>( StringComparer.Ordinal );
		foreach ( var other in Levels )
			Collect( Data( db, other, out _ ), "", other, table, [] );
		o.WriteLine( "path\t" + string.Join( "\t", Levels ) );
		foreach ( var (path, values) in table )
		{
			var row = Levels.Select( l => values.GetValueOrDefault( l, "" ) ).ToArray();
			if ( row.Distinct().Count() == 1 && path.Contains( "/post_effect/" ) is false && path.Contains( "/others/" ) is false ) continue;
			o.WriteLine( path + "\t" + string.Join( "\t", row ) );
		}
	}

	static ScriptTable Data( BundleDatabase db, string level, out string envPath )
	{
		var world = ScriptData.Read( db.Find( "world", $"levels/{level}/world" ) );
		envPath = world.Table( "environment" )?.Table( "environment_values" )?.Text( "environment" );
		var bytes = envPath is null ? null : db.Find( "environment", envPath );
		return bytes is null ? null : ScriptData.Read( bytes ).Table( "data" );
	}

	static object Param( ScriptTable table, string key ) =>
		table?.Children.FirstOrDefault( p => p.Meta == "param" && p.Text( "key" ) == key )?.GetValueOrDefault( "value" );

	static void Tree( TextWriter o, ScriptTable table, string indent, HashSet<ScriptTable> seen )
	{
		if ( table is null || !seen.Add( table ) ) return;
		foreach ( var (key, value) in table )
		{
			if ( value is not ScriptTable child ) { o.WriteLine( $"{indent}{key} = {Format( value )}" ); continue; }
			if ( child.Meta == "param" ) { o.WriteLine( $"{indent}param {child.Text( "key" )} = {Format( child.GetValueOrDefault( "value" ) )}" ); continue; }
			o.WriteLine( $"{indent}{child.Meta ?? key}{(child.Meta is null ? "" : $" [{key}]")}:" );
			Tree( o, child, indent + "  ", seen );
		}
	}

	static void Collect( ScriptTable table, string path, string level, SortedDictionary<string, Dictionary<string, string>> result, HashSet<ScriptTable> seen )
	{
		if ( table is null || !seen.Add( table ) ) return;
		foreach ( var (key, value) in table )
		{
			if ( value is not ScriptTable child ) continue;
			if ( child.Meta == "param" )
			{
				string p = $"{path}/{child.Text( "key" )}";
				if ( !result.TryGetValue( p, out var values ) ) result[p] = values = new Dictionary<string, string>();
				values[level] = Format( child.GetValueOrDefault( "value" ) );
				continue;
			}
			Collect( child, $"{path}/{child.Meta ?? key}", level, result, seen );
		}
	}

	static string Format( object value ) => value switch
	{
		null => "nil",
		float f => f.ToString( "0.####", CultureInfo.InvariantCulture ),
		Vector3 v => $"{v.x.ToString( "0.###", CultureInfo.InvariantCulture )},{v.y.ToString( "0.###", CultureInfo.InvariantCulture )},{v.z.ToString( "0.###", CultureInfo.InvariantCulture )}",
		_ => Convert.ToString( value, CultureInfo.InvariantCulture ),
	};
}
