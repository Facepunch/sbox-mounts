using System;

namespace Source1;

/// <summary>
/// HL2's code-driven firearm presentation rebuilt from mounted sprites and models: muzzle flashes
/// from c_te_legacytempents' MuzzleFlash_*_Player and fx.cpp's FX_MuzzleEffectAttached, chosen by
/// the type the anim event carries, Counter-Strike's from fx_cs_muzzleflash.cpp, chosen by the
/// script's MuzzleFlashStyle, and the shell models CTempEnts::EjectBrass throws.
/// </summary>
static class SourceWeaponEffects
{
	static readonly string[] FlashSprites = ["effects/muzzleflash1", "effects/muzzleflash2", "effects/muzzleflash3", "effects/muzzleflash4"];
	static readonly string[] CombineSprites = ["effects/combinemuzzle1", "effects/combinemuzzle2"];
	// CL_EVENT_EJECTBRASS1's shell types: 0 pistol, 1 rifle, 2 shotgun.
	static readonly string[] Shells = ["models/weapons/shell.mdl", "models/weapons/rifleshell.mdl", "models/weapons/shotgun_shell.mdl"];

	static string Pick( string[] names ) => names[Game.Random.Int( 0, names.Length - 1 )];

	/// <summary>The flash type of a muzzle event's options, such as "SMG1 muzzle" or "357 muzzle".</summary>
	public static string FlashType( string options ) => options?.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).FirstOrDefault()?.ToUpperInvariant();

	/// <summary>The flash type authored on a model's own muzzle events.</summary>
	public static string FlashType( Model model ) => FlashType( model?.GetData<SourceModelSequenceData[]>() ?? [] );

	public static string FlashType( SourceModelSequenceData[] sequences ) => sequences.SelectMany( s => s.Events )
		.Where( e => e.Name is "AE_MUZZLEFLASH" or "AE_NPC_MUZZLEFLASH" ).Select( e => FlashType( e.Options ) ).FirstOrDefault( x => x is not null );

	/// <summary>
	/// A Counter-Strike AE_CLIENT_EFFECT_ATTACH brass event, "EjectBrass_556 2 150": the shell model,
	/// the attachment it leaves from and its speed. Null for any other client effect.
	/// </summary>
	public static (string Model, string Attachment, float Speed)? Brass( string options )
	{
		var parts = options?.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) ?? [];
		const string prefix = "EjectBrass";
		if ( parts.Length < 2 || parts[0].Length <= prefix.Length + 1 || !parts[0].StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) ) return null;
		var speed = parts.Length > 2 && float.TryParse( parts[2], out var value ) ? value : 100;
		return ($"models/shells/shell_{parts[0][(prefix.Length + 1)..].ToLowerInvariant()}.mdl", parts[1], speed);
	}

	/// <summary>
	/// Viewmodel flashes are parented to the viewmodel attachment, as Source draws them with the
	/// viewmodel; world flashes use FX_MuzzleEffectAttached at the NPC scale for the type, or at
	/// three quarters of a Counter-Strike script's scale (C_CSPlayer::ProcessMuzzleFlashEvent).
	/// </summary>
	public static void MuzzleFlash( string type, GameObject attachment, bool firstPerson, string mount, float scale = 1 )
	{
		if ( Application.IsDedicatedServer || !attachment.IsValid() || type == CssRules.FlashNone ) return;
		var root = new GameObject( attachment, false, "HL2 muzzle flash" );
		root.NetworkMode = NetworkMode.Never;
		root.LocalTransform = global::Transform.Zero;
		var burst = root.AddComponent<SourceSpriteBurst>();
		if ( firstPerson && CssRules.IsFlash( type ) ) CounterStrikePlayerFlash( type, burst, mount, scale );
		else if ( firstPerson ) PlayerFlash( type, burst, mount );
		else WorldFlash( type, burst, mount, CssRules.IsFlash( type ) ? scale * 0.75f : WorldScale( type ) );
		root.Enabled = true;
		if ( CssRules.IsFlash( type ) ) MuzzleLight( attachment );
	}

	/// <summary>
	/// CS_MuzzleFlashCallback's three stacked sprites/muzzleflash4 (which draws effects/muzzleflash4) or
	/// CS_MuzzleFlashXCallback's single effects/muzzleflashX, at the muzzle for 0.08 seconds, fading and shrinking.
	/// </summary>
	static void CounterStrikePlayerFlash( string type, SourceSpriteBurst burst, string mount, float scale )
	{
		if ( type == CssRules.FlashX )
		{
			var size = 6 * scale * Game.Random.Float( 0.9f, 1.1f );
			var x = burst.Add( "effects/muzzleflashx", mount, 0, size, Color.White * (130 / 255f), 0.08f, Game.Random.Float( -0.25f, 0.25f ).RadianToDegree() );
			burst.FadeTo( x, size * 0.8f, Color.White * (80 / 255f) );
			return;
		}
		for ( var i = 0; i < 3; i++ )
		{
			var size = (3 + 3 * i) * scale;
			var sprite = burst.Add( "effects/muzzleflash4", mount, 0, size, Color.White * (80 / 255f), 0.08f, ((float)Game.Random.Int( 0, 3 )).RadianToDegree() );
			burst.FadeTo( sprite, size * 0.8f, Color.White * (30 / 255f) );
		}
	}

	/// <summary>Counter-Strike's muzzle dlight: 255 192 64 at exponent 5, radius 70, for 0.05 seconds.</summary>
	static void MuzzleLight( GameObject attachment )
	{
		var go = new GameObject( true, "Muzzle flash light" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = attachment.WorldPosition;
		var light = go.AddComponent<PointLight>();
		light.Radius = 70;
		light.LightColor = new Color( 1, 192 / 255f, 64 / 255f ) * 4;
		light.Shadows = false;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0.05f;
	}

	static void PlayerFlash( string type, SourceSpriteBurst burst, string mount )
	{
		var blue = Game.Random.Int( 200, 255 ) / 255f;
		var color = new Color( 1, 1, blue );
		if ( type is "COMBINE" or "AR2" )
		{
			var combineScale = Game.Random.Float( 2, 2.25f );
			for ( var i = 1; i < 6; i++ )
				burst.Add( Pick( CombineSprites ), mount, i * 8 * combineScale, Game.Random.Float( 6, 8 ) * (12 - i) / 12 * combineScale, color, 0.025f );
			burst.Add( Pick( CombineSprites ), mount, 0, Game.Random.Float( 10, 16 ), Color.White * (Game.Random.Int( 64, 128 ) / 255f), 0.025f );
			return;
		}
		var pistol = type == "PISTOL";
		var scale = pistol ? Game.Random.Float( 1, 1.25f ) : Game.Random.Float( 1.25f, 1.5f );
		var spacing = pistol ? 4 : 8;
		var lifetime = type switch
		{
			"SHOTGUN" => 0.0001f,
			"PISTOL" or "357" => 0.01f,
			_ => 0.025f
		};
		for ( var i = 1; i < 6; i++ )
			burst.Add( Pick( FlashSprites ), mount, i * spacing * scale, Game.Random.Float( 6, 8 ) * (8 - i) / 6 * scale, color, lifetime );
	}

	static float WorldScale( string type ) => type switch
	{
		"PISTOL" => 0.5f,
		"SHOTGUN" => 0.75f,
		"RPG" => 1.5f,
		_ => 1f
	};

	static void WorldFlash( string type, SourceSpriteBurst burst, string mount, float scale )
	{
		// MuzzleFlash_Combine_NPC is a starburst; this keeps its sprites on the shared FX_MuzzleEffect layout.
		var sprites = type is "COMBINE" or "AR2" ? CombineSprites : FlashSprites;
		var size = Math.Clamp( Game.Random.Float( scale - 0.25f, scale + 0.25f ), 0.5f, 8 );
		for ( var i = 1; i < 9; i++ )
			burst.Add( Pick( sprites ), mount, i * 2 * scale, Game.Random.Float( 6, 9 ) * (12 - i) / 9 * size, Color.White, 0.1f );
	}

	/// <summary>
	/// The 357 and shotgun fire with a tracer frequency of 0; everything else draws one
	/// every second shot (CBaseCombatWeapon::PrimaryAttack and the NPC FireBullets calls).
	/// </summary>
	public static bool HasTracer( string type, int shot ) => type is not ("357" or "SHOTGUN") && shot % 2 == 0;

	/// <summary>
	/// The shooter's own tracer, FX_PlayerTracer or FX_PlayerAR2Tracer: a static streak for one
	/// frame starting beside the eye (ComputeTracerStartPosition plus the client's 4 units right).
	/// </summary>
	public static void PlayerTracer( string type, Transform eye, Vector3 end, string mount )
	{
		var start = eye.Position + eye.Rotation.Forward * 16 + eye.Rotation.Right * 6 + Vector3.Down * 4.5f;
		var direction = (end - start).Normal;
		var distance = start.Distance( end );
		// CFXStaticLine's scale is the full width.
		if ( type is "COMBINE" or "AR2" )
		{
			if ( distance < 128 ) return;
			var length = MathF.Min( distance, Game.Random.Float( 256, 1024 ) );
			SourceTracer.Create( "effects/gunshiptracer", start + direction * Game.Random.Float( 0, 8 ), direction, length, length, Game.Random.Float( 6, 12 ), 0, mount );
			return;
		}
		if ( distance < 256 ) return;
		var streak = distance * Game.Random.Float( 0.1f, 0.6f );
		SourceTracer.Create( "effects/spark", start + direction * (8 + Game.Random.Float( -24, 64 )), direction, streak, streak, Game.Random.Float( 0.5f, 0.75f ), 0, mount );
	}

	/// <summary>
	/// Everyone else's tracer, FX_Tracer or FX_AR2Tracer: a streak flying along the shot
	/// (FX_AddDiscreetLine, whose scale is a half-width).
	/// </summary>
	public static void Tracer( string type, Vector3 start, Vector3 end, string mount )
	{
		var direction = (end - start).Normal;
		var distance = start.Distance( end );
		if ( type is "COMBINE" or "AR2" )
		{
			if ( distance >= 128 )
				SourceTracer.Create( "effects/gunshiptracer", start, direction, distance, Game.Random.Float( 128, 256 ), Game.Random.Float( 0.5f, 1.5f ) * 2, 8000, mount );
			return;
		}
		if ( distance >= 256 )
			SourceTracer.Create( "effects/spark", start, direction, distance, Game.Random.Float( 64, 128 ), Game.Random.Float( 0.75f, 0.9f ) * 2, 5000, mount );
	}

	/// <summary>
	/// A mounted impact PCF for the hit surface (the effect's Z axis is the surface normal), with the
	/// surface's sound and decal. False when the mount has none, so the caller keeps sandbox's impact.
	/// </summary>
	public static bool Impact( in BaseCombatWeapon.ShotEffect shot, string mount )
	{
		if ( Application.IsDedicatedServer || !shot.Hit || !shot.Surface.IsValid() ) return false;
		var particle = ImpactParticle( shot.Surface );
		if ( particle is null || !SourceMountedEffects.Prefab( particle, mount ).IsValid() ) return false;
		var surface = shot.Surface;
		var baseSurface = surface.GetBaseSurface();
		var sound = surface.SoundCollection.Bullet ?? baseSurface?.SoundCollection.Bullet;
		if ( sound.IsValid() ) Sound.Play( sound, shot.HitPosition );
		var decal = surface.PrefabCollection.BulletImpactDecal ?? baseSurface?.PrefabCollection.BulletImpactDecal;
		if ( decal.IsValid() && shot.HitObject.IsValid() )
		{
			var mark = decal.Clone( new CloneConfig { Transform = new Transform( shot.HitPosition, Rotation.LookAt( -shot.Normal, Vector3.Random ) ), StartEnabled = true } );
			mark.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			mark.SetParent( shot.HitObject, true );
		}
		SourceMountedEffects.Spawn( particle, new Transform( shot.HitPosition, Rotation.LookAt( shot.Normal ) * Rotation.FromPitch( 90 ) ), mount );
		return true;
	}

	/// <summary>Source surfaceprops by name: alien, antlion and zombie flesh bleed yellow, other flesh red.</summary>
	static string ImpactParticle( Surface surface )
	{
		var name = surface.ResourceName ?? "";
		if ( name.Contains( "alienflesh" ) || name.Contains( "antlion" ) || name.Contains( "zombieflesh" ) ) return "blood_impact_yellow_01";
		if ( name.Contains( "flesh" ) ) return "blood_impact_red_01";
		return surface.AudioSurface == AudioSurface.Metal ? "impact_metal" : null;
	}

	/// <summary>The .357 tips all six casings out at the reload animation's weapon event.</summary>
	public static void EjectCylinder( Vector3 center, Rotation facing, string mount )
	{
		for ( var i = 0; i < 6; i++ )
		{
			var offset = new Vector3( Game.Random.Float( -4, 4 ), Game.Random.Float( -4, 4 ), Game.Random.Float( -4, 4 ) );
			EjectBrass( Shells[0], center + offset, Vector3.Down, default, default, null, facing, default, mount );
		}
	}

	/// <summary>The shell model of an HL2 CL_EVENT_EJECTBRASS1 type.</summary>
	public static string ShellModel( int type ) => Shells[Math.Clamp( type, 0, Shells.Length - 1 )];

	/// <summary>
	/// A spent shell thrown from an ejection port whose axes are given. With an authored speed this is
	/// Counter-Strike's CTempEnts::CSEjectBrass: the port's forward at 1.2-2.8 times the event's speed,
	/// a little up and right scatter, plus the shooter's velocity, slowly tumbling for ten seconds.
	/// Without one it is HL2's CTempEnts::EjectBrass: 150-200 along forward with world scatter, spinning
	/// fast for one to two seconds. Both face the shooter's view, fall at full gravity, bounce at half
	/// speed and fade out over two seconds.
	/// </summary>
	public static void EjectBrass( string model, Vector3 position, Vector3 forward, Vector3 right, Vector3 up, float? speed, Rotation facing, Vector3 inherited, string mount )
	{
		if ( Application.IsDedicatedServer || string.IsNullOrWhiteSpace( model ) ) return;
		var path = $"mount://{mount}/{model}.vmdl";
		if ( Sandbox.Mounting.Directory.Get( mount )?.GetByPath( path ) is null ) return;
		var mesh = Model.Load( path );
		if ( !mesh.IsValid() || mesh.IsError ) return;
		Vector3 velocity;
		Angles spin;
		string bounce;
		float lifetime;
		if ( speed is { } authored )
		{
			velocity = forward * authored * Game.Random.Float( 1.2f, 2.8f ) + up * Game.Random.Float( -10, 10 ) + right * Game.Random.Float( -20, 20 ) + inherited;
			spin = new Angles( Game.Random.Float( -256, 256 ), Game.Random.Float( -256, 256 ), 0 );
			lifetime = 10;
			bounce = model.Contains( "12gauge", StringComparison.OrdinalIgnoreCase ) ? "Bounce.ShotgunShell"
				: model.Contains( "9mm", StringComparison.OrdinalIgnoreCase ) || model.Contains( "_57", StringComparison.OrdinalIgnoreCase ) ? "Bounce.PistolShell"
				: "Bounce.RifleShell";
		}
		else
		{
			velocity = forward * Game.Random.Float( 150, 200 ) + new Vector3( Game.Random.Float( -64, 64 ), Game.Random.Float( -64, 64 ), Game.Random.Float( 0, 64 ) );
			spin = new Angles( Game.Random.Float( -1024, 1024 ), Game.Random.Float( -1024, 1024 ), Game.Random.Float( -1024, 1024 ) );
			lifetime = Game.Random.Float( 1, 2 );
			bounce = model == Shells[2] ? "Bounce.ShotgunShell" : "Bounce.Shell";
		}
		var go = new GameObject( false, "HL2 shell" );
		go.NetworkMode = NetworkMode.Never;
		go.Tags.Add( "debris" );
		go.WorldTransform = new Transform( position, facing );
		go.AddComponent<ModelRenderer>().Model = mesh;
		var collider = go.AddComponent<BoxCollider>();
		collider.Center = mesh.Bounds.Center;
		collider.Scale = Vector3.Max( mesh.Bounds.Size, 0.5f );
		// Tempents keep half their speed on each bounce.
		collider.Elasticity = 0.5f;
		var body = go.AddComponent<Rigidbody>();
		var shell = go.AddComponent<SourceShell>();
		shell.Lifetime = lifetime;
		shell.BounceSound = SourceSounds.Sound( bounce, mount );
		shell.Casing = speed is null;
		go.Enabled = true;
		body.Velocity = velocity;
		// Tempent spin is pitch, yaw and roll in degrees per second.
		body.AngularVelocity = (facing.Right * spin.pitch + facing.Up * spin.yaw + facing.Forward * spin.roll).Normal * spin.AsVector3().Length.DegreeToRadian();
	}

	/// <summary>Approximation: trail particles fall and slow down; these streaks fly straight for at most this long.</summary>
	const float SparkFlight = 0.25f;

	/// <summary>FX_SparkFan: 35 metal scrape streaks, faster the narrower they spread.</summary>
	public static void SparkFan( Vector3 position, Vector3 normal, string mount )
	{
		if ( Application.IsDedicatedServer )
			return;
		var origin = position + normal;
		for ( var i = 0; i < 35; i++ )
		{
			var spread = Game.Random.Float( 0, 2 );
			var offset = 0.3f * spread;
			var direction = normal + new Vector3( Game.Random.Float( -offset, offset ), Game.Random.Float( -offset, offset ), Game.Random.Float( -offset, offset ) );
			var speed = Game.Random.Float( 128 * (2 - spread), 512 * (2 - spread) );
			Streak( origin, direction.Normal, speed, Game.Random.Float( 0.015f, 0.06f ), Game.Random.Float( 2, 5 ), mount, 2 );
		}
	}

	/// <summary>FX_ElectricSpark with a trail length of 1, as g_pEffects->Sparks draws it: big and little streaks and a yellow flare.</summary>
	public static void ElectricSpark( Vector3 position, int magnitude, Vector3 direction, string mount )
	{
		if ( Application.IsDedicatedServer )
			return;
		var big = (int)(magnitude * magnitude * Game.Random.Float( 2, 4 ));
		for ( var i = 0; i < big; i++ )
		{
			var dir = new Vector3( Game.Random.Float( -1, 1 ), Game.Random.Float( -1, 1 ), Game.Random.Float( 0.5f, 1 ) ) + 2 * direction;
			Streak( position, dir.Normal, Game.Random.Float( 64, 300 ), Game.Random.Float( 0.02f, 0.05f ), Game.Random.Float( 2, 5 ), mount, magnitude * Game.Random.Float( 1, 2 ) );
		}

		var little = magnitude * Game.Random.Int( 16, 32 );
		for ( var i = 0; i < little; i++ )
		{
			var dir = new Vector3( Game.Random.Float( -1, 1 ), Game.Random.Float( -1, 1 ), Game.Random.Float( -1, 1 ) ) + direction;
			Streak( position, dir.Normal, Game.Random.Float( 128, 256 ), Game.Random.Float( 0.02f, 0.03f ), Game.Random.Float( 2, 4 ), mount, magnitude * Game.Random.Float( 0.1f, 0.2f ) );
		}

		var go = new GameObject( true, "Spark" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;
		var burst = go.AddComponent<SourceSpriteBurst>();
		// effects/yellowflare_noz draws the effects/yellowflare texture.
		var flare = burst.Add( "effects/yellowflare", mount, 0, magnitude * Game.Random.Int( 4, 8 ), Color.White, 0.2f );
		burst.FadeTo( flare, 0, Color.White );
	}

	/// <summary>
	/// c_weapon_stunstick's StunstickImpact, 4 units off the hit: a sprites/light_glow02_add quad of 16 to 32 that doubles and fades
	/// out over 0.1 s, and FX_Sparks at magnitude 1. Approximation: FX_Sparks is drawn as <see cref="ElectricSpark"/>.
	/// </summary>
	public static void StunstickImpact( Vector3 position, Vector3 normal, string mount )
	{
		if ( Application.IsDedicatedServer )
			return;
		var origin = position + normal * 4;
		var go = new GameObject( true, "Stunstick impact" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = origin;
		var size = Game.Random.Float( 16, 32 );
		var burst = go.AddComponent<SourceSpriteBurst>();
		// sprites/light_glow02_add draws the sprites/light_glow02 texture.
		var glow = burst.Add( "sprites/light_glow02", mount, 0, size, Color.White, 0.1f );
		burst.FadeTo( glow, size * 2, Color.Black );
		ElectricSpark( origin, 1, normal, mount );
	}

	/// <summary>One trail particle as a streak as long as it flies in <paramref name="trail"/> seconds, living at most <see cref="SparkFlight"/>.</summary>
	static void Streak( Vector3 position, Vector3 direction, float speed, float trail, float width, string mount, float life )
	{
		var flight = MathF.Min( life, SparkFlight );
		SourceTracer.Create( "effects/spark", position, direction, speed * flight, speed * trail, width, speed, mount );
	}
}

/// <summary>A tempent shell: plays its bounce sound on some impacts, then fades out over two seconds once its life ends.</summary>
[Title( "Source Shell" )]
[Category( "Source 1" )]
public sealed class SourceShell : Component, Component.ICollisionListener
{
	const float FadeTime = 2;
	public float Lifetime { get; set; } = 1;
	public SoundEvent BounceSound { get; set; }
	/// <summary>HL2's casings sound on every hard bounce and one in four soft ones; Counter-Strike's shells on one in six.</summary>
	public bool Casing { get; set; }
	TimeSince _age;

	protected override void OnStart() => _age = 0;

	public void OnCollisionStart( Collision collision )
	{
		if ( BounceSound is null ) return;
		var play = Casing ? MathF.Abs( collision.Contact.Speed.z ) >= 200 || Game.Random.Int( 0, 3 ) == 0 : Game.Random.Int( 0, 5 ) == 0;
		if ( play ) Sound.Play( BounceSound, WorldPosition );
	}

	protected override void OnUpdate()
	{
		var fade = _age - Lifetime;
		if ( fade <= 0 ) return;
		if ( fade >= FadeTime )
		{
			GameObject.Destroy();
			return;
		}
		if ( GetComponent<ModelRenderer>() is { } renderer ) renderer.Tint = renderer.Tint.WithAlpha( 1 - fade / FadeTime );
	}
}

/// <summary>Short-lived additive sprites placed along the parent's forward axis, like Source's simple particles.</summary>
[Title( "Source Sprite Burst" )]
[Category( "Source 1" )]
public sealed class SourceSpriteBurst : Component
{
	readonly List<(SpriteRenderer Sprite, Vector2 Size, Color Color, Vector2 EndSize, Color EndColor)> _fades = new();
	float _lifetime;
	int _frames;
	TimeSince _age;

	/// <summary>Source particle sizes are radii, so the drawn sprite is twice as wide. Roll is in degrees, random when unset.</summary>
	public SpriteRenderer Add( string sprite, string mount, float offset, float size, Color color, float lifetime, float? roll = null )
	{
		var renderer = SourceMountedEffects.AddSprite( GameObject, sprite, mount );
		if ( !renderer.IsValid() ) return null;
		renderer.GameObject.LocalPosition = new Vector3( offset, 0, 0 );
		renderer.GameObject.LocalRotation = Rotation.FromRoll( roll ?? Game.Random.Int( 0, 360 ) );
		renderer.Size = size * 2;
		renderer.Color = color.WithAlpha( 1 );
		_lifetime = MathF.Max( _lifetime, lifetime );
		return renderer;
	}

	/// <summary>Blends a sprite to this radius and color over the burst's lifetime, like a particle's end size and alpha.</summary>
	public void FadeTo( SpriteRenderer sprite, float size, Color color )
	{
		if ( sprite.IsValid() ) _fades.Add( (sprite, sprite.Size, sprite.Color, new Vector2( size * 2 ), color.WithAlpha( 1 )) );
	}

	protected override void OnStart() => _age = 0;

	protected override void OnUpdate()
	{
		// Source flashes can live for a single frame; always show at least one.
		if ( ++_frames > 1 && _age > _lifetime )
		{
			GameObject.Destroy();
			return;
		}
		var t = _lifetime > 0 ? Math.Clamp( _age / _lifetime, 0, 1 ) : 1;
		foreach ( var fade in _fades )
		{
			if ( !fade.Sprite.IsValid() ) continue;
			fade.Sprite.Size = Vector2.Lerp( fade.Size, fade.EndSize, t );
			fade.Sprite.Color = Color.Lerp( fade.Color, fade.EndColor, t );
		}
	}
}

/// <summary>
/// A Source tracer line: CFXStaticLine (no speed, shown for a single frame) or CFXDiscreetLine,
/// whose head flies at the speed with the tail trailing by the length, both clipped to the hit.
/// </summary>
[Title( "Source Tracer" )]
[Category( "Source 1" )]
public sealed class SourceTracer : Component
{
	LineRenderer _line;
	Vector3 _start;
	Vector3 _direction;
	float _distance;
	float _length;
	float _width;
	float _speed;
	TimeSince _age;
	int _frames;

	public static void Create( string material, Vector3 start, Vector3 direction, float distance, float length, float width, float speed, string mount )
	{
		if ( Application.IsDedicatedServer ) return;
		var go = new GameObject( false, "HL2 tracer" );
		go.NetworkMode = NetworkMode.Never;
		var line = SourceMountedEffects.AddBeam( go, material, mount );
		if ( !line.IsValid() )
		{
			go.Destroy();
			return;
		}
		var tracer = go.AddComponent<SourceTracer>();
		tracer._line = line;
		tracer._start = start;
		tracer._direction = direction;
		tracer._distance = distance;
		tracer._length = length;
		tracer._width = width;
		tracer._speed = speed;
		tracer.Draw();
		go.Enabled = true;
	}

	protected override void OnStart() => _age = 0;

	protected override void OnUpdate()
	{
		_frames++;
		var done = _speed <= 0 ? _frames > 1 && _age > 0.01f : _speed * _age - _length >= _distance;
		if ( done )
		{
			GameObject.Destroy();
			return;
		}
		Draw();
	}

	void Draw()
	{
		var head = _speed <= 0 ? _length : MathF.Min( _speed * _age, _distance );
		var tail = _speed <= 0 ? 0 : Math.Clamp( _speed * _age - _length, 0, _distance );
		SourceMountedEffects.SetBeam( _line, _start + _direction * tail, _start + _direction * head, head > tail ? _width : 0, 255 );
	}
}
