using Diesel;
using System.Text;
using Vec = System.Numerics.Vector3;

/// <summary>
/// Ground truth for the sun direction: find a level's sky-dome units, locate the painted sun
/// (brightest texels) in their diffuse textures, map that UV back onto the placed dome mesh and
/// report the resulting direction next to each candidate rig evaluation.
/// Run with: Diesel.Verify &lt;install&gt; --dump-sky [level] [output.txt]   (default level: suburbia)
/// </summary>
static class SkyDump
{
	record Placement( string Unit, Vector3 Position, Rotation Rotation );

	public static void Run( BundleDatabase db, string level, TextWriter o )
	{
		string worldPath = $"levels/{level}/world";
		var world = ScriptData.Read( db.Find( "world", worldPath ) ?? throw new FileNotFoundException( worldPath ) );
		var placements = new List<Placement>();
		void Layer( ScriptTable layer )
		{
			foreach ( var category in new[] { "statics", "dynamics" } )
				foreach ( var entry in layer.Table( category )?.Children ?? [] )
					if ( entry.Table( "unit_data" ) is { } unit && unit.Text( "name" ) is { } name )
						placements.Add( new( name,
							unit.GetValueOrDefault( "position" ) is Vector3 p ? Coordinates.Position( p ) : Vector3.Zero,
							unit.GetValueOrDefault( "rotation" ) is Rotation r ? Coordinates.Rotation( r ) : Rotation.Identity ) );
		}
		Layer( world );
		string dir = $"levels/{level}/";
		if ( world.Table( "world_data" )?.Text( "continents_file" ) is { } continentsFile && db.Find( "continents", dir + continentsFile ) is { } cbytes )
			foreach ( var (name, value) in ScriptData.Read( cbytes ) )
				if ( value is ScriptTable c && !c.Flag( "editor_only" ) && c.Flag( "enabled", true ) && db.Find( "continent", $"{dir}{name}/{name}" ) is { } layer )
					Layer( ScriptData.Read( layer ) );
		o.WriteLine( $"{worldPath}: {placements.Count} placements" );

		var environment = EnvironmentSettings.Read( db, world );
		o.WriteLine( $"environment: sun_anim={environment.SunVertical} sun_anim_x={environment.SunHorizontal} sky_rot={environment.SkyRotation} underlay={environment.Underlay}" );

		var references = new List<Vector3>();
		var reported = new HashSet<string>();
		var units = new Dictionary<string, (XmlNode Unit, XmlNode Object, string ObjectPath, Dictionary<ulong, MaterialDefinition> Materials)>();
		foreach ( var placement in placements )
		{
			if ( !units.TryGetValue( placement.Unit, out var info ) )
			{
				info = default;
				try
				{
					var unit = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "unit", placement.Unit ) ) );
					string objectPath = unit.Element( "object" )?.Attribute( "file" );
					var obj = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "object", objectPath ) ) );
					string config = obj.Element( "diesel" )?.Attribute( "materials" );
					var materials = config is not null && db.Find( "material_config", config ) is { } mbytes
						? MaterialConfig.Read( mbytes ) : new Dictionary<ulong, MaterialDefinition>();
					info = (unit, obj, objectPath, materials);
				}
				catch ( Exception ) { }
				units[placement.Unit] = info;
			}
			if ( info.Materials is null ) continue;
			var sky = info.Materials.Where( m => m.Value.Diffuse is { } d && (d.Contains( "sky", StringComparison.OrdinalIgnoreCase ) || d.Contains( "sun", StringComparison.OrdinalIgnoreCase )) ).ToList();
			if ( sky.Count == 0 || !reported.Add( placement.Unit ) ) continue;
			// Only true sky domes are trusted as references; other units merely share the config.
			bool isDome = placement.Unit.Contains( "sky", StringComparison.OrdinalIgnoreCase ) || placement.Unit.Contains( "backdrop", StringComparison.OrdinalIgnoreCase );
			o.WriteLine();
			o.WriteLine( $"## {placement.Unit} at {placement.Position} rot {placement.Rotation.Angles()}" );
			var geometry = UnitGeometry.Read( info.Unit, info.Object );
			string orientation = info.Object.Element( "diesel" )?.Attribute( "orientation_object" );
			var model = new ModelFile( db.Find( "model", info.ObjectPath ) );
			var parts = model.ReadGeometry( geometry.Graphics, orientation is null ? 0 : IdString.Hash( orientation ) ).ToList();
			foreach ( var (slot, material) in sky )
			{
				o.WriteLine( $"  material {material.Name}: diffuse {material.Diffuse}" );
				if ( db.Find( "texture", material.Diffuse ) is not { } dds || Brightest( dds ) is not { } spot )
				{
					o.WriteLine( "    (texture missing or not DXT1/3/5)" );
					continue;
				}
				o.WriteLine( $"    brightest texels: centroid uv=({spot.U:0.####}, {spot.V:0.####}) spread=({spot.SpreadU:0.####}, {spot.SpreadV:0.####}) count={spot.Count} peak={spot.Peak:0.###} size={spot.Width}x{spot.Height}" );
				// Triangles drawn with this slot. Draw.Start/Count index into Indices (as UnitModel does).
				var triangles = new List<(ModelFile.Geometry Part, int A, int B, int C)>();
				foreach ( var part in parts )
					foreach ( var draw in part.Draws.Where( d => d.Material == slot ) )
						for ( int i = draw.Start; i + 2 < Math.Min( draw.Start + draw.Count, part.Indices.Length ); i += 3 )
							triangles.Add( (part, part.Indices[i], part.Indices[i + 1], part.Indices[i + 2]) );
				o.WriteLine( $"    parts: {string.Join( ", ", parts.Select( p => $"{p.Name:x16} draws [{string.Join( " ", p.Draws.Select( d => $"{d.Material:x16}:{d.Start}+{d.Count}" ) )}]" ) )}" );
				o.WriteLine( $"    slot {slot:x16}: {triangles.Count} triangles" );
				if ( triangles.Count == 0 ) continue;
				var corners = triangles.SelectMany( t => new[] { t.Part.UVs[t.A], t.Part.UVs[t.B], t.Part.UVs[t.C] } ).ToList();
				o.WriteLine( $"    uv bounds: u {corners.Min( c => c.x ):0.###}..{corners.Max( c => c.x ):0.###}, v {corners.Min( c => c.y ):0.###}..{corners.Max( c => c.y ):0.###}" );
				var dome = triangles.SelectMany( t => new[] { t.Part.Positions[t.A], t.Part.Positions[t.B], t.Part.Positions[t.C] } ).ToList();
				var center = dome.Aggregate( Vector3.Zero, ( s, p ) => s + p ) / dome.Count;
				o.WriteLine( $"    mesh centre (local) {center}, radius ~{dome.Average( p => (p - center).Length ):0}" );
				foreach ( bool flip in new[] { false, true } )
				{
					string label = flip ? "v flipped" : "v as-is ";
					var target = new Vector2( spot.U, flip ? 1 - spot.V : spot.V );
					Vector3? hit = null;
					foreach ( var (part, a, b, c) in triangles )
					{
						// Tiled UVs: try the texel's position in each repeat covered by this triangle.
						var ua = part.UVs[a]; var ub = part.UVs[b]; var uc = part.UVs[c];
						float ox = MathF.Floor( MathF.Min( ua.x, MathF.Min( ub.x, uc.x ) ) ), oy = MathF.Floor( MathF.Min( ua.y, MathF.Min( ub.y, uc.y ) ) );
						for ( int tx = 0; tx < 2 && hit is null; tx++ )
							for ( int ty = 0; ty < 2 && hit is null; ty++ )
								if ( Barycentric( target + new Vector2( ox + tx, oy + ty ), ua, ub, uc, out var w ) )
									hit = part.Positions[a] * w.x + part.Positions[b] * w.y + part.Positions[c] * w.z;
						if ( hit is not null ) break;
					}
					if ( hit is { } local )
					{
						// Measure from the dome's pivot (the viewer), not the mesh centroid, which sits high on a hemisphere.
						var direction = (placement.Rotation * local).Normal;
						o.WriteLine( $"    {label}: on-triangle dome direction {Describe( direction )}" );
						if ( !flip && isDome ) references.Add( direction );
					}
					else o.WriteLine( $"    {label}: not inside any triangle" );
					// Nearest vertex by (wrapped) UV distance, as a fallback/sanity check.
					float Wrap( float d ) => MathF.Abs( d - MathF.Round( d ) );
					var nearest = triangles.SelectMany( t => new[] { (t.Part, t.A), (t.Part, t.B), (t.Part, t.C) } )
						.MinBy( v => { var uv = v.Part.UVs[v.Item2]; float du = Wrap( uv.x - target.x ), dv = Wrap( uv.y - target.y ); return du * du + dv * dv; } );
					var nuv = nearest.Part.UVs[nearest.Item2];
					var ndir = (placement.Rotation * nearest.Part.Positions[nearest.Item2]).Normal;
					o.WriteLine( $"    {label}: nearest vertex uv=({nuv.x:0.###}, {nuv.y:0.###}) direction {Describe( ndir )}" );
					if ( !flip && hit is null && isDome ) references.Add( ndir );
				}
			}
		}

		o.WriteLine();
		o.WriteLine( "== rig candidates (direction towards the sun, s&box axes) ==" );
		if ( environment.SunVertical is not { } v || environment.SunHorizontal is not { } h ) return;
		var candidates = new List<(string Name, Vector3? Direction)>();
		foreach ( var (label, vv, hh) in new[] { ("as authored", v, h), ("horizontal reversed", v, 1 - h), ("vertical reversed", 1 - v, h) } )
		{
			if ( SunDirection.RigDirection( db, environment.Underlay, vv, hh ) is not { } rig ) continue;
			// "implemented" is SunDirection's own convention; "opposite" flips the sky_rot sign.
			foreach ( var (rotLabel, rot) in new[] { ("sky_rot implemented", environment.SkyRotation), ("sky_rot opposite", -environment.SkyRotation), ("no sky_rot", 0f) } )
				candidates.Add( ($"{label}, {rotLabel}", SunDirection.ToSbox( rig, rot )) );
			// Mirror candidates in case an axis convention is flipped.
			candidates.Add( ($"{label}, implemented, mirrored X", Mirror( SunDirection.ToSbox( rig, environment.SkyRotation ), true )) );
			candidates.Add( ($"{label}, implemented, mirrored Y", Mirror( SunDirection.ToSbox( rig, environment.SkyRotation ), false )) );
		}
		foreach ( var (name, direction) in candidates )
		{
			if ( direction is not { } d ) continue;
			string score = references.Count == 0 ? "" : $"  angle to painted sun {references.Min( r => MathF.Acos( Math.Clamp( Vector3.Dot( r, d ), -1, 1 ) ) * 180 / MathF.PI ):0.0}°";
			o.WriteLine( $"  {name,-40} {Describe( d )}{score}" );
		}
	}

	static Vector3 Mirror( Vector3 d, bool x ) => x ? new Vector3( -d.x, d.y, d.z ) : new Vector3( d.x, -d.y, d.z );

	static string Describe( Vector3 d ) =>
		$"({d.x:0.###}, {d.y:0.###}, {d.z:0.###}) elevation {MathF.Asin( Math.Clamp( d.z, -1, 1 ) ) * 180 / MathF.PI:0.0}° azimuth {MathF.Atan2( d.y, d.x ) * 180 / MathF.PI:0.0}°";

	static bool Barycentric( Vector2 p, Vector2 a, Vector2 b, Vector2 c, out Vector3 weights )
	{
		weights = default;
		float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
		if ( MathF.Abs( d ) < 1e-12f ) return false;
		float w0 = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / d;
		float w1 = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / d;
		float w2 = 1 - w0 - w1;
		const float e = -1e-4f;
		if ( w0 < e || w1 < e || w2 < e ) return false;
		weights = new Vector3( w0, w1, w2 );
		return true;
	}

	record Spot( float U, float V, float SpreadU, float SpreadV, int Count, float Peak, int Width, int Height );

	/// <summary>Decode mip 0 of a DXT1/3/5 DDS and return the centroid of texels within 2% of the peak luminance.</summary>
	static Spot Brightest( byte[] dds )
	{
		if ( dds.Length < 128 || BitConverter.ToUInt32( dds, 0 ) != 0x20534444 ) return null;
		int height = BitConverter.ToInt32( dds, 12 ), width = BitConverter.ToInt32( dds, 16 );
		string fourCc = Encoding.ASCII.GetString( dds, 84, 4 );
		int blockBytes = fourCc switch { "DXT1" => 8, "DXT3" or "DXT5" => 16, _ => 0 };
		if ( blockBytes == 0 || width <= 0 || height <= 0 ) return null;
		int bw = Math.Max( 1, (width + 3) / 4 ), bh = Math.Max( 1, (height + 3) / 4 );
		if ( 128 + (long)bw * bh * blockBytes > dds.Length ) return null;
		var luminance = new float[width * height];
		for ( int by = 0; by < bh; by++ )
			for ( int bx = 0; bx < bw; bx++ )
			{
				int p = 128 + (by * bw + bx) * blockBytes + (blockBytes - 8);
				ushort c0 = BitConverter.ToUInt16( dds, p ), c1 = BitConverter.ToUInt16( dds, p + 2 );
				uint bits = BitConverter.ToUInt32( dds, p + 4 );
				var palette = new Vec[4];
				palette[0] = Rgb565( c0 ); palette[1] = Rgb565( c1 );
				if ( c0 > c1 || blockBytes == 16 ) { palette[2] = (2 * palette[0] + palette[1]) / 3; palette[3] = (palette[0] + 2 * palette[1]) / 3; }
				else { palette[2] = (palette[0] + palette[1]) / 2; palette[3] = Vec.Zero; }
				for ( int t = 0; t < 16; t++ )
				{
					int x = bx * 4 + t % 4, y = by * 4 + t / 4;
					if ( x >= width || y >= height ) continue;
					var c = palette[(bits >> (t * 2)) & 3];
					luminance[y * width + x] = 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
				}
			}
		float peak = luminance.Max();
		double su = 0, sv = 0, su2 = 0, sv2 = 0; int count = 0;
		for ( int i = 0; i < luminance.Length; i++ )
		{
			if ( luminance[i] < peak * 0.98f ) continue;
			double u = (i % width + 0.5) / width, v = (i / width + 0.5) / height;
			su += u; sv += v; su2 += u * u; sv2 += v * v; count++;
		}
		double mu = su / count, mv = sv / count;
		return new( (float)mu, (float)mv, (float)Math.Sqrt( Math.Max( 0, su2 / count - mu * mu ) ), (float)Math.Sqrt( Math.Max( 0, sv2 / count - mv * mv ) ), count, peak, width, height );
	}

	static Vec Rgb565( ushort c ) => new( ((c >> 11) & 31) / 31f, ((c >> 5) & 63) / 63f, (c & 31) / 31f );
}
