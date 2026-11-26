// Run in the standalone client after loading First World Bank through Sandbox's map command.
var deadline = DateTime.UtcNow.AddMinutes( 3 );
while ( Game.ActiveScene is null || !Game.ActiveScene.GetAllComponents<ModelRenderer>()
	.Any( r => r.Model?.ResourcePath?.StartsWith( "mount://payday/", StringComparison.OrdinalIgnoreCase ) == true ) )
{
	cancellationToken.ThrowIfCancellationRequested();
	if ( DateTime.UtcNow >= deadline ) throw new Exception( "Client map did not become available." );
	await Task.Delay( 250, cancellationToken );
}
var scene = Game.ActiveScene;
var renderers = scene.GetAllComponents<ModelRenderer>().ToArray();
var mounted = renderers.Where( r => r.Model?.ResourcePath?.StartsWith( "mount://payday/", StringComparison.OrdinalIgnoreCase ) == true ).ToArray();
if ( mounted.Length == 0 ) throw new Exception( "No PAYDAY map models found." );
var errors = renderers.Where( r => r.Model is null || r.Model.IsError ).Select( r => r.GameObject.Name ).ToArray();
if ( errors.Length != 0 ) throw new Exception( $"Invalid client models: {string.Join( ", ", errors.Take( 20 ) )}" );
var shaders = mounted.Select( r => r.Model ).Distinct().SelectMany( m => m.Materials ).Select( m => m.ShaderName ).Distinct().ToArray();
if ( shaders.Length == 0 || shaders.Any( s => s != "shaders/diesel/surface.shader" ) )
	throw new Exception( $"Unexpected PAYDAY map shaders: {string.Join( ", ", shaders )}" );
return new { Scene = scene.Name, Renderers = renderers.Length, PaydayRenderers = mounted.Length, InvalidModels = errors.Length, Shaders = shaders };
