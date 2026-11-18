namespace Renderware;

public class FrameList : Chunk
{
	public Frame[] Frames { get; }

	readonly Dictionary<int, Frame> byNodeId = new();

	public FrameList( BinaryReader br, ChunkHeader header ) : base( header )
	{
		Chunks.ReadStruct( br );

		Frames = new Frame[br.ReadInt32()];
		for ( int i = 0; i < Frames.Length; i++ )
			Frames[i] = new Frame( br );

		// then an extension per frame with its name and animation node
		foreach ( var frame in Frames )
		{
			foreach ( var extension in Chunks.ReadExtensions( br, this ) )
			{
				if ( extension is FrameName name )
					frame.Name = name.Value;
				else if ( extension is HAnim anim )
					byNodeId[(frame.HAnim = anim).NodeId] = frame;
			}
		}

		foreach ( var frame in Frames )
			frame.Transform = GetWorldTransform( frame );
	}

	/// <summary>
	/// A frame by its HAnim node id.
	/// </summary>
	public bool TryGetBone( int nodeId, out Frame bone ) => byNodeId.TryGetValue( nodeId, out bone );

	public Frame GetParent( Frame frame ) => frame.ParentIndex < 0 ? null : Frames[frame.ParentIndex];

	/// <summary>
	/// A frame's transform in model space, walking up its parents.
	/// </summary>
	public Transform GetWorldTransform( Frame frame )
	{
		// RW matrices are rows of right / up / at, at being the z axis
		var local = new Transform( frame.Position, Rotation.LookAt( frame.Right, frame.Forward ) );
		return GetParent( frame ) is { } parent ? GetWorldTransform( parent ).ToWorld( local ) : local;
	}
}

public class Frame
{
	public Vector3 Right;
	public Vector3 Up;
	public Vector3 Forward;
	public Vector3 Position;
	public int ParentIndex;
	public int MatrixFlags;

	public string Name;
	public HAnim HAnim;

	/// <summary>Model space transform, see <see cref="FrameList.GetWorldTransform"/>.</summary>
	public Transform Transform;

	public int NodeId => HAnim?.NodeId ?? -1;

	BBox localBounds;
	public BBox GetLocalBounds() => localBounds;
	public void AddBoundsPoint( Vector3 position ) => localBounds = localBounds.AddPoint( position );

	public Frame( BinaryReader br )
	{
		Right = br.ReadVector3();
		Up = br.ReadVector3();
		Forward = br.ReadVector3();
		Position = br.ReadVector3();
		ParentIndex = br.ReadInt32();
		MatrixFlags = br.ReadInt32();
	}

	public override string ToString() => Name ?? base.ToString();
}

public class FrameName : StringChunk
{
	public FrameName( BinaryReader br, ChunkHeader header ) : base( br, header ) { }
}

/// <summary>
/// Animation hierarchy node attached to a frame - marks it as a bone.
/// </summary>
public class HAnim : Chunk
{
	public readonly record struct Node( int NodeId, int NodeIndex, uint Flags );

	public uint AnimVersion { get; }
	public int NodeId { get; }
	public Node[] Nodes { get; }

	public HAnim( BinaryReader br, ChunkHeader header ) : base( header )
	{
		AnimVersion = br.ReadUInt32();
		NodeId = br.ReadInt32();
		Nodes = new Node[br.ReadUInt32()];

		if ( Nodes.Length == 0 )
			return;

		br.ReadUInt32(); // flags
		br.ReadUInt32(); // key frame size

		for ( int i = 0; i < Nodes.Length; i++ )
			Nodes[i] = new Node( br.ReadInt32(), br.ReadInt32(), br.ReadUInt32() );
	}
}
