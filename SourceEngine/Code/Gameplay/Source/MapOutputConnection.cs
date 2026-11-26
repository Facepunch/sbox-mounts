namespace Source1;

/// <summary>One BSP output connection, including every entity matched by its target name.</summary>
public sealed class MapOutputConnection
{
	public int Remaining { get; set; } = -1;
	public float Delay { get; set; }
	public Doo Action { get; set; }

	internal void Fire( Component sender, GameObject user )
	{
		if ( Remaining == 0 ) return;
		if ( Remaining > 0 ) Remaining--;
		sender.RunDoo( Action, context => context.SetArgument( "user", user ) );
	}
}

/// <summary>Stores finite fire counts for entities whose native events are Doos.</summary>
public sealed class LimitedMapOutputs : Component
{
	[Property] public List<MapOutputConnection> Connections { get; set; } = [];

	public void Fire( int index, GameObject user ) => Connections[index].Fire( this, user );
}
