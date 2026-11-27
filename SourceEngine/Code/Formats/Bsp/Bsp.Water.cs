using System;
using System.Runtime.InteropServices;

namespace Source1;

internal partial class BspFile
{
	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	internal struct WaterData
	{
		public float SurfaceHeight;
		public float MinimumHeight;
		public short TexInfo;
		private short padding;
	}

	internal WaterData[] Water = [];
	internal short[] leafWaterIds = [];

	void ReadWater( Stream stream, long start, (int offset, int length, int version, int uncompressedSize)[] lumps )
	{
		Water = ReadLump<WaterData>( stream, start, lumps[36] );
		if ( Water.Length == 0 ) return;
		ReadSpatialData( stream, start, lumps );
		foreach ( var data in Water )
		{
			if ( !float.IsFinite( data.SurfaceHeight ) || !float.IsFinite( data.MinimumHeight ) )
				throw new InvalidDataException( "Invalid BSP water heights." );
			ValidateRange( data.TexInfo, 1, TexInfos.Length, "Water texture info" );
		}
		foreach ( var id in leafWaterIds )
			if ( id != -1 ) ValidateRange( id, 1, Water.Length, "Leaf water index" );
	}

	internal (List<SourceWaterMap.Node> Nodes, int Root) BuildWaterTree( bool skybox )
	{
		var result = new List<SourceWaterMap.Node>();
		if ( Water.Length == 0 ) return (result, -1);
		var mapped = new Dictionary<int, int>();
		var pending = new Stack<(int Index, bool Visited)>();
		pending.Push( (Models[0].HeadNode, false) );
		while ( pending.TryPop( out var entry ) )
		{
			if ( mapped.ContainsKey( entry.Index ) ) continue;
			if ( entry.Index < 0 )
			{
				int leafIndex = -entry.Index - 1;
				var leaf = leaves[leafIndex];
				bool include = skybox ? IsActiveSkyboxLeaf( leaf ) : !skyboxAreas.Contains( leaf.Area );
				// Dry leaves only need expensive water views when VBSP marks them CONTENTS_TESTFOGVOLUME.
				int child = include && (leaf.Contents & 0x100) != 0 ? -1 : SourceWaterMap.NoVisibleWater;
				if ( include && leafWaterIds[leafIndex] >= 0 && (leaf.Contents & 0x30) != 0 )
					child = -leafWaterIds[leafIndex] - 2;
				mapped[entry.Index] = child;
				continue;
			}
			var node = nodes[entry.Index];
			if ( !entry.Visited )
			{
				pending.Push( (entry.Index, true) );
				pending.Push( (node.Front, false) );
				pending.Push( (node.Back, false) );
				continue;
			}
			int front = mapped[node.Front], back = mapped[node.Back];
			if ( front == back )
			{
				mapped[entry.Index] = front;
				continue;
			}
			var plane = Planes[node.Plane];
			mapped[entry.Index] = result.Count;
			result.Add( new SourceWaterMap.Node { Plane = new Vector4( plane.Normal, plane.Dist ), Front = front, Back = back } );
		}
		return (result, mapped[Models[0].HeadNode]);
	}
}
