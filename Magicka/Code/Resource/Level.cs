using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Nv3 = System.Numerics.Vector3;

namespace Magicka;

/// <summary>
/// Builds a playable s&amp;box scene from a Magicka <see cref="LevelModel"/> XNB: static BiTree
/// geometry, animated level parts, triangle collision meshes, lights, and player spawn points.
/// </summary>
internal sealed class LevelSceneLoader( string file ) : SceneLoader<MagickaMount>
{
	protected override void BuildScene()
	{
		var xnb = XnbContent.Read( Host.ReadAllBytes( file ) );
		if ( xnb.Root is not LevelModel level )
			return;

		var materialHost = new MagickaModelBuilder( Host, file, Path, xnb, null );
		var materials = new Dictionary<object, Material>();
		var materialIndex = 0;

		if ( Game.ActiveScene?.NavMesh is { } nav )
			nav.IsEnabled = true;

		var levelBounds = BuildGeometry( level, materialHost, materials, ref materialIndex );
		BuildAnimatedParts( level.AnimatedParts, xnb, materialHost );
		BuildLights( level.Lights );
		BuildEffects( level.Effects );
		BuildSky();
		BuildAmbience();
		BuildTransitions( level );
		BuildLevelNpcs( level );
		BuildSpawnPoints( level, levelBounds );
	}

	private static readonly Regex PlaySoundTagRegex = new( @"<playSound\b([^>]*)/?>", RegexOptions.IgnoreCase );
	private static readonly Regex CueAttrRegex = new( @"\bcue\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase );
	private static readonly Regex SpawnTagRegex = new( @"<spawn\b([^>]*)/?>", RegexOptions.IgnoreCase );
	private static readonly Regex TypeAttrRegex = new( @"\btype\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase );
	private static readonly Regex ReactToAttrRegex = new( @"\breactto\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase );
	private static readonly Regex TriggerBlockRegex = new( @"<trigger\b[^>]*>([\s\S]*?)</trigger>", RegexOptions.IgnoreCase );
	private static readonly Regex ChangeSceneRegex = new( @"<changescene\b[^>]*\bscene\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase );
	private static readonly Regex EndGameRegex = new( @"<endgame\b[^>]*\btype\s*=\s*""LevelComplete""", RegexOptions.IgnoreCase );
	private static readonly Regex AreaAttrRegex = new( @"\barea\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase );

	private static readonly Dictionary<string, string> NextChapterStart = new( StringComparer.OrdinalIgnoreCase )
	{
		["levels/wizardcastle"] = "levels/mountaindale/md_s1",
		["levels/mountaindale"] = "levels/highlands/hl_s1",
		["levels/highlands"] = "levels/havindr/havindr_s1",
		["levels/havindr"] = "levels/battlefield/bf_s1",
		["levels/battlefield"] = "levels/mines/mi_s1",
		["levels/mines"] = "levels/ruins/ru_s1",
		["levels/ruins"] = "levels/swamp/sw_s1",
		["levels/swamp"] = "levels/niflheim/nh_s1",
		["levels/niflheim"] = "levels/endofworld/ew_s1",
		["levels/tsar/tsar_wizardcastle"] = "levels/tsar/tsar_mountaindale/tmd_s1",
		["levels/tsar/tsar_mountaindale"] = "levels/tsar/tsar_rlyeh/tr_s1",
	};

	private void BuildTransitions( LevelModel level )
	{
		if ( level.TriggerAreas is null || level.TriggerAreas.Count == 0 )
			return;

		var xmlFile = file.EndsWith( ".xnb", StringComparison.OrdinalIgnoreCase )
			? $"{file[..^4]}.xml"
			: null;
		if ( xmlFile is null || !Host.FileSystem.FileExists( xmlFile ) )
			return;

		var areasByName = new Dictionary<string, TriggerArea>( StringComparer.OrdinalIgnoreCase );
		foreach ( var area in level.TriggerAreas )
		{
			if ( !string.IsNullOrWhiteSpace( area.Name ) )
				areasByName.TryAdd( area.Name, area );
		}

		var placements = new List<(TriggerArea Area, string DestinationUrl)>();
		var placedAreas = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		try
		{
			var text = Encoding.UTF8.GetString( Host.ReadAllBytes( xmlFile ) );
			foreach ( Match block in TriggerBlockRegex.Matches( text ) )
			{
				var body = block.Groups[1].Value;
				string destUrl = null;

				var sceneMatch = ChangeSceneRegex.Match( body );
				if ( sceneMatch.Success )
				{
					destUrl = ResolveLevelSceneUrl( sceneMatch.Groups[1].Value.Trim() );
				}
				else if ( EndGameRegex.IsMatch( body ) )
				{
					destUrl = ResolveNextChapterUrl();
				}

				if ( destUrl is null || string.Equals( destUrl, Path, StringComparison.OrdinalIgnoreCase ) )
					continue;

				foreach ( Match areaMatch in AreaAttrRegex.Matches( body ) )
				{
					var areaName = areaMatch.Groups[1].Value.Trim();
					if ( areasByName.TryGetValue( areaName, out var area ) && placedAreas.Add( area.Name ) )
						placements.Add( (area, destUrl) );
				}
			}
		}
		catch
		{
			return;
		}

		if ( placements.Count == 0 )
			return;

		var container = new GameObject( true, "Transitions" );
		foreach ( var (area, destUrl) in placements )
		{
			var (center, rot, scale) = TriggerAreaPose( area );
			var go = new GameObject( container, true, $"transition_{area.Name}" );
			go.WorldPosition = center;
			go.WorldRotation = rot;
			go.Tags.Add( "trigger" );

			var collider = go.AddComponent<BoxCollider>();
			collider.Scale = scale;
			collider.IsTrigger = true;

			var change = go.AddComponent<MagickaChangeLevel>();
			change.Destination = destUrl;
		}
	}

	private static (Vector3 Center, Rotation Rotation, Vector3 Scale) TriggerAreaPose( TriggerArea area )
	{
		var rot = MagickaSpace.ToWorld( area.Orientation );
		var scale = new Vector3(
			MathF.Max( MathF.Abs( area.SideLengths.z ), 1f ),
			MathF.Max( MathF.Abs( area.SideLengths.x ), 1f ),
			MathF.Max( MathF.Abs( area.SideLengths.y ), 2.5f ) ) * MagickaSpace.UnitScale;

		var cornerWorld = MagickaSpace.Position( new Nv3( area.Corner.x, area.Corner.y, area.Corner.z ) );
		var halfWorld = rot * (new Vector3( -area.SideLengths.z, -area.SideLengths.x, area.SideLengths.y ) * (0.5f * MagickaSpace.UnitScale));
		return (cornerWorld + halfWorld, rot, scale);
	}

	private string ResolveNextChapterUrl()
	{
		var currentKey = ModelKey;
		var slash = currentKey.LastIndexOf( '/' );
		if ( slash < 0 )
			return null;

		var folder = currentKey[..slash];
		if ( !NextChapterStart.TryGetValue( folder, out var nextPath ) )
			return null;

		var url = Host.Url( nextPath, "scene" );
		return Host.GetByPath( url ) is not null ? url : null;
	}

	private string ResolveLevelSceneUrl( string sceneStem )
	{
		if ( string.IsNullOrWhiteSpace( sceneStem ) )
			return null;

		var clean = sceneStem.Replace( '\\', '/' ).TrimStart( '/' );
		if ( clean.EndsWith( ".xml", StringComparison.OrdinalIgnoreCase ) || clean.EndsWith( ".xnb", StringComparison.OrdinalIgnoreCase ) )
			clean = clean[..^4];

		var currentKey = ModelKey;
		var slash = currentKey.LastIndexOf( '/' );
		if ( slash >= 0 )
		{
			var siblingUrl = Host.Url( $"{currentKey[..slash]}/{clean.ToLowerInvariant()}", "scene" );
			if ( Host.GetByPath( siblingUrl ) is not null )
				return siblingUrl;
		}

		var suffix = $"/{clean}.scene";
		foreach ( var resource in Host.Resources )
		{
			if ( resource.Type == ResourceType.Scene && resource.Path.EndsWith( suffix, StringComparison.OrdinalIgnoreCase ) )
				return resource.Path;
		}

		return null;
	}

	private void BuildLevelNpcs( LevelModel level )
	{
		var xmlFile = file.EndsWith( ".xnb", StringComparison.OrdinalIgnoreCase )
			? $"{file[..^4]}.xml"
			: null;
		if ( xmlFile is null || !Host.FileSystem.FileExists( xmlFile ) )
			return;

		var spots = new Dictionary<string, (Vector3 Pos, Rotation Rot)>( StringComparer.OrdinalIgnoreCase );
		if ( level.Locators is not null )
		{
			foreach ( var loc in level.Locators )
			{
				if ( string.IsNullOrWhiteSpace( loc.Name ) )
					continue;

				var pos = MagickaSpace.Position( MagickaSpace.Translation( loc.Transform ) );
				var rot = MagickaSpace.ToWorld( loc.Transform );
				spots.TryAdd( loc.Name, (pos, rot) );
			}
		}

		if ( level.TriggerAreas is not null )
		{
			foreach ( var area in level.TriggerAreas )
			{
				if ( string.IsNullOrWhiteSpace( area.Name ) )
					continue;

				var (center, rot, _) = TriggerAreaPose( area );
				spots.TryAdd( area.Name, (center, rot) );
			}
		}

		if ( spots.Count == 0 )
			return;

		var spawns = new List<(string PrefabUrl, Vector3 Pos, Rotation Rot, bool Passive)>();
		var usedAreas = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		try
		{
			var text = Encoding.UTF8.GetString( Host.ReadAllBytes( xmlFile ) );
			foreach ( Match tag in SpawnTagRegex.Matches( text ) )
			{
				if ( spawns.Count >= 24 )
					break;

				var attrs = tag.Groups[1].Value;
				var typeMatch = TypeAttrRegex.Match( attrs );
				var areaMatch = AreaAttrRegex.Match( attrs );
				if ( !typeMatch.Success || !areaMatch.Success )
					continue;

				var areaName = areaMatch.Groups[1].Value.Trim();
				if ( !usedAreas.Add( areaName ) || !spots.TryGetValue( areaName, out var spot ) )
					continue;

				var id = MagickaMount.SanitizeId( typeMatch.Groups[1].Value.Trim() );
				if ( string.IsNullOrEmpty( id ) )
					continue;

				var prefabUrl = Host.Url( $"entities/npcs/{id}", "prefab" );
				if ( Host.GetByPath( prefabUrl ) is null )
					continue;

				var reactTo = ReactToAttrRegex.Match( attrs );
				var isPassive = (reactTo.Success && reactTo.Groups[1].Value.Equals( "none", StringComparison.OrdinalIgnoreCase ))
					|| attrs.Contains( "dialog=", StringComparison.OrdinalIgnoreCase );

				spawns.Add( (prefabUrl, spot.Pos + Vector3.Up * 6f, spot.Rot, isPassive) );
			}
		}
		catch
		{
			return;
		}

		if ( spawns.Count == 0 )
			return;

		var container = new GameObject( true, "Level NPCs" );
		for ( var i = 0; i < spawns.Count; i++ )
		{
			var (url, pos, rot, isPassive) = spawns[i];
			var marker = new GameObject( container, true, $"npc_spawn_{i}" );
			marker.WorldPosition = pos;
			marker.WorldRotation = rot;

			var spawner = marker.AddComponent<MagickaLevelNpcSpawner>();
			spawner.PrefabUrl = url;
			spawner.ForcePassive = isPassive;
		}
	}

	private void BuildAmbience()
	{
		var xmlFile = file.EndsWith( ".xnb", StringComparison.OrdinalIgnoreCase )
			? $"{file[..^4]}.xml"
			: null;
		if ( xmlFile is null || !Host.FileSystem.FileExists( xmlFile ) )
			return;

		var tracks = new List<string>();
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		try
		{
			var text = Encoding.UTF8.GetString( Host.ReadAllBytes( xmlFile ) );
			foreach ( Match tag in PlaySoundTagRegex.Matches( text ) )
			{
				var attrs = tag.Groups[1].Value;
				// Global ambient loops have no localized area attribute.
				if ( attrs.Contains( "area=", StringComparison.OrdinalIgnoreCase ) )
					continue;

				var cueMatch = CueAttrRegex.Match( attrs );
				if ( !cueMatch.Success )
					continue;

				var cue = cueMatch.Groups[1].Value;
				var slash = cue.LastIndexOf( '/' );
				var cueName = slash >= 0 ? cue[(slash + 1)..] : cue;
				var waves = Host.ResolveCueWaves( cueName );
				if ( waves is { Length: > 0 } && seen.Add( waves[0] ) )
					tracks.Add( waves[0] );
			}
		}
		catch
		{
			return;
		}

		if ( tracks.Count == 0 )
			return;

		var go = new GameObject( true, "Ambience" );
		var amb = go.AddComponent<MagickaLevelAmbience>();
		amb.Sounds = tracks.ToArray();
	}

	private static void BuildSky()
	{
		var go = new GameObject( true, "SkyBox" );
		var sky = go.AddComponent<SkyBox2D>();
		sky.SkyIndirectLighting = false;
	}

	private static void BuildEffects( IReadOnlyList<VisualEffectPlacement> effects )
	{
		if ( effects is null || effects.Count == 0 )
			return;

		var container = new GameObject( true, "Effects" );
		var count = 0;
		foreach ( var fx in effects )
		{
			if ( count >= 48 )
				break;

			if ( string.IsNullOrWhiteSpace( fx.Effect ) )
				continue;

			var go = new GameObject( container, true, string.IsNullOrEmpty( fx.Id ) ? fx.Effect : fx.Id );
			go.WorldPosition = MagickaSpace.Position( fx.Position );
			var dir = MagickaSpace.ToWorld( fx.Forward );
			if ( dir.LengthSquared > 0.001f )
				go.WorldRotation = Rotation.LookAt( dir.Normal );

			var comp = go.AddComponent<MagickaLevelEffect>();
			comp.EffectName = fx.Effect;
			count++;
		}
	}

	private string ModelKey => MagickaMount.ResourcePath( file );

	private BBox BuildGeometry(
		LevelModel level,
		MagickaModelBuilder materialHost,
		Dictionary<object, Material> materials,
		ref int materialIndex )
	{
		var builder = Model.Builder.WithName( $"magickamap/{ModelKey}" );
		var totalBounds = new BBox { Mins = float.MaxValue, Maxs = float.MinValue };
		var hasBounds = false;

		if ( level.Geometry?.Roots is { Count: > 0 } roots )
		{
			foreach ( var root in roots )
			{
				if ( !root.Visible || root.Effect is RenderDeferredLiquidEffect or LavaEffect )
					continue;

				var material = materialHost.MaterialFor( root.Effect, materials, ref materialIndex );
				if ( material is null )
					continue;

				var mesh = BuildBiTreeMesh( root, material, out var meshBounds );
				if ( mesh is null )
					continue;

				builder.AddMesh( mesh );
				totalBounds = hasBounds ? totalBounds.AddBBox( meshBounds ) : meshBounds;
				hasBounds = true;
			}
		}

		// Attach all static collision meshes from the level.
		if ( level.CollisionMeshes is not null )
		{
			foreach ( var cm in level.CollisionMeshes )
			{
				if ( cm is not { Vertices.Count: > 0, Triangles.Count: > 0 } )
					continue;

				var verts = new Vector3[cm.Vertices.Count];
				for ( var i = 0; i < verts.Length; i++ )
					verts[i] = MagickaSpace.Position( cm.Vertices[i] );

				var indices = new int[cm.Triangles.Count * 3];
				for ( var i = 0; i < cm.Triangles.Count; i++ )
				{
					var tri = cm.Triangles[i];
					indices[i * 3] = tri.I0;
					indices[i * 3 + 1] = tri.I1;
					indices[i * 3 + 2] = tri.I2;
				}

				builder.AddCollisionMesh( verts, indices );
				builder.AddTraceMesh( verts, indices );
			}
		}

		if ( hasBounds )
		{
			var model = builder.Create();
			var go = new GameObject( true, "Level Geometry" );
			go.AddComponent<ModelRenderer>().Model = model;

			var collider = go.AddComponent<ModelCollider>();
			collider.Model = model;
			collider.Static = true;

			go.AddComponent<MagickaLevelNavMesh>();
		}

		return hasBounds ? totalBounds : default;
	}

	private void BuildAnimatedParts( IReadOnlyList<AnimatedLevelPart> parts, XnbContent xnb, MagickaModelBuilder materialHost )
	{
		if ( parts is null || parts.Count == 0 )
			return;

		var container = new GameObject( true, "Animated Parts" );
		var index = 0;
		foreach ( var part in parts )
			AddAnimatedPart( part, container, xnb, ref index );
	}

	private void AddAnimatedPart( AnimatedLevelPart part, GameObject parent, XnbContent xnb, ref int index )
	{
		var go = new GameObject( parent, true, string.IsNullOrWhiteSpace( part.Name ) ? $"part_{index}" : part.Name );
		var hasAnimTransform = part.Animation is { Count: > 0 };
		if ( hasAnimTransform )
		{
			var pose = part.Animation[0].Pose;
			go.LocalPosition = MagickaSpace.Position( pose.Translation );
			go.LocalRotation = MagickaSpace.ToWorld( pose.Orientation );
		}

		if ( part.Model is not null )
		{
			var partBuilder = new MagickaModelBuilder( Host, file, $"magickamap/{ModelKey}_part{index++}", xnb, null );
			var model = partBuilder.BuildFromModel( part.Model, part.Collision?.Mesh, skipRootBoneTransform: hasAnimTransform );
			if ( model is not null )
			{
				go.AddComponent<ModelRenderer>().Model = model;
				if ( part.Collision?.Mesh is { Triangles.Count: > 0 } )
				{
					var collider = go.AddComponent<ModelCollider>();
					collider.Model = model;
					collider.Static = true;
				}
			}
		}

		if ( part.Children is not null )
		{
			foreach ( var child in part.Children )
				AddAnimatedPart( child, go, xnb, ref index );
		}
	}

	private static void BuildLights( IReadOnlyList<LevelLight> lights )
	{
		var container = new GameObject( true, "Lights" );
		var hasDirectional = false;

		if ( lights is not null )
		{
			foreach ( var light in lights )
			{
				var color = new Color(
					Math.Clamp( light.DiffuseColor.x, 0f, 2f ),
					Math.Clamp( light.DiffuseColor.y, 0f, 2f ),
					Math.Clamp( light.DiffuseColor.z, 0f, 2f ) );

				var go = new GameObject( container, true, string.IsNullOrWhiteSpace( light.Name ) ? $"light_{light.Type}" : light.Name );
				go.WorldPosition = MagickaSpace.Position( light.Position );

				switch ( light.Type )
				{
					case LevelLightType.Directional:
					{
						hasDirectional = true;
						var dir = MagickaSpace.ToWorld( light.Direction );
						if ( dir.LengthSquared > 0.001f )
							go.WorldRotation = Rotation.LookAt( dir.Normal );

						var dl = go.AddComponent<DirectionalLight>();
						// Magicka's deferred lighting is pre-tonemapped (~0.8 sun); boost for s&box PBR.
						dl.LightColor = new Color( color.r * 2.2f, color.g * 2.2f, color.b * 2.2f );
						dl.SkyColor = new Color(
							Math.Clamp( light.AmbientColor.x * 0.55f, 0.06f, 0.6f ),
							Math.Clamp( light.AmbientColor.y * 0.55f, 0.06f, 0.6f ),
							Math.Clamp( light.AmbientColor.z * 0.55f, 0.06f, 0.6f ) );
						dl.Shadows = light.CastShadows;
						break;
					}

					case LevelLightType.Point:
					{
						var pl = go.AddComponent<PointLight>();
						pl.LightColor = color;
						pl.Radius = Math.Clamp( light.Range * MagickaSpace.UnitScale, 64f, 2048f );
						pl.Shadows = false;
						break;
					}

					case LevelLightType.Spot:
					{
						var dir = MagickaSpace.ToWorld( light.Direction );
						if ( dir.LengthSquared > 0.001f )
							go.WorldRotation = Rotation.LookAt( dir.Normal );

						var sl = go.AddComponent<SpotLight>();
						sl.LightColor = color;
						sl.Radius = Math.Clamp( light.Range * MagickaSpace.UnitScale, 64f, 2048f );
						sl.ConeOuter = Math.Clamp( light.CutoffAngle * (180f / MathF.PI), 15f, 85f );
						sl.Shadows = false;
						break;
					}
				}
			}
		}

		if ( !hasDirectional )
		{
			var sun = new GameObject( container, true, "Sun" );
			sun.WorldRotation = new Angles( 55f, 45f, 0f ).ToRotation();
			var dl = sun.AddComponent<DirectionalLight>();
			dl.LightColor = new Color( 0.95f, 0.92f, 0.86f );
			dl.SkyColor = new Color( 0.28f, 0.34f, 0.42f );
			dl.Shadows = true;
		}
	}

	private static void BuildSpawnPoints( LevelModel level, BBox bounds )
	{
		var spawned = 0;

		if ( level.Locators is not null )
		{
			foreach ( var locator in level.Locators )
			{
				if ( string.IsNullOrEmpty( locator.Name )
					|| (!locator.Name.StartsWith( "start", StringComparison.OrdinalIgnoreCase )
						&& !locator.Name.StartsWith( "spawn_player", StringComparison.OrdinalIgnoreCase )) )
					continue;

				var go = new GameObject( true, locator.Name );
				go.WorldPosition = MagickaSpace.Position( MagickaSpace.Translation( locator.Transform ) ) + Vector3.Up * 12f;
				go.AddComponent<SpawnPoint>();
				go.Tags.Add( "spawn", "info_player_start" );
				spawned++;
			}
		}

		if ( spawned == 0 )
		{
			var pos = bounds.Size.LengthSquared > 0.01f
				? new Vector3( bounds.Center.x, bounds.Center.y, bounds.Maxs.z + 32f )
				: new Vector3( 0, 0, 64f );

			var go = new GameObject( true, "start0" );
			go.WorldPosition = pos;
			go.AddComponent<SpawnPoint>();
			go.Tags.Add( "spawn", "info_player_start" );
		}
	}

	private static Mesh BuildBiTreeMesh( BiTreeRoot root, Material material, out BBox bounds )
	{
		bounds = default;
		var stride = root.VertexStride > 0 ? root.VertexStride : root.Declaration?.Stride ?? 0;
		if ( stride <= 0 || root.VertexBuffer is null || root.IndexBuffer is null || root.NumVertices <= 0 )
			return null;

		var data = root.VertexBuffer.Data;
		if ( (long)root.NumVertices * stride > data.Length )
			return null;

		var posOffset = root.Declaration.Find( VertexElementUsage.Position )?.Offset ?? -1;
		var normOffset = root.Declaration.Find( VertexElementUsage.Normal )?.Offset ?? -1;
		var tanOffset = root.Declaration.Find( VertexElementUsage.Tangent )?.Offset ?? -1;
		var uv0Offset = root.Declaration.Find( VertexElementUsage.TextureCoordinate, 0 )?.Offset ?? -1;
		var uv1Offset = root.Declaration.Find( VertexElementUsage.TextureCoordinate, 1 )?.Offset ?? uv0Offset;
		var colorElem = root.Declaration.Find( VertexElementUsage.Color );
		var colorOffset = colorElem?.Offset ?? -1;
		var colorFormat = colorElem?.Format ?? VertexElementFormat.Color;
		var useVertexColor = root.Effect is RenderDeferredEffect { VertexColorEnabled: true };
		if ( posOffset < 0 )
			return null;

		bounds = new BBox { Mins = float.MaxValue, Maxs = float.MinValue };
		var vertices = new LevelVertex[root.NumVertices];

		for ( var i = 0; i < root.NumVertices; i++ )
		{
			var o = i * stride;
			var pos = MagickaSpace.Position( ReadVector3( data, o + posOffset ) );
			var norm = normOffset >= 0 ? MagickaSpace.ToWorld( ReadVector3( data, o + normOffset ) ).Normal : Vector3.Up;
			var tan = tanOffset >= 0 ? MagickaSpace.ToWorld( ReadVector3( data, o + tanOffset ) ) : Vector3.Forward;
			var uv0 = uv0Offset >= 0
				? new Vector2( ReadSingle( data, o + uv0Offset ), ReadSingle( data, o + uv0Offset + 4 ) )
				: Vector2.Zero;
			var uv1 = uv1Offset >= 0
				? new Vector2( ReadSingle( data, o + uv1Offset ), ReadSingle( data, o + uv1Offset + 4 ) )
				: uv0;
			var vc = ReadColor( data, o, colorOffset, colorFormat, useVertexColor );

			vertices[i] = new LevelVertex
			{
				Position = pos,
				Normal = norm,
				Tangent = new Vector4( tan, 1f ),
				TexCoord = uv0,
				TexCoord2 = uv1,
				Color = vc,
			};
			bounds = bounds.AddPoint( pos );
		}

		var ib = root.IndexBuffer;
		var elemSize = ib.Is16Bit ? 2 : 4;
		var startIndex = root.Tree?.StartIndex ?? 0;
		var primCount = root.Tree?.PrimitiveCount ?? (ib.Data.Length / elemSize / 3);
		var indexCount = primCount * 3;
		if ( indexCount <= 0 || (startIndex + indexCount) * elemSize > ib.Data.Length )
			return null;

		var indices = new int[indexCount];
		var startByte = startIndex * elemSize;
		for ( var i = 0; i + 2 < indexCount; i += 3 )
		{
			var bo = startByte + i * elemSize;
			indices[i] = ReadIndex( ib, bo );
			indices[i + 1] = ReadIndex( ib, bo + elemSize * 2 );
			indices[i + 2] = ReadIndex( ib, bo + elemSize );
		}

		var mesh = new Mesh( material ) { Bounds = bounds };
		mesh.CreateVertexBuffer( vertices.Length, vertices );
		mesh.CreateIndexBuffer( indices.Length, indices );
		return mesh;
	}

	private static int ReadIndex( IndexBuffer buffer, int byteOffset ) =>
		buffer.Is16Bit
			? BinaryPrimitives.ReadUInt16LittleEndian( buffer.Data.AsSpan( byteOffset ) )
			: (int)BinaryPrimitives.ReadUInt32LittleEndian( buffer.Data.AsSpan( byteOffset ) );

	private static Nv3 ReadVector3( byte[] data, int offset ) =>
		new( ReadSingle( data, offset ), ReadSingle( data, offset + 4 ), ReadSingle( data, offset + 8 ) );

	private static Color32 ReadColor( byte[] data, int offset, int colorOffset, VertexElementFormat format, bool useRgb )
	{
		if ( colorOffset < 0 )
			return Color32.White;

		var o = offset + colorOffset;
		if ( format is VertexElementFormat.Color or VertexElementFormat.Byte4 or VertexElementFormat.Rgba32 )
		{
			if ( o < 0 || o + 4 > data.Length )
				return Color32.White;

			return useRgb
				? new Color32( data[o + 2], data[o + 1], data[o], data[o + 3] )
				: new Color32( 255, 255, 255, data[o + 3] );
		}

		if ( o < 0 || o + 16 > data.Length )
			return Color32.White;

		var r = ToByte( ReadSingle( data, o ) );
		var g = ToByte( ReadSingle( data, o + 4 ) );
		var b = ToByte( ReadSingle( data, o + 8 ) );
		var a = ToByte( ReadSingle( data, o + 12 ) );
		return useRgb ? new Color32( r, g, b, a ) : new Color32( 255, 255, 255, a );
	}

	private static byte ToByte( float value ) => (byte)Math.Clamp( (int)(value * 255f + 0.5f), 0, 255 );

	private static float ReadSingle( byte[] data, int offset ) =>
		offset >= 0 && offset + 4 <= data.Length ? BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( offset ) ) : 0f;

	[StructLayout( LayoutKind.Sequential )]
	private struct LevelVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.TexCoord] public Vector2 TexCoord;
		[VertexLayout.TexCoord( 1 )] public Vector2 TexCoord2;
		[VertexLayout.Color] public Color32 Color;
	}
}

/// <summary>Plays a Magicka level's global ambient soundscape loops.</summary>
[Title( "Magicka Level Ambience" )]
[Category( "Magicka" )]
public sealed class MagickaLevelAmbience : Component
{
	[Property] public string[] Sounds { get; set; } = [];
	[Property] public float Volume { get; set; } = 0.35f;

	private readonly List<SoundHandle> _handles = [];

	protected override void OnStart()
	{
		foreach ( var path in Sounds ?? [] )
		{
			if ( string.IsNullOrEmpty( path ) )
				continue;

			var file = SoundFile.Load( path );
			if ( file is null )
				continue;

			var ev = new SoundEvent
			{
				Sounds = [file],
				Volume = Volume,
				UI = true,
			};
			var handle = Sound.Play( ev );
			if ( handle is not null )
				_handles.Add( handle );
		}
	}

	protected override void OnDestroy()
	{
		foreach ( var h in _handles )
			h?.Stop();
		_handles.Clear();
	}
}

/// <summary>Spawns a Magicka level's authored ambient XML particle effect on scene start.</summary>
[Title( "Magicka Level Effect" )]
[Category( "Magicka" )]
public sealed class MagickaLevelEffect : Component
{
	[Property] public string EffectName { get; set; }

	protected override void OnStart()
	{
		if ( !string.IsNullOrWhiteSpace( EffectName ) )
			MagickaEffectPlayer.Attach( GameObject, EffectName );
	}
}

/// <summary>Loads the next Magicka level scene when a player walks into an exit trigger area.</summary>
[Title( "Magicka Change Level" )]
[Category( "Magicka" )]
public sealed class MagickaChangeLevel : Component, Component.ITriggerListener
{
	[Property] public string Destination { get; set; }

	private TimeSince _age;
	private bool _changing;

	protected override void OnStart() => _age = 0;

	void Component.ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( !Networking.IsHost || _changing || _age < 1.5f || string.IsNullOrEmpty( Destination ) )
			return;

		if ( other.GetComponentInParent<PlayerController>() is null )
			return;

		var sceneFile = GameResource.Load<SceneFile>( Destination );
		if ( !sceneFile.IsValid() )
			return;

		_changing = true;
		var options = new SceneLoadOptions();
		options.SetScene( sceneFile );
		Game.ChangeScene( options );
	}
}

/// <summary>Spawns a Magicka level's authored NPC prefab at this locator on scene start.</summary>
[Title( "Magicka Level NPC Spawner" )]
[Category( "Magicka" )]
public sealed class MagickaLevelNpcSpawner : Component
{
	[Property] public string PrefabUrl { get; set; }
	[Property] public bool ForcePassive { get; set; }

	protected override void OnStart()
	{
		if ( !Networking.IsHost || string.IsNullOrEmpty( PrefabUrl ) )
			return;

		var prefab = GameObject.GetPrefab( PrefabUrl );
		if ( !prefab.IsValid() )
			return;

		var go = prefab.Clone( WorldTransform );
		if ( ForcePassive && go.GetComponent<MagickaNpc>() is { } npc )
		{
			npc.Passive = true;
			npc.Friendly = true;
		}

		go.NetworkSpawn( true, null );
	}
}

/// <summary>Generates the scene NavMesh on level start so NPCs can navigate Magicka maps.</summary>
[Hide]
public sealed class MagickaLevelNavMesh : Component
{
	protected override void OnStart()
	{
		if ( !Networking.IsHost || Scene?.NavMesh is null )
			return;

		Scene.NavMesh.IsEnabled = true;
		if ( !Scene.NavMesh.IsGenerating )
			_ = Scene.NavMesh.Generate( Scene.PhysicsWorld );
	}
}
