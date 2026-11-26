// Run in a standalone client, without the DieselEngine source project loaded.
if ( Application.IsEditor ) throw new Exception( "Run this check in the standalone client." );
var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
if ( mount is null ) throw new Exception( "PAYDAY mount did not load." );
// Mount assemblies are loaded from bytes, so Assembly.Location is empty.
// Confirm instance_status has no source project loaded before running this check.
var assembly = mount.GetType().Assembly.FullName;
var results = new System.Collections.Generic.List<object>();
foreach ( var loader in mount.GetAll( Sandbox.Mounting.ResourceType.Scene ) )
{
	cancellationToken.ThrowIfCancellationRequested();
	var texture = Texture.Load( $"thumb:{loader.Path}", false );
	if ( texture is null || texture == Texture.Invalid || texture.Width <= 0 || texture.Height <= 0 )
		throw new Exception( $"Packaged thumbnail failed: {loader.Path}" );
	results.Add( new { loader.Path, texture.Width, texture.Height } );
}
if ( results.Count != 9 ) throw new Exception( $"Expected nine map thumbnails, got {results.Count}." );
var model = await ResourceLibrary.LoadAsync<Model>( "mount://payday/characters/civilian.model.vmdl" );
if ( model is null || model.IsError || model.Materials.Length == 0 ) throw new Exception( "Packaged character model failed." );
var shaders = model.Materials.Select( m => m.ShaderName ).Distinct().ToArray();
if ( shaders.Any( s => s != "shaders/diesel/surface.shader" ) )
	throw new Exception( $"Packaged character used an unexpected shader: {string.Join( ", ", shaders )}" );
var sound = await ResourceLibrary.LoadAsync<SoundEvent>( "mount://payday/sounds/emitters_office/emitter_aquarium_wall.sound" );
if ( sound is null || sound.Sounds.Count == 0 || sound.Sounds.Any( s => !s.IsValidForPlayback ) )
	throw new Exception( "Packaged aquarium sound failed." );
return new { Assembly = assembly, Thumbnails = results, CharacterShaders = shaders, AquariumVariants = sound.Sounds.Count };
