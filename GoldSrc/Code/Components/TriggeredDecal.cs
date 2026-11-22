using System;

namespace GoldSrc.Maps;

[Title( "GoldSrc Decal Surfaces" ), Icon( "layers" )]
public sealed class DecalSurfaces : Component
{
	public sealed class Surface
	{
		public GameObject Receiver { get; set; }
		public PhysicsCollider Collider { get; set; }
		public Vector3[] Vertices { get; set; } = [];
		public Vector3 Normal { get; set; }
		public Vector3 TextureT { get; set; }
	}

	[Property] public List<Surface> Surfaces { get; set; } = [];

	public Vector3 TextureAxis( GameObject receiver, Vector3 position, Vector3 normal )
	{
		foreach ( var surface in Surfaces )
		{
			if ( surface.Receiver != receiver || surface.Vertices.Length < 3 ) continue;
			var transform = receiver.WorldTransform;
			var point = transform.PointToLocal( position );
			if ( Vector3.Dot( transform.Rotation * surface.Normal, normal ) < 0.99f
				|| MathF.Abs( Vector3.Dot( point - surface.Vertices[0], surface.Normal ) ) > 0.1f ) continue;
			var inside = true;
			var side = 0;
			for ( var i = 0; i < surface.Vertices.Length; i++ )
			{
				var a = surface.Vertices[i];
				var b = surface.Vertices[(i + 1) % surface.Vertices.Length];
				var cross = Vector3.Dot( Vector3.Cross( b - a, point - a ), surface.Normal );
				if ( MathF.Abs( cross ) < 0.001f ) continue;
				var sign = Math.Sign( cross );
				if ( side != 0 && side != sign ) { inside = false; break; }
				side = sign;
			}
			if ( !inside ) continue;
			var t = transform.Rotation * surface.TextureT;
			return t - normal * Vector3.Dot( t, normal );
		}
		return Vector3.Zero;
	}

	public bool Project( Vector3 origin, out Surface surface, out Vector3 position, out Rotation rotation )
	{
		surface = null;
		position = default;
		rotation = default;
		var fraction = 1f;
		foreach ( var candidate in Surfaces )
		{
			if ( !candidate.Receiver.IsValid() || !candidate.Collider.IsValid() || !candidate.Collider.Enabled || candidate.Collider.IsTrigger || candidate.Vertices.Length < 3 ) continue;
			var transform = candidate.Receiver.WorldTransform;
			var start = transform.PointToLocal( origin - new Vector3( 5 ) );
			var end = transform.PointToLocal( origin + new Vector3( 5 ) );
			var denominator = Vector3.Dot( candidate.Normal, end - start );
			if ( MathF.Abs( denominator ) < 0.00001f ) continue;
			var distance = Vector3.Dot( candidate.Normal, candidate.Vertices[0] - start ) / denominator;
			if ( distance < 0 || distance > fraction ) continue;
			var point = start + (end - start) * distance;
			var sign = 0;
			var inside = true;
			for ( var i = 0; i < candidate.Vertices.Length; i++ )
			{
				var a = candidate.Vertices[i];
				var b = candidate.Vertices[(i + 1) % candidate.Vertices.Length];
				var cross = Vector3.Dot( Vector3.Cross( b - a, point - a ), candidate.Normal );
				if ( MathF.Abs( cross ) < 0.001f ) continue;
				var current = Math.Sign( cross );
				if ( sign != 0 && current != sign ) { inside = false; break; }
				sign = current;
			}
			if ( !inside ) continue;
			fraction = distance;
			surface = candidate;
			var localOrigin = transform.PointToLocal( origin );
			var projected = localOrigin - candidate.Normal * Vector3.Dot( localOrigin - candidate.Vertices[0], candidate.Normal );
			position = transform.PointToWorld( projected );
			var normal = candidate.Normal;
			var t = candidate.TextureT - normal * Vector3.Dot( candidate.TextureT, normal );
			if ( t.LengthSquared < 0.000001f )
			{
				var s = MathF.Abs( normal.z ) > 0.70710678f
					? Vector3.Cross( normal, Vector3.Cross( Vector3.Forward, normal ) ).Normal
					: Vector3.Cross( normal, Vector3.Down ).Normal;
				t = Vector3.Cross( s, normal );
			}
			rotation = transform.Rotation * Rotation.LookAt( -normal, -t.Normal );
		}
		return surface is not null;
	}
}

[Title( "GoldSrc Triggered Decal" ), Icon( "lens_blur" )]
public sealed class TriggeredDecal : MapEntity
{
	[Property] public DecalDefinition Definition { get; set; }
	[Property] public DecalSurfaces Surfaces { get; set; }

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		if ( Definition is not null && Surfaces.IsValid() && Surfaces.Project( WorldPosition, out var surface, out var position, out var rotation ) )
		{
			var child = new GameObject( true, "infodecal" ) { Parent = surface.Receiver };
			child.WorldPosition = position;
			child.WorldRotation = rotation;
			var decal = child.AddComponent<Decal>( false );
			decal.Decals = [Definition];
			decal.Rotation = 0;
			decal.Transient = true;
			decal.Enabled = true;
		}
		RemoveAfter( 0.1f );
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );
}
