using System;

namespace Source1;

/// <summary>
/// Builds muzzle flashes and ejected shells from mounted sprites and models. HL2 animation events
/// select the flash type. Counter-Strike weapon scripts specify the muzzle flash style.
/// </summary>
static class WeaponEffects
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

	public static string FlashType( IEnumerable<SourceModelSequenceData> sequences ) => sequences.SelectMany( s => s.Events )
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
	/// Handgun and shotgun particles are emitted in world space. World flashes use the NPC scale for the type
	/// or three quarters of the Counter-Strike weapon script's scale.
	/// </summary>
	public static void MuzzleFlash( string type, GameObject attachment, bool firstPerson, string mount, float scale = 1 )
	{
		if ( Application.IsDedicatedServer || !attachment.IsValid() || type == Css.Rules.FlashNone ) return;
		if ( !firstPerson && type is ("AR2" or "COMBINE") )
		{
			CombineWorldFlash( attachment, mount );
			return;
		}
		var worldSpace = firstPerson && type is ("PISTOL" or "357" or "SHOTGUN");
		var root = new GameObject( worldSpace ? null : attachment, false, "HL2 muzzle flash" );
		root.NetworkMode = NetworkMode.Never;
		root.WorldTransform = attachment.WorldTransform;
		var burst = root.AddComponent<SpriteBurst>();
		burst.DrawBeforeViewModel = firstPerson && !Css.Rules.IsFlash( type );
		burst.CameraFade = new Vector2( 16, 64 );
		if ( firstPerson && type is ("SMG1" or "AR2" or "COMBINE") )
			burst.UseViewModelLens( attachment.GetComponentInParent<ViewModel>() );
		if ( firstPerson && Css.Rules.IsFlash( type ) ) CounterStrikePlayerFlash( type, burst, mount, scale );
		else if ( firstPerson ) PlayerFlash( type, burst, mount );
		else WorldFlash( type, burst, mount, Css.Rules.IsFlash( type ) ? scale * 0.75f : WorldScale( type ) );
		root.Enabled = true;
		if ( Css.Rules.IsFlash( type ) ) MuzzleLight( attachment );
	}

	/// <summary>
	/// CS_MuzzleFlashCallback's three stacked sprites/muzzleflash4 (which draws effects/muzzleflash4) or
	/// CS_MuzzleFlashXCallback's single effects/muzzleflashX, at the muzzle for 0.08 seconds, fading and shrinking.
	/// </summary>
	static void CounterStrikePlayerFlash( string type, SpriteBurst burst, string mount, float scale )
	{
		if ( type == Css.Rules.FlashX )
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
		var go = new GameObject( attachment, true, "Muzzle flash light" );
		go.NetworkMode = NetworkMode.Never;
		go.LocalPosition = Vector3.Zero;
		var light = go.AddComponent<PointLight>();
		light.Radius = 70;
		light.LightColor = new Color( 1, 192 / 255f, 64 / 255f ) * 4;
		light.Shadows = false;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0.05f;
	}

	static void PlayerFlash( string type, SpriteBurst burst, string mount )
	{
		var blue = Game.Random.Int( 200, 255 ) / 255f;
		var color = new Color( 1, 1, blue );
		if ( type is "COMBINE" or "AR2" )
		{
			var combineScale = Game.Random.Float( 2, 2.25f );
			for ( var i = 1; i < 6; i++ )
				burst.Add( Pick( CombineSprites ) + "_noz", mount, i * 8 * combineScale, (int)(Game.Random.Float( 6, 8 ) * (12 - i) / 12 * combineScale), color, 0.025f );
			var radius = (int)Game.Random.Float( 10, 16 );
			var core = burst.Add( Pick( CombineSprites ) + "_noz", mount, 0, radius, Color.White, 0.025f, alpha: Game.Random.Int( 64, 128 ) / 255f );
			burst.FadeTo( core, radius, Color.White, 32 / 255f );
			return;
		}
		var pistol = type == "PISTOL";
		if ( type == "357" || (pistol && Game.Random.Int( 0, 3 ) != 0) )
			HandgunSmoke( burst.WorldPosition, burst.WorldRotation.Forward, mount, magnum: !pistol );
		var scale = pistol ? Game.Random.Float( 1, 1.25f ) : Game.Random.Float( 1.25f, 1.5f );
		var spacing = pistol ? 4 : 8;
		var lifetime = type switch
		{
			"SHOTGUN" => 0.0001f,
			"PISTOL" or "357" => 0.01f,
			_ => 0.025f
		};
		for ( var i = 1; i < 6; i++ )
			burst.Add( Pick( FlashSprites ) + "_noz", mount, i * spacing * scale, (int)(Game.Random.Float( 6, 8 ) * (8 - i) / 6 * scale), color, lifetime );
	}

	static void HandgunSmoke( Vector3 muzzle, Vector3 forward, string mount, bool magnum )
	{
		var smoke = Particles.Emitter( Game.ActiveScene, "particle/particle_smokegrenade", mount, false, false, 1 );
		if ( !smoke.IsValid() ) return;
		var lifetime = magnum ? Game.Random.Float( 0.5f, 1 ) : Game.Random.Float( 0.25f, 0.5f );
		smoke.WorldPosition = muzzle + forward * 8;
		smoke.Lifetime = lifetime;
		var renderer = smoke.GetComponent<ParticleSpriteRenderer>();
		renderer.CameraFadeNear = 16;
		renderer.CameraFadeFar = 64;
		smoke.ApplyShape = true;
		smoke.InitialScale = 2 * Game.Random.Int( 2, 4 );
		smoke.Scale = magnum ? MagnumSmokeGrowth : SmokeGrowth;
		smoke.ApplyAlpha = true;
		smoke.Alpha = SmokeFade;
		smoke.ApplyRotation = true;
		smoke.InitialRoll = new ParticleFloat( 0, 360 );
		var spin = magnum ? 0.5f : 0.1f;
		smoke.AngularVelocity = new Vector3( 0, 0, Game.Random.Float( -spin, spin ).RadianToDegree() );
		smoke.Invoke( lifetime, smoke.DestroyGameObject );
		if ( smoke.Emit( smoke.WorldPosition, 0 ) is not { } puff ) return;
		puff.Color = (Color.White * Game.Random.Float( 200 / 255f, 1 )).WithAlpha( Game.Random.Float( 64 / 255f, 128 / 255f ) );
		puff.Velocity = forward * Game.Random.Float( magnum ? 8 : 48, 64 ) + Vector3.Up * Game.Random.Float( 4, 16 );
	}

	static float WorldScale( string type ) => type switch
	{
		"PISTOL" or "357" => 0.5f,
		"SHOTGUN" => 0.75f,
		"RPG" => 1.5f,
		_ => 1f
	};

	static void WorldFlash( string type, SpriteBurst burst, string mount, float scale )
	{
		var size = Math.Clamp( Game.Random.Float( scale - 0.25f, scale + 0.25f ), 0.5f, 8 );
		var singleFrame = type is "PISTOL" or "357" or "SMG1";
		var lifetime = singleFrame ? 0.0001f : 0.1f;
		for ( var i = 1; i < 9; i++ )
		{
			var radius = (int)(Game.Random.Float( 6, 9 ) * (12 - i) / 9 * size);
			var sprite = burst.Add( Pick( FlashSprites ), mount, i * 2 * scale, radius, Color.White, lifetime );
			if ( !singleFrame ) burst.FadeTo( sprite, radius, Color.White, 128 / 255f );
		}
		if ( type == "SHOTGUN" ) ShotgunSparks( burst.WorldPosition, burst.WorldRotation.Forward, mount );
	}

	static void CombineWorldFlash( GameObject muzzle, string mount )
	{
		var scale = Game.Random.Float( 1, 1.5f );
		var first = Emitter( CombineSprites[0], 20, 0.2f );
		var second = Emitter( CombineSprites[1], 20, 0.2f );
		EmitRow( Vector3.Forward, true );
		EmitRow( Vector3.Left - Vector3.Up, false );
		EmitRow( Vector3.Right - Vector3.Up, false );
		EmitRow( Vector3.Up, false );

		var core = Emitter( "effects/strider_muzzle", 1, Game.Random.Float( 0.3f, 0.4f ) );
		if ( core.IsValid() )
		{
			core.InitialScale = 2 * (int)(scale * Game.Random.Float( 12, 16 ));
			core.Scale = SmokeFade;
			core.Emit( muzzle.WorldPosition, 0 );
		}

		ParticleEffect Emitter( string material, int count, float lifetime )
		{
			var effect = Particles.Emitter( muzzle, material, mount, true, false, count );
			if ( !effect.IsValid() ) return null;
			effect.LocalSpace = 1;
			effect.ApplyShape = true;
			effect.ApplyRotation = true;
			effect.InitialRoll = new ParticleFloat( 0, 360 );
			effect.ApplyAlpha = true;
			effect.Alpha = SmokeFade;
			effect.Lifetime = lifetime;
			var renderer = effect.GetComponent<ParticleSpriteRenderer>();
			renderer.CameraFadeNear = 16;
			renderer.CameraFadeFar = 64;
			effect.Invoke( lifetime, effect.DestroyGameObject );
			return effect;
		}

		void EmitRow( Vector3 axis, bool forward )
		{
			var direction = muzzle.WorldRotation * axis;
			var velocity = direction * Game.Random.Float( 50, 150 ) * (forward ? 1 : 0.25f);
			for ( var i = 1; i <= 5; i++ )
			{
				var emitter = Game.Random.Int( 0, 1 ) == 0 ? first : second;
				if ( !emitter.IsValid() ) continue;
				var radius = forward ? Game.Random.Float( 6, 8 ) * (7.5f - i) / 6 : Game.Random.Float( 2, 4 ) * (6 - i) / 3;
				var particle = emitter.Emit( muzzle.WorldPosition + direction * (i * scale * (forward ? 2 : 1)), 0 );
				particle.StartScale = 2 * (int)(scale * radius);
				particle.Size = particle.StartScale;
				particle.DeathTime = particle.BornTime + (forward ? 0.1f : 0.2f);
				particle.Velocity = velocity;
			}
		}
	}

	static void ShotgunSparks( Vector3 origin, Vector3 forward, string mount )
	{
		const string material = "effects/muzzleflash1";
		var count = Game.Random.Int( 4, 8 );
		var trails = SparkTrails( origin, mount, count, new( 0.1f, 0.2f ), new( 0.25f, 0.5f ), ShotgunTrailLength, 0, false, material: material );
		if ( trails.IsValid() )
		{
			trails.ApplyColor = true;
			trails.Gradient = ShotgunTrailColor;
			trails.ApplyAlpha = true;
			trails.Alpha = SmokeFade;
			for ( var i = 0; i < count; i++ )
			{
				if ( trails.Emit( origin, 0 ) is not { } p ) continue;
				var direction = (forward + Particles.InCube( 0.05f )).Normal;
				var falloff = (1 - MathF.Abs( forward.Dot( direction ) )) / 0.05f;
				p.Velocity = direction * (Game.Random.Float( 256, 1024 ) * (1 - falloff));
			}
		}

		var emberCount = Game.Random.Int( 0, 2 ) == 0 ? Game.Random.Int( 0, 4 ) : 0;
		if ( emberCount == 0 ) return;
		var embers = Particles.Emitter( Game.ActiveScene, material, mount, true, false, emberCount );
		if ( !embers.IsValid() ) return;
		embers.WorldPosition = origin;
		embers.Lifetime = new ParticleFloat( 0.2f, 0.4f );
		embers.ApplyShape = true;
		embers.InitialScale = 2;
		embers.Scale = SmokeFade;
		embers.ApplyColor = true;
		embers.Gradient = ShotgunEmberColor;
		embers.ApplyAlpha = true;
		embers.Alpha = SmokeFade;
		for ( var i = 0; i < emberCount; i++ )
			if ( embers.Emit( origin, 0 ) is { } p )
				p.Velocity = (forward + Particles.InCube( 0.05f )).Normal * Game.Random.Float( 64, 256 );
		embers.OnPreStep = dt =>
		{
			foreach ( var p in embers.Particles )
			{
				var direction = (p.Velocity.Normal + Particles.InCube( 0.125f )).Normal;
				p.Velocity = direction * (p.Velocity.Length - 12 * dt);
			}
		};
		embers.Invoke( 0.4f, embers.DestroyGameObject );
	}

	static readonly ParticleFloat ShotgunTrailLength = new()
	{
		Type = ParticleFloat.ValueType.Curve, Evaluation = ParticleFloat.EvaluationType.Life, CurveA = DropLength( 0.05f )
	};
	static readonly ParticleGradient ShotgunTrailColor = FadeColor( new Color32( 255, 242, 191 ) );
	static readonly ParticleGradient ShotgunEmberColor = FadeColor( new Color32( 255, 128, 64 ) );

	static ParticleGradient FadeColor( Color color ) => new()
	{
		Type = ParticleGradient.ValueType.Gradient, Evaluation = ParticleGradient.EvaluationType.Life,
		GradientA = Gradient.FromColors( color, Color.Black )
	};

	/// <summary>A multiplayer weapon tracer travels from the rendered muzzle to the pellet endpoint.</summary>
	public static void DrawTracer( Scene scene, string type, Vector3 start, Vector3 end, string mount )
	{
		var direction = (end - start).Normal;
		var distance = start.Distance( end );
		if ( type is "COMBINE" or "AR2" )
		{
			if ( distance < 128 ) return;
			Tracer.Create( "effects/gunshiptracer", start, direction, distance, Game.Random.Float( 128, 256 ), Game.Random.Float( 0.5f, 1.5f ) * 2, 8000, mount );
			PlayFlyby( scene, start, end, direction, mount );
			return;
		}
		if ( distance >= 256 )
			Tracer.Create( "effects/spark", start, direction, distance, Game.Random.Float( 64, 128 ), Game.Random.Float( 0.75f, 0.9f ) * 2, 5000, mount );
	}

	static TimeSince _lastFlyby = 1;

	static void PlayFlyby( Scene scene, Vector3 start, Vector3 end, Vector3 direction, string mount )
	{
		if ( _lastFlyby < 0.1f || !scene.Camera.IsValid() ) return;
		var listener = scene.Camera.WorldPosition;
		var along = direction.Dot( listener - start );
		if ( along <= 0 || new Line( start, end ).SqrDistance( listener ) >= 72 * 72 ) return;

		var incoming = Sounds.Sound( "Bullets.GunshipNearmiss.incoming", mount );
		var outgoing = Sounds.Sound( "Bullets.GunshipNearmiss.outgoing", mount );
		if ( incoming?.Sounds is not { Count: > 0 } waves || outgoing?.Sounds.Count != waves.Count ) return;

		_lastFlyby = 0;
		var index = Game.Random.Int( 0, waves.Count - 1 );
		var closest = start + direction * along;
		var pitch = float.Lerp( 1.5f, 0.25f, Math.Clamp( (listener.Distance( closest ) - 12) / 228, 0, 1 ) );
		var volume = incoming.Volume.GetValue();
		// Each channel carries one half of the pass. Keep both on the same take and timeline.
		PlayChannel( waves[index], closest - direction * 48 );
		PlayChannel( outgoing.Sounds[index], closest + direction * 48 );

		void PlayChannel( SoundFile file, Vector3 position )
		{
			var sound = Sound.PlayFile( file, volume, pitch );
			if ( !sound.IsValid() ) return;
			sound.Position = position;
			sound.Distance = incoming.Distance;
			sound.Falloff = incoming.Falloff;
			sound.FalloffMode = incoming.FalloffMode;
		}
	}

	/// <summary>
	/// Play the surface sound and place its decal alongside the Source impact effect.
	/// </summary>
	public static bool Impact( in BaseCombatWeapon.ShotEffect shot, Vector3 direction, string mount )
	{
		if ( Application.IsDedicatedServer || !shot.Hit || !shot.Surface.IsValid() ) return false;
		var surface = shot.Surface;
		var blood = BloodParticle( surface );
		if ( blood is null ? surface.AudioSurface != AudioSurface.Metal : !MountedEffects.Prefab( blood, mount ).IsValid() ) return false;
		var baseSurface = surface.GetBaseSurface();
		var sound = surface.SoundCollection.Bullet ?? baseSurface?.SoundCollection.Bullet;
		if ( sound.IsValid() ) Sound.Play( sound, shot.HitPosition );
		var decal = surface.PrefabCollection.BulletImpactDecal ?? baseSurface?.PrefabCollection.BulletImpactDecal;
		PlaceDecal( decal, shot.HitObject, shot.HitPosition, shot.Normal );
		if ( blood is not null )
			MountedEffects.Spawn( blood, new Transform( shot.HitPosition, Rotation.LookAt( shot.Normal ) * Rotation.FromPitch( 90 ) ), mount );
		else if ( !surface.HasTag( "grate" ) )
			MetalSparks( shot.HitPosition + shot.Normal, direction, shot.Normal, mount );
		return true;
	}

	static void MetalSparks( Vector3 origin, Vector3 incoming, Vector3 normal, string mount )
	{
		var reflected = incoming - normal * (2 * incoming.Dot( normal )) + Particles.InCube( 0.2f );
		var count = 2 * Game.Random.Int( 4, 8 );
		var burst = new GameObject( true, "Metal impact" );
		burst.NetworkMode = NetworkMode.Never;
		burst.WorldPosition = origin;
		burst.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0.1f;
		var effect = Particles.Emitter( burst, "effects/spark", mount, true, false, count );
		if ( effect.IsValid() )
		{
			effect.Lifetime = new ParticleFloat( 0.05f, 0.1f );
			effect.InitialScale = new ParticleFloat( 1, 4 );
			effect.Damping = 8;
			effect.Force = true;
			effect.ForceDirection = Vector3.Down * 400;
			effect.Collision = true;
			effect.Bounce = 0.25f;
			var renderer = effect.GetComponent<ParticleSpriteRenderer>();
			renderer.VelocityStretch = true;
			renderer.StretchTime = new ParticleFloat( 0.025f, 0.1f );
			for ( var i = 0; i < count; i++ )
			{
				var spread = Game.Random.Float( 0, 2 );
				var dir = (reflected + Particles.InCube( 0.5f * spread )).Normal;
				var spark = effect.Emit( origin, 0 );
				if ( spark is not null ) spark.Velocity = dir * Game.Random.Float( 128, 512 ) * (2 - spread);
			}
		}
		Hl2.FlatQuad.Create( "effects/yellowflare", origin, normal, Game.Random.Int( 24, 28 ), 0, 0.1f, mount, linear: true );
	}

	internal static Transform DecalTransform( GameObject target, Vector3 position, Vector3 normal )
		=> target.WorldTransform.ToLocal( new Transform( position, Rotation.LookAt( -normal, Vector3.Random ) ) );

	internal static void PlaceDecal( GameObject prefab, GameObject target, Vector3 position, Vector3 normal )
	{
		if ( target.IsValid() ) PlaceDecal( prefab, target, DecalTransform( target, position, normal ) );
	}

	internal static void PlaceDecal( GameObject prefab, GameObject target, Transform local )
	{
		if ( !prefab.IsValid() || !target.IsValid() ) return;
		var mark = prefab.Clone( local, target );
		mark.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
	}

	/// <summary>Source surfaceprops by name: alien, antlion and zombie flesh bleed yellow, other flesh red.</summary>
	static string BloodParticle( Surface surface )
	{
		var name = surface.ResourceName ?? "";
		if ( name.Contains( "alienflesh" ) || name.Contains( "antlion" ) || name.Contains( "zombieflesh" ) ) return "blood_impact_yellow_01";
		return name.Contains( "flesh" ) ? "blood_impact_red_01" : null;
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
	/// Counter-Strike shells follow the port's forward at 1.2-2.8 times the event's speed,
	/// a little up and right scatter, plus the shooter's velocity, slowly tumbling for ten seconds.
	/// Without a port, eject at 150-200 along forward with world scatter, spinning
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
		body.RigidbodyFlags |= RigidbodyFlags.DisableCollisionSounds;
		var shell = go.AddComponent<Shell>();
		shell.Lifetime = lifetime;
		shell.BounceSound = Sounds.Sound( bounce, mount );
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

	/// <summary>The bolt load event emits sparks and fades a blue flare on the viewmodel's first attachment.</summary>
	public static void CrossbowLoad( BaseWeaponModel model, string mount )
	{
		var renderer = model?.Renderer;
		if ( !renderer.IsValid() || !renderer.Model.IsValid() || renderer.Model.Attachments.All.Count == 0 ) return;
		var attachment = renderer.GetAttachmentObject( renderer.Model.Attachments.All[0] );
		if ( !attachment.IsValid() ) return;
		if ( model is ViewModel view ) attachment = view.EffectAnchor( attachment );
		ElectricSpark( attachment.WorldPosition, 1, Vector3.Zero, mount );
		const string material = "sprites/blueflare1";
		if ( MountedEffects.SpriteTexture( material, mount ) is not { } texture ) return;
		var root = new GameObject( attachment, true, "Crossbow load flare" );
		root.NetworkMode = NetworkMode.Never;
		var burst = root.AddComponent<SpriteBurst>();
		var radius = texture.Width * 0.1f; // A Source sprite scale of 0.2 is its full width.
		var flare = burst.Add( material, mount, 0, radius, Color.White * 0.5f, 0.25f, 0 );
		burst.FadeTo( flare, radius, Color.Black );
	}

	static ParticleEffect SparkTrails( Vector3 position, string mount, int count, Vector2 lifetime, ParticleFloat width, ParticleFloat trail, float gravity, bool collide, GameObject parent = null, string material = "effects/spark" )
	{
		var sparks = Particles.Emitter( parent ?? Game.ActiveScene, material, mount, true, false, count );
		if ( !sparks.IsValid() ) return null;
		sparks.GameObject.WorldPosition = position;
		sparks.Lifetime = new ParticleFloat( lifetime.x, lifetime.y );
		sparks.ApplyShape = true;
		sparks.InitialScale = width;
		sparks.Force = true;
		sparks.ForceSpace = ParticleEffect.SimulationSpace.World;
		sparks.ForceDirection = Vector3.Down * gravity;
		sparks.Collision = collide;
		sparks.Bounce = 0.3f;
		var renderer = sparks.GetComponent<ParticleSpriteRenderer>();
		renderer.VelocityStretch = true;
		renderer.StretchTime = trail;
		sparks.Invoke( lifetime.y, sparks.DestroyGameObject );
		return sparks;
	}

	internal static void WaterSplash( Vector3 position, string mount, bool slime )
	{
		if ( Sounds.Sound( "Physics.WaterSplash", mount ) is { } sound ) Sound.Play( sound, position );
		if ( slime )
		{
			MountedEffects.Spawn( "slime_splash_03", new Transform( position, Rotation.LookAt( Vector3.Up ) ), mount );
			return;
		}
		var surface = Game.ActiveScene.Trace.Ray( position + Vector3.Up * 8, position + Vector3.Down * 64 )
			.WithTag( "water" ).HitTriggersOnly().Run();
		if ( surface.Hit && !surface.StartedSolid )
			Hl2.FlatQuad.WaterRipple( surface.HitPosition, surface.Normal, mount );

		SplashDrops( position, mount, explosion: false );
		SplashPlume( position, mount, 1 );
	}

	internal static void ExplosionSplash( Vector3 surface, string mount )
	{
		Hl2.FlatQuad.ExplosionRipple( surface, mount );
		Hl2.WaterShaft.CreatePair( surface, mount );
		SplashDrops( surface, mount, explosion: true );
		SplashPlume( surface, mount, 2 );
	}

	internal static void DeepWaterCloud( Vector3 position, string mount, float magnitude )
	{
		var scene = Game.ActiveScene;
		var direction = Explosion.ForceDirection( scene, position, magnitude );
		var first = WaterCloudEmitter( scene, position, mount, "effects/splash1" );
		var second = WaterCloudEmitter( scene, position, mount, "effects/splash2" );
		for ( var i = 0; i < 16; i++ )
		{
			var emitter = Game.Random.Int( 0, 1 ) == 0 ? first : second;
			if ( emitter?.Emit( position + Particles.InCube( 32 ), 0 ) is not { } p ) continue;
			// Source clamps explosion force to 2, giving 0.7 spread and 2100 axial speed.
			var travel = (Particles.InCube( 0.7f ) + direction * Game.Random.Float( 1, 6 )).Normal;
			p.Velocity = travel * (2100 * MathF.Abs( Vector3.Dot( travel, direction ) ));
			p.Angles = new Angles( 0, 0, Game.Random.Int( 0, 360 ) );
			p.StartScale = 2 * Game.Random.Int( 32, 64 );
			p.Size = p.StartScale;
		}
	}

	static ParticleEffect WaterCloudEmitter( Scene scene, Vector3 position, string mount, string material )
	{
		var cloud = Particles.Emitter( scene, material, mount, false, true, 16 );
		if ( !cloud.IsValid() ) return null;
		cloud.WorldPosition = position;
		cloud.Lifetime = new ParticleFloat( 2, 3 );
		cloud.ApplyShape = true;
		cloud.Scale = PlumeGrowth;
		var renderer = cloud.GetComponent<ParticleSpriteRenderer>();
		renderer.CameraFadeNear = 64;
		renderer.CameraFadeFar = 128;
		var spinScale = 1f;
		cloud.OnPreStep = dt =>
		{
			var decay = MathF.Pow( 0.0001f, dt / 0.5f );
			foreach ( var p in cloud.Particles )
			{
				p.Velocity = p.Velocity.Normal * MathF.Max( 8, p.Velocity.Length * decay );
				var spin = (16 * p.Rand() - 8) * spinScale;
				p.Angles = p.Angles.WithRoll( p.Angles.roll + (MathF.CopySign( MathF.Max( 0.25f, MathF.Abs( spin ) ), spin ) * dt).RadianToDegree() );
			}
			spinScale *= 1 - 8 * dt;
		};
		cloud.OnPostStep = _ =>
		{
			foreach ( var p in cloud.Particles )
			{
				var alpha = 1 - p.LifeDelta;
				p.Alpha = alpha < 0.75f ? alpha * alpha : alpha;
			}
		};
		cloud.Invoke( 3, cloud.DestroyGameObject );
		return cloud;
	}

	static void SplashDrops( Vector3 position, string mount, bool explosion )
	{
		var count = explosion ? 36 : 16;
		var drops = Particles.Emitter( Game.ActiveScene, "effects/splash2", mount, false, true, count );
		if ( !drops.IsValid() ) return;
		drops.WorldPosition = position;
		var lifetime = explosion ? 1f : 0.5f;
		drops.Lifetime = new ParticleFloat( lifetime * 0.5f, lifetime );
		drops.ApplyShape = true;
		drops.InitialScale = new ParticleFloat( 1, 3 );
		drops.Force = true;
		drops.ForceDirection = Vector3.Down * 800;
		var renderer = drops.GetComponent<ParticleSpriteRenderer>();
		renderer.VelocityStretch = true;
		renderer.StretchTime = explosion ? ExplosionDropStretch : SplashDropStretch;
		for ( var i = 0; i < count; i++ )
		{
			var origin = position + Particles.InCube( explosion ? 32 : 8 ).WithZ( 0 );
			if ( drops.Emit( origin, 0 ) is not { } p ) continue;
			if ( explosion )
			{
				// Four faster drops rise in a narrower cone.
				var jet = i >= 32;
				var speed = jet ? 300 : 200;
				p.Velocity = (Vector3.Up + Particles.InCube( jet ? 0.2f : 1 )) * Game.Random.Float( speed, speed * 2 ) + Vector3.Up * Game.Random.Float( 64, 256 );
				p.StartScale = jet ? 4 : Math.Min( 4, p.StartScale * 2 );
				p.Size = p.StartScale;
			}
			else
			{
				p.Velocity = (Vector3.Up + Particles.InCube( 0.8f )) * Game.Random.Float( 150, 300 ) + Vector3.Up * Game.Random.Float( 32, 64 );
			}
		}
		drops.OnPostStep = dt =>
		{
			var attenuation = Math.Max( 0, 1 - 2 * dt );
			foreach ( var drop in drops.Particles ) drop.Velocity *= attenuation;
		};
		drops.Invoke( lifetime, drops.DestroyGameObject );
	}

	static void SplashPlume( Vector3 position, string mount, int scale )
	{
		var count = 8 * scale;
		var plume = Particles.Emitter( Game.ActiveScene, "effects/splash2", mount, false, true, count );
		if ( !plume.IsValid() ) return;
		plume.WorldPosition = position;
		plume.Lifetime = 2;
		plume.Force = true;
		plume.ForceDirection = Vector3.Down * 800;
		plume.ApplyShape = true;
		plume.Scale = PlumeGrowth;
		for ( var i = 0; i < count; i++ )
		{
			if ( plume.Emit( position, 0 ) is not { } p ) continue;
			var fraction = Math.Min( i / 7f, 1 );
			p.StartScale = 2 * (int)(scale * float.Lerp( 12, 24, fraction ));
			p.Size = p.StartScale;
			p.Angles = new Angles( 0, 0, Game.Random.Int( 0, 360 ) );
			p.Color = Color.White.WithAlpha( (int)float.Lerp( 32, 255, fraction ) / 255f );
			var direction = Particles.InCube( 0.2f ) + Vector3.Up * Game.Random.Float( 4, 6 );
			// The extra explosion particles start at rest or travel down through the waterline.
			p.Velocity = direction.Normal * (50 * scale * (8 - i));
		}

		plume.OnPreStep = dt =>
		{
			var decay = MathF.Pow( 0.0001f, dt / 3 );
			foreach ( var p in plume.Particles )
			{
				p.Velocity *= decay;
				var spin = 8 * p.Rand() - 4;
				var speed = MathF.Max( 0.5f, MathF.Abs( spin ) * MathF.Exp( -4 * p.Age ) );
				p.Angles = p.Angles.WithRoll( p.Angles.roll + (MathF.CopySign( speed, spin ) * dt).RadianToDegree() );
			}
		};
		plume.OnPostStep = _ =>
		{
			foreach ( var p in plume.Particles )
				p.Alpha = Math.Clamp( 1 + (p.Position.z - position.z) * 4 / p.Size.x, 0, 1 );
		};
		plume.Invoke( 2, plume.DestroyGameObject );
	}

	public static void GaussImpact( Vector3 position, Vector3 direction, string mount )
	{
		if ( Application.IsDedicatedServer ) return;
		var count = Game.Random.Int( 16, 32 );
		var sparks = SparkTrails( position, mount, count, new( 0.25f, 1 ), new( 1, 4 ), new( 0.01f, 0.1f ), 800, true );
		if ( !sparks.IsValid() ) return;
		var trailDirection = direction;
		for ( var i = 0; i < count; i++ )
		{
			trailDirection = (direction + Particles.InCube( 0.6f )).Normal;
			if ( sparks.Emit( position, 0 ) is { } particle )
				particle.Velocity = trailDirection * Game.Random.Float( 128, 512 );
		}
		ElectricSpark( position, 1, trailDirection, mount );
	}

	/// <summary>FX_ElectricSpark with a trail length of 1, as g_pEffects->Sparks draws it: big and little streaks and a yellow flare.</summary>
	public static void ElectricSpark( Vector3 position, int magnitude, Vector3 direction, string mount, GameObject parent = null )
	{
		if ( Application.IsDedicatedServer )
			return;
		var big = (int)(magnitude * magnitude * Game.Random.Float( 2, 4 ));
		var bigTrails = SparkTrails( position, mount, big, new( magnitude, magnitude * 2 ), new( 2, 5 ), new( 0.02f, 0.05f ), 800, true, parent );
		if ( bigTrails.IsValid() )
		{
			for ( var i = 0; i < big; i++ )
			{
				var dir = Particles.InCube( 1 ).WithZ( Game.Random.Float( 0.5f, 1 ) ) + 2 * direction;
				if ( bigTrails.Emit( position, 0 ) is { } particle )
					particle.Velocity = dir.Normal * Game.Random.Float( 64, 300 );
			}
		}

		var little = magnitude * Game.Random.Int( 16, 32 );
		var littleTrails = SparkTrails( position, mount, little, new( magnitude * 0.1f, magnitude * 0.2f ), new( 2, 4 ), new( 0.02f, 0.03f ), 400, false, parent );
		if ( littleTrails.IsValid() )
		{
			for ( var i = 0; i < little; i++ )
			{
				var dir = Particles.InCube( 1 ) + direction;
				if ( littleTrails.Emit( position, 0 ) is { } particle )
					particle.Velocity = dir.Normal * Game.Random.Float( 128, 256 );
			}
		}

		var go = new GameObject( parent ?? Game.ActiveScene, true, "Spark" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;
		var burst = go.AddComponent<SpriteBurst>();
		// effects/yellowflare_noz draws the effects/yellowflare texture.
		var flare = burst.Add( "effects/yellowflare", mount, 0, magnitude * Game.Random.Int( 4, 8 ), Color.White, 0.2f );
		burst.FadeTo( flare, 0, Color.White );
		var grey = Game.Random.Int( 32, 64 ) / 255f;
		var halo = burst.Add( "effects/yellowflare", mount, 0, magnitude * Game.Random.Int( 32, 64 ), Color.White * (grey * grey), 0.2f );
		burst.FadeTo( halo, 0, Color.Black );
		SparkSmoke( go, mount );
	}

	// Trail particles shorten over their lifetime, down to 0.01 seconds of travel.
	static readonly ParticleFloat SplashDropStretch = DropStretch( 0.05f );
	static readonly ParticleFloat ExplosionDropStretch = DropStretch( 0.1f );

	static ParticleFloat DropStretch( float longest ) => new()
	{
		Type = ParticleFloat.ValueType.CurveRange,
		Evaluation = ParticleFloat.EvaluationType.Life,
		CurveA = DropLength( 0.025f ),
		CurveB = DropLength( longest )
	};

	static Curve DropLength( float initial ) => new(
		new Curve.Frame( 0, initial ) { Mode = Curve.HandleMode.Linear },
		new Curve.Frame( 1 - 0.01f / initial, 0.01f ) { Mode = Curve.HandleMode.Linear },
		new Curve.Frame( 1, 0.01f ) );

	static readonly ParticleFloat PlumeGrowth = LinearOverLife( 1, 2 );
	static readonly ParticleFloat SmokeGrowth = LinearOverLife( 1, 4 );
	static readonly ParticleFloat MagnumSmokeGrowth = LinearOverLife( 1, 8 );
	static readonly ParticleFloat SmokeFade = LinearOverLife( 1, 0 );

	internal static ParticleFloat LinearOverLife( float start, float end ) => new()
	{
		Type = ParticleFloat.ValueType.Curve,
		Evaluation = ParticleFloat.EvaluationType.Life,
		CurveA = new Curve( new Curve.Frame( 0, start ) { Mode = Curve.HandleMode.Linear }, new Curve.Frame( 1, end ) )
	};

	static void SparkSmoke( GameObject burst, string mount )
	{
		// Smoke evolves over one second, but dies with the glow emitter after 0.2 seconds.
		var smoke = Particles.Emitter( burst, "particle/particle_noisesphere", mount, false, false, 1 );
		if ( !smoke.IsValid() ) return;
		smoke.Lifetime = 1;
		smoke.ApplyShape = true;
		smoke.InitialScale = 2 * Game.Random.Int( 4, 8 );
		smoke.Scale = SmokeGrowth;
		smoke.ApplyAlpha = true;
		smoke.Alpha = SmokeFade;
		smoke.ApplyRotation = true;
		smoke.InitialRoll = new ParticleFloat( 0, 360 );
		smoke.AngularVelocity = new Vector3( 0, 0, Game.Random.Float( -2, 2 ).RadianToDegree() );
		var origin = burst.WorldPosition + Particles.InCube( 4 ).WithZ( 0 );
		if ( smoke.Emit( origin, 0 ) is not { } puff ) return;
		puff.Color = new Color( 1, 1, 200 / 255f, Game.Random.Int( 16, 32 ) / 255f );
		puff.Velocity = Particles.InCube( 16 ).WithZ( 16 );
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
		var burst = go.AddComponent<SpriteBurst>();
		// sprites/light_glow02_add draws the sprites/light_glow02 texture.
		var glow = burst.Add( "sprites/light_glow02", mount, 0, size, Color.White, 0.1f );
		burst.FadeTo( glow, size * 2, Color.Black );
		ElectricSpark( origin, 1, normal, mount );
	}

	/// <summary>One trail particle as a streak as long as it flies in <paramref name="trail"/> seconds, living at most <see cref="SparkFlight"/>.</summary>
	static void Streak( Vector3 position, Vector3 direction, float speed, float trail, float width, string mount, float life )
	{
		var flight = MathF.Min( life, SparkFlight );
		Tracer.Create( "effects/spark", position, direction, speed * flight, speed * trail, width, speed, mount );
	}
}

/// <summary>A tempent shell: plays its bounce sound on some impacts, then fades out over two seconds once its life ends.</summary>
[Title( "Source Shell" )]
[Category( "Source 1" )]
[Alias( "Source1.SourceShell", "SourceShell" )]
public sealed class Shell : Component, Component.ICollisionListener
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

/// <summary>Short-lived additive sprites placed along the parent's forward axis.</summary>
[Title( "Source Sprite Burst" )]
[Category( "Source 1" )]
[Alias( "Source1.SourceSpriteBurst", "SourceSpriteBurst" )]
public sealed class SpriteBurst : Component
{
	internal bool DrawBeforeViewModel { get; set; }
	internal Vector2 CameraFade { get; set; }
	readonly List<(SpriteRenderer Sprite, Vector2 Size, Color Color, Vector2 EndSize, Color EndColor)> _fades = new();
	float _lifetime;
	TimeSince _age;

	ViewModel _view;
	List<(SpriteRenderer Sprite, float Offset)> _localParticles;

	internal void UseViewModelLens( ViewModel view )
	{
		if ( !view.IsValid() ) return;
		_view = view;
		_localParticles = new();
		view.LensApplied += PlaceLocalParticles;
	}

	void PlaceLocalParticles( CameraView camera, float stretch )
	{
		foreach ( var (sprite, offset) in _localParticles )
		{
			var point = WorldPosition + WorldRotation.Forward * offset;
			var delta = point - camera.Position;
			var lateral = delta - camera.Rotation.Forward * delta.Dot( camera.Rotation.Forward );
			sprite.WorldPosition = point + lateral * (stretch - 1);
		}
	}

	protected override void OnDestroy()
	{
		if ( _view.IsValid() ) _view.LensApplied -= PlaceLocalParticles;
	}

	/// <summary>Source particle sizes are radii, so the drawn sprite is twice as wide. Roll is in degrees, random when unset.</summary>
	public SpriteRenderer Add( string sprite, string mount, float offset, float size, Color color, float lifetime, float? roll = null, float alpha = 1 )
	{
		var renderer = MountedEffects.AddSprite( GameObject, sprite, mount, useViewModelLayer: !DrawBeforeViewModel );
		if ( !renderer.IsValid() ) return null;
		renderer.GameObject.LocalPosition = new Vector3( offset, 0, 0 );
		_localParticles?.Add( (renderer, offset) );
		renderer.GameObject.LocalRotation = Rotation.FromRoll( roll ?? Game.Random.Int( 0, 360 ) );
		renderer.CameraFadeNear = CameraFade.x;
		renderer.CameraFadeFar = CameraFade.y;
		renderer.Size = size * 2;
		renderer.Color = color.WithAlpha( alpha );
		_lifetime = MathF.Max( _lifetime, lifetime );
		return renderer;
	}

	/// <summary>Blends a sprite to this radius and color over the burst's lifetime, like a particle's end size and alpha.</summary>
	public void FadeTo( SpriteRenderer sprite, float size, Color color, float alpha = 1 )
	{
		if ( sprite.IsValid() ) _fades.Add( (sprite, sprite.Size, sprite.Color, new Vector2( size * 2 ), color.WithAlpha( alpha )) );
	}

	protected override void OnEnabled() => _age = 0;

	protected override void OnUpdate()
	{
		if ( _age > _lifetime )
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
[Alias( "Source1.SourceTracer", "SourceTracer" )]
public sealed class Tracer : Component
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
		var line = MountedEffects.AddBeam( go, material, mount );
		if ( !line.IsValid() )
		{
			go.Destroy();
			return;
		}
		var tracer = go.AddComponent<Tracer>();
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
		MountedEffects.SetBeam( _line, _start + _direction * tail, _start + _direction * head, head > tail ? _width : 0, 255 );
	}
}
