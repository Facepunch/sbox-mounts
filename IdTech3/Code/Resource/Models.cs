using System.Runtime.InteropServices;
using IdTech3.Formats;

namespace IdTech3;

/// <summary>Vertex used by maps and models: position, normal, tangent, texture uv, lightmap uv, colour.</summary>
[StructLayout( LayoutKind.Sequential )]
public struct Id3Vertex
{
	[VertexLayout.Position] public Vector3 Position;
	[VertexLayout.Normal] public Vector3 Normal;
	[VertexLayout.Tangent] public Vector4 Tangent;
	[VertexLayout.TexCoord( 0 )] public Vector2 TexCoord;
	[VertexLayout.TexCoord( 1 )] public Vector2 LightmapCoord;
	[VertexLayout.Color] public Color32 Color;
}

static class Convert3
{
	public static Rotation R( Axis a ) => Rotation.LookAt( a.Forward, a.Up );

	/// <summary>idTech 3 pitch/yaw/roll in degrees to an s&amp;box rotation.</summary>
	public static Rotation Angles( Vector3 a ) => R( Axis.FromAngles( a.x, a.y, a.z ) );

	public static Vector4 TangentFor( Vector3 normal )
	{
		var t = MathF.Abs( normal.z ) < 0.999f ? Vector3.Cross( Vector3.Up, normal ) : Vector3.Cross( Vector3.Forward, normal );
		return new Vector4( t.Normal, 1 );
	}
}

static class ModelBuild
{
	/// <summary>
	/// Build an s&amp;box model from parsed model data. Each surface gets the material returned by
	/// <paramref name="materialFor"/>; surfaces without a material are skipped.
	/// </summary>
	public static Model Build( string name, ModelData data, Func<ModelSurface, Material> materialFor,
		IReadOnlyList<(string Group, Func<ModelSurface, Material> Materials)> materialGroups = null, bool collision = true )
	{
		var builder = Model.Builder.WithName( name );
		var hull = new List<Vector3>();
		var meshMaterials = new List<(ModelSurface Surface, Material Material)>();
		int meshes = 0;

		foreach ( var surface in data.Surfaces )
		{
			if ( surface.Vertices.Count == 0 || surface.Indices.Count < 3 ) continue;
			var material = materialFor( surface );
			if ( material is null ) continue;

			var verts = new List<Id3Vertex>( surface.Vertices.Count );
			var bounds = BBox.FromPositionAndSize( surface.Vertices[0].Position, 0 );
			foreach ( var v in surface.Vertices )
			{
				var p = v.Position;
				var n = v.Normal;
				verts.Add( new Id3Vertex
				{
					Position = p,
					Normal = n,
					Tangent = Convert3.TangentFor( n ),
					TexCoord =  v.TexCoord ,
					Color = new Color32( 255, 255, 255, 255 ),
				} );
				bounds = bounds.AddPoint( p );
				hull.Add( p );
			}

			// idTech 3 triangles are clockwise; s&box front faces are counter-clockwise
			var indices = new List<int>( surface.Indices.Count );
			for ( int i = 0; i + 2 < surface.Indices.Count; i += 3 )
			{
				indices.Add( surface.Indices[i] );
				indices.Add( surface.Indices[i + 2] );
				indices.Add( surface.Indices[i + 1] );
			}

			var mesh = new Mesh( material );
			mesh.CreateVertexBuffer( verts.Count, verts );
			mesh.CreateIndexBuffer( indices.Count, indices );
			mesh.Bounds = bounds;
			builder.AddMesh( mesh );
			meshMaterials.Add( (surface, material) );
			meshes++;
		}

		if ( meshes == 0 ) return null;

		foreach ( var tag in data.Tags )
			builder.AddAttachment( tag.Name,  tag.Origin , Convert3.R( tag.Axis ) );

		if ( materialGroups is not null && materialGroups.Count > 0 )
		{
			var defaults = new List<Material>();
			foreach ( var (_, m) in meshMaterials ) if ( !defaults.Contains( m ) ) defaults.Add( m );

			builder.AddMaterialGroup( "default" ).AddMaterials( defaults.ToArray() );
			foreach ( var (group, materials) in materialGroups )
			{
				var list = new List<Material>();
				foreach ( var d in defaults )
				{
					var surface = meshMaterials.First( x => x.Material == d ).Surface;
					list.Add( materials( surface ) ?? d );
				}
				builder.AddMaterialGroup( group ).AddMaterials( list.ToArray() );
			}
		}

		if ( collision && hull.Count >= 4 )
			builder.AddCollisionHull( hull );

		return builder.Create();
	}
}

/// <summary>MD3 / MDC / MDS model, at its first frame.</summary>
sealed class ModelLoader( GameData game, string path ) : ResourceLoader<IdTech3Mount>
{
	protected override object Load()
	{
		var data = ModelReader.Load( game.Read( path ), path );
		if ( data is null ) return null;

		var skins = ModelSkins.Find( game, path );
		return ModelBuild.Build( Path, data, s => ModelSkins.Material( game, s, skins.Default ), skins.Groups( game ) );
	}
}

/// <summary>
/// Q3 .skin files next to a model (NAME_default.skin, NAME_red.skin...). The default skin
/// (or the shaders in the model) is the default material group, the rest become material groups.
/// </summary>
sealed class ModelSkins
{
	public Dictionary<string, string> Default;
	public List<(string Name, Dictionary<string, string> Map)> Others = [];

	public static ModelSkins Find( GameData game, string modelPath )
	{
		var dir = PathUtil.Directory( modelPath );
		var stem = PathUtil.StripExtension( PathUtil.FileName( modelPath ) );
		var result = new ModelSkins();
		var prefix = $"{dir}/{stem}_";
		foreach ( var skin in game.Fs.FindFiles( dir, ".skin" ) )
		{
			if ( !skin.StartsWith( prefix, StringComparison.Ordinal ) ) continue;
			var skinName = PathUtil.StripExtension( skin )[prefix.Length..];
			if ( skinName.Contains( '/' ) ) continue;
			var map = SkinFile.Parse( game.Fs.ReadText( skin ) );
			if ( skinName == "default" ) result.Default = map;
			else result.Others.Add( (skinName, map) );
		}
		return result;
	}

	public static Material Material( GameData game, ModelSurface s, Dictionary<string, string> skin )
	{
		var shader = skin is not null && skin.TryGetValue( s.Name, out var mapped ) ? mapped : s.Shader;
		if ( string.IsNullOrEmpty( shader ) && skin is not null && skin.TryGetValue( "*", out var any ) ) shader = any;
		if ( string.IsNullOrEmpty( shader ) ) return game.GetModelMaterial( "*white" ) ?? FallbackMaterial;
		return game.GetModelMaterial( shader ) ?? FallbackMaterial;
	}

	static Material _fallback;
	static Material FallbackMaterial => _fallback ??= Sandbox.Material.Load( "materials/dev/dev_measuregeneric01.vmat" );

	public List<(string, Func<ModelSurface, Material>)> Groups( GameData game )
	{
		var list = new List<(string, Func<ModelSurface, Material>)>();
		foreach ( var (name, map) in Others )
			list.Add( (name, s => Material( game, s, map )) );
		return list;
	}
}

/// <summary>
/// Q3 / Team Arena player: lower, upper and head models joined at tag_torso and tag_head,
/// posed in the idle animation from animation.cfg. Skins become material groups.
/// </summary>
sealed class PlayerModelLoader( GameData game, string dir, string ext ) : ResourceLoader<IdTech3Mount>
{
	protected override object Load()
	{
		var anims = PlayerAnimations.Parse( game.Fs.ReadText( $"{dir}/animation.cfg" ) );
		int legsFrame = anims.FrameOf( PlayerAnimations.LegsIdle );
		int torsoFrame = anims.FrameOf( PlayerAnimations.TorsoStand );

		var lower = ModelReader.Load( game.Read( $"{dir}/lower{ext}" ), "lower", legsFrame );
		var upper = ModelReader.Load( game.Read( $"{dir}/upper{ext}" ), "upper", torsoFrame );
		var head = ModelReader.Load( game.Read( $"{dir}/head{ext}" ), "head", 0 );
		if ( lower is null || upper is null || head is null ) return null;

		var torsoTag = lower.FindTag( "tag_torso" );
		if ( torsoTag is { } tt ) upper.Transform( tt.Origin, tt.Axis );
		var headTag = upper.FindTag( "tag_head" );
		if ( headTag is { } ht ) head.Transform( ht.Origin, ht.Axis );

		// skins: lower_X.skin, upper_X.skin, head_X.skin
		var skinNames = game.Fs.FindFiles( dir, ".skin" )
			.Select( f => PathUtil.StripExtension( PathUtil.FileName( f ) ) )
			.Where( f => f.StartsWith( "lower_", StringComparison.Ordinal ) )
			.Select( f => f["lower_".Length..] )
			.ToList();

		var parts = new[] { ("lower", lower), ("upper", upper), ("head", head) };
		var combined = new ModelData { Name = dir };
		var partOf = new Dictionary<ModelSurface, string>();
		foreach ( var (part, data) in parts )
		{
			foreach ( var s in data.Surfaces ) { combined.Surfaces.Add( s ); partOf[s] = part; }
			foreach ( var t in data.Tags ) if ( !combined.Tags.Any( x => x.Name == t.Name ) ) combined.Tags.Add( t );
		}

		Dictionary<string, string> Skin( string part, string skin ) => SkinFile.Parse( game.Fs.ReadText( $"{dir}/{part}_{skin}.skin" ) );
		Func<ModelSurface, Material> ForSkin( string skin )
		{
			var maps = parts.ToDictionary( p => p.Item1, p => Skin( p.Item1, skin ) );
			return s => ModelSkins.Material( game, s, maps[partOf[s]] );
		}

		var groups = skinNames.Where( s => s != "default" ).Select( s => (s, ForSkin( s )) ).ToList();
		return ModelBuild.Build( Path, combined, ForSkin( "default" ), groups );
	}
}

/// <summary>Call of Duty xmodel.</summary>
sealed class XModelLoader( GameData game, string name ) : ResourceLoader<IdTech3Mount>
{
	protected override object Load()
	{
		var data = XModel.Load( name, game.Read );
		if ( data is null ) return null;
		return ModelBuild.Build( Path, data, s => XModelMaterial( game, s.Shader ) );
	}

	/// <summary>xmodel materials are skin images (skins/NAME.dds), optionally with a shader script.</summary>
	public static Material XModelMaterial( GameData game, string skin )
	{
		if ( string.IsNullOrEmpty( skin ) ) return null;
		var stripped = PathUtil.StripExtensionIfImage( PathUtil.Normalize( skin ) );
		var shaderName = stripped.StartsWith( "skins/" ) ? stripped : $"skins/{stripped}";
		if ( game.Shaders.Find( shaderName ) is not null || game.FindImage( shaderName ) is not null )
			return game.GetModelMaterial( shaderName );
		return game.GetModelMaterial( stripped );
	}
}
