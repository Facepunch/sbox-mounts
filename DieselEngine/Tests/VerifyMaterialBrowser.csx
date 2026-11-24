// Run with the editor's run_csharp tool. Does not modify the active scene.
var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
await mount.RefreshInternal();
var entries = mount.GetAll( Sandbox.Mounting.ResourceType.Material );
if ( entries.Count == 0 ) throw new Exception( "No standalone materials registered." );
var results = new System.Collections.Generic.List<object>();
foreach ( string config in new[]
{
	"units/vehicles/maserati/low/vehicle_maserati_low",
	"units/world/props/office/desk_accessories/desk_phone_02",
	"units/world/props/heliport_light/heliport_light"
} )
{
	var slots = entries.Where( e => e.Path.Contains( config + ".material_config/" ) ).ToArray();
	if ( slots.Length == 0 ) throw new Exception( "Missing material slots: " + config );
	foreach ( var slot in slots )
	{
		var material = await slot.GetOrCreate() as Material;
		if ( material is null || material.IsError ) throw new Exception( "Material failed: " + slot.Path );
		if ( !ReferenceEquals( material, await slot.GetOrCreate() ) )
			throw new Exception( "Material loader did not cache: " + slot.Path );
		var resource = await ResourceLibrary.LoadAsync<Material>( slot.Path );
		if ( !ReferenceEquals( material, resource ) ) throw new Exception( "Resource lookup mismatch: " + slot.Path );
		results.Add( new { slot.Path, Tags = slot.Tags.ToArray() } );
	}
}
return new
{
	Materials = entries.Count,
	Models = mount.GetAll( Sandbox.Mounting.ResourceType.Model ).Count,
	Textures = mount.GetAll( Sandbox.Mounting.ResourceType.Texture ).Count,
	Maps = mount.GetAll( Sandbox.Mounting.ResourceType.Scene ).Count,
	Verified = results
};
