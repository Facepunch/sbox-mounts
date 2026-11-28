using System;

namespace Source1.GarrysMod;

public class SkyPaint : Component, IMapInput
{
	internal const string Shader = "shaders/source1/gmod/painted_sky.shader";

	public record struct Settings
	{
		public Vector3 TopColor { get; set; }
		public Vector3 BottomColor { get; set; }
		public float FadeBias { get; set; }
		public float HDRScale { get; set; }
		public Vector3 SunNormal { get; set; }
		public Vector3 SunColor { get; set; }
		public float SunSize { get; set; }
		public Vector3 DuskColor { get; set; }
		public float DuskScale { get; set; }
		public float DuskIntensity { get; set; }
		public bool DrawStars { get; set; }
		public int StarLayers { get; set; }
		public float StarScale { get; set; }
		public float StarFade { get; set; }
		public float StarSpeed { get; set; }

		internal static Settings MaterialDefaults => new()
		{
			TopColor = new( 0, 0, 1 ), BottomColor = new( 0, 1, 1 ), FadeBias = 1, HDRScale = 1,
			SunNormal = new( 0, 1, 0.5f ), SunColor = Vector3.One, SunSize = 1,
			DuskColor = new( 1, 0.4f, 0 ), DuskScale = 1, DuskIntensity = 1,
			DrawStars = true, StarScale = 1, StarFade = 1
		};

		internal static Settings EntityDefaults => MaterialDefaults with
		{
			TopColor = new( 0.2f, 0.5f, 1 ), BottomColor = new( 0.8f, 1, 1 ), HDRScale = 0.66f,
			SunNormal = new( 0.4f, 0, 0.01f ), SunColor = new( 0.2f, 0.1f, 0 ), SunSize = 2,
			DuskColor = new( 1, 0.2f, 0 ), StarLayers = 1, StarScale = 0.5f, StarFade = 1.5f, StarSpeed = 0.01f
		};

		internal Settings Read( string field, VmtParameters p, string key, bool input = false )
		{
			Vector3 ColorValue() => p.Vector( key, default ) / (input ? 255 : 1);
			return field switch
			{
				"topcolor" => this with { TopColor = ColorValue() },
				"bottomcolor" => this with { BottomColor = ColorValue() },
				"fadebias" => this with { FadeBias = p.Float( key ) },
				"hdrscale" => this with { HDRScale = p.Float( key ) },
				"sunnormal" when !input => this with { SunNormal = p.Vector( key, default ) },
				"suncolor" => this with { SunColor = ColorValue() },
				"sunsize" => this with { SunSize = p.Float( key ) },
				"duskcolor" => this with { DuskColor = ColorValue() },
				"duskscale" => this with { DuskScale = p.Float( key ) },
				"duskintensity" => this with { DuskIntensity = p.Float( key ) },
				"drawstars" => this with { DrawStars = p.Boolean( key ) },
				"starlayers" => this with { StarLayers = p.Integer( key ) },
				"starscale" => this with { StarScale = p.Float( key ) },
				"starfade" => this with { StarFade = p.Float( key ) },
				"starspeed" => this with { StarSpeed = p.Float( key ) },
				_ => this
			};
		}

		internal readonly void Bind( Material m )
		{
			m.Set( "TopColor", TopColor );
			m.Set( "BottomColor", BottomColor );
			m.Set( "FadeBias", FadeBias );
			m.Set( "HDRScale", HDRScale );
			m.Set( "SunNormal", SunNormal.Length >= 0.001f ? SunNormal.Normal : Vector3.Zero );
			m.Set( "SunColor", SunColor );
			m.Set( "SunSize", SunSize );
			m.Set( "DuskColor", DuskColor );
			m.Set( "DuskScale", DuskScale );
			m.Set( "DuskIntensity", DuskIntensity );
			m.Set( "StarLayers", DrawStars ? Math.Clamp( StarLayers, 0, 3 ) : 0 );
			m.Set( "StarScale", StarScale == 0 ? 0 : 1 / StarScale );
			m.Set( "StarFade", StarFade );
			m.Set( "StarSpeed", StarSpeed );
		}
	}

	[Property, Sync( SyncFlags.FromHost ), Change( nameof( PaintChanged ) )] public Settings Paint { get; set; } = Settings.EntityDefaults;
	[Property, Sync( SyncFlags.FromHost ), Change( nameof( StarsChanged ) )] public string StarTexture { get; set; } = "skybox/starfield";
	[Property, Hide] public string ContentRoot { get; set; }

	Material _sky;
	bool _started;

	protected override void OnStart()
	{
		_started = true;
		Activate();
	}

	protected override void OnEnabled()
	{
		if ( _started ) Activate();
	}

	void Activate()
	{
		if ( Application.IsDedicatedServer || GameObject.IsDestroyed ) return;
		var active = Scene.GetAllComponents<SkyPaint>().FirstOrDefault( p => p != this && p._sky is not null );
		if ( active is not null )
		{
			if ( this is not SkyEditor ) return;
			active._sky = null;
		}
		_sky = Scene.GetAllComponents<SkyBox2D>().Select( sky => sky.SkyMaterial )
			.FirstOrDefault( m => m?.ShaderName == Shader && m.Attributes.GetBool( "SkyPaintProxy" ) );
		if ( _sky is null ) return;
		_sky.Set( "StarPosition", 0f );
		BindPaint( Paint );
		StarsChanged( null, StarTexture );
	}

	protected override void OnDisabled()
	{
		if ( _sky is null ) return;
		_sky = null;
		if ( !Scene.IsDestroyed )
			Scene.GetAllComponents<SkyPaint>().FirstOrDefault( p => p != this && p.Active && !p.GameObject.IsDestroyed )?.Activate();
	}

	void PaintChanged( Settings before, Settings after )
	{
		if ( _sky is not null ) BindPaint( after );
	}

	void BindPaint( Settings settings )
	{
		// The three star controls share a networked angle in the painted-sky entity.
		static float AngleValue( float value ) => ((int)(value * (65536f / 360)) & 65535) * (360f / 65536);
		var visible = settings with { StarScale = AngleValue( settings.StarScale ), StarFade = AngleValue( settings.StarFade ), StarSpeed = AngleValue( settings.StarSpeed ) };
		visible.Bind( _sky );
	}

	void StarsChanged( string before, string after )
	{
		if ( _sky is not null && Sandbox.Mounting.Directory.Get( "gmod" ) is Mount mount )
			_sky.Set( "g_tStars", mount.LoadStars( after, ContentRoot ) );
	}

	Doo.Block IMapInput.BuildInput( string input, string value, bool hasUser )
	{
		if ( !input.StartsWith( "Set", StringComparison.OrdinalIgnoreCase ) ) return null;
		var field = input[3..].ToLowerInvariant();
		if ( field == "sunnormal" || (field != "startexture" && TypeLibrary.GetType<Settings>().GetProperty( input[3..] ) is null) ) return null;
		return new Doo.InvokeBlock
		{
			InvokeType = Doo.InvokeType.Member,
			TargetComponent = new Doo.TargetComponent { Type = Doo.TargetComponent.TargetType.Direct, ComponentValue = this },
			Member = $"{typeof( SkyPaint ).FullName}.{nameof( SetValue )}",
			Arguments = [new Doo.LiteralExpression { LiteralValue = field }, new Doo.LiteralExpression { LiteralValue = value }]
		};
	}

	public void SetValue( string field, string value )
	{
		if ( !Networking.IsHost ) return;
		if ( field == "startexture" ) StarTexture = value;
		else Paint = Paint.Read( field, new VmtParameters( new Dictionary<string, string> { [field] = value }, GameObject.Name ), field, input: true );
	}
}
