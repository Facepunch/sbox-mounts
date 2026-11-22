using GoldSrc.Bsp;
using System;

namespace GoldSrc.Maps;

partial class MapBuilder
{
	private readonly List<Entity> _authoredDecals = [];
	private readonly List<(BspModel Model, GameObject Receiver, Vector3 GeometryOrigin)> _decalReceivers = [];

	private void SpawnDecals( BspFile bsp )
	{
		var surfaces = PrepareExplosions( bsp );
		foreach ( var entity in _authoredDecals )
		{
			var name = entity.GetString( "texture", "" );
			if ( string.IsNullOrEmpty( name ) )
			{
				Log.Warning( $"{FilePath}: infodecal at {entity.Origin} has no texture; omitted." );
				continue;
			}
			var definition = Host.GetDecal( name );
			if ( definition is null )
				continue;
			if ( !string.IsNullOrEmpty( entity.TargetName ) )
			{
				surfaces ??= BuildDecalSurfaces( bsp );
				var trigger = new GameObject( true, EntityName( entity ) );
				trigger.WorldPosition = entity.Origin;
				var component = trigger.AddComponent<TriggeredDecal>();
				component.TargetName = entity.TargetName;
				component.Definition = definition;
				component.Surfaces = surfaces;
				RegisterTarget( entity, component );
				continue;
			}

			var distance = 5f;
			var position = Vector3.Zero;
			var rotation = Rotation.Identity;
			GameObject receiver = null;
			foreach ( var candidate in _decalReceivers )
			{
				var transform = candidate.Receiver.WorldTransform;
				var local = transform.PointToLocal( entity.Origin ) + candidate.GeometryOrigin;
				if ( !GoldSrcDecalTrace.Project( bsp, candidate.Model, local, distance, out var hit, out var normal, out var textureT ) )
					continue;
				distance = (hit - local).Length;
				receiver = candidate.Receiver;
				position = transform.PointToWorld( hit - candidate.GeometryOrigin );
				var t = textureT - normal * Vector3.Dot( textureT, normal );
				if ( t.LengthSquared <= 0.000001f )
				{
					var s = MathF.Abs( normal.z ) > 0.70710678f
						? Vector3.Cross( normal, Vector3.Cross( Vector3.Forward, normal ) ).Normal
						: Vector3.Cross( normal, Vector3.Down ).Normal;
					t = Vector3.Cross( s, normal );
				}
				var localRotation = Rotation.LookAt( -normal, -t.Normal );
				rotation = transform.Rotation * localRotation;
			}
			if ( receiver is null )
			{
				Log.Warning( $"{FilePath}: decal '{name}' at {entity.Origin} has no eligible surface within 5 units; omitted." );
				continue;
			}

			var child = new GameObject( true, $"infodecal ({name})" ) { Parent = receiver };
			child.WorldPosition = position;
			child.WorldRotation = rotation;
			var decal = child.AddComponent<Decal>( false );
			decal.Decals = [definition];
			decal.Rotation = 0;
			decal.Enabled = true;
		}
	}

	private DecalSurfaces BuildDecalSurfaces( BspFile bsp )
	{
		var result = _mapState.GameObject.AddComponent<DecalSurfaces>();
		foreach ( var candidate in _decalReceivers )
		{
			var collider = candidate.Receiver.GetComponent<PhysicsCollider>();
			if ( !collider.IsValid() || collider.IsTrigger ) continue;
			for ( var index = candidate.Model.FirstFace; index < candidate.Model.FirstFace + candidate.Model.FaceCount; index++ )
			{
				var face = bsp.Faces[index];
				if ( face.EdgeCount < 3 ) continue;
				var info = bsp.TexInfos[face.TextureInfo];
				var texture = bsp.Textures[info.MipTex];
				if ( texture.IsTool || texture.IsSky || texture.IsLiquid ) continue;
				var vertices = new Vector3[face.EdgeCount];
				for ( var edge = 0; edge < vertices.Length; edge++ )
					vertices[edge] = bsp.VertexFromSurfEdge( face.FirstEdge + edge ) - candidate.GeometryOrigin;
				result.Surfaces.Add( new()
				{
					Receiver = candidate.Receiver,
					Collider = collider,
					Vertices = vertices,
					Normal = bsp.Planes[face.Plane].Normal * (face.PlaneSide == 0 ? 1 : -1),
					TextureT = info.T
				} );
			}
		}
		return result;
	}
}
