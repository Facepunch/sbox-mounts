using Diesel;
using System.Text;

// Integration verification against a locally installed game; no game data is copied into this repository.
if ( args.Length != 1 ) throw new ArgumentException( "Usage: Diesel.Verify <PAYDAY The Heist install directory>" );
string root = args[0];
var db = new BundleDatabase( p => File.OpenRead( Path.Combine( root, p ) ),
	Directory.EnumerateFiles( Path.Combine( root, "assets" ), "*_h.bundle" ).Select( p => Path.GetRelativePath( root, p ).Replace( '\\', '/' ) ) );
if ( IdString.Hash( "levels/bank/world" ) != 0x8b050fc653c8f5e6UL ) throw new Exception( "IdString hash mismatch." );
if ( IdString.Hash( "world" ) != 0x69bb6b38986b3104UL ) throw new Exception( "Short IdString hash mismatch." );
void Reject( Action action )
{
	try { action(); }
	catch ( InvalidDataException ) { return; }
	throw new Exception( "Malformed resource was accepted." );
}
Reject( () => ScriptData.Read( db.Find( "world", "levels/bank/world" )[..100] ) );
Reject( () => new ModelFile( new byte[] { 255, 255, 255, 255 } ) );
Reject( () => XmlNode.Parse( "<unit><object></unit>" ) );
var fenceDds = db.Find( "texture", "units/world/props/street/fence/fence_df" );
var fenceUpload = DdsTexture.ForUpload( fenceDds );
if ( BitConverter.ToInt32( fenceUpload, 28 ) != 4 || BitConverter.ToInt32( fenceDds, 28 ) != 10
	|| !fenceUpload.AsSpan( 128 ).SequenceEqual( fenceDds.AsSpan( 128 ) ) )
	throw new Exception( "Fence DDS upload must preserve source data and retain the four block-sized mips." );
if ( !ReferenceEquals( fenceUpload, DdsTexture.ForUpload( fenceUpload ) ) )
	throw new Exception( "Block-sized DDS mip chain was modified again." );
var normalDds = db.Find( "texture", "units/world/props/street/fence/fence_nm" );
var normalUpload = DdsTexture.ForUpload( normalDds );
if ( !DdsTexture.IsNormalRA( normalDds ) || BitConverter.ToUInt32( normalUpload, 80 ) != 0x20001
	|| !normalUpload.AsSpan( 128 ).SequenceEqual( normalDds.AsSpan( 128 ) )
	|| BitConverter.ToInt32( normalUpload, 28 ) != BitConverter.ToInt32( normalDds, 28 ) )
	throw new Exception( "Diesel R8A8 normal map conversion changed its pixels or mip count." );
var fenceMaterials = MaterialConfig.Read( db.Find( "material_config", "units/world/props/street/fence/fence_instanceoff" ) );
if ( !fenceMaterials[IdString.Hash( "fence_plane" )].DoubleSided
	|| fenceMaterials[IdString.Hash( "fence_plane" )].Normal != "units/world/props/street/fence/fence_nm"
	|| fenceMaterials[IdString.Hash( "fence_poles" )].DoubleSided )
	throw new Exception( "Fence normal map or two-sided material flags were not resolved." );
UnitGeometry GeometryFor( string path )
{
	var unit = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "unit", path ) ) );
	var obj = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "object", unit.Element( "object" ).Attribute( "file" ) ) ) );
	return UnitGeometry.Read( unit, obj );
}
var blocker = GeometryFor( "units/dev_tools/level_tools/dev_collision_10m" );
if ( blocker.Graphics.Count != 0 || blocker.Collision.Count == 0 )
	throw new Exception( "Dev blocker must be invisible while retaining collision." );
var lightHelper = GeometryFor( "units/lights/light_omni_shadow_projection_01/light_omni_shadow_projection_01" );
if ( lightHelper.Graphics.Count != 0 || lightHelper.Collision.Count != 0 )
	throw new Exception( "Editor light gizmo must have neither graphics nor collision." );
var architecture = GeometryFor( "units/world/architecture/diamondheist/diamondheist_floor_1" );
if ( architecture.Graphics.Count == 0 || architecture.Collision.Count == 0 )
	throw new Exception( "Runtime architecture lost graphics or collision." );
Console.WriteLine( "PASS: invisible blockers, nonphysical light gizmos, and visible runtime architecture." );
var names = Encoding.Latin1.GetString( db.Find( "idstring_lookup", "idstring_lookup" ) ).Split( '\0' );
var maps = names.Where( s => s.StartsWith( "levels/" ) && db.Entries.ContainsKey( (IdString.Hash( "world" ), IdString.Hash( s )) ) ).Distinct().ToArray();
if ( maps.Length != 9 ) throw new Exception( $"Expected nine installed heist worlds, got {maps.Length}." );
var units = new HashSet<string>();
var configs = new Dictionary<string, Dictionary<ulong, MaterialDefinition>>();
var texturePaths = new HashSet<string>();
int draws = 0, resolvedMaterials = 0, missingMaterials = 0, invalidConfigs = 0;
int visibleDraws = 0, visibleFallbacks = 0;
int placements = 0, meshes = 0, triangles = 0, missing = 0;
int primitiveCount = 0;
var lightUnits = new List<(string Map, ScriptTable Unit)>();
var lightDefinitions = new Dictionary<string, Dictionary<string, LightDefinition>>();
var intensities = LightIntensities.Load( db );
if ( intensities["none"] != 0 || intensities["candle"] != 4 || intensities["sun"] != 1000 )
	throw new Exception( "Authored light intensity database was not decoded correctly." );
if ( LightIntensities.Resolve( intensities, "2.5" ) != 2.5f || LightIntensities.Resolve( intensities, "none" ) != 0 )
	throw new Exception( "Numeric or disabled illumination multiplier failed." );
var ticketMaterials = MaterialConfig.Read( db.Find( "material_config", "units/world/props/bank/ticket_dispenser/ticket_dispenser_01" ) );
var ticket = ticketMaterials[IdString.Hash( "ticket_dispenser_01" )];
if ( !ticket.SelfIlluminated || ticket.Emission != "units/world/props/bank/ticket_dispenser/ticket_dispenser_01_il"
	|| LightIntensities.Resolve( intensities, ticket.EmissionMultiplier ) != 1000 )
	throw new Exception( "Ticket dispenser emissive mask or preset was lost." );
var sky = MaterialConfig.Read( db.Find( "material_config", "units/world/architecture/suburbia/suburbia" ) )[IdString.Hash( "sky" )];
if ( !sky.Backdrop || LightIntensities.Resolve( intensities, sky.EmissionMultiplier ) != 0 )
	throw new Exception( "Suburbia sky must remain an unlit backdrop with emission disabled." );
var ticker = MaterialConfig.Read( db.Find( "material_config", "units/world/props/signs/sign_ticker/sign_ticker" ) )[IdString.Hash( "ssign0" )];
var smoke = MaterialConfig.Read( db.Find( "material_config", "units/world/props/slaughterhouse/backdrop_powerplant/smokepillar" ) )[IdString.Hash( "effect" )];
var wave = MaterialConfig.Read( db.Find( "material_config", "units/world/architecture/suburbia/suburbia" ) )[IdString.Hash( "wave" )];
var policeGlow = MaterialConfig.Read( db.Find( "material_config", "units/vehicles/police_ny/vehicle_police_ny" ) )[IdString.Hash( "glow" )];
if ( !policeGlow.ViewAngleFalloff || MathF.Abs( policeGlow.FalloffExponent - 5.4301f ) > 0.0001f
	|| wave.ViewAngleFalloff || smoke.ViewAngleFalloff )
	throw new Exception( "Effect view-angle falloff must preserve its authored exponent and template flag." );
if ( !smoke.Translucent || smoke.EffectBlend != 2 || smoke.DetailBlend != 1 || smoke.Detail is null
	|| MathF.Abs( smoke.DetailScroll.x - 0.031f ) > 0.00001f || smoke.DiffuseScroll != Vector2.Zero
	|| wave.EffectBlend != 1 || wave.DetailBlend != 2 || wave.DetailScroll != Vector2.Zero
	|| MathF.Abs( wave.DiffuseScroll.y + 0.04f ) > 0.00001f )
	throw new Exception( "Smoke and water effect blend modes or independent layer scrolling were lost." );
if ( MathF.Abs( ticker.DiffuseScroll.x - 0.15f ) > 0.00001f || ticker.DiffuseScroll.y != 0
	|| ticker.EmissionScroll != ticker.DiffuseScroll || ticker.NormalScroll != Vector2.Zero
	|| ticket.DiffuseScroll != Vector2.Zero || sky.DiffuseScroll != Vector2.Zero )
	throw new Exception( "Ticker layers must scroll together while static materials and normal maps remain still." );
foreach ( string map in maps )
{
	var world = ScriptData.Read( db.Find( "world", map ) );
	string directory = map[..(map.LastIndexOf( '/' ) + 1)];
	var continents = ScriptData.Read( db.Find( "continents", directory + world.Table( "world_data" ).Text( "continents_file" ) ) );
	int before = placements;
	foreach ( var (name, value) in continents )
	{
		var continent = (ScriptTable)value;
		if ( continent.Flag( "editor_only" ) || !continent.Flag( "enabled", true ) ) continue;
		var layer = ScriptData.Read( db.Find( "continent", $"{directory}{name}/{name}" ) );
		foreach ( var category in new[] { "statics", "dynamics" } )
			foreach ( var entry in layer.Table( category )?.Children ?? [] )
			{
				placements++;
				units.Add( entry.Table( "unit_data" ).Text( "name" ) );
				if ( entry.Table( "unit_data" ).Table( "lights" )?.Count > 0 )
					lightUnits.Add( (map, entry.Table( "unit_data" )) );
			}
	}
	Console.WriteLine( $"{map}: {placements - before} placements" );
}
foreach ( string unit in units )
{
	try
	{
		var bytes = db.Find( "unit", unit );
		if ( bytes is null ) { missing++; continue; }
		var xml = XmlNode.Parse( Encoding.UTF8.GetString( bytes ) );
		string path = xml.Element( "object" )?.Attribute( "file" );
		if ( path is null ) { missing++; continue; }
		var obj = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "object", path ) ) );
		string config = obj.Element( "diesel" )?.Attribute( "materials" ) ?? "";
		if ( !configs.TryGetValue( config, out var definitions ) )
		{
			definitions = [];
			if ( db.Find( "material_config", config ) is { } configBytes )
			{
				try { definitions = MaterialConfig.Read( configBytes ); }
				catch ( InvalidDataException )
				{
					// Two shipped configs contain malformed XML; production reports these and uses fallback materials.
					invalidConfigs++;
					Console.WriteLine( $"Fallback material config: {config}" );
				}
			}
			configs[config] = definitions;
		}
		var geometry = UnitGeometry.Read( xml, obj );
		var visible = geometry.Graphics;
		var selected = new HashSet<ulong>( visible );
		selected.UnionWith( geometry.Collision );
		var file = new ModelFile( db.Find( "model", path ) );
		lightDefinitions[unit] = UnitLights.Read( obj, file );
		string orientation = obj.Element( "diesel" )?.Attribute( "orientation_object" );
		var primitives = file.ReadCollisionPrimitives( geometry.CollisionTypes, orientation is null ? 0 : IdString.Hash( orientation ) ).ToArray();
		primitiveCount += primitives.Length;
		foreach ( var primitive in primitives )
			if ( primitive.Points.Length < 8 || primitive.Points.Any( v => !float.IsFinite( v.x + v.y + v.z ) ) )
				throw new Exception( "Invalid bounds-only collision primitive." );
		if ( path == "units/world/architecture/suburbia/suburbia" && primitives.Length != 141 )
			throw new Exception( "Suburbia terrain lost its 141 bounds-only collision boxes." );
		// c_box_173 appears twice in the object XML; only one hull is needed.
		if ( path == "units/world/architecture/suburbia/suburbia_house1" && primitives.Length != 194 )
			throw new Exception( $"Suburbia house lost its bounds-only boxes or capsules: {primitives.Length}." );
		foreach ( var mesh in file.ReadGeometry( selected, orientation is null ? 0 : IdString.Hash( orientation ) ) )
		{
			meshes++;
			triangles += mesh.Indices.Length / 3;
			if ( mesh.Positions.Any( p => !float.IsFinite( p.x ) || !float.IsFinite( p.y ) || !float.IsFinite( p.z ) ) )
				throw new Exception( "Non-finite transformed vertex." );
			if ( mesh.Normals.Any( n => !float.IsFinite( n.x ) || !float.IsFinite( n.y ) || !float.IsFinite( n.z ) ) )
				throw new Exception( "Non-finite transformed normal." );
			if ( mesh.Draws.Sum( d => d.Count ) != mesh.Indices.Length ) throw new Exception( "Render atoms do not cover their indices." );
			foreach ( var draw in mesh.Draws )
			{
				draws++;
				if ( visible.Contains( mesh.Name ) ) visibleDraws++;
				if ( !definitions.TryGetValue( draw.Material, out var material ) )
				{
					missingMaterials++;
					if ( visible.Contains( mesh.Name ) ) visibleFallbacks++;
					continue;
				}
				resolvedMaterials++;
				if ( material.Diffuse is not null ) texturePaths.Add( material.Diffuse );
				if ( material.Opacity is not null ) texturePaths.Add( material.Opacity );
				if ( material.Normal is not null ) texturePaths.Add( material.Normal );
				if ( material.DetailBlend > 0 && material.Detail is not null ) texturePaths.Add( material.Detail );
				if ( material.SelfIlluminated && material.Emission is not null ) texturePaths.Add( material.Emission );
			}
		}
	}
	catch ( Exception e ) { throw new Exception( $"Failed unit {unit}: {e.Message}", e ); }
}
Console.WriteLine( $"PASS: {maps.Length} worlds, {placements} placements, {units.Count} unique units, {meshes} meshes, {triangles} triangles, {missing} non-model units." );
Console.WriteLine( $"PASS: {primitiveCount} bounds-only collision primitives." );
foreach ( var group in lightUnits.GroupBy( entry => entry.Map ) )
{
	int count = 0, spots = 0;
	foreach ( var entry in group )
	{
		var definitions = lightDefinitions[entry.Unit.Text( "name" )];
		foreach ( var authored in entry.Unit.Table( "lights" ).Children )
			if ( !definitions.ContainsKey( authored.Text( "name" ) ) )
				throw new Exception( $"Unresolved light node: {entry.Unit.Text( "name" )}/{authored.Text( "name" )}" );
		foreach ( var light in UnitLights.Place( entry.Unit, definitions, intensities ) )
		{
			if ( light.Range <= 0 || !float.IsFinite( light.Range ) || light.InnerAngle > light.OuterAngle
				|| MathF.Abs( light.Definition.Node.Direction.Length - 1 ) > 0.01f )
				throw new Exception( "Invalid placed light." );
			count++;
			if ( light.Definition.Node.Spot ) spots++;
		}
	}
	Console.WriteLine( $"PASS: {group.Key}: {count} active lights ({spots} spots)." );
}
int textures = 0, missingTextures = 0;
foreach ( string texture in texturePaths )
{
	var bytes = db.Find( "texture", texture );
	if ( bytes is null ) { missingTextures++; continue; }
	if ( bytes.Length < 128 || BitConverter.ToUInt32( bytes ) != 0x20534444 || BitConverter.ToInt32( bytes, 4 ) != 124 )
		throw new Exception( $"Invalid DDS texture: {texture}" );
	textures++;
}
if ( resolvedMaterials < 1000 || textures < 100 ) throw new Exception( "Material/texture resolution unexpectedly incomplete." );
Console.WriteLine( $"PASS: {draws} draw ranges, {resolvedMaterials} resolved material assignments, {textures} DDS textures; {missingMaterials} fallback assignments (including collision-only materials), {invalidConfigs} malformed shipped configs, {missingTextures} missing textures." );
Console.WriteLine( $"Visible material coverage: {visibleDraws - visibleFallbacks}/{visibleDraws} draw ranges." );
