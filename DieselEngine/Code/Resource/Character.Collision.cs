namespace Diesel;

partial class PaydayCharacter
{
	internal sealed record CollisionPart( uint Node, Model Model );
	List<CollisionPart> collisionParts;
	readonly Dictionary<string, Dictionary<ulong, AnimationFile.Track>> embeddedTracks = new( StringComparer.Ordinal );

	Dictionary<ulong, AnimationFile.Track> EmbeddedTracks( string group )
	{
		group ??= "";
		if ( embeddedTracks.TryGetValue( group, out var tracks ) ) return tracks;
		var enabled = group.Length == 0 ? File.RigNodes.Select( n => n.Name ).ToHashSet()
			: animationGroups.TryGetValue( group, out var objects ) ? objects
			: throw new InvalidDataException( $"Unknown authored animation group: {group}" );
		return embeddedTracks[group] = File.ControllerTracks( enabled, includeDescendants: group.Length == 0 );
	}

	internal IReadOnlyList<CollisionPart> GetScriptedCollision( string path )
	{
		if ( !scripted ) throw new InvalidOperationException( "Animated collision requires a scripted-prop model." );
		if ( collisionParts is not null ) return collisionParts;
		var result = new List<CollisionPart>();
		var rest = File.Pose( null, 0 );
		var nodes = File.RigNodes.ToDictionary( n => n.Name );
		var orientationHash = IdString.Hash( orientation );
		var builders = new Dictionary<ulong, ModelBuilder>();
		ModelBuilder Builder( ulong name )
		{
			if ( !builders.TryGetValue( name, out var builder ) )
				builders[name] = builder = Model.Builder.WithName( $"{path}/collision/{name:x16}" );
			return builder;
		}
		Vector3[] LocalPoints( ulong name, Vector3[] points )
		{
			var bind = ModelFile.Convert( rest[nodes[name].Id] * bindRoot );
			return points.Select( p => bind.PointToLocal( p ) ).ToArray();
		}
		foreach ( var part in File.ReadGeometry( unitGeometry.Collision, orientationHash ) )
		{
			var points = LocalPoints( part.Name, part.Positions );
			if ( points.Length == 0 || part.Indices.Length == 0 ) continue;
			// Moving collision uses a hull per authored object, like articulated doors.
			Builder( part.Name ).AddCollisionHull( points.AsSpan() );
		}
		foreach ( var part in File.ReadCollisionPrimitives( unitGeometry.CollisionTypes, orientationHash ) )
			Builder( part.Name ).AddCollisionHull( LocalPoints( part.Name, part.Points ).AsSpan() );
		foreach ( var (name, builder) in builders )
			result.Add( new CollisionPart( nodes[name].Id, builder.Create() ) );
		return collisionParts = result;
	}

	internal Dictionary<uint, Transform> SampleEmbedded( float time, string group = null )
	{
		return File.Pose( null, time, EmbeddedTracks( group ) )
			.ToDictionary( p => p.Key, p => ModelFile.Convert( p.Value * bindRoot ) );
	}
}
