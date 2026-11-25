// Uses the engine's mount thumbnail generator (disposable editor scenes).
// Run in the editor; generated PNGs are source assets for BuildMount's hashed textures.
var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
var method = typeof(Sandbox.Mounting.MountUtility).GetMethods( System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic )
	.Single( m => m.Name == "GenerateMapThumb" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType != typeof(string) );
var results = new System.Collections.Generic.List<string>();
foreach ( var loader in mount.GetAll( Sandbox.Mounting.ResourceType.Scene ) )
{
	cancellationToken.ThrowIfCancellationRequested();
	bool success = await (Task<bool>)method.Invoke( null, new object[] { loader } );
	if ( !success ) throw new Exception( $"Thumbnail generation failed: {loader.Path}" );
	results.Add( loader.Path );
}
if ( results.Count != 9 ) throw new Exception( $"Expected nine PAYDAY scenes; found {results.Count}." );
return results;
