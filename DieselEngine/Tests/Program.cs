using Diesel;
using System.Text;

// Integration verification against a locally installed game; no game data is copied into this repository.
if ( args.Length < 1 || (args.Length > 1 && args[1] is not ("--dump-sun" or "--dump-underlay" or "--dump-sky" or "--dump-brush" or "--dump-env" or "--dump-mission" or "--dump-sequence" or "--dump-strings" or "--dump-sound" or "--dump-door")) || args.Length > 4 )
	throw new ArgumentException( "Usage: Diesel.Verify <PAYDAY The Heist install directory> [--dump-sun|--dump-underlay [output.txt] | --dump-sky|--dump-brush|--dump-env|--dump-mission [level] [output.txt] | --dump-sequence [units] [output.txt]]" );
string root = args[0];
var db = new BundleDatabase( p => File.OpenRead( Path.Combine( root, p ) ),
	Directory.EnumerateFiles( Path.Combine( root, "assets" ), "*_h.bundle" ).Select( p => Path.GetRelativePath( root, p ).Replace( '\\', '/' ) ) );
if ( args.Length > 1 )
{
	if ( args[1] == "--dump-door" )
	{
		string level = args.Length > 2 ? args[2] : "apartment";
		string doorOutput = args.Length > 3 ? args[3] : $"door_{level}.txt";
		using ( var doorWriter = new StreamWriter( doorOutput ) ) DoorDump.Run( db, level, doorWriter );
		Console.WriteLine( $"Wrote {Path.GetFullPath( doorOutput )}" );
		return;
	}
	if ( args[1] == "--dump-sound" )
	{
		string level = args.Length > 2 ? args[2] : "bank";
		string soundOutput = args.Length > 3 ? args[3] : $"sound_{level}.txt";
		using ( var soundWriter = new StreamWriter( soundOutput ) ) SoundDump.Run( db, level, soundWriter );
		Console.WriteLine( $"Wrote {Path.GetFullPath( soundOutput )}" );
		return;
	}
	if ( args[1] == "--dump-strings" )
	{
		string stringsOutput = args.Length > 2 ? args[2] : "strings_dump.txt";
		using ( var stringsWriter = new StreamWriter( stringsOutput ) ) StringsDump.Run( db, stringsWriter );
		Console.WriteLine( $"Wrote {Path.GetFullPath( stringsOutput )}" );
		return;
	}
	if ( args[1] == "--dump-sequence" )
	{
		string wantedUnits = args.Length > 2 ? args[2] : "bank_window_large,diamondheist_rooftop_window,slaughterhouse_glass_tall,hanging_lightbulb,diamond_stand";
		string sequenceOutput = args.Length > 3 ? args[3] : "sequence_dump.txt";
		using ( var sequenceWriter = new StreamWriter( sequenceOutput ) ) SequenceDump.Run( db, wantedUnits, sequenceWriter );
		Console.WriteLine( $"Wrote {Path.GetFullPath( sequenceOutput )}" );
		return;
	}
	if ( args[1] == "--dump-mission" )
	{
		string level = args.Length > 2 ? args[2] : "bank";
		string missionOutput = args.Length > 3 ? args[3] : $"mission_{level}.txt";
		using ( var missionWriter = new StreamWriter( missionOutput ) ) MissionDump.Run( db, level, missionWriter );
		Console.WriteLine( $"Wrote {Path.GetFullPath( missionOutput )}" );
		return;
	}
	if ( args[1] == "--dump-env" )
	{
		string level = args.Length > 2 ? args[2] : "bank";
		string envOutput = args.Length > 3 ? args[3] : $"env_{level}.txt";
		using ( var envWriter = new StreamWriter( envOutput ) ) EnvDump.Run( db, level, envWriter );
		Console.WriteLine( $"Wrote {Path.GetFullPath( envOutput )}" );
		return;
	}
	if ( args[1] == "--dump-brush" )
	{
		string level = args.Length > 2 ? args[2] : "bank";
		string brushOutput = args.Length > 3 ? args[3] : $"brush_{level}.txt";
		using ( var brushWriter = new StreamWriter( brushOutput ) ) BrushDump.Run( db, level, brushWriter );
		Console.WriteLine( $"Wrote {Path.GetFullPath( brushOutput )}" );
		return;
	}
	if ( args[1] == "--dump-sky" )
	{
		string level = args.Length > 2 ? args[2] : "suburbia";
		string skyOutput = args.Length > 3 ? args[3] : $"sky_{level}.txt";
		using ( var skyWriter = new StreamWriter( skyOutput ) ) SkyDump.Run( db, level, skyWriter );
		Console.WriteLine( $"Wrote {Path.GetFullPath( skyOutput )}" );
		return;
	}
	bool underlay = args[1] == "--dump-underlay";
	string output = args.Length > 2 ? args[2] : underlay ? "underlay_dump.txt" : "sun_dump.txt";
	using ( var writer = new StreamWriter( output ) )
	{
		if ( underlay ) SunDump.RunUnderlay( db, writer );
		else SunDump.Run( db, writer );
	}
	Console.WriteLine( $"Wrote {Path.GetFullPath( output )}" );
	return;
}
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
CubemapVerification.Run( db );
WeaponPresentationVerification.Run( db );
ArsenalVerification.Run( db );
NpcVerification.Run( db );
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
var plazaGround = MaterialConfig.Read( db.Find( "material_config", "units/world/architecture/bank/bank_plaza/bank_plaza" ) )[IdString.Hash( "ground_blend" )];
if ( !plazaGround.GroundBlend || plazaGround.BlendDiffuse != "units/world/architecture/street/ground/ground_df"
	|| plazaGround.BlendMask != "units/world/architecture/street/ground/ground_mask_df" )
	throw new Exception( "Bank plaza blend textures were not resolved." );
var roofBlend = MaterialConfig.Read( db.Find( "material_config", "units/world/architecture/diamondheist/diamondheist" ) )[IdString.Hash( "roof_blend" )];
var facadeTile = MaterialConfig.Read( db.Find( "material_config", "units/world/architecture/street/facade" ) )[IdString.Hash( "facade_tile" )];
if ( roofBlend.GroundBlend || !roofBlend.BlendMaskInAlpha || !roofBlend.BlendTintSecond
	|| roofBlend.BlendDiffuse != "units/world/architecture/diamondheist/diamondheist_roof_blend_df"
	|| roofBlend.BlendMask is not null || plazaGround.BlendMaskInAlpha || facadeTile.GroundBlend )
	throw new Exception( "Encoded tint layers and pixel-tile materials must retain the original diffuse fallback." );
var apartmentRoad = MaterialConfig.Read( db.Find( "material_config", "units/world/architecture/apartment/back_alley/apartment_alley" ) )[IdString.Hash( "ground_blend" )];
if ( !apartmentRoad.BlendTintSecond || apartmentRoad.GroundBlend
	|| apartmentRoad.Diffuse != "units/world/architecture/street/ground/ground_blend_df" )
	throw new Exception( "Apartment roads must not display the blue encoded secondary texture as colour." );
if ( !fenceMaterials[IdString.Hash( "fence_plane" )].DoubleSided
	|| fenceMaterials[IdString.Hash( "fence_plane" )].Normal != "units/world/props/street/fence/fence_nm"
	|| fenceMaterials[IdString.Hash( "fence_poles" )].DoubleSided )
	throw new Exception( "Fence normal map or two-sided material flags were not resolved." );
if ( fenceMaterials[IdString.Hash( "fence_poles" )].SpecularPower is not float fenceGloss
	|| MathF.Abs( fenceGloss - 0.999f ) > 0.00001f
	|| fenceMaterials[IdString.Hash( "fence_poles" )].Roughness >= 0.1f )
	throw new Exception( "Authored fence-pole gloss was not imported." );
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
foreach ( var (unit, intact, damaged) in new[]
{
	("units/world/architecture/bank/bank_window/bank_window_large", "g_bank_window_large", "g_bank_window_large_dmg"),
	("units/world/architecture/bank/lobby_railing/lobby_railing", "g_window_1", "g_window_1_dmg"),
	("units/world/props/bank/bank_fence/bankfence_2", "g_bankfence_glass", "g_bankfence_glass_cracked"),
	("units/world/architecture/bank/bank_lobby/bank_entrance_door/bank_entrance_door_closed", "g_door_left_window_top", "g_door_left_window_top_dmg")
} )
{
	var geometry = GeometryFor( unit );
	if ( !geometry.Graphics.Contains( IdString.Hash( intact ) ) || geometry.Graphics.Contains( IdString.Hash( damaged ) ) || geometry.Collision.Count == 0 )
		throw new Exception( $"Bank glass must start intact with collision: {unit}" );
}
var glassObject = XmlNode.Parse( """
	<dynamic_object><graphics><object name="intact" enabled="true"/>
	<object name="cracked"/><object name="shattered" enabled="false"/>
	<lod_object><object name="near" lod="0"/><object name="far" lod="1" enabled="true"/></lod_object>
	</graphics></dynamic_object>
	""" );
var glassState = new SequenceVisibility();
glassState.Objects["intact"] = false;
glassState.Objects["cracked"] = true;
var glassOverride = UnitGeometry.Read( XmlNode.Parse( "<unit/>" ), glassObject, glassState );
if ( glassOverride.Graphics.Contains( IdString.Hash( "intact" ) )
	|| !glassOverride.Graphics.Contains( IdString.Hash( "cracked" ) )
	|| glassOverride.Graphics.Contains( IdString.Hash( "shattered" ) )
	|| glassOverride.Graphics.Contains( IdString.Hash( "near" ) )
	|| !glassOverride.Graphics.Contains( IdString.Hash( "far" ) ) )
	throw new Exception( "Explicit graphics variations and enabled-only LOD selection regressed." );
foreach ( string can in new[] { "food_can_01", "food_can_02", "food_can_03", "food_can_04" } )
{
	var geometry = GeometryFor( "units/world/props/apartment/kitchen_counter/" + can );
	if ( geometry.CollisionMasses.GetValueOrDefault( IdString.Hash( "c_can" ) ) != 0.05f )
		throw new Exception( "Food can lost its authored 0.05 kg collision mass." );
}
var massGeometry = UnitGeometry.Read( XmlNode.Parse( "<unit/>" ), XmlNode.Parse( """
	<dynamic_object><bodies>
	<body enabled="false"><object name="disabled" collision_type="box" mass="100"/></body>
	<body template="editor"><object name="helper" collision_type="box" mass="100"/></body>
	<body><object name="a" collision_type="box" mass="0.05"/>
	<object name="b" collision_type="box" mass="NaN"/>
	<object name="c" collision_type="box" mass="-1"/>
	<object name="d" collision_type="box" mass="0"/>
	<object name="e" collision_type="box" mass="Infinity"/></body>
	</bodies></dynamic_object>
	""" ) );
if ( massGeometry.CollisionMasses.Count != 1 ) throw new Exception( "Invalid/disabled collision masses were retained." );
var massShapes = new Dictionary<ulong, Vector3[]> { [IdString.Hash( "a" )] = [Vector3.Zero, Vector3.One] };
if ( PropMass.Calculate( massShapes, massGeometry, new Dictionary<string, float>() ) != 0.05f )
	throw new Exception( "Explicit mass was clamped to the density-estimate minimum." );
massShapes[IdString.Hash( "b" )] = [Vector3.Zero, Vector3.One];
if ( MathF.Abs( PropMass.Calculate( massShapes, massGeometry, new Dictionary<string, float>() ) - 0.15f ) > 0.00001f )
	throw new Exception( "Partial authored mass failed to estimate only the missing collider." );
var physicsDensities = PhysicsMaterials.Read( db.Find( "physics_settings", "settings/physics_settings" ) );
if ( physicsDensities.GetValueOrDefault( "wood" ) != 500f || physicsDensities.GetValueOrDefault( "plastic" ) != 955f
	|| physicsDensities.GetValueOrDefault( "steel" ) != 7480f )
	throw new Exception( "Shipped physics material densities were not read in kg/m³." );
var invalidDensities = PhysicsMaterials.Read( Encoding.UTF8.GetBytes(
	"<physics_settings><material name='a' density='NaN'/><material name='b' density='-5'/><material name='c' density='Infinity'/></physics_settings>" ) );
if ( invalidDensities.Count != 0 ) throw new Exception( "Invalid density accepted." );
var cubeShapes = new Dictionary<ulong, Vector3[]> { [IdString.Hash( "cube" )] = [Vector3.Zero, Vector3.One / 0.0254f] };
var scaledBox = new PropMass.BoxVolume( new Vector3( 2, 3, 4 ), 3 );
if ( Math.Abs( scaledBox.CubicMeters( 0 ) - 72 ) > 0.00001
	|| Math.Abs( scaledBox.CubicMeters( 10 ) - (24 - 1.8 * 2.8 * 3.8) * 3 ) > 0.00001
	|| Math.Abs( scaledBox.CubicMeters( 500 ) - 72 ) > 0.00001 )
	throw new Exception( "Source box volume or scaled hollow shell was incorrect." );
float CubeMass( string attributes ) => PropMass.Calculate( cubeShapes,
	UnitGeometry.Read( XmlNode.Parse( "<unit/>" ), XmlNode.Parse(
		$"<dynamic_object><bodies><body><object name='cube' collision_type='box' {attributes}/></body></bodies></dynamic_object>" ) ),
	physicsDensities );
if ( MathF.Abs( CubeMass( "material='wood'" ) - 500f ) > 0.01f
	|| MathF.Abs( CubeMass( "material='plastic'" ) - 955f ) > 0.01f
	|| MathF.Abs( CubeMass( "material='wood' shell='1'" ) - 29.404f ) > 0.01f
	|| MathF.Abs( CubeMass( "material='wood' shell='100'" ) - 500f ) > 0.01f
	|| CubeMass( "material='steel' shell='1' mass='0.05'" ) != 0.05f
	|| MathF.Abs( CubeMass( "material='unknown' shell='NaN'" ) - 500f ) > 0.01f )
	throw new Exception( "Density/shell estimate, unit conversion, explicit override or unknown-material fallback failed." );
var maserati = GeometryFor( "units/vehicles/maserati/low/vehicle_maserati_low" );
if ( !maserati.Graphics.Contains( IdString.Hash( "g_maserati" ) )
	|| !maserati.Graphics.Contains( IdString.Hash( "g_window" ) )
	|| maserati.Graphics.Contains( IdString.Hash( "g_maserati_lod" ) ) )
	throw new Exception( "Maserati must import its near body and glass without the overlapping far LOD." );
foreach ( var (path, kind) in new[]
{
	("units/world/props/office/desk_accessories/desk_phone_02", UnitKind.Prop),
	("units/world/architecture/diamondheist/diamondheist_floor_2", UnitKind.Architecture),
	("units/multiplayer/suit_male_5_husk/suit_male_5_husk", UnitKind.Character),
	("units/characters/npc/criminal/suit/russian_npc", UnitKind.Character),
	("units/vehicles/animated_cars/vehicle_street_drill_pickup_event/vehicle_street_drill_pickup_event", UnitKind.Scripted)
} )
	if ( UnitClassification.Classify( path, XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "unit", path ) ) ) ) != kind )
		throw new Exception( $"Incorrect browser classification: {path}" );
var phoneGeometry = GeometryFor( "units/world/props/office/desk_accessories/desk_phone_02" );
if ( phoneGeometry.CollisionTypes.GetValueOrDefault( IdString.Hash( "c_convex" ) ) != "convex" )
	throw new Exception( "Desk phone lost its authored convex collider." );
const string streetLights = "units/world/street/street_lights/street_lights";
var streetUnit = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "unit", streetLights ) ) );
var streetObject = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "object", streetLights ) ) );
var streetSequence = ScriptData.Read( db.Find( "sequence_manager", streetLights ) );
var streetState = SequenceVisibility.Read( streetSequence, "g_street_lights_c" )
	?? throw new Exception( "Literal street-light geometry state was not resolved." );
var streetGeometry = UnitGeometry.Read( streetUnit, streetObject, streetState );
if ( !streetGeometry.Graphics.Contains( IdString.Hash( "g_street_lights_c" ) )
	|| streetGeometry.Graphics.Contains( IdString.Hash( "g_street_lights_a" ) )
	|| !streetGeometry.Collision.Contains( IdString.Hash( "c_street_lights_c" ) )
	|| streetGeometry.Collision.Contains( IdString.Hash( "c_street_lights_a" ) )
	|| SequenceVisibility.Read( streetSequence, "missing_state" ) is not null )
	throw new Exception( "Street-light mesh variation lost matching graphics/collision selection." );
foreach ( string suffix in new[] { "", "_1m", "_10m" } )
{
	var splitter = GeometryFor( "units/dev_tools/level_tools/navigation_splitter" + suffix );
	if ( splitter.Graphics.Count != 0 || splitter.Collision.Count != 0 || splitter.CollisionTypes.Count != 0 )
		throw new Exception( $"Editor-only navigation splitter{suffix} leaked runtime geometry." );
}
if ( architecture.Graphics.Count == 0 || architecture.Collision.Count == 0 )
	throw new Exception( "Runtime architecture lost graphics or collision." );
Console.WriteLine( "PASS: invisible blockers, nonphysical light gizmos, and visible runtime architecture." );
var groupedWeapon = UnitGeometry.Read( XmlNode.Parse( "<unit/>" ), XmlNode.Parse( """
<dynamic_object><graphics><graphic_group enabled="true">
<object name="body" enabled="true"/><object name="dormant"/>
<graphic_group enabled="false"><object name="hidden" enabled="true"/></graphic_group>
<lod_object><object name="near" enabled="true"/><object name="far" lod="1" enabled="true"/></lod_object>
</graphic_group></graphics></dynamic_object>
""" ) );
if ( !groupedWeapon.Graphics.SetEquals( new[] { IdString.Hash( "body" ), IdString.Hash( "near" ) } ) )
	throw new Exception( "Weapon graphics groups leaked disabled/default/alternate LOD objects." );
var names = Encoding.Latin1.GetString( db.Find( "idstring_lookup", "idstring_lookup" ) ).Split( '\0' );
var maps = names.Where( s => s.StartsWith( "levels/" ) && db.Entries.ContainsKey( (IdString.Hash( "world" ), IdString.Hash( s )) ) ).Distinct().ToArray();
if ( maps.Length != 9 ) throw new Exception( $"Expected nine installed heist worlds, got {maps.Length}." );
foreach ( var map in maps )
{
	var environment = EnvironmentSettings.Read( db, ScriptData.Read( db.Find( "world", map ) ) );
	var reflection = environment.Reflection;
	if ( !environment.HasSky || !environment.Sun.HasValue )
		throw new Exception( $"Missing authored sun/hemisphere colours: {map}" );
	if ( string.IsNullOrEmpty( reflection ) || db.Find( "texture", reflection ) is not { } cubeBytes || !DdsCubemap.IsCube( cubeBytes ) )
		throw new Exception( $"Missing initial environment cubemap: {map}: {reflection}" );
}
var goldReflection = MaterialConfig.Read( db.Find( "material_config", "units/world/props/slaughterhouse/gold_bar/gold_bar" ) ).Values.Single( m => m.Name == "gold_bar" );
if ( !goldReflection.CubeReflection || goldReflection.Reflection != "environments/cubemaps/cubemap_gold" || goldReflection.GlobalReflection )
	throw new Exception( "Explicit gold reflection parsing failed." );
var truckGlassReflection = MaterialConfig.Read( db.Find( "material_config", "units/vehicles/truck_regular/man_vehicle_truck_variation_1" ) ).Values.Single( m => m.Name == "glass_breakable" );
if ( !truckGlassReflection.CubeFresnel || MathF.Abs( truckGlassReflection.Fresnel.y - 0.73f ) > 0.001f || MathF.Abs( truckGlassReflection.Fresnel.z - 0.24f ) > 0.001f )
	throw new Exception( "Authored reflection Fresnel parsing failed." );
Console.WriteLine( "PASS: nine initial environment cubemaps and explicit reflection/Fresnel materials." );
var bankEnvironment = EnvironmentSettings.Read( db, ScriptData.Read( db.Find( "world", "levels/bank/world" ) ) );
if ( MathF.Abs( bankEnvironment.Sun.Value.x - 1.5f ) > 0.0001f
	|| MathF.Abs( bankEnvironment.SkyTop.Value.x - 0.78740156f * 0.4f ) > 0.0001f
	|| MathF.Abs( bankEnvironment.SkyBottom.Value.z - 0.74901962f * 0.4f ) > 0.0001f )
	throw new Exception( "Environment colour scaling failed." );
var emptyEnvironment = EnvironmentSettings.FromData( new ScriptTable() );
if ( emptyEnvironment.Sun.HasValue || emptyEnvironment.HasSky ) throw new Exception( "Missing environment did not retain fallbacks." );
var sunParameter = new ScriptTable { Meta = "param", ["key"] = "sun_ray_color", ["value"] = new Vector3( 1, 0.5f, 0.25f ) };
var scaleParameter = new ScriptTable { Meta = "param", ["key"] = "sun_ray_color_scale", ["value"] = 0f };
var environmentFixture = new ScriptTable { ["others"] = new ScriptTable { ["1"] = sunParameter, ["2"] = scaleParameter } };
if ( EnvironmentSettings.FromData( environmentFixture ).Sun != Vector3.Zero ) throw new Exception( "Zero sunlight was replaced by preview light." );
scaleParameter["value"] = float.NaN;
if ( EnvironmentSettings.FromData( environmentFixture ).Sun.HasValue ) throw new Exception( "Non-finite environment scale accepted." );
scaleParameter["value"] = -1f;
if ( EnvironmentSettings.FromData( environmentFixture ).Sun.HasValue ) throw new Exception( "Negative environment scale accepted." );
Console.WriteLine( "PASS: nine authored sun/hemisphere environments, colour scaling, zero and invalid-value handling." );
// Sun direction from the underlay rig. Reference poses come from the dumped embedded controllers:
// both groups at 0.5 put d_sun at (340864, 0, 340864) in Diesel axes, i.e. 45° up along Diesel +X.
const string skyUnderlay = "core/environments/skies/default/default";
static bool Near( Vector3 a, Vector3 b ) => (a - b).Length < 0.01f;
if ( SunDirection.Evaluate( db, skyUnderlay, 0.5f, 0.5f, 0 ) is not { } midSun || !Near( midSun, new Vector3( 0, -0.70710677f, 0.70710677f ) ) )
	throw new Exception( "Underlay sun rig sampled at (0.5, 0.5) did not match the reference pose." );
if ( SunDirection.Evaluate( db, skyUnderlay, 1, 1, 0 ) is not { } zenith || zenith.z < 0.999f )
	throw new Exception( "Underlay sun rig at full vertical time must be overhead." );
// sky_rot turns the dome clockwise from above: Diesel +X (s&box -Y) at 90° becomes Diesel -Y (s&box -X).
if ( SunDirection.Evaluate( db, skyUnderlay, 0.5f, 0.5f, 90 ) is not { } turned || !Near( turned, new Vector3( -0.70710677f, 0, 0.70710677f ) ) )
	throw new Exception( "sky_rot must spin the underlay clockwise about up." );
// Ground truth: Counterfeit's own sky dome (suburbia_sky) paints its sun at azimuth ≈ -122.5°, elevation ≈ 45°.
var counterfeitSun = EnvironmentSettings.Read( db, ScriptData.Read( db.Find( "world", "levels/suburbia/world" ) ) ).SunDirection
	?? throw new Exception( "Counterfeit sun direction missing." );
float counterfeitAzimuth = MathF.Atan2( counterfeitSun.y, counterfeitSun.x ) * 180 / MathF.PI;
float counterfeitElevation = MathF.Asin( counterfeitSun.z ) * 180 / MathF.PI;
if ( MathF.Abs( counterfeitAzimuth + 122.5f ) > 15 || MathF.Abs( counterfeitElevation - 45f ) > 8 )
	throw new Exception( $"Counterfeit sun ({counterfeitAzimuth:0.0}° az, {counterfeitElevation:0.0}° el) no longer matches its painted sky dome." );
if ( SunDirection.Evaluate( db, "missing/underlay", 0.5f, 0.5f, 0 ) is not null || SunDirection.Evaluate( db, skyUnderlay, float.NaN, 0.5f, 0 ) is not null )
	throw new Exception( "Missing underlay or invalid sun time was accepted." );
foreach ( var sunMap in new[] { "apartment", "bank", "bridge", "diamondheist", "l4d", "secret_stash", "slaughterhouse", "street", "suburbia" } )
{
	var sunEnvironment = EnvironmentSettings.Read( db, ScriptData.Read( db.Find( "world", $"levels/{sunMap}/world" ) ) );
	if ( sunEnvironment.SunDirection is not { } towardsSun || MathF.Abs( towardsSun.Length - 1 ) > 0.001f || towardsSun.z <= 0 )
		throw new Exception( $"{sunMap}: authored sun direction missing or below the horizon ({sunEnvironment.SunDirection})." );
	float sunElevation = MathF.Asin( towardsSun.z ) * 180 / MathF.PI;
	float sunAzimuth = MathF.Atan2( towardsSun.y, towardsSun.x ) * 180 / MathF.PI;
	Console.WriteLine( $"  {sunMap}: sun_anim={sunEnvironment.SunVertical:0.###} sun_anim_x={sunEnvironment.SunHorizontal:0.###} sky_rot={sunEnvironment.SkyRotation:0} -> elevation {sunElevation:0.0}°, azimuth {sunAzimuth:0.0}°" );
}
Console.WriteLine( "PASS: underlay sun rig reference poses, sky rotation, invalid inputs and nine authored sun directions." );
// Distance fog: Bank's authored values; No Mercy authors none (alpha1 0, colour scale 0).
var bankFog = bankEnvironment.Fog ?? throw new Exception( "Bank fog missing." );
if ( MathF.Abs( bankFog.Start - 0.001f ) > 0.0001f || MathF.Abs( bankFog.End - 0.365f ) > 0.001f || MathF.Abs( bankFog.FarOpacity - 0.741f ) > 0.001f
	|| MathF.Abs( bankFog.Color.x - 0.76f ) > 0.001f )
	throw new Exception( $"Bank fog parse regressed: {bankFog}" );
if ( EnvironmentSettings.Read( db, ScriptData.Read( db.Find( "world", "levels/l4d/world" ) ) ).Fog is not null )
	throw new Exception( "No Mercy's disabled fog was imported." );
var counterfeitFog = EnvironmentSettings.Read( db, ScriptData.Read( db.Find( "world", "levels/suburbia/world" ) ) ).Fog;
if ( counterfeitFog is null || counterfeitFog.Color.x <= 1 ) throw new Exception( "Counterfeit's over-bright fog colour scale (2.56) was lost." );
Console.WriteLine( "PASS: authored distance fog (Bank values, No Mercy disabled, Counterfeit HDR colour)." );
// Underlay sky gradient: every map authors three colours; g_sky's V encodes height (horizon 0, zenith -1).
var bankSky = bankEnvironment.SkyGradient ?? throw new Exception( "Bank sky gradient missing." );
if ( MathF.Abs( bankSky[0].x - 0.637f * 0.8f ) > 0.001f || MathF.Abs( bankSky[2].z - 1f * 0.8f ) > 0.001f )
	throw new Exception( "Bank sky colour scaling regressed." );
if ( (EnvironmentSettings.SampleSky( bankSky, 1 ) - bankSky[0]).Length > 0.0001f || (EnvironmentSettings.SampleSky( bankSky, 0 ) - bankSky[2]).Length > 0.0001f
	|| (EnvironmentSettings.SampleSky( bankSky, 0.5f ) - bankSky[1]).Length > 0.0001f || (EnvironmentSettings.SampleSky( bankSky, -1 ) - bankSky[2]).Length > 0.0001f )
	throw new Exception( "Sky gradient stops regressed." );
foreach ( var skyMap in new[] { "apartment", "bridge", "diamondheist", "l4d", "secret_stash", "slaughterhouse", "street", "suburbia" } )
	if ( EnvironmentSettings.Read( db, ScriptData.Read( db.Find( "world", $"levels/{skyMap}/world" ) ) ).SkyGradient is null )
		throw new Exception( $"{skyMap}: sky gradient missing." );
var skyGeometry = new ModelFile( db.Find( "model", skyUnderlay ) ).ReadGeometry( new HashSet<ulong> { IdString.Hash( "g_sky" ) }, IdString.Hash( "rp_skydome" ) ).Single();
int zenithIndex = Array.IndexOf( skyGeometry.Positions, skyGeometry.Positions.MaxBy( p => p.z ) );
if ( skyGeometry.UVs[zenithIndex].y > -0.9f || skyGeometry.UVs.Max( uv => uv.y ) > 0.2f )
	throw new Exception( "g_sky no longer maps V to dome height." );
Console.WriteLine( "PASS: nine underlay sky gradients and g_sky height mapping." );
// Mission scripts: every level parses, startup scripts exist, and mission-referenced unit ids resolve to placements.
var sequenceListeners = new Dictionary<string, (int Count, Dictionary<string, int> Units)>();
var sequenceVisible = new Dictionary<string, (int Visible, int Total)>();
var lenientCache = new Dictionary<(string, string), bool>();
bool LenientChanges( string unitPath, string sequence )
{
	if ( lenientCache.TryGetValue( (unitPath, sequence), out var cached ) ) return cached;
	bool changes = false;
	try
	{
		var unitXml = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "unit", unitPath ) ) );
		var objXml = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "object", unitXml.Element( "object" ).Attribute( "file" ) ) ) );
		if ( objXml.Element( "sequence_manager" )?.Attribute( "file" ) is string smPath && db.Find( "sequence_manager", smPath ) is { } smBytes
			&& SequenceVisibility.Read( ScriptData.Read( smBytes ), sequence, lenient: true ) is { } state )
		{
			var graphicsNames = UnitGeometry.GraphicsObjects( objXml ).Select( e => e.Attribute( "name" ) ).ToHashSet();
			var bodyNames = (objXml.Element( "bodies" )?.Elements( "body" ) ?? []).Select( e => e.Attribute( "name" ) ).ToHashSet();
			// Mirror UnitModel.Consistent: hide-only states (no replacement, collision kept) aren't applied at runtime.
			bool shows = state.Objects.Any( o => o.Value && graphicsNames.Contains( o.Key ) );
			bool hides = state.Objects.Any( o => !o.Value && graphicsNames.Contains( o.Key ) );
			bool removesBody = state.Bodies.Any( b => !b.Value && bodyNames.Contains( b.Key ) );
			changes = (state.Objects.Keys.Any( graphicsNames.Contains ) || state.Bodies.Keys.Any( bodyNames.Contains )) && (shows || removesBody || !hides);
		}
	}
	catch ( Exception ) { }
	return lenientCache[(unitPath, sequence)] = changes;
}
foreach ( var missionMap in new[] { "apartment", "bank", "bridge", "diamondheist", "l4d", "secret_stash", "slaughterhouse", "street", "suburbia" } )
{
	var mission = MissionData.Read( db, $"levels/{missionMap}" );
	if ( mission.Elements.Count == 0 ) throw new Exception( $"{missionMap}: no mission elements." );
	var placedIds = new HashSet<int>();
	var placedNames = new Dictionary<int, string>();
	void CollectIds( ScriptTable layer )
	{
		foreach ( var category in new[] { "statics", "dynamics" } )
			foreach ( var entry in layer.Table( category )?.Children ?? [] )
				if ( entry.Table( "unit_data" ) is { } unitData && unitData.GetValueOrDefault( "unit_id" ) is float uid )
				{
					placedIds.Add( (int)uid );
					placedNames[(int)uid] = unitData.Text( "name" ) ?? "?";
				}
	}
	var missionWorld = ScriptData.Read( db.Find( "world", $"levels/{missionMap}/world" ) );
	CollectIds( missionWorld );
	if ( missionWorld.Table( "world_data" )?.Text( "continents_file" ) is { } continentsName
		&& db.Find( "continents", $"levels/{missionMap}/{continentsName}" ) is { } continentsBytes )
		foreach ( var (continentName, _) in ScriptData.Read( continentsBytes ) )
			if ( db.Find( "continent", $"levels/{missionMap}/{continentName}/{continentName}" ) is { } layerBytes ) CollectIds( ScriptData.Read( layerBytes ) );
	var referenced = mission.ReferencedUnits();
	int resolved = referenced.Count( placedIds.Contains );
	var startup = mission.Scripts.Where( s => s.ActivateOnParsed || s.Name == "default" ).ToList();
	var dangling = mission.Elements.Values.SelectMany( e => e.OnExecuted ).Count( l => !mission.Elements.ContainsKey( l.Id ) );
	Console.WriteLine( $"  {missionMap}: {mission.Scripts.Count} scripts ({string.Join( ", ", mission.Scripts.Where( s => s.ActivateOnParsed ).Select( s => s.Name ) )} on parse), "
		+ $"default={mission.Scripts.Any( s => s.Name == "default" )}, {mission.Elements.Count} elements, "
		+ $"{startup.Sum( s => s.Elements.Count( e => e.ExecuteOnStartup ) )} startup elements, {dangling} dangling links, "
		+ $"units {resolved}/{referenced.Count} resolved ({placedIds.Count} placements carry unit_id)" );
	foreach ( var trigger in mission.Elements.Values.Where( e => e.Class == "ElementUnitSequenceTrigger" ) )
		foreach ( var entry in trigger.Values.Table( "sequence_list" )?.Children ?? [] )
			if ( entry.Text( "sequence" ) is { Length: > 0 } listened )
			{
				if ( !sequenceListeners.TryGetValue( listened, out var stats ) ) sequenceListeners[listened] = stats = (0, new Dictionary<string, int>());
				string unitName = entry.GetValueOrDefault( "unit_id" ) is float lid && placedNames.TryGetValue( (int)lid, out var placedUnitName ) ? placedUnitName[(placedUnitName.LastIndexOf( '/' ) + 1)..] : "unresolved";
				stats.Units[unitName] = stats.Units.GetValueOrDefault( unitName ) + 1;
				sequenceListeners[listened] = (stats.Count + 1, stats.Units);
				// Would the runtime's lenient evaluator change this unit's visible geometry for this sequence?
				bool visible = false;
				if ( entry.GetValueOrDefault( "unit_id" ) is float vid && placedNames.TryGetValue( (int)vid, out var fullUnit ) )
					visible = LenientChanges( fullUnit, listened );
				var tally = sequenceVisible.GetValueOrDefault( listened );
				sequenceVisible[listened] = (tally.Visible + (visible ? 1 : 0), tally.Total + 1);
			}
	if ( startup.Count == 0 ) throw new Exception( $"{missionMap}: no mission script would start (no activate_on_parsed and no 'default')." );
	if ( referenced.Count > 0 && resolved < referenced.Count * 0.9f ) throw new Exception( $"{missionMap}: only {resolved}/{referenced.Count} mission units resolve to placements." );
}
Console.WriteLine( "  sequences listened for by ElementUnitSequenceTrigger (count: top units):" );
foreach ( var (listened, stats) in sequenceListeners.OrderByDescending( s => s.Value.Count ).Take( 40 ) )
	Console.WriteLine( $"    {listened} ×{stats.Count} (visible change {sequenceVisible.GetValueOrDefault( listened ).Visible}/{sequenceVisible.GetValueOrDefault( listened ).Total}): {string.Join( ", ", stats.Units.OrderByDescending( u => u.Value ).Take( 6 ).Select( u => $"{u.Key}×{u.Value}" ) )}" );
// Unit damage rules (sequence-manager body endurance), checked against the dumped Bank large window.
UnitEndurance EnduranceOf( string unitPath )
{
	var unitXml = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "unit", unitPath ) ) );
	var objXml = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "object", unitXml.Element( "object" ).Attribute( "file" ) ) ) );
	return objXml.Element( "sequence_manager" )?.Attribute( "file" ) is string managerPath && db.Find( "sequence_manager", managerPath ) is { } managerBytes
		? UnitEndurance.Read( ScriptData.Read( managerBytes ), objXml ) : null;
}
var bankWindow = EnduranceOf( "units/world/architecture/bank/bank_window/bank_window_large" ) ?? throw new Exception( "Bank window endurance missing." );
var windowStages = bankWindow.Bodies.GetValueOrDefault( "window3" );
if ( windowStages is not { Count: 2 } || windowStages[0].Thresholds["bullet"] != 1 || windowStages[1].Thresholds["bullet"] != 2
	|| windowStages[0].Sequences.Single() != "destroy_01" || windowStages[1].Sequences.Single() != "destroy_02"
	|| !bankWindow.DefaultEnabledBodies.Contains( "window3" ) || bankWindow.BodyToggles( "destroy_02" ).GetValueOrDefault( "window3", true ) )
	throw new Exception( "Bank window endurance stages/body toggles regressed." );
var rooftop = EnduranceOf( "units/world/architecture/diamondheist/windows/diamondheist_rooftop_window" );
if ( rooftop is null || rooftop.DefaultEnabledBodies.Contains( "dmg_01" ) || rooftop.BodyToggles( "destroy_01" ).GetValueOrDefault( "dmg_01" ) != true )
	throw new Exception( "Rooftop window body hand-off (window_vault -> dmg_01/dmg_02) regressed." );
int breakablePlacements = 0, enduranceUnits = 0;
var enduranceByUnit = new Dictionary<string, bool>();
foreach ( var breakMap in new[] { "apartment", "bank", "bridge", "diamondheist", "l4d", "secret_stash", "slaughterhouse", "street", "suburbia" } )
{
	int mapBreakables = 0;
	void CountBreakables( ScriptTable layer )
	{
		foreach ( var category in new[] { "statics", "dynamics" } )
			foreach ( var entry in layer.Table( category )?.Children ?? [] )
				if ( entry.Table( "unit_data" )?.Text( "name" ) is { } placedUnit )
				{
					if ( !enduranceByUnit.TryGetValue( placedUnit, out bool has ) )
					{
						try { has = EnduranceOf( placedUnit ) is not null; } catch ( Exception ) { has = false; }
						enduranceByUnit[placedUnit] = has;
						if ( has ) enduranceUnits++;
					}
					if ( has ) mapBreakables++;
				}
	}
	var breakWorld = ScriptData.Read( db.Find( "world", $"levels/{breakMap}/world" ) );
	CountBreakables( breakWorld );
	if ( breakWorld.Table( "world_data" )?.Text( "continents_file" ) is { } breakContinents && db.Find( "continents", $"levels/{breakMap}/{breakContinents}" ) is { } breakBytes )
		foreach ( var (breakContinentName, breakValue) in ScriptData.Read( breakBytes ) )
			if ( breakValue is ScriptTable breakContinent && !breakContinent.Flag( "editor_only" ) && breakContinent.Flag( "enabled", true )
				&& db.Find( "continent", $"levels/{breakMap}/{breakContinentName}/{breakContinentName}" ) is { } breakLayerBytes )
				CountBreakables( ScriptData.Read( breakLayerBytes ) );
	breakablePlacements += mapBreakables;
	Console.WriteLine( $"  {breakMap}: {mapBreakables} breakable placements" );
}
Console.WriteLine( $"PASS: unit endurance rules (Bank window stages, rooftop body hand-off); {enduranceUnits} placed unit types, {breakablePlacements} breakable placements." );
// AI-facing elements: which NPC event names and special-objective actions the shipped missions use.
var npcEventNames = new Dictionary<string, int>();
var soActions = new Dictionary<string, int>();
var soTriggerEvents = new Dictionary<string, int>();
foreach ( var aiMap in new[] { "apartment", "bank", "bridge", "diamondheist", "l4d", "secret_stash", "slaughterhouse", "street", "suburbia" } )
	foreach ( var element in MissionData.Read( db, $"levels/{aiMap}" ).Elements.Values )
	{
		var counts = element.Class switch
		{
			"ElementEnemyDummyTrigger" => npcEventNames,
			"ElementSpecialObjective" => soActions,
			"ElementSpecialObjectiveTrigger" => soTriggerEvents,
			_ => null,
		};
		if ( counts is null ) continue;
		string key = element.Class == "ElementSpecialObjective" ? element.Text( "so_action" ) ?? "nil" : element.Text( "event" ) ?? "nil";
		counts[key] = counts.GetValueOrDefault( key ) + 1;
	}
Console.WriteLine( $"  ElementEnemyDummyTrigger events: {string.Join( ", ", npcEventNames.OrderByDescending( e => e.Value ).Select( e => $"{e.Key}×{e.Value}" ) )}" );
Console.WriteLine( $"  ElementSpecialObjectiveTrigger events: {string.Join( ", ", soTriggerEvents.OrderByDescending( e => e.Value ).Select( e => $"{e.Key}×{e.Value}" ) )}" );
Console.WriteLine( $"  ElementSpecialObjective so_action (top 40): {string.Join( ", ", soActions.OrderByDescending( e => e.Value ).Take( 40 ).Select( e => $"{e.Key}×{e.Value}" ) )}" );
// General sound import: harvested event names vs. every bank's playable events, and world_sounds emitter resolution.
var soundLevels = new[] { "apartment", "bank", "bridge", "diamondheist", "l4d", "secret_stash", "slaughterhouse", "street", "suburbia" }.Select( l => $"levels/{l}" ).ToList();
var soundStarted = DateTime.UtcNow;
var harvested = SoundNames.Collect( db, soundLevels );
Console.WriteLine( $"  sound names harvested: {harvested.Count} in {(DateTime.UtcNow - soundStarted).TotalSeconds:0.0}s" );
var bnkType = IdString.Hash( "bnk" );
var bankNames = Encoding.Latin1.GetString( db.Find( "idstring_lookup", "idstring_lookup" ) ).Split( '\0' )
	.Where( n => n.Length > 0 && db.Entries.ContainsKey( (bnkType, IdString.Hash( n )) ) ).Distinct().OrderBy( n => n ).ToList();
int allEvents = 0, playableEvents = 0, namedEvents = 0;
var eventBanks = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
foreach ( var bankPath in bankNames )
{
	string shortBank = SoundNames.BankShortName( bankPath );
	try
	{
		var bank = new WwiseBank( db.Find( "bnk", bankPath ), id => db.Find( "stream", $"soundbanks/streamed/{shortBank}/{id}" ) );
		var ids = bank.EventIds.ToList();
		var playable = ids.Where( bank.IsPlayable ).ToList();
		int namedHere = playable.Count( harvested.ContainsKey );
		foreach ( var id in playable.Where( harvested.ContainsKey ) ) eventBanks.TryAdd( harvested[id], shortBank );
		allEvents += ids.Count; playableEvents += playable.Count; namedEvents += namedHere;
		Console.WriteLine( $"    {shortBank}: {ids.Count} events, {playable.Count} playable, {namedHere} named" );
	}
	catch ( Exception e ) { Console.WriteLine( $"    {shortBank}: unreadable ({e.Message})" ); }
}
// Resolving an event does not prove its media decodes: the aquarium includes 6 kHz ADPCM.
var aquariumBank = new WwiseBank( db.Find( "bnk", bankNames.Single( p => SoundNames.BankShortName( p ) == "emitters_office" ) ),
	id => db.Find( "stream", $"soundbanks/streamed/emitters_office/{id}" ) );
var aquariumMedia = aquariumBank.Resolve( "emitter_aquarium_wall" ).Select( aquariumBank.Decode ).ToArray();
if ( !aquariumMedia.Any( wav => BitConverter.ToInt32( wav, 24 ) == 6000 && wav.Length > 44 ) )
	throw new Exception( "Aquarium 6 kHz media did not decode." );
var aquariumLayers = aquariumBank.RenderLoopLayers( WwiseBank.Hash( "emitter_aquarium_wall" ), "emitter_aquarium_wall" );
if ( aquariumLayers.Count == 0 || aquariumLayers.Any( wav => wav.Length <= 44 || BitConverter.ToInt32( wav, 24 ) != 44100 ) )
	throw new Exception( "Aquarium loop resampling failed." );
Console.WriteLine( $"PASS: aquarium 6 kHz media decoded and rendered into {aquariumLayers.Count} playback layers." );
int emittersTotal = 0, emittersResolved = 0;
foreach ( var level in soundLevels )
	if ( db.Find( "world_sounds", $"{level}/world_sounds" ) is { } worldSoundBytes )
		foreach ( var emitter in ScriptData.Read( worldSoundBytes ).Table( "sound_emitters" )?.Children ?? [] )
		{
			emittersTotal++;
			if ( emitter.Text( "emitter_event" ) is { } emitterEvent && eventBanks.ContainsKey( emitterEvent ) ) emittersResolved++;
		}
Console.WriteLine( $"PASS: {playableEvents} playable of {allEvents} sound events across {bankNames.Count} banks, {namedEvents} named; world_sounds emitters resolved {emittersResolved}/{emittersTotal}." );
// Asset sounds: sequence `sound` actions on breakables and doors, resolved against the registered (named) bank events.
ScriptTable ManagerOf( string unitPath )
{
	var unitXml = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "unit", unitPath ) ) );
	var objXml = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "object", unitXml.Element( "object" ).Attribute( "file" ) ) ) );
	return objXml.Element( "sequence_manager" )?.Attribute( "file" ) is string managerPath && db.Find( "sequence_manager", managerPath ) is { } managerBytes
		? ScriptData.Read( managerBytes ) : null;
}
var windowSounds = SequenceSounds.Read( ManagerOf( "units/world/architecture/bank/bank_window/bank_window_large" ), "destroy_02" );
if ( !windowSounds.Contains( "window_large_shatter" ) || !eventBanks.ContainsKey( "window_large_shatter" ) )
	throw new Exception( $"Bank window destroy_02 sound regressed: [{string.Join( ", ", windowSounds )}]." );
int stageSequences = 0, stageWithSound = 0, stageResolved = 0, doorUnits = 0, doorOpen = 0, doorClose = 0;
var unresolvedAssetSounds = new SortedSet<string>( StringComparer.OrdinalIgnoreCase );
var doorSamples = new List<string>();
foreach ( var placedUnit in enduranceByUnit.Keys.OrderBy( u => u ) )
{
	ScriptTable manager;
	try { manager = ManagerOf( placedUnit ); } catch ( Exception ) { continue; }
	if ( manager is null ) continue;
	if ( enduranceByUnit[placedUnit] && EnduranceOf( placedUnit ) is { } rules )
		foreach ( var sequence in rules.Bodies.Values.SelectMany( s => s ).SelectMany( s => s.Sequences ).Distinct() )
		{
			stageSequences++;
			var sounds = SequenceSounds.Read( manager, sequence );
			if ( sounds.Count > 0 ) stageWithSound++;
			if ( sounds.Any( eventBanks.ContainsKey ) ) stageResolved++;
			foreach ( var sound in sounds.Where( s => !eventBanks.ContainsKey( s ) ) ) unresolvedAssetSounds.Add( sound );
		}
	if ( placedUnit.Contains( "door", StringComparison.OrdinalIgnoreCase ) || placedUnit.Contains( "gate", StringComparison.OrdinalIgnoreCase ) )
	{
		var (open, close) = SequenceSounds.DoorSounds( manager );
		if ( open is null && close is null && !SequenceSounds.SequenceNames( manager ).Any( n => n.Contains( "open" ) ) ) continue;
		doorUnits++;
		if ( open is not null && eventBanks.ContainsKey( open ) ) doorOpen++;
		if ( close is not null && eventBanks.ContainsKey( close ) ) doorClose++;
		if ( doorSamples.Count < 12 ) doorSamples.Add( $"{placedUnit[(placedUnit.LastIndexOf( '/' ) + 1)..]}: open={open ?? "-"} close={close ?? "-"}" );
	}
}
foreach ( var sample in doorSamples ) Console.WriteLine( $"    door {sample}" );
if ( unresolvedAssetSounds.Count > 0 ) Console.WriteLine( $"  unresolved asset sounds: {string.Join( ", ", unresolvedAssetSounds.Take( 30 ) )}" );
Console.WriteLine( $"PASS: asset sounds: {stageWithSound}/{stageSequences} breakable stage sequences play a sound ({stageResolved} resolve); "
	+ $"{doorUnits} openable door units, {doorOpen} with an open sound, {doorClose} with a close sound." );
// Localization tables: waypoint text ids resolve through strings/system_text.
var stringTables = StringTable.ReadAll( db );
if ( stringTables.Count < 1000 || stringTables.GetValueOrDefault( IdString.Hash( "wp_bank_get_drill" ) ) is not { Length: > 0 } drillText )
	throw new Exception( "strings tables / waypoint text lookup regressed." );
Console.WriteLine( $"PASS: {stringTables.Count} localized strings; wp_bank_get_drill = \"{drillText}\"." );
var unmappedSpawns = new SortedSet<string>();
foreach ( var spawnMap in new[] { "apartment", "bank", "bridge", "diamondheist", "l4d", "secret_stash", "slaughterhouse", "street", "suburbia" } )
	foreach ( var spawn in MissionData.Read( db, $"levels/{spawnMap}" ).Elements.Values.Where( e => e.Class is "ElementSpawnCivilian" or "ElementSpawnEnemyDummy" ) )
		if ( NpcDefinition.ForUnit( spawn.Text( "enemy" ) ) is { } npc && npc.Unit != spawn.Text( "enemy" ) ) unmappedSpawns.Add( $"{spawn.Text( "enemy" )} -> {npc.Id}" );
Console.WriteLine( $"  spawn units approximated by roster: {string.Join( "; ", unmappedSpawns )}" );
Console.WriteLine( "PASS: nine mission script sets, startup scripts and mission unit references." );
// Brush scatter (massunit). Every referenced unit must resolve through the installed lookup, as MapLoader does.
var unitTypeHash = IdString.Hash( "unit" );
var lookupUnits = new Dictionary<ulong, string>();
foreach ( var lookupName in Encoding.Latin1.GetString( db.Find( "idstring_lookup", "idstring_lookup" ) ).Split( '\0' ) )
	if ( lookupName.Length > 0 && db.Entries.ContainsKey( (unitTypeHash, IdString.Hash( lookupName )) ) )
		lookupUnits.TryAdd( IdString.Hash( lookupName ), lookupName );
int brushInstances = 0, brushUnits = 0;
foreach ( var brushMap in new[] { "apartment", "bank", "bridge", "diamondheist", "l4d", "secret_stash", "slaughterhouse", "street", "suburbia" } )
{
	var brushWorld = ScriptData.Read( db.Find( "world", $"levels/{brushMap}/world" ) );
	string brushFile = brushWorld.Table( "brush" )?.Text( "file" ) ?? throw new Exception( $"{brushMap}: world has no brush file." );
	var massUnits = MassUnitFile.Read( db.Find( "massunit", $"levels/{brushMap}/{brushFile}" ) ?? throw new Exception( $"{brushMap}: missing massunit." ) );
	foreach ( var entry in massUnits )
	{
		if ( !lookupUnits.TryGetValue( entry.Unit, out var brushUnit ) ) throw new Exception( $"{brushMap}: brush unit {entry.Unit:x16} is not an installed unit." );
		foreach ( var instance in entry.Instances )
			if ( instance.Position.Length > 1_000_000 ) throw new Exception( $"{brushMap}: brush {brushUnit} placed implausibly far away." );
		brushInstances += entry.Instances.Length;
	}
	brushUnits += massUnits.Count;
	Console.WriteLine( $"  {brushMap}: {massUnits.Count} brush units, {massUnits.Sum( e => e.Instances.Length )} instances" );
}
var bankBrushes = MassUnitFile.Read( db.Find( "massunit", "levels/bank/massunit" ) );
if ( bankBrushes.Count != 27 || bankBrushes[0].Unit != IdString.Hash( "units/world/brushes/bank_papers/bank_papers_2" ) || bankBrushes[0].Instances.Length != 11 )
	throw new Exception( "Bank massunit header/entry layout regressed." );
Reject( () => MassUnitFile.Read( db.Find( "massunit", "levels/bank/massunit" )[..900] ) );
Console.WriteLine( $"PASS: {brushUnits} brush unit entries and {brushInstances} brush instances across nine massunit files." );
var units = new HashSet<string>();
var configs = new Dictionary<string, Dictionary<ulong, MaterialDefinition>>();
var texturePaths = new HashSet<string>();
int draws = 0, resolvedMaterials = 0, missingMaterials = 0, invalidConfigs = 0;
int visibleDraws = 0, visibleFallbacks = 0;
int placements = 0, meshes = 0, triangles = 0, missing = 0;
int primitiveCount = 0;
var variations = new Dictionary<string, HashSet<string>>();
int variationPlacements = 0;
var meshVariations = new Dictionary<string, Dictionary<string, int>>();
int supportedMeshPlacements = 0, unsupportedMeshPlacements = 0;
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
var redContainer = MaterialConfig.Read( db.Find( "material_config", "units/world/props/container/container_red" ) )[IdString.Hash( "container" )];
if ( !redContainer.BlueMaskTint || MathF.Abs( redContainer.TintColor.x - 0.5490196f ) > 0.00001f
	|| MathF.Abs( redContainer.TintColor.z - 0.3254902f ) > 0.00001f || ticket.BlueMaskTint )
	throw new Exception( "Container blue-mask tint must retain its authored colour and template flag." );
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
				var unitData = entry.Table( "unit_data" );
				string meshVariation = unitData.Text( "mesh_variation" );
				if ( !string.IsNullOrEmpty( meshVariation ) && meshVariation != "default" )
				{
					string unitName = unitData.Text( "name" );
					if ( !meshVariations.TryGetValue( unitName, out var states ) ) meshVariations[unitName] = states = [];
					states[meshVariation] = states.GetValueOrDefault( meshVariation ) + 1;
				}
				string variation = unitData.Text( "material_variation" );
				if ( !string.IsNullOrEmpty( variation ) && variation != "default" )
				{
					variationPlacements++;
					string unitName = unitData.Text( "name" );
					if ( !variations.TryGetValue( unitName, out var values ) ) variations[unitName] = values = [];
					values.Add( variation );
				}
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
		var alternateConfigs = (variations.GetValueOrDefault( unit ) ?? []).Select( variation =>
		{
			var bytes = db.Find( "material_config", variation ) ?? throw new Exception( $"Missing variation {variation}" );
			return MaterialConfig.Read( bytes );
		} ).ToArray();
		var selected = new HashSet<ulong>( visible );
		selected.UnionWith( geometry.Collision );
		var file = new ModelFile( db.Find( "model", path ) );
		if ( meshVariations.TryGetValue( unit, out var requestedStates ) )
		{
			string sequencePath = obj.Element( "sequence_manager" )?.Attribute( "file" );
			var sequenceBytes = sequencePath is null ? null : db.Find( "sequence_manager", sequencePath );
			var sequence = sequenceBytes is null ? null : ScriptData.Read( sequenceBytes );
			foreach ( var (state, count) in requestedStates )
			{
				var visibility = sequence is null ? null : SequenceVisibility.Read( sequence, state );
				if ( visibility is null || !visibility.Matches( obj ) ) { unsupportedMeshPlacements += count; continue; }
				supportedMeshPlacements += count;
				var variantGeometry = UnitGeometry.Read( xml, obj, visibility );
				var variantNodes = new HashSet<ulong>( variantGeometry.Graphics );
				variantNodes.UnionWith( variantGeometry.Collision );
				foreach ( var mesh in file.ReadGeometry( variantNodes, 0 ) )
					if ( mesh.Indices.Any( i => i < 0 || i >= mesh.Positions.Length ) )
						throw new Exception( $"Invalid variant geometry: {unit}/{state}" );
			}
		}
		lightDefinitions[unit] = UnitLights.Read( obj, file );
		string orientation = obj.Element( "diesel" )?.Attribute( "orientation_object" );
		var primitives = file.ReadCollisionPrimitives( geometry.CollisionTypes, orientation is null ? 0 : IdString.Hash( orientation ) ).ToArray();
		primitiveCount += primitives.Length;
		foreach ( var primitive in primitives )
		{
			if ( primitive.Points.Length < 8 || primitive.Points.Any( v => !float.IsFinite( v.x + v.y + v.z ) ) )
				throw new Exception( "Invalid bounds-only collision primitive." );
			if ( primitive.BoxVolume is { } box )
			{
				var p = primitive.Points;
				double fromCorners = Math.Abs( Vector3.Dot( p[1] - p[0], Vector3.Cross( p[2] - p[0], p[4] - p[0] ) ) )
					* Math.Pow( 0.0254, 3 );
				double volume = box.CubicMeters( 0 );
				if ( !double.IsFinite( volume ) || volume < 0 || Math.Abs( fromCorners - volume ) > Math.Max( 0.00001, volume * 0.001 ) )
					throw new Exception( $"Source box volume disagrees with transformed collider corners: {path}." );
			}
		}
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
				if ( visible.Contains( mesh.Name ) )
					foreach ( var alternate in alternateConfigs )
					{
						if ( !alternate.TryGetValue( draw.Material, out var alternateMaterial ) && !definitions.TryGetValue( draw.Material, out alternateMaterial ) )
							throw new Exception( $"Material variation lost visible assignment in {unit}" );
						foreach ( string texture in new[] { alternateMaterial.Diffuse, alternateMaterial.Opacity, alternateMaterial.Normal, alternateMaterial.Emission, alternateMaterial.Detail } )
							if ( texture is not null ) texturePaths.Add( texture );
					}
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
				if ( material.GroundBlend )
				{
					if ( material.BlendDiffuse is not null ) texturePaths.Add( material.BlendDiffuse );
					if ( material.BlendMask is not null ) texturePaths.Add( material.BlendMask );
				}
				if ( material.SelfIlluminated && material.Emission is not null ) texturePaths.Add( material.Emission );
			}
		}
	}
	catch ( Exception e ) { throw new Exception( $"Failed unit {unit}: {e.Message}", e ); }
}
Console.WriteLine( $"PASS: {maps.Length} worlds, {placements} placements, {units.Count} unique units, {meshes} meshes, {triangles} triangles, {missing} non-model units." );
Console.WriteLine( $"PASS: {variationPlacements} material-variation placements, {variations.Sum( pair => pair.Value.Count )} distinct unit/config pairs." );
Console.WriteLine( $"PASS: {supportedMeshPlacements} geometry-only mesh-variation placements; {unsupportedMeshPlacements} complex states retain default geometry." );
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
