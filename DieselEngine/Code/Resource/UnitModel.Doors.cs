namespace Diesel;

static partial class UnitModel
{
	public sealed record DoorPart( Model Model, Transform Target, Transform Closed, Vector3 Pivot, bool StartsOpen, float Initial, int Group );

	static Result BuildArticulatedDoor( BundleDatabase database, MapMaterials materials, string unit, string resourceName,
		string materialVariation, string variation, ArticulatedDoorDefinition definition )
	{
		string path = Xml( database.Find( "unit", unit ) ).Element( "object" ).Attribute( "file" );
		var obj = Xml( database.Find( "object", path ) );
		var file = new ModelFile( database.Find( "model", path ) );
		var nodes = file.RigNodes.ToDictionary( n => n.Id );
		var rest = file.Pose( null, 0 );
		System.Numerics.Matrix4x4.Invert( file.NodePose( rest, obj.Element( "diesel" ).Attribute( "orientation_object" ) ), out var root );
		var moving = new HashSet<ulong>();
		var parts = new List<DoorPart>();
		foreach ( string name in definition.Roots )
		{
			var hinge = nodes.Values.Single( n => n.Name == IdString.Hash( name ) );
			bool Descends( uint id )
			{
				while ( id != 0 ) { if ( id == hinge.Id ) return true; id = nodes[id].Parent; }
				return false;
			}
			var selected = nodes.Values.Where( n => Descends( n.Id ) ).Select( n => n.Name ).ToHashSet();
			var tracks = file.Controllers( new HashSet<ulong> { hinge.Name } ).Where( p => p.Key == hinge.Name ).ToDictionary( p => p.Key, p => p.Value );
			tracks.TryGetValue( hinge.Name, out var track );
			bool manual = definition.Angles is not null && float.IsFinite( definition.Angles[parts.Count] );
			if ( track is null && !manual ) throw new InvalidDataException( $"Missing door controller {name}." );
			float end = definition.End < 0 ? track.PositionTimes.Concat( track.RotationTimes ).Max() : definition.End;
			var closed = file.NodePose( rest, name ) * root;
			var opened = file.NodePose( file.Pose( null, end, tracks ), name ) * root;
			System.Numerics.Matrix4x4.Invert( closed, out var inverse );
			var target = ModelFile.Convert( inverse * opened );
			var pivot = ModelFile.Convert( closed ).Position;
			var closedTarget = ModelFile.Convert( inverse * file.NodePose( file.Pose( null, 0, tracks ), name ) * root );
			bool startsOpen = definition.StartsOpen;
			var leaf = BuildCore( database, materials, unit, resourceName + "." + name, materialVariation, variation, false, selected );
			if ( leaf?.Model is null ) throw new InvalidDataException( $"Missing moving door geometry {unit}/{name}." );
			if ( definition.Reverse ) (closedTarget, target) = (target, closedTarget);
			if ( manual )
			{
				target = global::Transform.Zero.RotateAround( pivot, Rotation.FromYaw( definition.Angles[parts.Count] ) );
				if ( definition.AlignPair )
				{
					string other = definition.Roots[parts.Count ^ 1];
					var otherPivot = ModelFile.Convert( file.NodePose( rest, other ) * root ).Position;
					var direction = (leaf.Model.Bounds.Center - pivot).WithZ( 0 ).Normal;
					var desired = (otherPivot - pivot).WithZ( 0 ).Normal;
					float angle = MathF.Atan2( Vector3.Cross( direction, desired ).z, Vector3.Dot( direction, desired ) ) * 180 / MathF.PI;
					if ( MathF.Abs( angle ) > 20 )
					{
						closedTarget = global::Transform.Zero.RotateAround( pivot, Rotation.FromYaw( angle ) );
						target = global::Transform.Zero;
						startsOpen = true;
					}
					else startsOpen = false;
				}
			}
			parts.Add( new( leaf.Model, target, closedTarget, pivot, startsOpen, definition.Initial, definition.GroupSize > 0 ? parts.Count / definition.GroupSize : 0 ) );
			moving.UnionWith( selected );
		}
		if ( definition.Omit is not null ) moving.UnionWith( definition.Omit.Select( IdString.Hash ) );
		var frame = BuildCore( database, materials, unit, resourceName, materialVariation, variation, false, moving, true );
		return (frame ?? Result.Empty) with { DoorParts = parts };
	}
}
