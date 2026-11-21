using IdTech3.Formats;

namespace IdTech3;

/// <summary>How a surface is lit, mirroring the renderer's lightmap index argument to R_FindShader.</summary>
public enum LightingMode
{
	/// <summary>BSP surface with a lightmap.</summary>
	Lightmap,
	/// <summary>BSP surface lit by vertex colours (LIGHTMAP_BY_VERTEX).</summary>
	Vertex,
	/// <summary>Model (LIGHTMAP_NONE): lit by the light grid or the scene.</summary>
	Model,
}

public sealed class FogVolume
{
	public Vector3 Color;
	public float DepthForOpaque = 300;
	public BBox Bounds;
}

public sealed class MaterialRequest
{
	public string ShaderName;
	public LightingMode Mode;
	public Texture Lightmap;
	public FogVolume Fog;
	/// <summary>Darken the baked lighting under the sun's realtime shadows (world surfaces of a map with a baked sun).</summary>
	public bool SunShadows;
	/// <summary>Distinguishes materials that share a shader but differ in lightmap page or fog.</summary>
	public string Variant = "";
}

public static class MaterialBuilder
{
	public const string ShaderPath = "shaders/idtech3/idtech3.shader";
	const int MaxStages = 4;
	const int MaxTcMods = 4;

	/// <summary>True for shaders that never draw anything (caulk, clip, hint, fog volumes...).</summary>
	public static bool IsInvisible( ShaderDef def )
	{
		if ( def is null ) return false;
		if ( def.NoDraw ) return true;
		if ( def.IsFog && def.Stages.Count == 0 ) return true;
		if ( def.IsSky ) return false;
		// "surfaceparm nodraw" is sometimes missing on tool textures
		return def.Name.StartsWith( "textures/common/", StringComparison.Ordinal ) && def.Stages.Count == 0;
	}

	/// <summary>
	/// True for surfaces that block light: opaque, not alpha tested, not sky or fog. Shadow blockers are built from these.
	/// </summary>
	public static bool CastsShadow( ShaderDef def )
	{
		if ( def is null ) return true;
		if ( def.IsSky || def.IsFog || IsInvisible( def ) ) return false;
		if ( def.ImplicitMode is "implicitmask" or "implicitblend" ) return false;
		if ( def.Stages.Count == 0 ) return true;
		var first = def.Stages[0];
		return first.IsOpaque && first.AlphaFunc == AlphaFunc.None;
	}

	public static Material Build( GameData game, MaterialRequest req )
	{
		var def = game.Shaders.Find( req.ShaderName );
		if ( IsInvisible( def ) ) return null;

		bool sky = def?.IsSky == true;
		var stages = sky ? def.Stages.Select( s => s.Clone() ).ToList() : ResolveStages( game, def, req.ShaderName, req.Mode );
		if ( !sky && stages.Count == 0 ) return null;

		// drop stages whose image is missing unless it's the base stage
		var resolved = new List<(ShaderStage Stage, Texture Texture)>();
		foreach ( var stage in stages )
		{
			var tex = StageTexture( game, def, stage, req );
			if ( tex is null )
			{
				if ( resolved.Count == 0 && stage.IsOpaque ) tex = Texture.White;
				else continue;
			}
			resolved.Add( (stage, tex) );
			if ( resolved.Count == MaxStages ) break;
		}
		if ( !sky && resolved.Count == 0 ) return null;

		var name = $"{game.Ident}/{game.Dir}/{req.ShaderName}#{req.Mode}{req.Variant}";
		var material = Material.Create( name, ShaderPath );
		if ( material is null ) return null;

		int blend = 0, outputMode = 0;
		bool alphaTest = false, depthWrite = false;

		if ( !sky && resolved.Count > 0 )
		{
			var first = resolved[0].Stage;
			alphaTest = first.AlphaFunc != AlphaFunc.None;

			if ( first.IsOpaque || (alphaTest && first.DepthWrite) )
			{
				blend = 0;
			}
			else
			{
				(blend, outputMode) = (first.Src, first.Dst) switch
				{
					(BlendFactor.One, BlendFactor.One ) => (2, 0),
					(BlendFactor.SrcAlpha, BlendFactor.One ) => (2, 1),
					(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha ) => (1, 0),
					(BlendFactor.One, BlendFactor.OneMinusSrcAlpha ) => (4, 0),
					(BlendFactor.DstColor, BlendFactor.Zero ) => (3, 0),
					(BlendFactor.Zero, BlendFactor.SrcColor ) => (3, 0),
					(BlendFactor.DstColor, BlendFactor.SrcColor ) => (5, 0),
					(BlendFactor.SrcColor, BlendFactor.DstColor ) => (5, 0),
					(BlendFactor.Zero, BlendFactor.OneMinusSrcColor ) => (3, 2),
					(BlendFactor.OneMinusSrcAlpha, BlendFactor.SrcAlpha ) => (1, 3),
					(BlendFactor.DstColor, BlendFactor.One ) => (5, 4),
					(BlendFactor.Zero, BlendFactor.One ) => (-1, 0),
					(BlendFactor.One, BlendFactor.Zero ) => (0, 0),
					(_, BlendFactor.One ) => (2, 0),
					(BlendFactor.One, BlendFactor.OneMinusSrcColor ) => (2, 0),
					_ => (1, 0),
				};
				if ( blend < 0 ) return null;
				depthWrite = first.HasDepthWriteKeyword;
			}
		}

		material.SetFeature( "F_ID3_BLEND", blend );
		material.SetFeature( "F_ID3_ALPHA_TEST", alphaTest ? 1 : 0 );
		material.SetFeature( "F_ID3_DEPTH_WRITE", depthWrite ? 1 : 0 );
		material.SetFeature( "F_ID3_SKY", sky ? 1 : 0 );
		material.SetFeature( "F_RENDER_BACKFACES", def is not null && def.Cull != CullMode.Front ? 1 : 0 );

		float hasFarBox = 0;
		if ( sky )
		{
			var cube = game.GetSkyCube( def.SkyFarBox );
			if ( cube is not null )
			{
				material.Set( "SkyCube", cube );
				hasFarBox = 1;
			}
		}

		material.Set( "g_flId3StageCount", (float)resolved.Count );
		material.Set( "g_flId3SunShadows", req.SunShadows && req.Mode != LightingMode.Model && !sky ? 1f : 0f );
		material.Set( "g_vId3Params", new Vector4( hasFarBox, def?.CloudHeight ?? 128, outputMode, 1 ) );

		for ( int i = 0; i < resolved.Count; i++ )
			SetStage( material, i, resolved[i].Stage, resolved[i].Texture, sky, BakedLight( resolved[i].Stage, req.Mode ) );

		if ( req.Fog is { } fog && req.Mode != LightingMode.Model )
		{
			material.Set( "g_vId3FogColor", new Vector4( fog.Color, 1 ) );
			material.Set( "g_vId3FogMins", new Vector4( fog.Bounds.Mins, 1f / MathF.Max( 1, fog.DepthForOpaque ) ) );
			material.Set( "g_vId3FogMaxs", new Vector4( fog.Bounds.Maxs, 0 ) );
		}

		if ( def is not null ) SetDeform( material, def );

		return material;
	}

	static void SetDeform( Material material, ShaderDef def )
	{
		foreach ( var d in def.Deforms )
		{
			switch ( d.Type )
			{
				case DeformType.Wave:
					material.Set( "g_vId3Deform", new Vector4( 1, d.Spread, 0, 0 ) );
					material.Set( "g_vId3DeformWave", Wave( d.Wave ) );
					material.Set( "g_vId3DeformMove", new Vector4( d.Wave.Freq, 0, 0, 0 ) );
					return;
				case DeformType.Move:
					material.Set( "g_vId3Deform", new Vector4( 2, 0, 0, 0 ) );
					material.Set( "g_vId3DeformWave", Wave( d.Wave ) );
					material.Set( "g_vId3DeformMove", new Vector4( d.Wave.Freq, d.Vector.x, d.Vector.y, d.Vector.z ) );
					return;
				case DeformType.Bulge:
					material.Set( "g_vId3Deform", new Vector4( 3, 0, d.BulgeWidth, d.BulgeHeight ) );
					material.Set( "g_vId3DeformMove", new Vector4( d.BulgeSpeed, 0, 0, 0 ) );
					return;
			}
		}
	}

	static Vector4 Wave( WaveForm w ) => new( (float)(w.Func == WaveFunc.None ? WaveFunc.Sin : w.Func), w.Base, w.Amp, w.Phase );

	/// <summary>
	/// Which part of a stage carries baked lighting, so the shader can add s&amp;box's local lights to it:
	/// 0 none, 1 the texture (lightmap), 2 the rgbGen (vertex lighting).
	/// </summary>
	static float BakedLight( ShaderStage s, LightingMode mode )
	{
		if ( mode == LightingMode.Lightmap && s.IsLightmap ) return 1;
		if ( mode == LightingMode.Vertex && s.RgbGen is RgbGen.Vertex or RgbGen.ExactVertex ) return 2;
		return 0;
	}

	static void SetStage( Material m, int i, ShaderStage s, Texture texture, bool sky, float bakedLight )
	{
		m.Set( $"Stage{i}", texture );

		float tcGen = s.TcGen switch
		{
			TcGen.Lightmap => 1,
			TcGen.Environment or TcGen.Fireriseenv => 2,
			TcGen.Vector => 3,
			_ => sky ? 4 : 0,
		};
		bool clamp = s.Clamp || s.IsLightmap;
		m.Set( $"g_vS{i}Blend", new Vector4( (float)s.Src, (float)s.Dst, (float)s.AlphaFunc, clamp ? 1 : 0 ) );

		var rgbGen = s.RgbGen == RgbGen.Default ? RgbGen.Identity : s.RgbGen;
		var alphaGen = s.AlphaGen == AlphaGen.Default ? AlphaGen.Identity : s.AlphaGen;
		m.Set( $"g_vS{i}Gen", new Vector4( tcGen, (float)rgbGen, (float)alphaGen, Math.Max( 1, s.Maps.Count ) ) );
		m.Set( $"g_vS{i}RgbWave", Wave( s.RgbWave ) );
		m.Set( $"g_vS{i}AlphaWave", Wave( s.AlphaWave ) );
		m.Set( $"g_vS{i}Freq", new Vector4( s.RgbWave.Freq, s.AlphaWave.Freq, s.AnimFrequency, sky ? 0 : bakedLight ) );
		m.Set( $"g_vS{i}Const", new Vector4( s.ConstColor.x, s.ConstColor.y, s.ConstColor.z, s.ConstAlpha ) );
		m.Set( $"g_vS{i}TcS", new Vector4( s.TcGenS.x, s.TcGenS.y, s.TcGenS.z, 0 ) );
		m.Set( $"g_vS{i}TcT", new Vector4( s.TcGenT.x, s.TcGenT.y, s.TcGenT.z, 0 ) );

		for ( int j = 0; j < MaxTcMods; j++ )
		{
			Vector4 a = default, b = default;
			if ( j < s.TcMods.Count )
			{
				var t = s.TcMods[j];
				switch ( t.Type )
				{
					case TcModType.Scroll:
					case TcModType.Scale:
						a = new( (float)t.Type, t.P0, t.P1, 0 ); break;
					case TcModType.Rotate:
						a = new( (float)t.Type, t.P0, 0, 0 ); break;
					case TcModType.Stretch:
					case TcModType.Turb:
						a = new( (float)t.Type, t.Wave.Base, t.Wave.Amp, t.Wave.Phase );
						b = new( t.Wave.Freq, 0, 0, (float)(t.Wave.Func == WaveFunc.None ? WaveFunc.Sin : t.Wave.Func) );
						break;
					case TcModType.Transform:
						a = new( (float)t.Type, t.P0, t.P1, t.P2 );
						b = new( t.P3, t.P4, t.P5, 0 );
						break;
					case TcModType.Swap:
						a = new( (float)t.Type, 0, 0, 0 ); break;
				}
			}
			m.Set( $"g_vS{i}M{j}A", a );
			m.Set( $"g_vS{i}M{j}B", b );
		}
	}

	static Texture StageTexture( GameData game, ShaderDef def, ShaderStage s, MaterialRequest req )
	{
		if ( s.IsLightmap ) return req.Mode == LightingMode.Lightmap && req.Lightmap is not null ? req.Lightmap : Texture.White;
		if ( s.IsWhiteImage ) return Texture.White;
		if ( s.Maps.Count > 1 ) return game.GetAnimAtlas( def, s.Maps );
		if ( s.Map is null ) return null;
		return game.GetTexture( s.Map );
	}

	/// <summary>
	/// Stages after applying implicit shaders (no script) and the lighting mode.
	/// </summary>
	static List<ShaderStage> ResolveStages( GameData game, ShaderDef def, string shaderName, LightingMode mode )
	{
		List<ShaderStage> stages;

		if ( def is not null && def.ImplicitMode is not null )
		{
			var image = def.ImplicitImage is null or "-" ? shaderName : def.ImplicitImage;
			stages = ImplicitStages( image, mode, def.ImplicitMode );
		}
		else if ( def is not null && def.Stages.Count > 0 )
		{
			stages = def.Stages.Select( s => s.Clone() ).ToList();
		}
		else
		{
			// no script (or a script with no stages): the image with the same name
			var image = shaderName;
			if ( game.FindImage( image ) is null && def?.EditorImage is not null ) image = def.EditorImage;
			if ( game.FindImage( image ) is null && def?.LightImage is not null ) image = def.LightImage;
			if ( game.FindImage( image ) is null ) return [];
			stages = ImplicitStages( image, mode, null );
		}

		foreach ( var s in stages )
		{
			if ( !s.IsLightmap ) continue;
			if ( mode == LightingMode.Vertex )
			{
				// vertex lit surface: the lightmap becomes the vertex colours
				s.IsLightmap = false;
				s.IsWhiteImage = true;
				s.TcGen = TcGen.Base;
				if ( s.RgbGen is RgbGen.Default or RgbGen.Identity or RgbGen.IdentityLighting ) s.RgbGen = RgbGen.ExactVertex;
			}
			else if ( mode == LightingMode.Model )
			{
				s.IsLightmap = false;
				s.IsWhiteImage = true;
				s.TcGen = TcGen.Base;
				if ( s.RgbGen is RgbGen.Default or RgbGen.Identity or RgbGen.IdentityLighting ) s.RgbGen = RgbGen.LightingDiffuse;
			}
		}
		return stages;
	}

	static List<ShaderStage> ImplicitStages( string image, LightingMode mode, string implicitMode )
	{
		var tex = new ShaderStage { Maps = { PathUtil.Normalize( image ) } };
		bool mask = implicitMode == "implicitmask";
		bool blend = implicitMode == "implicitblend";

		if ( mask ) { tex.AlphaFunc = AlphaFunc.GE128; tex.DepthWrite = true; }

		if ( mode == LightingMode.Model )
		{
			tex.RgbGen = RgbGen.LightingDiffuse;
			if ( blend ) { tex.HasBlendFunc = true; tex.Src = BlendFactor.SrcAlpha; tex.Dst = BlendFactor.OneMinusSrcAlpha; }
			return [tex];
		}

		if ( mode == LightingMode.Vertex )
		{
			tex.RgbGen = RgbGen.ExactVertex;
			if ( blend ) { tex.HasBlendFunc = true; tex.Src = BlendFactor.SrcAlpha; tex.Dst = BlendFactor.OneMinusSrcAlpha; }
			return [tex];
		}

		if ( blend )
		{
			// lit translucent: texture blended, modulated by the lightmap in the same pass
			tex.HasBlendFunc = true; tex.Src = BlendFactor.SrcAlpha; tex.Dst = BlendFactor.OneMinusSrcAlpha;
			var lmMul = new ShaderStage { IsLightmap = true, TcGen = TcGen.Lightmap, HasBlendFunc = true, Src = BlendFactor.DstColor, Dst = BlendFactor.Zero };
			return [tex, lmMul];
		}

		if ( mask )
		{
			// alpha-tested texture first so it writes depth, lightmap multiplied on top
			var lmMul = new ShaderStage { IsLightmap = true, TcGen = TcGen.Lightmap, HasBlendFunc = true, Src = BlendFactor.DstColor, Dst = BlendFactor.Zero };
			return [tex, lmMul];
		}

		// default two-pass lightmap: $lightmap, then texture with GL_DST_COLOR GL_ZERO
		var lm = new ShaderStage { IsLightmap = true, TcGen = TcGen.Lightmap };
		tex.HasBlendFunc = true; tex.Src = BlendFactor.DstColor; tex.Dst = BlendFactor.Zero;
		return [lm, tex];
	}
}
