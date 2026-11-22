namespace GoldSrc.Maps;

public interface IGoldSrcBlastTarget
{
	Vector3 BodyTarget { get; }
	int WaterLevel { get; }
}

[Title( "GoldSrc Explosion Environment" ), Icon( "water" )]
public sealed class ExplosionEnvironment : Component
{
	public sealed class Node
	{
		public Vector3 Normal { get; set; }
		public float Distance { get; set; }
		public int Front { get; set; }
		public int Back { get; set; }
	}

	[Property] public int Root { get; set; }
	[Property] public List<Node> Nodes { get; set; } = [];
	[Property] public int[] Leaves { get; set; } = [];

	public int Contents( Vector3 position )
	{
		position = WorldTransform.PointToLocal( position );
		var node = Root;
		for ( var steps = 0; node >= 0 && steps <= Nodes.Count; steps++ )
		{
			if ( node >= Nodes.Count ) return -1;
			var entry = Nodes[node];
			node = Vector3.Dot( position, entry.Normal ) >= entry.Distance ? entry.Front : entry.Back;
		}
		var leaf = -1 - node;
		return leaf >= 0 && leaf < Leaves.Length ? Leaves[leaf] : -1;
	}

	public int WaterLevel( BBox bounds )
	{
		var center = bounds.Center;
		if ( !Liquid( center.WithZ( bounds.Mins.z + 1 ) ) ) return 0;
		if ( !Liquid( center ) ) return 1;
		return Liquid( center.WithZ( bounds.Maxs.z - 1 ) ) ? 3 : 2;
	}

	private bool Liquid( Vector3 position ) => Contents( position ) is -3 or -4 or -5;
}
