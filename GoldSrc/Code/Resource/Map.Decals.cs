using GoldSrc.Bsp;
using System;

namespace GoldSrc;

partial class MapLoader
{
	private readonly List<Entity> _authoredDecals = [];
	private readonly List<(BspModel Model, GameObject Receiver, Vector3 GeometryOrigin)> _decalReceivers = [];

	private void SpawnDecals( BspFile bsp )
	{
		foreach ( var entity in _authoredDecals )
		{
			if ( !string.IsNullOrEmpty( entity.TargetName ) )
			{
				Log.Warning( $"{FilePath}: triggered infodecal '{entity.TargetName}' is omitted; activation is not implemented by the Decal component." );
				continue;
			}
			var name = entity.GetString( "texture", "" );
			if ( string.IsNullOrEmpty( name ) )
			{
				Log.Warning( $"{FilePath}: infodecal at {entity.Origin} has no texture; omitted." );
				continue;
			}
			var definition = Host.GetDecal( name );
			if ( definition is null )
				continue;

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
}
