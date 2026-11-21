namespace IdTech3.Formats;

public enum WaveFunc { None, Sin, Triangle, Square, Sawtooth, InverseSawtooth, Noise }

public struct WaveForm
{
	public WaveFunc Func;
	public float Base, Amp, Phase, Freq;

	public readonly float Evaluate( float time )
	{
		float x = Phase + time * Freq;
		x -= MathF.Floor( x );
		float t = Func switch
		{
			WaveFunc.Sin => MathF.Sin( x * MathF.PI * 2 ),
			WaveFunc.Triangle => x < 0.25f ? x * 4 : x < 0.75f ? 2 - x * 4 : x * 4 - 4,
			WaveFunc.Square => x < 0.5f ? 1 : -1,
			WaveFunc.Sawtooth => x,
			WaveFunc.InverseSawtooth => 1 - x,
			_ => 0,
		};
		return Base + t * Amp;
	}
}

public enum BlendFactor
{
	Zero, One, SrcColor, OneMinusSrcColor, DstColor, OneMinusDstColor,
	SrcAlpha, OneMinusSrcAlpha, DstAlpha, OneMinusDstAlpha, SrcAlphaSaturate
}

public enum RgbGen { Default, Identity, IdentityLighting, Vertex, ExactVertex, OneMinusVertex, Wave, Const, LightingDiffuse, Entity, OneMinusEntity }
public enum AlphaGen { Default, Identity, Vertex, OneMinusVertex, Wave, Const, Portal, LightingSpecular, Entity, OneMinusEntity, Skip }
public enum TcGen { Base, Lightmap, Environment, Vector, Fireriseenv }
public enum AlphaFunc { None, GT0, LT128, GE128 }
public enum CullMode { Front, Back, None }
public enum TcModType { None, Scroll, Scale, Rotate, Stretch, Turb, Transform, EntityTranslate, Swap }

public struct TcMod
{
	public TcModType Type;
	public float P0, P1, P2, P3, P4, P5;
	public WaveForm Wave;
}

public enum DeformType { None, Wave, Normal, Bulge, Move, AutoSprite, AutoSprite2, Text }

public struct Deform
{
	public DeformType Type;
	public float Spread;
	public WaveForm Wave;
	public Vector3 Vector;
	public float BulgeWidth, BulgeHeight, BulgeSpeed;
}

public sealed class ShaderStage
{
	/// <summary>Image names. More than one means animMap.</summary>
	public List<string> Maps = new();
	public float AnimFrequency;
	public bool IsLightmap;
	public bool IsWhiteImage;
	public bool IsVideo;
	public bool Clamp;

	public bool HasBlendFunc;
	public BlendFactor Src = BlendFactor.One;
	public BlendFactor Dst = BlendFactor.Zero;

	public RgbGen RgbGen = RgbGen.Default;
	public Vector3 ConstColor = new( 1, 1, 1 );
	public WaveForm RgbWave;

	public AlphaGen AlphaGen = AlphaGen.Default;
	public float ConstAlpha = 1;
	public WaveForm AlphaWave;

	public TcGen TcGen = TcGen.Base;
	public Vector3 TcGenS = new( 1, 0, 0 ), TcGenT = new( 0, 1, 0 );
	public List<TcMod> TcMods = new();

	public AlphaFunc AlphaFunc;
	public bool DepthWrite;
	public bool HasDepthWriteKeyword;
	public bool DepthFuncEqual;
	public bool Detail;

	public string Map => Maps.Count > 0 ? Maps[0] : null;

	public ShaderStage Clone()
	{
		return new ShaderStage
		{
			Maps = new List<string>( Maps ), AnimFrequency = AnimFrequency, IsLightmap = IsLightmap, IsWhiteImage = IsWhiteImage,
			IsVideo = IsVideo, Clamp = Clamp, HasBlendFunc = HasBlendFunc, Src = Src, Dst = Dst,
			RgbGen = RgbGen, ConstColor = ConstColor, RgbWave = RgbWave, AlphaGen = AlphaGen, ConstAlpha = ConstAlpha, AlphaWave = AlphaWave,
			TcGen = TcGen, TcGenS = TcGenS, TcGenT = TcGenT, TcMods = new List<TcMod>( TcMods ),
			AlphaFunc = AlphaFunc, DepthWrite = DepthWrite, HasDepthWriteKeyword = HasDepthWriteKeyword, DepthFuncEqual = DepthFuncEqual, Detail = Detail,
		};
	}

	/// <summary>Opaque replace: blendFunc GL_ONE GL_ZERO or none.</summary>
	public bool IsOpaque => !HasBlendFunc || (Src == BlendFactor.One && Dst == BlendFactor.Zero);
}

public sealed class ShaderDef
{
	public string Name;
	public string SourceFile;
	public List<ShaderStage> Stages = new();
	public HashSet<string> SurfaceParms = new( StringComparer.OrdinalIgnoreCase );
	public CullMode Cull = CullMode.Front;
	public List<Deform> Deforms = new();

	public bool IsSky;
	public string SkyFarBox;
	public string SkyNearBox;
	public float CloudHeight = 128;

	public bool IsFog;
	public Vector3 FogColor;
	public float FogDepthForOpaque = 300;

	public bool PolygonOffset;
	public bool NoMipMaps;
	public bool IsPortal;
	public string Sort;
	public bool EntityMergable;

	/// <summary>qer_editorimage — good fallback image for shaders without stages.</summary>
	public string EditorImage;
	/// <summary>q3map_lightimage / implicit map image.</summary>
	public string LightImage;

	public bool HasSun;
	public Vector3 SunColor = new( 1, 1, 1 );
	public float SunIntensity;
	public float SunYaw, SunElevation;

	/// <summary>RTCW: "implicitMap/implicitMask/implicitBlend".</summary>
	public string ImplicitMode;
	public string ImplicitImage;

	public bool NoDraw => SurfaceParms.Contains( "nodraw" );
	public bool NonSolid => SurfaceParms.Contains( "nonsolid" );
	public bool IsTranslucentParm => SurfaceParms.Contains( "trans" );

	public override string ToString() => $"{Name} ({Stages.Count} stages)";
}

/// <summary>
/// Parser for idTech3 shader scripts (scripts/*.shader). Supports the Quake 3 syntax plus
/// the RTCW/ET and CoD extensions that matter for rendering.
/// </summary>
public static class ShaderParser
{
	public static List<ShaderDef> Parse( string text, string sourceFile = null )
	{
		var list = new List<ShaderDef>();
		var tok = new Tokenizer( text );

		while ( !tok.AtEnd )
		{
			var name = tok.Next();
			if ( name.Length == 0 ) break;
			if ( name == "{" ) { tok.SkipBracedSection(); continue; }
			if ( name == "}" ) continue;

			var open = tok.Next();
			if ( open != "{" )
			{
				// malformed; resync on next brace
				if ( open.Length == 0 ) break;
				continue;
			}

			var shader = new ShaderDef { Name = PathUtil.Normalize( PathUtil.StripExtensionIfImage( name ) ), SourceFile = sourceFile };
			try
			{
				ParseShaderBody( tok, shader );
			}
			catch ( Exception )
			{
				// skip broken shader
				continue;
			}
			list.Add( shader );
		}
		return list;
	}

	static void ParseShaderBody( Tokenizer tok, ShaderDef shader )
	{
		while ( true )
		{
			var t = tok.Next();
			if ( t.Length == 0 ) return;
			if ( t == "}" ) return;

			if ( t == "{" )
			{
				var stage = ParseStage( tok );
				if ( stage is not null ) shader.Stages.Add( stage );
				continue;
			}

			var key = t.ToLowerInvariant();
			var args = tok.RestOfLine();

			switch ( key )
			{
				case "surfaceparm":
					if ( args.Count > 0 ) shader.SurfaceParms.Add( args[0] );
					if ( args.Count > 0 && args[0].Equals( "sky", StringComparison.OrdinalIgnoreCase ) ) shader.IsSky = true;
					break;

				case "skyparms":
					shader.IsSky = true;
					if ( args.Count > 0 && args[0] != "-" ) shader.SkyFarBox = PathUtil.Normalize( args[0] );
					if ( args.Count > 1 && args[1] != "-" ) shader.CloudHeight = Num.F( args[1], 128 );
					if ( args.Count > 2 && args[2] != "-" ) shader.SkyNearBox = PathUtil.Normalize( args[2] );
					break;

				case "cull":
					if ( args.Count > 0 )
					{
						var c = args[0].ToLowerInvariant();
						if ( c is "none" or "twosided" or "disable" ) shader.Cull = CullMode.None;
						else if ( c is "back" or "backside" or "backsided" ) shader.Cull = CullMode.Back;
						else shader.Cull = CullMode.Front;
					}
					break;

				case "fogparms":
				{
					// fogParms ( r g b ) depth
					var nums = args.Where( a => a != "(" && a != ")" ).ToList();
					if ( nums.Count >= 4 )
					{
						shader.IsFog = true;
						shader.FogColor = new Vector3( Num.F( nums[0] ), Num.F( nums[1] ), Num.F( nums[2] ) );
						shader.FogDepthForOpaque = MathF.Max( 1, Num.F( nums[3], 300 ) );
					}
					break;
				}

				case "deformvertexes":
					if ( args.Count > 0 ) shader.Deforms.Add( ParseDeform( args ) );
					break;

				case "polygonoffset": shader.PolygonOffset = true; break;
				case "nomipmaps": shader.NoMipMaps = true; break;
				case "nopicmip": break;
				case "portal": shader.IsPortal = true; break;
				case "entitymergable": shader.EntityMergable = true; break;
				case "sort": if ( args.Count > 0 ) shader.Sort = args[0].ToLowerInvariant(); break;

				case "qer_editorimage":
					if ( args.Count > 0 ) shader.EditorImage = PathUtil.Normalize( args[0] );
					break;

				case "q3map_lightimage":
					if ( args.Count > 0 ) shader.LightImage = PathUtil.Normalize( args[0] );
					break;

				case "q3map_sun":
				case "q3map_sunext":
				case "sun":
				{
					// r g b intensity degrees elevation
					var nums = args.Where( a => a != "(" && a != ")" ).ToList();
					if ( nums.Count >= 6 )
					{
						shader.HasSun = true;
						shader.SunColor = new Vector3( Num.F( nums[0] ), Num.F( nums[1] ), Num.F( nums[2] ) );
						shader.SunIntensity = Num.F( nums[3] );
						shader.SunYaw = Num.F( nums[4] );
						shader.SunElevation = Num.F( nums[5] );
					}
					break;
				}

				case "implicitmap":
				case "implicitmask":
				case "implicitblend":
					shader.ImplicitMode = key;
					shader.ImplicitImage = args.Count > 0 ? args[0] : "-";
					break;

				default:
					// q3map_*, qer_*, light, tesssize, clamptime, nocompress, sunshader, skyfogvars, etc.
					break;
			}
		}
	}

	static ShaderStage ParseStage( Tokenizer tok )
	{
		var s = new ShaderStage();
		bool depthMaskExplicit = false;

		while ( true )
		{
			var t = tok.Next();
			if ( t.Length == 0 ) break;
			if ( t == "}" ) break;
			if ( t == "{" ) { tok.SkipBracedSection(); continue; }

			var key = t.ToLowerInvariant();
			var args = tok.RestOfLine();
			var a0 = args.Count > 0 ? args[0] : string.Empty;

			switch ( key )
			{
				case "map":
				case "mapcomp":
				case "mapnocomp":
				case "clampmap":
				case "clampmapcomp":
				case "clampmapnocomp":
					if ( key.StartsWith( "clampmap" ) ) s.Clamp = true;
					if ( key is "mapcomp" or "clampmapcomp" ) break; // alternate low-quality image, ignore
					s.Maps.Clear();
					if ( a0.Equals( "$lightmap", StringComparison.OrdinalIgnoreCase ) ) { s.IsLightmap = true; s.TcGen = TcGen.Lightmap; }
					else if ( a0.Equals( "$whiteimage", StringComparison.OrdinalIgnoreCase ) || a0.Equals( "*white", StringComparison.OrdinalIgnoreCase ) ) s.IsWhiteImage = true;
					else if ( a0.Length > 0 ) s.Maps.Add( PathUtil.Normalize( a0 ) );
					break;

				case "lightmap":
					s.IsLightmap = true; s.TcGen = TcGen.Lightmap;
					break;

				case "animmap":
				case "animmapcomp":
				case "animmapnocomp":
					if ( key == "animmapcomp" ) break;
					s.Maps.Clear();
					s.AnimFrequency = Num.F( a0 );
					for ( int i = 1; i < args.Count && s.Maps.Count < 8; i++ )
						s.Maps.Add( PathUtil.Normalize( args[i] ) );
					break;

				case "videomap":
					s.IsVideo = true;
					s.IsWhiteImage = true;
					break;

				case "blendfunc":
					s.HasBlendFunc = true;
					if ( args.Count == 1 )
					{
						switch ( a0.ToLowerInvariant() )
						{
							case "add": s.Src = BlendFactor.One; s.Dst = BlendFactor.One; break;
							case "filter": s.Src = BlendFactor.DstColor; s.Dst = BlendFactor.Zero; break;
							case "blend": s.Src = BlendFactor.SrcAlpha; s.Dst = BlendFactor.OneMinusSrcAlpha; break;
						}
					}
					else if ( args.Count >= 2 )
					{
						s.Src = Factor( args[0], true );
						s.Dst = Factor( args[1], false );
					}
					break;

				case "rgbgen":
					switch ( a0.ToLowerInvariant() )
					{
						case "identity": s.RgbGen = RgbGen.Identity; break;
						case "identitylighting": s.RgbGen = RgbGen.IdentityLighting; break;
						case "vertex":
						case "vertexlit":
						case "lightingdiffuseentity":
							s.RgbGen = a0.Equals( "lightingdiffuseentity", StringComparison.OrdinalIgnoreCase ) ? RgbGen.LightingDiffuse : RgbGen.Vertex; break;
						case "exactvertex": s.RgbGen = RgbGen.ExactVertex; break;
						case "onminusvertex":
						case "oneminusvertex": s.RgbGen = RgbGen.OneMinusVertex; break;
						case "lightingdiffuse": s.RgbGen = RgbGen.LightingDiffuse; break;
						case "entity": s.RgbGen = RgbGen.Entity; break;
						case "oneminusentity": s.RgbGen = RgbGen.OneMinusEntity; break;
						case "wave": s.RgbGen = RgbGen.Wave; s.RgbWave = ParseWave( args, 1 ); break;
						case "const":
						case "constant":
						{
							var nums = args.Skip( 1 ).Where( a => a != "(" && a != ")" ).ToList();
							s.RgbGen = RgbGen.Const;
							if ( nums.Count >= 3 ) s.ConstColor = new Vector3( Num.F( nums[0] ), Num.F( nums[1] ), Num.F( nums[2] ) );
							break;
						}
					}
					break;

				case "alphagen":
					switch ( a0.ToLowerInvariant() )
					{
						case "identity": s.AlphaGen = AlphaGen.Identity; break;
						case "vertex": s.AlphaGen = AlphaGen.Vertex; break;
						case "oneminusvertex": s.AlphaGen = AlphaGen.OneMinusVertex; break;
						case "entity": s.AlphaGen = AlphaGen.Entity; break;
						case "oneminusentity": s.AlphaGen = AlphaGen.OneMinusEntity; break;
						case "portal": s.AlphaGen = AlphaGen.Portal; break;
						case "lightingspecular": s.AlphaGen = AlphaGen.LightingSpecular; break;
						case "wave": s.AlphaGen = AlphaGen.Wave; s.AlphaWave = ParseWave( args, 1 ); break;
						case "const":
						case "constant": s.AlphaGen = AlphaGen.Const; s.ConstAlpha = Num.F( args.Count > 1 ? args[1] : "1", 1 ); break;
						case "skip": s.AlphaGen = AlphaGen.Skip; break;
						case "normalzfade": s.AlphaGen = AlphaGen.Identity; break;
					}
					break;

				case "tcgen":
				case "texgen":
					switch ( a0.ToLowerInvariant() )
					{
						case "environment": s.TcGen = TcGen.Environment; break;
						case "firerisenv": s.TcGen = TcGen.Environment; break;
						case "lightmap": s.TcGen = TcGen.Lightmap; break;
						case "texture":
						case "base": s.TcGen = TcGen.Base; break;
						case "vector":
						{
							var nums = args.Skip( 1 ).Where( a => a != "(" && a != ")" ).ToList();
							if ( nums.Count >= 6 )
							{
								s.TcGen = TcGen.Vector;
								s.TcGenS = new Vector3( Num.F( nums[0] ), Num.F( nums[1] ), Num.F( nums[2] ) );
								s.TcGenT = new Vector3( Num.F( nums[3] ), Num.F( nums[4] ), Num.F( nums[5] ) );
							}
							break;
						}
					}
					break;

				case "tcmod":
				{
					var m = ParseTcMod( args );
					if ( m.Type != TcModType.None ) s.TcMods.Add( m );
					break;
				}

				case "alphafunc":
					s.AlphaFunc = a0.ToUpperInvariant() switch
					{
						"GT0" => AlphaFunc.GT0,
						"LT128" => AlphaFunc.LT128,
						"GE128" => AlphaFunc.GE128,
						_ => AlphaFunc.None,
					};
					break;

				case "depthfunc":
					s.DepthFuncEqual = a0.Equals( "equal", StringComparison.OrdinalIgnoreCase );
					break;

				case "depthwrite":
					s.DepthWrite = true;
					depthMaskExplicit = true;
					break;

				case "detail":
					s.Detail = true;
					break;

				default:
					// fog on/off, nodepthfog, etc.
					break;
			}
		}

		s.HasDepthWriteKeyword = depthMaskExplicit;
		if ( !depthMaskExplicit )
			s.DepthWrite = s.IsOpaque;

		return s;
	}

	static BlendFactor Factor( string name, bool src )
	{
		switch ( name.ToUpperInvariant() )
		{
			case "GL_ONE": return BlendFactor.One;
			case "GL_ZERO": return BlendFactor.Zero;
			case "GL_DST_COLOR": return BlendFactor.DstColor;
			case "GL_ONE_MINUS_DST_COLOR": return BlendFactor.OneMinusDstColor;
			case "GL_SRC_COLOR": return BlendFactor.SrcColor;
			case "GL_ONE_MINUS_SRC_COLOR": return BlendFactor.OneMinusSrcColor;
			case "GL_SRC_ALPHA": return BlendFactor.SrcAlpha;
			case "GL_ONE_MINUS_SRC_ALPHA": return BlendFactor.OneMinusSrcAlpha;
			case "GL_DST_ALPHA": return BlendFactor.DstAlpha;
			case "GL_ONE_MINUS_DST_ALPHA": return BlendFactor.OneMinusDstAlpha;
			case "GL_SRC_ALPHA_SATURATE": return BlendFactor.SrcAlphaSaturate;
			default: return src ? BlendFactor.One : BlendFactor.Zero;
		}
	}

	static WaveFunc ParseWaveFunc( string s ) => s.ToLowerInvariant() switch
	{
		"sin" => WaveFunc.Sin,
		"triangle" => WaveFunc.Triangle,
		"square" => WaveFunc.Square,
		"sawtooth" => WaveFunc.Sawtooth,
		"inversesawtooth" => WaveFunc.InverseSawtooth,
		"noise" => WaveFunc.Noise,
		_ => WaveFunc.Sin,
	};

	static WaveForm ParseWave( List<string> args, int start )
	{
		var w = new WaveForm();
		if ( args.Count <= start ) return w;
		w.Func = ParseWaveFunc( args[start] );
		w.Base = Num.F( args.ElementAtOrDefault( start + 1 ) );
		w.Amp = Num.F( args.ElementAtOrDefault( start + 2 ) );
		w.Phase = Num.F( args.ElementAtOrDefault( start + 3 ) );
		w.Freq = Num.F( args.ElementAtOrDefault( start + 4 ) );
		return w;
	}

	static TcMod ParseTcMod( List<string> args )
	{
		var m = new TcMod();
		if ( args.Count == 0 ) return m;
		float P( int i ) => Num.F( args.ElementAtOrDefault( i ) );

		switch ( args[0].ToLowerInvariant() )
		{
			case "scroll": m.Type = TcModType.Scroll; m.P0 = P( 1 ); m.P1 = P( 2 ); break;
			case "scale": m.Type = TcModType.Scale; m.P0 = P( 1 ); m.P1 = P( 2 ); break;
			case "rotate": m.Type = TcModType.Rotate; m.P0 = P( 1 ); break;
			case "stretch": m.Type = TcModType.Stretch; m.Wave = ParseWave( args, 1 ); break;
			case "turb":
			{
				// turb [sin] base amp phase freq  (the waveform name is optional)
				m.Type = TcModType.Turb;
				int i = 1;
				if ( args.Count > 1 && !char.IsDigit( args[1][0] ) && args[1][0] != '-' && args[1][0] != '.' ) i = 2;
				m.Wave = new WaveForm { Func = WaveFunc.Sin, Base = P( i ), Amp = P( i + 1 ), Phase = P( i + 2 ), Freq = P( i + 3 ) };
				break;
			}
			case "transform":
				m.Type = TcModType.Transform;
				m.P0 = P( 1 ); m.P1 = P( 2 ); m.P2 = P( 3 ); m.P3 = P( 4 ); m.P4 = P( 5 ); m.P5 = P( 6 );
				break;
			case "entitytranslate": m.Type = TcModType.EntityTranslate; break;
			case "swap": m.Type = TcModType.Swap; break;
		}
		return m;
	}

	static Deform ParseDeform( List<string> args )
	{
		var d = new Deform();
		float P( int i ) => Num.F( args.ElementAtOrDefault( i ) );
		switch ( args[0].ToLowerInvariant() )
		{
			case "wave":
				d.Type = DeformType.Wave;
				d.Spread = P( 1 ) != 0 ? 1f / P( 1 ) : 100f;
				d.Wave = ParseWave( args, 2 );
				break;
			case "normal":
				d.Type = DeformType.Normal;
				d.Wave = new WaveForm { Func = WaveFunc.Sin, Amp = P( 1 ), Freq = P( 2 ) };
				break;
			case "bulge":
				d.Type = DeformType.Bulge;
				d.BulgeWidth = P( 1 ); d.BulgeHeight = P( 2 ); d.BulgeSpeed = P( 3 );
				break;
			case "move":
				d.Type = DeformType.Move;
				d.Vector = new Vector3( P( 1 ), P( 2 ), P( 3 ) );
				d.Wave = ParseWave( args, 4 );
				break;
			case "autosprite": d.Type = DeformType.AutoSprite; break;
			case "autosprite2": d.Type = DeformType.AutoSprite2; break;
			default: d.Type = args[0].StartsWith( "text", StringComparison.OrdinalIgnoreCase ) ? DeformType.Text : DeformType.None; break;
		}
		return d;
	}
}

/// <summary>
/// All shaders from every scripts/*.shader file of a game directory, resolved with idTech3 precedence.
/// </summary>
public sealed class ShaderLibrary
{
	readonly Dictionary<string, ShaderDef> _shaders = new( StringComparer.OrdinalIgnoreCase );

	public IEnumerable<ShaderDef> All => _shaders.Values;
	public int Count => _shaders.Count;

	public ShaderDef Find( string name )
	{
		if ( string.IsNullOrEmpty( name ) ) return null;
		var n = PathUtil.Normalize( name );
		if ( _shaders.TryGetValue( n, out var s ) ) return s;
		var stripped = PathUtil.StripExtensionIfImage( n );
		return stripped != n && _shaders.TryGetValue( stripped, out s ) ? s : null;
	}

	public static ShaderLibrary Load( LayeredFileSystem fs, string scriptDir = "scripts" )
	{
		var lib = new ShaderLibrary();

		// idTech3 concatenates shader files in reverse order so later files take precedence;
		// the first definition found wins.
		var files = fs.FindFiles( scriptDir, ".shader" ).Reverse().ToList();
		foreach ( var file in files )
		{
			var text = fs.ReadText( file );
			if ( text is null ) continue;
			foreach ( var def in ShaderParser.Parse( text, file ) )
				lib._shaders.TryAdd( def.Name, def );
		}
		return lib;
	}

	public void Add( ShaderDef def ) => _shaders.TryAdd( def.Name, def );
}

