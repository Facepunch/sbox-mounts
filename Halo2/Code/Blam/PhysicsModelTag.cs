using System;
using System.Collections.Generic;
using System.IO;

namespace Halo2.Blam;

/// <summary>
/// Parses a Halo 2 physics_model (phmo) tag for ragdoll physics.
/// References: XboxChaos/Assembly Halo2MCC/phmo.xml, HaloTagDefinitions H2/Vista/physics_model.xml
/// </summary>
public class PhysicsModelTag
{
	public List<RigidBody> RigidBodies { get; } = new();
	public List<PhysSphere> Spheres { get; } = new();
	public List<PhysPill> Pills { get; } = new();
	public List<Vector3[]> Polyhedra { get; } = new();
	public List<PhysBox> Boxes { get; } = new();
	public List<(short Type, short Index)[]> ShapeLists { get; } = new();
	public List<RagdollConstraint> RagdollConstraints { get; } = new();
	public List<HingeConstraint> HingeConstraints { get; } = new();
	public List<LimitedHingeConstraint> LimitedHingeConstraints { get; } = new();
	public List<BallSocketConstraint> BallSocketConstraints { get; } = new();
	public List<NodeEdge> NodeEdges { get; } = new();
	public List<PhysNode> Nodes { get; } = new();

	public PhysicsModelTag( MapFile map, TagEntry tag )
	{
		if ( tag.DataSize == 0 )
			return;

		using var reader = map.GetTagDataReader( tag );

		// Header is 0x110 bytes with TagBlocks at known offsets
		// Each TagBlock = count(4) + pointer(4)

		ReadTagBlock( reader, 0x30, out int edgeCount, out int edgePtr );
		ReadTagBlock( reader, 0x38, out int rbCount, out int rbPtr );
		ReadTagBlock( reader, 0x48, out int sphCount, out int sphPtr );
		ReadTagBlock( reader, 0x58, out int pillCount, out int pillPtr );
		ReadTagBlock( reader, 0x60, out int boxCount, out int boxPtr );
		ReadTagBlock( reader, 0xB0, out int hingeCount, out int hingePtr );
		ReadTagBlock( reader, 0xB8, out int ragdollCount, out int ragdollPtr );
		ReadTagBlock( reader, 0xC8, out int nodeCount, out int nodePtr );
		ReadTagBlock( reader, 0xE8, out int limHingeCount, out int limHingePtr );
		ReadTagBlock( reader, 0xF0, out int ballCount, out int ballPtr );

		if ( nodeCount > 0 && nodePtr != 0 )
			ReadNodes( map, nodePtr, nodeCount );
		if ( rbCount > 0 && rbPtr != 0 )
			ReadRigidBodies( map, rbPtr, rbCount );
		if ( sphCount > 0 && sphPtr != 0 )
			ReadSpheres( map, sphPtr, sphCount );
		if ( pillCount > 0 && pillPtr != 0 )
			ReadPills( map, pillPtr, pillCount );
		if ( boxCount > 0 && boxPtr != 0 )
			ReadBoxes( map, boxPtr, boxCount );
		ReadPolyhedra( map, reader );
		ReadShapeLists( map, reader );
		if ( edgeCount > 0 && edgePtr != 0 )
			ReadNodeEdges( map, edgePtr, edgeCount );
		if ( ragdollCount > 0 && ragdollPtr != 0 )
			ReadRagdollConstraints( map, ragdollPtr, ragdollCount );
		if ( hingeCount > 0 && hingePtr != 0 )
			ReadHingeConstraints( map, hingePtr, hingeCount );
		if ( limHingeCount > 0 && limHingePtr != 0 )
			ReadLimitedHingeConstraints( map, limHingePtr, limHingeCount );
		if ( ballCount > 0 && ballPtr != 0 )
			ReadBallSocketConstraints( map, ballPtr, ballCount );
	}

	static void ReadTagBlock( BinaryReader reader, int offset, out int count, out int ptr )
	{
		reader.BaseStream.Seek( offset, SeekOrigin.Begin );
		count = reader.ReadInt32();
		ptr = reader.ReadInt32();
	}

	void ReadPolyhedra( MapFile map, BinaryReader header )
	{
		ReadTagBlock( header, 0x70, out int count, out int pointer );
		if ( count == 0 ) return;
		ReadTagBlock( header, 0x78, out int externalGroups, out int externalPointer );
		int external = externalGroups > 0 ? map.VaToOffset( externalPointer ) : 0;
		int row = map.VaToOffset( pointer );
		for ( int i = 0; i < count; i++, row += 0x120 )
		{
			int groups = map.ReadInt32( row + 0x68 );
			int vertices = map.ReadInt32( row + 0x100 );
			if ( vertices < 0 || groups != ((long)vertices + 3) / 4 || (groups > 3 && groups > externalGroups) )
				throw new InvalidDataException( "Invalid physics polyhedron vertex count." );

			// Up to three groups live inline. Larger hulls consume the shared block in tag order.
			int data = row + 0x70;
			if ( groups > 3 )
			{
				data = external;
				external += checked(groups * 48);
				externalGroups -= groups;
			}

			var points = new Vector3[vertices];
			for ( int j = 0; j < vertices; j++ )
			{
				int lane = data + (j / 4) * 48 + (j % 4) * 4;
				points[j] = new Vector3( BitConverter.Int32BitsToSingle( map.ReadInt32( lane ) ),
					BitConverter.Int32BitsToSingle( map.ReadInt32( lane + 16 ) ),
					BitConverter.Int32BitsToSingle( map.ReadInt32( lane + 32 ) ) );
			}
			Polyhedra.Add( points );
		}
	}

	void ReadShapeLists( MapFile map, BinaryReader header )
	{
		ReadTagBlock( header, 0x90, out int count, out int ptr );
		ReadTagBlock( header, 0x98, out int sharedCount, out int sharedPtr );
		int sharedIndex = 0;
		for ( int i = 0; i < count; i++ )
		{
			int offset = map.VaToOffset( ptr ) + i * 0x68;
			using var list = map.OpenReader( offset + 0x20, 4 );
			int size = list.ReadInt32();

			// Up to four child references fit inside the list. Larger lists use
			// consecutive entries in the shared list-shapes block.
			int children = offset + 0x28;
			if ( size > 4 )
			{
				if ( size > sharedCount - sharedIndex ) throw new InvalidDataException( "Invalid physics shape list." );
				children = map.VaToOffset( sharedPtr ) + sharedIndex * 16;
				sharedIndex += size;
			}
			using var reader = map.OpenReader( children, checked(size * 16) );
			var shapes = new (short, short)[size];
			for ( int j = 0; j < size; j++ )
			{
				reader.BaseStream.Position = j * 16;
				shapes[j] = (reader.ReadInt16(), reader.ReadInt16());
			}
			ShapeLists.Add( shapes );
		}
	}

	void ReadNodes( MapFile map, int ptr, int count )
	{
		int baseOff = map.VaToOffset( ptr );
		for ( int i = 0; i < count; i++ )
		{
			using var r = map.OpenReader( baseOff + i * 0x0C, 0x0C );

			Nodes.Add( new PhysNode
			{
				NameStringId = r.ReadInt32(),
				Flags = r.ReadInt16(),
				ParentIndex = r.ReadInt16(),
				SiblingIndex = r.ReadInt16(),
				ChildIndex = r.ReadInt16()
			} );
		}
	}

	void ReadRigidBodies( MapFile map, int ptr, int count )
	{
		// MCC rigid body = 0xA0 bytes
		const int elementSize = 0xA0;
		int baseOff = map.VaToOffset( ptr );

		for ( int i = 0; i < count; i++ )
		{
			using var r = map.OpenReader( baseOff + i * elementSize, elementSize );

			var rb = new RigidBody
			{
				NodeIndex = r.ReadInt16(),       // +0x00
				RegionIndex = r.ReadInt16(),      // +0x02
				PermutationIndex = r.ReadInt16(), // +0x04
			};
			r.ReadInt16(); // +0x06: pad

			// Bounding sphere at +0x08
			rb.BoundingSphereX = r.ReadSingle();
			rb.BoundingSphereY = r.ReadSingle();
			rb.BoundingSphereZ = r.ReadSingle();
			rb.BoundingSphereRadius = r.ReadSingle(); // +0x14

			rb.Flags = r.ReadInt16();    // +0x18
			rb.MotionType = r.ReadInt16(); // +0x1A
			r.ReadInt16(); // +0x1C: no phantom power alt
			r.ReadInt16(); // +0x1E: size

			rb.InertiaScale = r.ReadSingle(); // +0x20: inertia tensor scale
			rb.LinearDamping = r.ReadSingle(); // +0x24: linear damping
			rb.AngularDamping = r.ReadSingle(); // +0x28: angular damping

			// Center of mass offset at +0x2C
			rb.CenterOfMassOffsetX = r.ReadSingle();
			rb.CenterOfMassOffsetY = r.ReadSingle();
			rb.CenterOfMassOffsetZ = r.ReadSingle();

			rb.ShapeType = r.ReadInt16();  // +0x38
			rb.ShapeIndex = r.ReadInt16(); // +0x3A

			// MCC: runtime shape pointer pad at +0x3C (4 bytes)
			r.ReadInt32();

			rb.Mass = r.ReadSingle();      // +0x40

			RigidBodies.Add( rb );
		}
	}

	void ReadSpheres( MapFile map, int ptr, int count )
	{
		// MCC sphere = 0xA0 bytes (from Assembly Halo2MCC/phmo.xml)
		const int elementSize = 0xA0;
		int baseOff = map.VaToOffset( ptr );

		for ( int i = 0; i < count; i++ )
		{
			using var r = map.OpenReader( baseOff + i * elementSize, elementSize );

			var sph = new PhysSphere();

			// MCC: Radius at 0x38
			r.BaseStream.Seek( 0x38, SeekOrigin.Begin );
			sph.Radius = r.ReadSingle();

			// MCC: Translation (sphere center) at 0x90
			r.BaseStream.Seek( 0x90, SeekOrigin.Begin );
			sph.CenterX = r.ReadSingle();
			sph.CenterY = r.ReadSingle();
			sph.CenterZ = r.ReadSingle();

			Spheres.Add( sph );
		}
	}

	void ReadPills( MapFile map, int ptr, int count )
	{
		// MCC pill (capsule) = 0x60 bytes
		const int elementSize = 0x60;
		int baseOff = map.VaToOffset( ptr );

		for ( int i = 0; i < count; i++ )
		{
			using var r = map.OpenReader( baseOff + i * elementSize, elementSize );

			var pill = new PhysPill();

			// MCC: Radius at 0x38
			r.BaseStream.Seek( 0x38, SeekOrigin.Begin );
			pill.Radius = r.ReadSingle();

			// MCC: padding (4 bytes) then Bottom at 0x40
			r.BaseStream.Seek( 0x40, SeekOrigin.Begin );
			pill.BottomX = r.ReadSingle();
			pill.BottomY = r.ReadSingle();
			pill.BottomZ = r.ReadSingle();
			r.ReadSingle(); // w

			// MCC: Top at 0x50
			pill.TopX = r.ReadSingle();
			pill.TopY = r.ReadSingle();
			pill.TopZ = r.ReadSingle();

			Pills.Add( pill );
		}
	}

	void ReadBoxes( MapFile map, int ptr, int count )
	{
		// MCC box = 0xB0 bytes (from Assembly Halo2MCC/phmo.xml)
		const int elementSize = 0xB0;
		int baseOff = map.VaToOffset( ptr );

		for ( int i = 0; i < count; i++ )
		{
			using var r = map.OpenReader( baseOff + i * elementSize, elementSize );

			var box = new PhysBox();

			// MCC: Radius at 0x38, Half Extents at 0x40
			r.BaseStream.Seek( 0x38, SeekOrigin.Begin );
			box.ConvexRadius = r.ReadSingle();
			r.ReadInt32(); // 0x3C padding

			box.HalfExtentX = r.ReadSingle(); // 0x40
			box.HalfExtentY = r.ReadSingle(); // 0x44
			box.HalfExtentZ = r.ReadSingle(); // 0x48

			// MCC: Rotation i at 0x70, j at 0x80, k at 0x90, Translation at 0xA0
			r.BaseStream.Seek( 0x70, SeekOrigin.Begin );
			box.RotIX = r.ReadSingle(); box.RotIY = r.ReadSingle(); box.RotIZ = r.ReadSingle(); r.ReadSingle();
			box.RotJX = r.ReadSingle(); box.RotJY = r.ReadSingle(); box.RotJZ = r.ReadSingle(); r.ReadSingle();
			box.RotKX = r.ReadSingle(); box.RotKY = r.ReadSingle(); box.RotKZ = r.ReadSingle(); r.ReadSingle();
			box.TransX = r.ReadSingle(); box.TransY = r.ReadSingle(); box.TransZ = r.ReadSingle();

			Boxes.Add( box );
		}
	}

	void ReadConstraintBase( BinaryReader r, ConstraintBase c )
	{
		c.NameStringId = r.ReadInt32(); // +0x00
		c.NodeAIndex = r.ReadInt16();    // +0x04
		c.NodeBIndex = r.ReadInt16();    // +0x06
		c.AScale = r.ReadSingle();       // +0x08

		// Body A local frame
		c.AForwardX = r.ReadSingle(); c.AForwardY = r.ReadSingle(); c.AForwardZ = r.ReadSingle();
		c.ALeftX = r.ReadSingle(); c.ALeftY = r.ReadSingle(); c.ALeftZ = r.ReadSingle();
		c.AUpX = r.ReadSingle(); c.AUpY = r.ReadSingle(); c.AUpZ = r.ReadSingle();
		c.APosX = r.ReadSingle(); c.APosY = r.ReadSingle(); c.APosZ = r.ReadSingle();

		// Body B local frame
		c.BScale = r.ReadSingle();
		c.BForwardX = r.ReadSingle(); c.BForwardY = r.ReadSingle(); c.BForwardZ = r.ReadSingle();
		c.BLeftX = r.ReadSingle(); c.BLeftY = r.ReadSingle(); c.BLeftZ = r.ReadSingle();
		c.BUpX = r.ReadSingle(); c.BUpY = r.ReadSingle(); c.BUpZ = r.ReadSingle();
		c.BPosX = r.ReadSingle(); c.BPosY = r.ReadSingle(); c.BPosZ = r.ReadSingle();

		c.EdgeIndex = r.ReadInt16();
		r.ReadInt16(); // pad
		r.ReadInt32(); // pad
	}

	void ReadRagdollConstraints( MapFile map, int ptr, int count )
	{
		const int elementSize = 0x94;
		int baseOff = map.VaToOffset( ptr );

		for ( int i = 0; i < count; i++ )
		{
			using var r = map.OpenReader( baseOff + i * elementSize, elementSize );

			var c = new RagdollConstraint();
			ReadConstraintBase( r, c );

			c.TwistMin = r.ReadSingle();
			c.TwistMax = r.ReadSingle();
			c.ConeMin = r.ReadSingle();
			c.ConeMax = r.ReadSingle();
			c.PlaneMin = r.ReadSingle();
			c.PlaneMax = r.ReadSingle();
			c.MaxFrictionTorque = r.ReadSingle();

			RagdollConstraints.Add( c );
		}
	}

	void ReadHingeConstraints( MapFile map, int ptr, int count )
	{
		const int elementSize = 0x78;
		int baseOff = map.VaToOffset( ptr );

		for ( int i = 0; i < count; i++ )
		{
			using var r = map.OpenReader( baseOff + i * elementSize, elementSize );

			var c = new HingeConstraint();
			ReadConstraintBase( r, c );
			HingeConstraints.Add( c );
		}
	}

	void ReadLimitedHingeConstraints( MapFile map, int ptr, int count )
	{
		const int elementSize = 0x84;
		int baseOff = map.VaToOffset( ptr );

		for ( int i = 0; i < count; i++ )
		{
			using var r = map.OpenReader( baseOff + i * elementSize, elementSize );

			var c = new LimitedHingeConstraint();
			ReadConstraintBase( r, c );

			c.LimitFriction = r.ReadSingle();
			c.LimitMin = r.ReadSingle();
			c.LimitMax = r.ReadSingle();

			LimitedHingeConstraints.Add( c );
		}
	}

	void ReadBallSocketConstraints( MapFile map, int ptr, int count )
	{
		const int elementSize = 0x78;
		int baseOff = map.VaToOffset( ptr );

		for ( int i = 0; i < count; i++ )
		{
			using var r = map.OpenReader( baseOff + i * elementSize, elementSize );

			var c = new BallSocketConstraint();
			ReadConstraintBase( r, c );
			BallSocketConstraints.Add( c );
		}
	}

	void ReadNodeEdges( MapFile map, int ptr, int count )
	{
		const int elementSize = 0x18;
		int baseOff = map.VaToOffset( ptr );

		for ( int i = 0; i < count; i++ )
		{
			using var r = map.OpenReader( baseOff + i * elementSize, elementSize );

			var edge = new NodeEdge
			{
				NodeAMaterialIndex = r.ReadInt16(),
				NodeBMaterialIndex = r.ReadInt16(),
				NodeAIndex = r.ReadInt16(),
				NodeBIndex = r.ReadInt16(),
			};

			// Constraints sub-block at +0x08
			int subCount = r.ReadInt32();
			int subPtr = r.ReadInt32();

			if ( subCount > 0 && subPtr != 0 )
			{
				int subOff = map.VaToOffset( subPtr );
				for ( int j = 0; j < subCount; j++ )
				{
					using var sr = map.OpenReader( subOff + j * 0x0C, 0x0C );

					edge.Constraints.Add( new EdgeConstraintRef
					{
						Type = sr.ReadInt16(),
						Index = sr.ReadInt16(),
						Flags = sr.ReadInt32(),
						Friction = sr.ReadSingle()
					} );
				}
			}

			NodeEdges.Add( edge );
		}
	}
}

// Data classes

public class RigidBody
{
	public short NodeIndex { get; set; }
	public short RegionIndex { get; set; }
	public short PermutationIndex { get; set; }
	public float BoundingSphereX { get; set; }
	public float BoundingSphereY { get; set; }
	public float BoundingSphereZ { get; set; }
	public float BoundingSphereRadius { get; set; }
	public short Flags { get; set; }
	public short MotionType { get; set; }
	public float AngularDamping { get; set; }
	public float LinearDamping { get; set; }
	public float InertiaScale { get; set; }
	public float CenterOfMassOffsetX { get; set; }
	public float CenterOfMassOffsetY { get; set; }
	public float CenterOfMassOffsetZ { get; set; }
	public short ShapeType { get; set; }
	public short ShapeIndex { get; set; }
	public float Mass { get; set; }
}

public class PhysSphere
{
	public float Radius { get; set; }
	public float CenterX { get; set; }
	public float CenterY { get; set; }
	public float CenterZ { get; set; }
}

public class PhysPill
{
	public float Radius { get; set; }
	public float BottomX { get; set; }
	public float BottomY { get; set; }
	public float BottomZ { get; set; }
	public float TopX { get; set; }
	public float TopY { get; set; }
	public float TopZ { get; set; }
}

public class PhysBox
{
	public float ConvexRadius { get; set; }
	public float HalfExtentX { get; set; }
	public float HalfExtentY { get; set; }
	public float HalfExtentZ { get; set; }
	public float RotIX { get; set; }
	public float RotIY { get; set; }
	public float RotIZ { get; set; }
	public float RotJX { get; set; }
	public float RotJY { get; set; }
	public float RotJZ { get; set; }
	public float RotKX { get; set; }
	public float RotKY { get; set; }
	public float RotKZ { get; set; }
	public float TransX { get; set; }
	public float TransY { get; set; }
	public float TransZ { get; set; }
}

public class ConstraintBase
{
	public int NameStringId { get; set; }
	public short NodeAIndex { get; set; }
	public short NodeBIndex { get; set; }
	public float AScale { get; set; }
	public float AForwardX { get; set; }
	public float AForwardY { get; set; }
	public float AForwardZ { get; set; }
	public float ALeftX { get; set; }
	public float ALeftY { get; set; }
	public float ALeftZ { get; set; }
	public float AUpX { get; set; }
	public float AUpY { get; set; }
	public float AUpZ { get; set; }
	public float APosX { get; set; }
	public float APosY { get; set; }
	public float APosZ { get; set; }
	public float BScale { get; set; }
	public float BForwardX { get; set; }
	public float BForwardY { get; set; }
	public float BForwardZ { get; set; }
	public float BLeftX { get; set; }
	public float BLeftY { get; set; }
	public float BLeftZ { get; set; }
	public float BUpX { get; set; }
	public float BUpY { get; set; }
	public float BUpZ { get; set; }
	public float BPosX { get; set; }
	public float BPosY { get; set; }
	public float BPosZ { get; set; }
	public short EdgeIndex { get; set; }
}

public class RagdollConstraint : ConstraintBase
{
	public float TwistMin { get; set; }
	public float TwistMax { get; set; }
	public float ConeMin { get; set; }
	public float ConeMax { get; set; }
	public float PlaneMin { get; set; }
	public float PlaneMax { get; set; }
	public float MaxFrictionTorque { get; set; }
}

public class HingeConstraint : ConstraintBase { }

public class LimitedHingeConstraint : ConstraintBase
{
	public float LimitFriction { get; set; }
	public float LimitMin { get; set; }
	public float LimitMax { get; set; }
}

public class BallSocketConstraint : ConstraintBase { }

public class NodeEdge
{
	public short NodeAMaterialIndex { get; set; }
	public short NodeBMaterialIndex { get; set; }
	public short NodeAIndex { get; set; }
	public short NodeBIndex { get; set; }
	public List<EdgeConstraintRef> Constraints { get; } = new();
}

public class EdgeConstraintRef
{
	// Type: 0=Hinge, 1=LimitedHinge, 2=Ragdoll, 3=StiffSpring, 4=BallAndSocket, 5=Prismatic
	public short Type { get; set; }
	public short Index { get; set; }
	public int Flags { get; set; }
	public float Friction { get; set; }
}

public class PhysNode
{
	public int NameStringId { get; set; }
	public short Flags { get; set; }
	public short ParentIndex { get; set; }
	public short SiblingIndex { get; set; }
	public short ChildIndex { get; set; }
}
