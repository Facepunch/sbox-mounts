using System;

namespace Source1;

[Hide]
public sealed class MapSpawn : Component, IMapOutputs
{
	[Property] public Doo OnMapSpawn { get; set; }

	void IMapOutputs.Wire( Func<string, bool, Doo> output ) => OnMapSpawn = output( "OnMapSpawn", false );

	protected override void OnStart()
	{
		if ( Networking.IsHost && OnMapSpawn is not null ) RunDoo( OnMapSpawn );
	}
}
