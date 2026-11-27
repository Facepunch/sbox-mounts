// Run on each peer promptly after SpawnNetworkEffects.csx.
if ( !Networking.IsActive ) throw new Exception( "Requires an active connection." );
await Sandbox.Mounting.Directory.Mount( "payday" );
var scene = Game.ActiveScene ?? throw new Exception( "No active scene." );
var deadline = DateTime.UtcNow.AddSeconds( 20 );
while ( true )
{
	var all = scene.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() ).ToArray();
	var effect = all.FirstOrDefault( c => c.GetType().Name == "PaydayEffect" && c.GameObject.Name == "PAYDAY network effect fixture" );
	var fragments = all.Where( c => c.GetType().Name == "PaydayDebris" ).ToArray();
	if ( effect is not null && fragments.Length == 5 && (int)effect.GetType().GetProperty( "ParticleCount" ).GetValue( effect ) > 0 )
	{
		foreach ( var fragment in fragments )
		{
			var model = fragment.GameObject.GetComponent<ModelRenderer>()?.Model;
			if ( model is null || model.IsError || !model.ResourcePath.Contains( "/debris/" ) )
				throw new Exception( $"Invalid replicated debris model: {model?.ResourcePath}" );
		}
		return new { Networking.IsHost, Effect = effect.GameObject.Id,
			Particles = effect.GetType().GetProperty( "ParticleCount" ).GetValue( effect ),
			Fragments = fragments.Select( f => new { Id = f.GameObject.Id, Position = f.WorldPosition,
				Model = f.GameObject.GetComponent<ModelRenderer>().Model.ResourcePath } ).ToArray() };
	}
	if ( DateTime.UtcNow >= deadline ) throw new Exception( $"Replication incomplete: effect={effect is not null}, fragments={fragments.Length}." );
	await Task.Delay( 100, cancellationToken );
}
