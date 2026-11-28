using System;
using System.Runtime.InteropServices;
using static Source1.BspGeometry;

namespace Source1;

partial class MapLoader
{
	float BuildWorldPhysics( BspFile bsp, GameObject world, bool skybox = false, int modelIndex = 0, bool staticCollider = true )
	{
		var fluids = new List<BspPhysicsFluid>();
		var (hulls, meshes, virtualTerrain, mass) = bsp.ParsePhysicsGeometry( modelIndex, skybox ? null : fluids );
		BuildWaterPhysics( fluids, world, modelIndex );
		if ( skybox )
			bsp.FilterSkyboxPhysics( hulls, meshes );
		var bodies = new Dictionary<(Surface Surface, BspCollisionKind Kind), PhysicsBodyBuilder>();
		var surfaces = new List<Surface>();
		var surfaceIndices = new Dictionary<Surface, byte>();
		foreach ( var hull in hulls )
			GetBody( hull.SurfaceName, hull.Kind ).AddHull( CollectionsMarshal.AsSpan( hull.Vertices ) );
		foreach ( var mesh in meshes )
		{
			var materials = mesh.SurfaceNames.Select( name => GetMaterial( Host.GetSurface( name, FullPath ) ) ).ToArray();
			GetBody( mesh.SurfaceName, mesh.Kind ).AddMesh( CollectionsMarshal.AsSpan( mesh.Vertices ), CollectionsMarshal.AsSpan( mesh.Indices ), materials );
		}

		bool hasDisplacements = modelIndex == 0 && virtualTerrain && AddDisplacementPhysics( bsp, name => GetBody( name, BspCollisionKind.Solid ), GetMaterial, skybox );
		if ( hulls.Count == 0 && meshes.Count == 0 && !hasDisplacements )
			return 0;
		foreach ( var group in bodies.GroupBy( entry => entry.Key.Kind ) )
		{
			var kind = group.Key;
			var parts = group.Select( entry => entry.Value ).ToList();
			var target = world;
			string clip = kind switch
			{
				BspCollisionKind.PlayerClip => MapClipRules.PlayerClip,
				BspCollisionKind.NpcClip => MapClipRules.NpcClip,
				BspCollisionKind.ActorClip => MapClipRules.ActorClip,
				_ => null
			};
			if ( kind != BspCollisionKind.Solid )
			{
				target = new GameObject( false, clip ?? (kind == BspCollisionKind.Sky ? "sky collision" : "window collision") )
				{
					Parent = world,
					IsStatic = staticCollider
				};
				if ( clip is not null ) target.Tags.Add( clip );
				else target.Tags.Add( "world" );
				if ( kind == BspCollisionKind.Window ) target.Tags.Add( "source1_window" );
				else target.Tags.Add( "passaudio" );
				if ( kind == BspCollisionKind.Sky ) target.Tags.Add( "source1_sky" );
			}

			var suffix = kind switch { BspCollisionKind.Sky => "_sky", BspCollisionKind.Window => "_window", _ => clip is null ? "" : $"_{clip}" };
			var physics = PhysicsGroupDescription.Create( $"source1/{Host.Ident}/{FullPath}/{(skybox ? "skybox_" : "")}model_{modelIndex}{suffix}.vphys", parts, surfaces );
			_worldPhysics.Add( physics );
			var collider = target.AddComponent<PhysicsCollider>( false );
			if ( clip is not null ) collider.ColliderFlags |= ColliderFlags.IgnoreTraces;
			collider.Static = staticCollider;
			collider.Physics = physics;
			collider.Enabled = true;
			if ( kind != BspCollisionKind.Solid )
				target.Enabled = true;
		}

		return mass;

		PhysicsBodyBuilder GetBody( string name, BspCollisionKind kind )
		{
			var surface = Host.GetSurface( name, FullPath );
			if ( !bodies.TryGetValue( (surface, kind), out var body ) )
			{
				body = new PhysicsBodyBuilder
				{
					Mass = 1000,
					Surface = surface
				};
				bodies.Add( (surface, kind), body );
			}

			return body;
		}

		byte GetMaterial( Surface surface )
		{
			if ( surfaceIndices.TryGetValue( surface, out var index ) )
				return index;
			if ( surfaces.Count >= 256 )
				throw new NotSupportedException( $"{FullPath}: world collision exceeds the native 256-surface material table." );
			index = (byte)surfaces.Count;
			surfaceIndices.Add( surface, index );
			surfaces.Add( surface );
			return index;
		}
	}

	bool AddDisplacementPhysics( BspFile bsp, Func<string, PhysicsBodyBuilder> getBody, Func<Surface, byte> getMaterial, bool skybox = false )
	{
		const int CONTENTS_SOLID = 0x1;
		const int CONTENTS_GRATE = 0x8;
		const int SURF_NOPHYSICS_COLL = 0x2;
		bool added = false;
		var materialSurfaces = new Dictionary<int, (string Primary, byte[] Indices)>();
		Span<Vector3> corners = stackalloc Vector3[4];
		ref var world = ref bsp.Models[0];
		for ( int i = world.FirstFace; i < world.FirstFace + world.FaceCount; i++ )
		{
			ref var face = ref bsp.Faces[i];
			if ( face.DispInfo < 0 || skybox && !bsp.IsActiveSkyboxFace( i ) )
				continue;
			ref var disp = ref bsp.DispInfos[face.DispInfo];
			if ( (disp.Flags & SURF_NOPHYSICS_COLL) != 0 || (disp.Contents & (CONTENTS_SOLID | BspFile.ContentsWindow | CONTENTS_GRATE)) == 0 )
				continue;
			int size = (1 << disp.Power) + 1;
			var indices = new List<int>( (size - 1) * (size - 1) * 6 );
			AddDisplacementIndices( indices, 0, size, DisplacementTriangleTags( bsp, disp ) );
			if ( indices.Count == 0 )
				continue;
			GetDisplacementCorners( bsp, face, corners );
			var vertices = new Vector3[size * size];
			for ( int y = 0; y < size; y++ )
			{
				float ty = y / (float)(size - 1);
				for ( int x = 0; x < size; x++ )
				{
					float tx = x / (float)(size - 1);
					int vertex = x + y * size;
					ref var offset = ref bsp.DispVerts[disp.DispVertStart + vertex];
					vertices[vertex] = DisplacementBasePosition( corners, tx, ty ) + offset.Vector * offset.Dist;
				}
			}

			int texData = face.TexInfo < 0 ? -1 : bsp.TexInfos[face.TexInfo].TexData;
			if ( !materialSurfaces.TryGetValue( texData, out var surfaceNames ) )
			{
				var materialPath = $"texture data {texData}";
				var primary = "default";
				VmtParameters? parameters = null;
				try
				{
					if ( texData < 0 )
						throw new InvalidDataException( "The displacement has no texture information." );
					materialPath = VmtFile.NormalizePath( bsp.GetTexDataString( texData ), ".vmt" );
					if ( Host.GetContentPath( materialPath, ContentRoot ) is null )
						materialPath = $"{GetBaseMaterialPath( materialPath[..^4] )}.vmt";
					var material = VmtFile.Load( materialPath, path => Host.OpenContentFile( path, ContentRoot ), null, diagnostic => Log.Trace( $"{diagnostic.Path}:{diagnostic.Line}: {diagnostic.Message}" ) );
					parameters = new VmtParameters( material.Parameters, materialPath );
					primary = parameters.Value.String( "$surfaceprop", "default" );
				}
				catch ( Exception e ) when ( e is FileNotFoundException or InvalidDataException or NotSupportedException )
				{
					Log.Trace( $"{FullPath}: displacement surface properties could not be read from '{materialPath}': {e.Message} Using the mount's default surface." );
				}

				var layers = new byte[4];
				layers[0] = getMaterial( Host.GetSurface( primary, FullPath ) );
				for ( int layer = 1; layer < layers.Length; layer++ )
				{
					var name = parameters?.String( $"$surfaceprop{layer + 1}" );
					layers[layer] = string.IsNullOrWhiteSpace( name ) ? layers[0] : getMaterial( Host.GetSurface( name, FullPath ) );
				}
				surfaceNames = (primary, layers);
				materialSurfaces.Add( texData, surfaceNames );
			}

			var materials = new byte[indices.Count / 3];
			bool multiblend = (disp.Flags & 0x40000000) != 0;
			for ( int triangle = 0; triangle < materials.Length; triangle++ )
			{
				if ( disp.BlendStart >= 0 )
				{
					var coverage = Vector4.Zero;
					for ( int corner = 0; corner < 3; corner++ )
					{
						var w = bsp.DisplacementBlends[disp.BlendStart + indices[triangle * 3 + corner]];
						float lower = (1 - w.z) * (1 - w.w);
						coverage += new Vector4( (1 - w.y) * lower, w.y * lower, w.z * (1 - w.w), w.w );
					}
					int dominant = 0;
					for ( int layer = 1; layer < 4; layer++ )
						if ( coverage[layer] > coverage[dominant] ) dominant = layer;
					materials[triangle] = surfaceNames.Indices[dominant];
					continue;
				}
				float alpha = 0;
				for ( int corner = 0; corner < 3; corner++ )
					alpha += bsp.DispVerts[disp.DispVertStart + indices[triangle * 3 + corner]].Alpha;
				materials[triangle] = surfaceNames.Indices[!multiblend && alpha > 382.5f ? 1 : 0];
			}

			if ( multiblend && disp.BlendStart < 0 )
				Log.Trace( $"{FullPath}: virtualterrain displacement {face.DispInfo} uses multiblend; its primary surface is used because multiblend data is not imported." );
			var meshIndices = new uint[indices.Count];
			for ( int index = 0; index < meshIndices.Length; index++ )
				meshIndices[index] = (uint)indices[index];
			getBody( surfaceNames.Primary ).AddMesh( vertices, meshIndices, materials );
			added = true;
		}

		return added;
	}
}
