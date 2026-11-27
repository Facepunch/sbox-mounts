namespace UnityMount;

public enum PostalProjectileVisualKind
{
	Generic = 0,
	Rocket = 1,
	Grenade = 2,
	Timebomb = 3,
	DildoBolt = 4,
	Cat = 5,
	LightningOrb = 6,
	EnemyPlasma = 7,
}

public enum PostalDecalKind
{
	Bullet = 0,
	BulletMetal = 1,
	Explosion = 2,
	Blood = 3,
	Lightning = 4,
	Shovel = 5,
}

/// <summary>
/// Real-time 3D combat VFX, authentic PBD surface decals, sticking/wobbling 3D Dildo bolts,
/// SMG Temporal Slow-Mo Bubble Field, Holy Hand Grenade celestial blasts, and multi-filament Tesla arcs.
/// Loads original POSTAL: Brain Damaged textures from sharedassets0 with crisp procedural fallbacks.
/// </summary>
public static class PostalVfx
{
	static Sprite _softGlowSprite;
	static Sprite _muzzleStarSprite;
	static Sprite _cloudPuffSprite;
	static Sprite _bloodSplatSprite;

	// Authentic mounted PBD VFX sprites (loaded lazily from sharedassets0 with procedural fallbacks)
	static readonly Dictionary<string, Sprite> _mountedSprites = new( StringComparer.OrdinalIgnoreCase );
	static readonly Dictionary<string, Texture> _mountedTextures = new( StringComparer.OrdinalIgnoreCase );
	static Model _timeBubbleSphereModel;
	static Material _timeBubbleMat;

	static Sprite SoftGlowSprite => _softGlowSprite ??= Sprite.FromTexture( BuildSoftGlowTexture() );
	static Sprite MuzzleStarSprite => _muzzleStarSprite ??= Sprite.FromTexture( BuildMuzzleStarTexture() );
	static Sprite CloudPuffSprite => _cloudPuffSprite ??= Sprite.FromTexture( BuildCloudPuffTexture() );
	static Sprite BloodSplatSprite => _bloodSplatSprite ??= Sprite.FromTexture( BuildBloodSplatTexture() );

	internal static Texture LoadMountedTexture( string mountPath )
	{
		if ( string.IsNullOrEmpty( mountPath ) ) return null;
		if ( _mountedTextures.TryGetValue( mountPath, out var cached ) )
			return cached;

		var tex = Texture.Load( mountPath, warnOnMissing: false );
		if ( tex is not null && !tex.IsError )
		{
			_mountedTextures[mountPath] = tex;
			return tex;
		}
		_mountedTextures[mountPath] = null;
		return null;
	}

	static Sprite LoadMountedSprite( string mountPath, Sprite fallback )
	{
		if ( _mountedSprites.TryGetValue( mountPath, out var cached ) )
			return cached ?? fallback;

		var tex = LoadMountedTexture( mountPath );
		var sp = tex is not null ? Sprite.FromTexture( tex ) : null;
		_mountedSprites[mountPath] = sp;
		return sp ?? fallback;
	}

	static Sprite FireExplosionSprite => LoadMountedSprite( "mount://pbd/textures/sharedassets0/fire_explosion.vtex", CloudPuffSprite );
	static Sprite ExplosionSprite => LoadMountedSprite( "mount://pbd/textures/sharedassets0/explosion.vtex", CloudPuffSprite );
	static Sprite ExplosionAltSprite => LoadMountedSprite( "mount://pbd/textures/sharedassets0/explosion_02.vtex", CloudPuffSprite );
	static Sprite ExplosionSmokeSprite => LoadMountedSprite( "mount://pbd/textures/sharedassets0/explosion_smoke.vtex", CloudPuffSprite );
	static Sprite HolyFireSprite => LoadMountedSprite( "mount://pbd/textures/sharedassets0/fire_explosion_hhg.vtex", CloudPuffSprite );
	static Sprite HolyEnergySprite => LoadMountedSprite( "mount://pbd/textures/sharedassets0/explosion_hhg_energy.vtex", SoftGlowSprite );
	static Sprite ElectricRingSprite => LoadMountedSprite( "mount://pbd/textures/sharedassets0/electricity_ring.vtex", SoftGlowSprite );
	static Sprite ElectricFlashSprite => LoadMountedSprite( "mount://pbd/textures/sharedassets0/electricity_02.vtex", MuzzleStarSprite );
	static Sprite ElectricExplosionSprite => LoadMountedSprite( "mount://pbd/textures/sharedassets0/electricity_explosion.vtex", SoftGlowSprite );
	static Sprite ElectricCloudSprite => LoadMountedSprite( "mount://pbd/textures/sharedassets0/smoke_electriccloud.vtex", CloudPuffSprite );
	static Sprite MuzzlePistolSprite => LoadMountedSprite( "mount://pbd/textures/sharedassets0/muzzle_flash_pistol.vtex", MuzzleStarSprite );
	static Sprite MuzzleRocketSprite => LoadMountedSprite( "mount://pbd/textures/sharedassets0/muzzle_flash_rocket.vtex", MuzzleStarSprite );
	static Sprite TimeBubbleCircleSprite => LoadMountedSprite( "mount://pbd/textures/sharedassets0/circle_solid_timebubble.vtex", SoftGlowSprite );

	static Texture BuildSoftGlowTexture()
	{
		const int sz = 64;
		var data = new byte[sz * sz * 4];
		for ( int y = 0; y < sz; y++ )
		{
			float ny = (y + 0.5f) / sz * 2f - 1f;
			for ( int x = 0; x < sz; x++ )
			{
				float nx = (x + 0.5f) / sz * 2f - 1f;
				float r = MathF.Sqrt( nx * nx + ny * ny );
				float a = Math.Clamp( 1f - r, 0f, 1f );
				a = a * a * MathF.Sqrt( a );
				int idx = (y * sz + x) * 4;
				data[idx + 0] = 255;
				data[idx + 1] = 255;
				data[idx + 2] = 255;
				data[idx + 3] = (byte)(a * 255f);
			}
		}
		return Texture.Create( sz, sz ).WithData( data ).WithName( "pbd_vfx_glow" ).Finish();
	}

	static Texture BuildMuzzleStarTexture()
	{
		const int sz = 64;
		var data = new byte[sz * sz * 4];
		for ( int y = 0; y < sz; y++ )
		{
			float ny = (y + 0.5f) / sz * 2f - 1f;
			for ( int x = 0; x < sz; x++ )
			{
				float nx = (x + 0.5f) / sz * 2f - 1f;
				float r = MathF.Sqrt( nx * nx + ny * ny );
				float ang = MathF.Atan2( ny, nx );
				float spikes = 0.52f + 0.48f * MathF.Pow( MathF.Abs( MathF.Cos( ang * 3f ) ), 2.2f );
				float core = MathF.Pow( Math.Clamp( 1f - r * 1.45f, 0f, 1f ), 1.6f );
				float ray = MathF.Pow( Math.Clamp( 1f - r / spikes, 0f, 1f ), 1.5f );
				float a = Math.Clamp( MathF.Max( core, ray * 0.92f ), 0f, 1f );
				int idx = (y * sz + x) * 4;
				data[idx + 0] = 255;
				data[idx + 1] = 255;
				data[idx + 2] = 255;
				data[idx + 3] = (byte)(a * 255f);
			}
		}
		return Texture.Create( sz, sz ).WithData( data ).WithName( "pbd_vfx_muzzle" ).Finish();
	}

	static Texture BuildCloudPuffTexture()
	{
		const int sz = 64;
		var data = new byte[sz * sz * 4];
		for ( int y = 0; y < sz; y++ )
		{
			float ny = (y + 0.5f) / sz * 2f - 1f;
			for ( int x = 0; x < sz; x++ )
			{
				float nx = (x + 0.5f) / sz * 2f - 1f;
				float r = MathF.Sqrt( nx * nx + ny * ny );
				float ang = MathF.Atan2( ny, nx );
				float lobe = 0.78f + 0.14f * MathF.Sin( ang * 5f + 0.7f ) + 0.08f * MathF.Cos( ang * 8f - 1.1f );
				float edge = Math.Clamp( 1f - r / lobe, 0f, 1f );
				float innerNoise = 0.82f + 0.18f * MathF.Sin( nx * 6.5f + ny * 4.2f ) * MathF.Cos( ny * 5.8f - nx * 3.1f );
				float a = MathF.Pow( edge, 1.35f ) * innerNoise;
				byte lum = (byte)Math.Clamp( (0.68f + 0.32f * MathF.Pow( edge, 0.7f )) * 255f, 0f, 255f );
				int idx = (y * sz + x) * 4;
				data[idx + 0] = lum;
				data[idx + 1] = lum;
				data[idx + 2] = lum;
				data[idx + 3] = (byte)Math.Clamp( a * 255f, 0f, 255f );
			}
		}
		return Texture.Create( sz, sz ).WithData( data ).WithName( "pbd_vfx_cloud" ).Finish();
	}

	static Texture BuildBloodSplatTexture()
	{
		const int sz = 64;
		var data = new byte[sz * sz * 4];
		for ( int y = 0; y < sz; y++ )
		{
			float ny = (y + 0.5f) / sz * 2f - 1f;
			for ( int x = 0; x < sz; x++ )
			{
				float nx = (x + 0.5f) / sz * 2f - 1f;
				float r = MathF.Sqrt( nx * nx + ny * ny );
				float ang = MathF.Atan2( ny, nx );
				float splat = 0.62f + 0.24f * MathF.Sin( ang * 4f ) + 0.14f * MathF.Cos( ang * 7f + 0.9f );
				float a = Math.Clamp( (splat - r) * 4.5f, 0f, 1f );
				byte lum = (byte)Math.Clamp( (0.75f + 0.25f * Math.Clamp( 1f - r, 0f, 1f )) * 255f, 0f, 255f );
				int idx = (y * sz + x) * 4;
				data[idx + 0] = lum;
				data[idx + 1] = lum;
				data[idx + 2] = lum;
				data[idx + 3] = (byte)Math.Clamp( a * 255f, 0f, 255f );
			}
		}
		return Texture.Create( sz, sz ).WithData( data ).WithName( "pbd_vfx_blood" ).Finish();
	}

	static Gradient MakeBeamGradient( Color startCol, Color endCol )
	{
		return new Gradient(
			new Gradient.ColorFrame( 0f, startCol ),
			new Gradient.ColorFrame( 0.75f, endCol ),
			new Gradient.ColorFrame( 1f, endCol.WithAlpha( 0f ) )
		);
	}

	static SpriteRenderer CreateSprite( GameObject go, Sprite sprite, float size, Color color, bool additive )
	{
		var sr = go.AddComponent<SpriteRenderer>();
		sr.Sprite = sprite;
		sr.Size = new Vector2( size, size );
		sr.Color = color;
		sr.Additive = additive;
		sr.Opaque = false;
		sr.Lighting = false;
		sr.Shadows = false;
		return sr;
	}

	static Model GetOrBuildTimeBubbleSphere()
	{
		if ( _timeBubbleSphereModel is not null && !_timeBubbleSphereModel.IsError )
			return _timeBubbleSphereModel;

		_timeBubbleMat = Material.Create( "pbd_timebubble_dome", "shaders/unity_urp.shader" );
		if ( _timeBubbleMat is not null )
		{
			var bubbleTex = LoadMountedTexture( "mount://pbd/textures/sharedassets0/timebubble.vtex" )
				?? LoadMountedTexture( "mount://pbd/textures/sharedassets0/energy_timebubble.vtex" )
				?? Texture.White;
			_timeBubbleMat.SetFeature( "F_TRANSLUCENT", 1 );
			_timeBubbleMat.SetFeature( "F_RENDER_BACKFACES", 1 );
			_timeBubbleMat.Set( "g_nShaderMode", 6 );
			_timeBubbleMat.Set( "g_tColor", bubbleTex );
			_timeBubbleMat.Set( "g_vBaseColor", new Vector4( 0.22f, 0.85f, 1.0f, 0.34f ) );
			_timeBubbleMat.Set( "g_vMainTexST", new Vector4( 3f, 2f, 0f, 0f ) );
			_timeBubbleMat.Set( "g_vEmissionColor", new Vector4( 0.22f, 0.85f, 1.0f, 1f ) );
			_timeBubbleMat.Set( "g_bHasNormal", 0 );
			_timeBubbleMat.Set( "g_bHasEmission", 0 );
			_timeBubbleMat.Set( "g_bHasMetallicMap", 0 );
			_timeBubbleMat.Set( "g_bUseVertexColor", 0 );
			_timeBubbleMat.Set( "g_bPointFilter", 0 );
		}

		// Build a clean UV-mapped unit sphere (radius = 1) so scaling to any dome radius is instant & exact!
		const int rings = 14;
		const int slices = 22;
		var verts = new List<SimpleVertex>();
		var indices = new List<int>();

		for ( int r = 0; r <= rings; r++ )
		{
			float v = r / (float)rings;
			float phi = v * MathF.PI;
			float sinPhi = MathF.Sin( phi );
			float cosPhi = MathF.Cos( phi );

			for ( int s = 0; s <= slices; s++ )
			{
				float u = s / (float)slices;
				float theta = u * MathF.PI * 2f;
				var nrm = new Vector3( sinPhi * MathF.Cos( theta ), sinPhi * MathF.Sin( theta ), cosPhi );
				var tan = new Vector3( -MathF.Sin( theta ), MathF.Cos( theta ), 0f );
				verts.Add( new SimpleVertex( nrm, nrm, tan, new Vector2( u, v ) ) );
			}
		}

		int stride = slices + 1;
		for ( int r = 0; r < rings; r++ )
		{
			for ( int s = 0; s < slices; s++ )
			{
				int a = r * stride + s;
				int b = (r + 1) * stride + s;
				int c = b + 1;
				int d = a + 1;
				indices.Add( a ); indices.Add( b ); indices.Add( c );
				indices.Add( a ); indices.Add( c ); indices.Add( d );
			}
		}

		var mesh = new Mesh( _timeBubbleMat ?? UnityMaterialLoader.CreateDefaultMaterial() )
		{
			Bounds = new BBox( new Vector3( -1f ), new Vector3( 1f ) )
		};
		mesh.CreateVertexBuffer( verts.Count, verts );
		mesh.CreateIndexBuffer( indices.Count, indices );

		_timeBubbleSphereModel = Model.Builder
			.WithName( "pbd_timebubble_dome_model" )
			.AddMesh( mesh )
			.Create();
		return _timeBubbleSphereModel;
	}

	/// <summary>
	/// Projects an authentic POSTAL: Brain Damaged surface decal (bullet hole, scorch mark, blood splat,
	/// Tesla electric burn, or shovel slash) onto walls, floors, and props using native s&amp;box Decals!
	/// </summary>
	public static void SpawnSurfaceDecal( Scene scene, Vector3 pos, Vector3 normal, PostalDecalKind kind, float size = 16f )
	{
		if ( !scene.IsValid() ) return;

		string texPath = kind switch
		{
			PostalDecalKind.Bullet => "mount://pbd/textures/sharedassets0/decal_bullet.vtex",
			PostalDecalKind.BulletMetal => "mount://pbd/textures/sharedassets0/decal_bullet_metal.vtex",
			PostalDecalKind.Explosion => "mount://pbd/textures/sharedassets0/decal_explosion.vtex",
			PostalDecalKind.Blood => "mount://pbd/textures/sharedassets0/decal_blood.vtex",
			PostalDecalKind.Lightning => "mount://pbd/textures/sharedassets0/decal_lightninggun.vtex",
			PostalDecalKind.Shovel => "mount://pbd/textures/sharedassets0/decal_shovel.vtex",
			_ => "mount://pbd/textures/sharedassets0/decal_bullet.vtex"
		};

		var tex = LoadMountedTexture( texPath );
		if ( tex is null ) return;

		using ( scene.Push() )
		{
			var n = normal.LengthSquared > 0.001f ? normal.Normal : Vector3.Up;
			var go = new GameObject( false, $"Vfx_Decal_{kind}" );
			go.Flags |= GameObjectFlags.NotSaved;
			go.WorldPosition = pos + n * 0.5f;
			go.WorldRotation = Rotation.LookAt( -n ) * Rotation.FromRoll( Game.Random.Float( 0f, 360f ) );

			var decal = go.AddComponent<Decal>();
			decal.Decals =
			[
				new DecalDefinition
				{
					ColorTexture = tex,
					Width = size,
					Height = size,
					Tint = kind == PostalDecalKind.Blood ? new Color( 0.85f, 0.08f, 0.08f, 1f ) : Color.White,
				}
			];
			decal.Depth = kind == PostalDecalKind.Explosion ? 18f : 8f;
			decal.Transient = true;

			var life = go.AddComponent<PostalVfxTimedDestroy>();
			life.Duration = kind == PostalDecalKind.Explosion ? 45f : 28f;
			go.Enabled = true;
		}
	}

	/// <summary>
	/// Sticks an authentic 3D PBD Dildo Bow rubber dildo into any wall, floor, prop, or enemy,
	/// complete with elastic damped spring wobble ("boing-boing") just like POSTAL: Brain Damaged!
	/// </summary>
	public static void SpawnStuckDildo(
		Scene scene,
		Vector3 hitPos,
		Vector3 surfaceNormal,
		Vector3 flightDir,
		string modelPath,
		GameObject attachTarget = null )
	{
		if ( !scene.IsValid() ) return;
		var model = !string.IsNullOrEmpty( modelPath ) ? Model.Load( modelPath ) : null;
		if ( model is null || model.IsError ) return;

		using ( scene.Push() )
		{
			var dir = flightDir.LengthSquared > 0.001f ? flightDir.Normal : -surfaceNormal.Normal;
			var nrm = surfaceNormal.LengthSquared > 0.001f ? surfaceNormal.Normal : -dir;
			// Blend between surface normal (suction cup flat against wall!) and flight angle
			var stickDir = (-nrm * 0.72f + dir * 0.28f).Normal;

			var go = new GameObject( true, "Vfx_StuckDildo" );
			go.Flags |= GameObjectFlags.NotSaved;
			go.WorldPosition = hitPos + nrm * 1.5f;
			go.WorldRotation = Rotation.LookAt( stickDir );

			float span = MathF.Max( model.Bounds.Size.Length, 1f );
			float scale = Math.Clamp( 19f / span, 0.25f, 1.8f );

			var meshHolder = new GameObject( go, true, "DildoMesh" );
			meshHolder.Flags |= GameObjectFlags.NotSaved;
			meshHolder.LocalScale = new Vector3( scale );
			// Offset slightly back so suction cup base sits flush on the impact wall
			meshHolder.LocalPosition = new Vector3( -3.5f, 0f, 0f );

			var mr = meshHolder.AddComponent<ModelRenderer>();
			mr.Model = model;
			mr.RenderType = ModelRenderer.ShadowRenderType.On;

			var stuck = go.AddComponent<PostalStuckDildo>();
			stuck.VisualChild = meshHolder;
			stuck.BaseRotation = go.WorldRotation;
			stuck.Duration = 28f;

			if ( attachTarget.IsValid() && !attachTarget.Tags.Has( "world" ) )
			{
				stuck.AttachTarget = attachTarget;
				stuck.LocalPosOffset = attachTarget.WorldTransform.PointToLocal( go.WorldPosition );
				stuck.LocalRotOffset = attachTarget.WorldRotation.Inverse * go.WorldRotation;
			}
		}
	}

	/// <summary>
	/// Spawns a persistent physical Cat in the world after being fired by the Cat Cannon,
	/// bouncing/rolling cleanly without exploding so the player can suck it back up with the Cat Vacuum!
	/// </summary>
	public static void SpawnWorldCat( Scene scene, Vector3 pos, Vector3 initialVelocity, string modelPath )
	{
		if ( !scene.IsValid() ) return;
		string effectivePath = !string.IsNullOrEmpty( modelPath )
			? modelPath
			: "mount://pbd/models/prefabs/characters/other/cat.vmdl";
		var model = Model.Load( effectivePath );
		if ( model is null || model.IsError ) return;

		// Keep at most 20 loose world cats for steady performance
		var existing = scene.GetAllComponents<PostalWorldCat>().OrderByDescending( c => c.Age ).ToList();
		while ( existing.Count >= 20 )
		{
			if ( existing[0].IsValid() ) existing[0].GameObject.Destroy();
			existing.RemoveAt( 0 );
		}

		using ( scene.Push() )
		{
			var go = new GameObject( true, "PostalWorldCat" );
			go.Flags |= GameObjectFlags.NotSaved;
			go.Tags.Add( "pbd_cat", "removable" );
			go.WorldPosition = pos;
			go.WorldRotation = initialVelocity.LengthSquared > 1f
				? Rotation.LookAt( initialVelocity.WithZ( 0 ).LengthSquared > 0.1f ? initialVelocity.WithZ( 0 ).Normal : Vector3.Forward )
				: Rotation.FromYaw( Game.Random.Float( 0f, 360f ) );

			float span = MathF.Max( model.Bounds.Size.Length, 1f );
			float scale = Math.Clamp( 24f / span, 0.2f, 1.5f );

			var meshGo = new GameObject( go, true, "CatMesh" );
			meshGo.Flags |= GameObjectFlags.NotSaved;
			meshGo.LocalScale = new Vector3( scale );
			meshGo.LocalPosition = -model.Bounds.Center * scale;

			var mr = meshGo.AddComponent<ModelRenderer>();
			mr.Model = model;
			mr.RenderType = ModelRenderer.ShadowRenderType.On;

			var col = go.AddComponent<SphereCollider>();
			col.Radius = 11f;
			col.Friction = 0.55f;

			var rb = go.AddComponent<Rigidbody>();
			rb.MassOverride = 12f;
			rb.Velocity = initialVelocity;
			rb.AngularVelocity = Vector3.Random * 7f;

			var cat = go.AddComponent<PostalWorldCat>();
			cat.Body = rb;
		}
	}

	/// <summary>
	/// Spawns the authentic POSTAL: Brain Damaged SMG Timebomb Temporal Slow-Mo Bubble Field!
	/// Slows all enemies, enemy projectiles, and physics debris inside the glowing translucent blue dome to 0.15x
	/// while ticking down, then implodes and detonates in a chrono-electric nail shrapnel blast.
	/// </summary>
	public static void SpawnTimeBubble(
		Scene scene,
		Vector3 center,
		float radius,
		float duration,
		GameObject attacker,
		GameObject weapon,
		float finalDamage )
	{
		if ( !scene.IsValid() ) return;
		using ( scene.Push() )
		{
			var go = new GameObject( true, "Vfx_TimeBubble" );
			go.Flags |= GameObjectFlags.NotSaved;
			go.WorldPosition = center;

			var domeGo = new GameObject( go, true, "Dome" );
			domeGo.Flags |= GameObjectFlags.NotSaved;
			var domeMr = domeGo.AddComponent<ModelRenderer>();
			domeMr.Model = GetOrBuildTimeBubbleSphere();
			domeMr.RenderType = ModelRenderer.ShadowRenderType.Off;

			var coreGo = new GameObject( go, true, "CoreSprite" );
			coreGo.Flags |= GameObjectFlags.NotSaved;
			var coreSr = CreateSprite( coreGo, TimeBubbleCircleSprite, 42f, new Color( 0.45f, 0.94f, 1f, 0.85f ), additive: true );

			var pl = go.AddComponent<PointLight>();
			pl.LightColor = new Color( 0.18f, 0.82f, 1f, 1f ) * 5.2f;
			pl.Radius = radius * 1.9f;
			pl.Shadows = false;

			var bubble = go.AddComponent<PostalTimeBubble>();
			bubble.DomeVisual = domeGo;
			bubble.CoreSprite = coreSr;
			bubble.Light = pl;
			bubble.Radius = radius;
			bubble.Duration = duration;
			bubble.Attacker = attacker;
			bubble.Weapon = weapon;
			bubble.FinalDamage = finalDamage;

			UnitySoundUtil.PlayAt( "mount://pbd/sounds/player/time_warp_stop_02_mono.vsnd", center, 0.95f, 2000f, 1.25f, 1.35f );
			SpawnShockwave( scene, center, radius, new Color( 0.25f, 0.9f, 1f, 1f ), duration: 0.32f, lineWidth: 7f );
		}
	}

	/// <summary>
	/// Crisp retro FPS star-burst muzzle flash using authentic PBD textures + bright dynamic PointLight.
	/// </summary>
	public static void SpawnMuzzleFlash( Scene scene, Vector3 pos, Vector3 forward, Color color, float scale = 1f, float lightRadius = 260f )
	{
		if ( !scene.IsValid() ) return;
		using ( scene.Push() )
		{
			var go = new GameObject( true, "Vfx_MuzzleFlash" );
			go.Flags |= GameObjectFlags.NotSaved;
			go.WorldPosition = pos;
			var dir = forward.LengthSquared > 0.001f ? forward.Normal : Vector3.Forward;
			go.WorldRotation = Rotation.LookAt( dir ) * Rotation.FromRoll( Game.Random.Float( 0f, 360f ) );

			var flashSprite = scale >= 1.5f ? MuzzleRocketSprite : MuzzlePistolSprite;
			var sr = CreateSprite( go, flashSprite, 24f * scale, Color.Lerp( color, Color.White, 0.4f ), additive: true );

			var coreGo = new GameObject( go, true, "Core" );
			CreateSprite( coreGo, SoftGlowSprite, 14f * scale, Color.White, additive: true );

			var pl = go.AddComponent<PointLight>();
			pl.LightColor = color * 3.8f;
			pl.Radius = lightRadius;
			pl.Shadows = false;

			var anim = go.AddComponent<PostalVfxSpriteAnim>();
			anim.Duration = 0.065f;
			anim.Sprite = sr;
			anim.StartSize = 26f * scale;
			anim.EndSize = 6f * scale;
			anim.StartColor = Color.Lerp( color, Color.White, 0.4f );
			anim.EndColor = color.WithAlpha( 0f );
			anim.FadeLight = pl;
		}
	}

	/// <summary>
	/// Authentic multi-filament Tesla Lightning Gun beam: braided core + 2 coiled high-voltage plasma arcs +
	/// branching forks + authentic PBD Electricity_Ring flash at impact &amp; dynamic electric PointLight!
	/// </summary>
	public static void SpawnTeslaArc(
		Scene scene,
		Vector3 start,
		Vector3 end,
		Color color,
		float width = 3.8f,
		float duration = 0.12f,
		int forks = 4,
		bool impactRing = true )
	{
		if ( !scene.IsValid() ) return;
		using ( scene.Push() )
		{
			var outerBlue = new Color( 0.18f, 0.52f, 1f, 1f );
			var hotCyan = Color.Lerp( color, Color.White, 0.45f );

			// 1. Wide outer electric corona beam
			SpawnSingleBeam( start, end, outerBlue, width * 1.45f, duration, jagged: true, jitterScale: 0.065f );
			// 2. Primary cyan jagged coil
			SpawnSingleBeam( start, end, hotCyan, width * 0.9f, duration * 0.92f, jagged: true, jitterScale: 0.052f );
			// 3. Incandescent pure-white inner filament
			SpawnSingleBeam( start, end, Color.White, width * 0.4f, duration * 0.8f, jagged: true, jitterScale: 0.032f );

			// 4. Jagged electric branch forks crackling off the main arc
			for ( int f = 0; f < forks; f++ )
			{
				float t = Game.Random.Float( 0.14f, 0.85f );
				var bStart = Vector3.Lerp( start, end, t ) + Vector3.Random * 6f;
				var bEnd = bStart + Vector3.Random.Normal * Game.Random.Float( 42f, 118f );
				SpawnSingleBeam( bStart, bEnd, hotCyan, width * 0.52f, duration * 0.78f, jagged: true, jitterScale: 0.09f );
			}

			// 5. Authentic PBD Electricity_Ring + Electricity_02 flash & blue dynamic light at impact point!
			if ( impactRing )
			{
				var ringGo = new GameObject( true, "Vfx_TeslaImpact" );
				ringGo.Flags |= GameObjectFlags.NotSaved;
				ringGo.WorldPosition = end;
				var sr = CreateSprite( ringGo, ElectricRingSprite, 34f, hotCyan, additive: true );

				var sparkGo = new GameObject( ringGo, true, "Flash" );
				CreateSprite( sparkGo, ElectricFlashSprite, 24f, Color.White, additive: true );

				var pl = ringGo.AddComponent<PointLight>();
				pl.LightColor = color * 5.0f;
				pl.Radius = 260f;
				pl.Shadows = false;

				var anim = ringGo.AddComponent<PostalVfxSpriteAnim>();
				anim.Duration = 0.14f;
				anim.Sprite = sr;
				anim.StartSize = 18f;
				anim.EndSize = 68f;
				anim.StartColor = hotCyan;
				anim.EndColor = outerBlue.WithAlpha( 0f );
				anim.FadeLight = pl;
			}
		}
	}

	/// <summary>
	/// Real 3D additive glowing beam / bullet tracer / jagged Chain Lightning bolt / Meat Hook chain.
	/// </summary>
	public static void SpawnTracerBeam(
		Scene scene,
		Vector3 start,
		Vector3 end,
		Color color,
		float width = 1.8f,
		float duration = 0.07f,
		bool jagged = false,
		int forks = 0 )
	{
		if ( !scene.IsValid() ) return;
		using ( scene.Push() )
		{
			SpawnSingleBeam( start, end, color, width, duration, jagged );

			if ( jagged )
			{
				SpawnSingleBeam( start, end, Color.White, width * 0.42f, duration * 0.85f, true );

				for ( int f = 0; f < forks; f++ )
				{
					float t = Game.Random.Float( 0.2f, 0.78f );
					var branchStart = Vector3.Lerp( start, end, t ) + Vector3.Random * 8f;
					var branchEnd = branchStart + Vector3.Random.Normal * Game.Random.Float( 36f, 105f );
					SpawnSingleBeam( branchStart, branchEnd, color, width * 0.55f, duration * 0.75f, true );
				}
			}
		}
	}

	static void SpawnSingleBeam( Vector3 start, Vector3 end, Color color, float width, float duration, bool jagged, float jitterScale = 0.048f )
	{
		var go = new GameObject( true, "Vfx_Beam" );
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = start;

		var lr = go.AddComponent<LineRenderer>();
		lr.UseVectorPoints = true;
		lr.Additive = true;
		lr.Opaque = false;
		lr.Lighting = false;
		lr.CastShadows = false;
		lr.Color = MakeBeamGradient( Color.Lerp( color, Color.White, 0.35f ), color );
		lr.Width = new Curve( new Curve.Frame( 0f, width ), new Curve.Frame( 0.5f, width * 0.85f ), new Curve.Frame( 1f, width * 0.35f ) );

		var pts = new List<Vector3>();
		if ( jagged )
		{
			float len = start.Distance( end );
			int segs = Math.Clamp( (int)(len / 40f), 6, 18 );
			float jitter = Math.Clamp( len * jitterScale, 5f, 28f );
			pts.Add( start );
			for ( int i = 1; i < segs; i++ )
			{
				float t = (float)i / segs;
				float envelope = MathF.Sin( t * MathF.PI );
				pts.Add( Vector3.Lerp( start, end, t ) + Vector3.Random * (jitter * envelope) );
			}
			pts.Add( end );
		}
		else
		{
			pts.Add( start );
			pts.Add( Vector3.Lerp( start, end, 0.5f ) );
			pts.Add( end );
		}
		lr.VectorPoints = pts;

		var life = go.AddComponent<PostalVfxBeamFade>();
		life.Duration = MathF.Max( duration, 0.03f );
		life.Line = lr;
		life.BaseWidth = width;
	}

	/// <summary>
	/// Curved glowing beam (Gatling smart homing bullets arcing around corners &amp; Cat Vacuum spiral vortex streams!).
	/// </summary>
	public static void SpawnCurvedBeam(
		Scene scene,
		Vector3 start,
		Vector3 end,
		Vector3 midOffset,
		Color color,
		float width = 2.0f,
		float duration = 0.09f )
	{
		if ( !scene.IsValid() ) return;
		using ( scene.Push() )
		{
			var go = new GameObject( true, "Vfx_CurvedBeam" );
			go.Flags |= GameObjectFlags.NotSaved;
			go.WorldPosition = start;

			var lr = go.AddComponent<LineRenderer>();
			lr.UseVectorPoints = true;
			lr.Additive = true;
			lr.Opaque = false;
			lr.Lighting = false;
			lr.CastShadows = false;
			lr.Color = MakeBeamGradient( Color.Lerp( color, Color.White, 0.35f ), color );
			lr.Width = new Curve( new Curve.Frame( 0f, width ), new Curve.Frame( 0.5f, width * 0.9f ), new Curve.Frame( 1f, width * 0.25f ) );

			var mid = Vector3.Lerp( start, end, 0.5f ) + midOffset;
			const int segs = 10;
			var pts = new List<Vector3>( segs + 1 );
			for ( int i = 0; i <= segs; i++ )
			{
				float t = i / (float)segs;
				float u = 1f - t;
				pts.Add( u * u * start + 2f * u * t * mid + t * t * end );
			}
			lr.VectorPoints = pts;

			var life = go.AddComponent<PostalVfxBeamFade>();
			life.Duration = MathF.Max( duration, 0.04f );
			life.Line = lr;
			life.BaseWidth = width;
		}
	}

	/// <summary>
	/// Wide sweeping melee slash arc trail for the Shovel swing!
	/// </summary>
	public static void SpawnMeleeSlashArc( Scene scene, Vector3 eyePos, Vector3 forward, Color color, bool leftToRight, float radius = 58f )
	{
		if ( !scene.IsValid() ) return;
		using ( scene.Push() )
		{
			var rot = Rotation.LookAt( forward.LengthSquared > 0.001f ? forward.Normal : Vector3.Forward );
			var center = eyePos + rot.Forward * 24f - rot.Up * 6f;

			var go = new GameObject( true, "Vfx_SlashArc" );
			go.Flags |= GameObjectFlags.NotSaved;
			go.WorldPosition = center;

			var lr = go.AddComponent<LineRenderer>();
			lr.UseVectorPoints = true;
			lr.Additive = true;
			lr.Opaque = false;
			lr.Lighting = false;
			lr.CastShadows = false;
			lr.Color = MakeBeamGradient( color.WithAlpha( 0.15f ), Color.Lerp( color, Color.White, 0.5f ) );
			lr.Width = new Curve( new Curve.Frame( 0f, 1.5f ), new Curve.Frame( 0.65f, 7.5f ), new Curve.Frame( 1f, 2f ) );

			const int segs = 12;
			var pts = new List<Vector3>( segs + 1 );
			float sign = leftToRight ? 1f : -1f;
			for ( int i = 0; i <= segs; i++ )
			{
				float t = i / (float)segs;
				float ang = (-55f + t * 110f) * sign * (MathF.PI / 180f);
				float tilt = (-12f + t * 24f) * sign;
				var pt = center
					+ rot.Forward * (MathF.Cos( ang ) * radius)
					+ rot.Right * (MathF.Sin( ang ) * radius)
					+ rot.Up * tilt;
				pts.Add( pt );
			}
			lr.VectorPoints = pts;

			var life = go.AddComponent<PostalVfxBeamFade>();
			life.Duration = 0.14f;
			life.Line = lr;
			life.BaseWidth = 7.5f;
		}
	}

	/// <summary>
	/// Lingering gun barrel smoke puff (Super Shotgun / Rocket Launcher / Pistol blasts!).
	/// </summary>
	public static void SpawnBarrelSmoke( Scene scene, Vector3 pos, Vector3 forward, float scale = 1f )
	{
		if ( !scene.IsValid() ) return;
		using ( scene.Push() )
		{
			var go = new GameObject( true, "Vfx_BarrelSmoke" );
			go.Flags |= GameObjectFlags.NotSaved;
			go.WorldPosition = pos;
			var col = new Color( 0.65f, 0.65f, 0.68f, 0.42f );
			var sr = CreateSprite( go, ExplosionSmokeSprite, 14f * scale, col, additive: false );

			var anim = go.AddComponent<PostalVfxSpriteAnim>();
			anim.Duration = 0.48f;
			anim.Sprite = sr;
			anim.StartSize = 12f * scale;
			anim.EndSize = 46f * scale;
			anim.DriftVelocity = forward * 35f + Vector3.Up * 38f + Vector3.Random * 16f;
			anim.StartColor = col;
			anim.EndColor = new Color( 0.55f, 0.55f, 0.58f, 0f );
		}
	}

	/// <summary>
	/// Expanding glowing shockwave ring (Goliath ground-slam, Karen stomp, Shovel projectile deflect, explosions!).
	/// </summary>
	public static void SpawnShockwave( Scene scene, Vector3 pos, float maxRadius, Color color, float duration = 0.25f, float lineWidth = 5f )
	{
		if ( !scene.IsValid() ) return;
		using ( scene.Push() )
		{
			var go = new GameObject( true, "Vfx_Shockwave" );
			go.Flags |= GameObjectFlags.NotSaved;
			go.WorldPosition = pos + Vector3.Up * 4f;

			var lr = go.AddComponent<LineRenderer>();
			lr.UseVectorPoints = true;
			lr.Additive = true;
			lr.Opaque = false;
			lr.Lighting = false;
			lr.CastShadows = false;
			lr.Color = MakeBeamGradient( color, color );
			lr.Width = new Curve( new Curve.Frame( 0f, lineWidth ), new Curve.Frame( 1f, lineWidth ) );

			var ring = go.AddComponent<PostalVfxRing>();
			ring.Line = lr;
			ring.Center = pos + Vector3.Up * 4f;
			ring.StartRadius = MathF.Max( 8f, maxRadius * 0.14f );
			ring.EndRadius = maxRadius;
			ring.Duration = duration;
			ring.BaseWidth = lineWidth;

			var pl = go.AddComponent<PointLight>();
			pl.LightColor = color * 3.0f;
			pl.Radius = maxRadius * 1.5f;
			pl.Shadows = false;
			ring.FadeLight = pl;
		}
	}

	/// <summary>
	/// Full multi-layered POSTAL: Brain Damaged stylized 3D explosion using authentic PBD textures
	/// (Fire_Explosion, Explosion, Explosion_Smoke, Fire_Explosion_HHG, Electricity_Explosion, Smoke_ElectricCloud)
	/// plus scorch surface decals, Holy Hand Grenade vertical heaven-pillar, and EMP electric arc novas!
	/// </summary>
	public static void SpawnExplosion( Scene scene, Vector3 pos, float radius, Color primaryColor, bool isElectric = false, bool isHoly = false )
	{
		if ( !scene.IsValid() ) return;
		using ( scene.Push() )
		{
			float s = Math.Clamp( radius / 190f, 0.65f, 2.0f );
			var coreCol = isElectric
				? new Color( 0.82f, 0.98f, 1f, 1f )
				: (isHoly ? new Color( 1f, 0.98f, 0.72f, 1f ) : new Color( 1f, 0.96f, 0.74f, 1f ));
			var outerCol = isElectric
				? new Color( 0.22f, 0.84f, 1f, 1f )
				: (isHoly ? new Color( 1f, 0.84f, 0.22f, 1f ) : primaryColor);

			// Project authentic PBD explosion scorch decal onto nearest floor/wall!
			var floorTr = scene.Trace.Ray( pos + Vector3.Up * 18f, pos - Vector3.Up * 96f )
				.WithoutTags( "trigger", "debris", "ragdoll", "npc", "playercontroller", "player" )
				.Run();
			if ( floorTr.Hit )
			{
				SpawnSurfaceDecal(
					scene,
					floorTr.HitPosition,
					floorTr.Normal,
					isElectric ? PostalDecalKind.Lightning : PostalDecalKind.Explosion,
					Math.Clamp( radius * 0.62f, 68f, 220f ) );
			}

			// 1. Blinding stylized burst core using authentic PBD explosion texture + dynamic blast PointLight
			var coreGo = new GameObject( true, "Vfx_ExplosionCore" );
			coreGo.Flags |= GameObjectFlags.NotSaved;
			coreGo.WorldPosition = pos;
			var coreSprite = isElectric ? ElectricExplosionSprite : (isHoly ? HolyEnergySprite : FireExplosionSprite);
			var coreSr = CreateSprite( coreGo, coreSprite, 105f * s, coreCol, additive: true );

			var pl = coreGo.AddComponent<PointLight>();
			pl.LightColor = outerCol * (isHoly ? 9.5f : 7.0f);
			pl.Radius = radius * 2.8f;
			pl.Shadows = false;

			var coreAnim = coreGo.AddComponent<PostalVfxSpriteAnim>();
			coreAnim.Duration = isHoly ? 0.42f : 0.30f;
			coreAnim.Sprite = coreSr;
			coreAnim.StartSize = 48f * s;
			coreAnim.EndSize = (isHoly ? 270f : 215f) * s;
			coreAnim.StartColor = coreCol;
			coreAnim.EndColor = outerCol.WithAlpha( 0f );
			coreAnim.FadeLight = pl;

			// 2. Layered boomer-shooter fireball / holy fire / plasma puffs using authentic PBD textures!
			int fireCount = isHoly ? 8 : 6;
			for ( int i = 0; i < fireCount; i++ )
			{
				var fireGo = new GameObject( true, $"Vfx_Fireball_{i}" );
				fireGo.Flags |= GameObjectFlags.NotSaved;
				fireGo.WorldPosition = pos + Vector3.Random * (26f * s) + Vector3.Up * (i * 11f * s);
				var fireCol = i == 0
					? coreCol
					: (i % 2 == 1 ? outerCol : Color.Lerp( outerCol, isHoly ? Color.White : Color.Red, isElectric ? 0f : 0.4f ));
				var fSprite = isElectric
					? (i % 2 == 0 ? ElectricExplosionSprite : ElectricRingSprite)
					: (isHoly ? HolyFireSprite : (i % 2 == 0 ? ExplosionSprite : ExplosionAltSprite));
				var fSr = CreateSprite( fireGo, fSprite, 86f * s, fireCol, additive: true );

				var fAnim = fireGo.AddComponent<PostalVfxSpriteAnim>();
				fAnim.Duration = (isHoly ? 0.46f : 0.38f) + i * 0.055f;
				fAnim.Sprite = fSr;
				fAnim.StartSize = (46f + i * 11f) * s;
				fAnim.EndSize = (165f + i * 26f) * s;
				fAnim.DriftVelocity = Vector3.Up * (68f + i * 24f) + Vector3.Random * 42f;
				fAnim.StartColor = fireCol;
				fAnim.EndColor = isElectric
					? new Color( 0.08f, 0.25f, 0.95f, 0f )
					: (isHoly ? new Color( 1f, 0.72f, 0.1f, 0f ) : new Color( 0.55f, 0.08f, 0.02f, 0f ));
			}

			// 3. Holy Hand Grenade signature vertical celestial pillar of golden light + double halo!
			if ( isHoly )
			{
				SpawnSingleBeam( pos - Vector3.Up * 12f, pos + Vector3.Up * (520f * s), new Color( 1f, 0.94f, 0.45f, 1f ), 42f * s, 0.65f, jagged: false );
				SpawnSingleBeam( pos - Vector3.Up * 12f, pos + Vector3.Up * (560f * s), Color.White, 16f * s, 0.52f, jagged: false );
				SpawnShockwave( scene, pos + Vector3.Up * 36f, radius * 1.35f, new Color( 1f, 0.95f, 0.4f, 1f ), duration: 0.42f, lineWidth: 11f * s );
			}

			// 4. Electric arc nova OR dark billowing PBD smoke plumes
			if ( isElectric )
			{
				for ( int a = 0; a < 11; a++ )
				{
					var end = pos + Vector3.Random.Normal * (radius * Game.Random.Float( 0.55f, 1.15f ));
					SpawnSingleBeam( pos, end, outerCol, 4.2f, 0.30f, jagged: true, jitterScale: 0.08f );
					if ( a < 5 )
						SpawnSingleBeam( pos, end, Color.White, 1.8f, 0.24f, jagged: true, jitterScale: 0.06f );
				}
				for ( int i = 0; i < 4; i++ )
				{
					var eCloudGo = new GameObject( true, $"Vfx_ElectricCloud_{i}" );
					eCloudGo.Flags |= GameObjectFlags.NotSaved;
					eCloudGo.WorldPosition = pos + Vector3.Random * (28f * s);
					var ecCol = new Color( 0.25f, 0.78f, 1f, 0.75f );
					var ecSr = CreateSprite( eCloudGo, ElectricCloudSprite, 90f * s, ecCol, additive: true );
					var ecAnim = eCloudGo.AddComponent<PostalVfxSpriteAnim>();
					ecAnim.Duration = Game.Random.Float( 0.5f, 0.85f );
					ecAnim.Sprite = ecSr;
					ecAnim.StartSize = 60f * s;
					ecAnim.EndSize = 195f * s;
					ecAnim.DriftVelocity = Vector3.Up * 45f + Vector3.Random * 40f;
					ecAnim.StartColor = ecCol;
					ecAnim.EndColor = new Color( 0.1f, 0.35f, 0.95f, 0f );
				}
			}
			else
			{
				for ( int i = 0; i < 6; i++ )
				{
					var smokeGo = new GameObject( true, $"Vfx_Smoke_{i}" );
					smokeGo.Flags |= GameObjectFlags.NotSaved;
					smokeGo.WorldPosition = pos + Vector3.Random * (30f * s) + Vector3.Up * (18f * s);
					var sCol = isHoly ? new Color( 0.95f, 0.88f, 0.65f, 0.55f ) : new Color( 0.18f, 0.17f, 0.16f, 0.85f );
					var sSr = CreateSprite( smokeGo, ExplosionSmokeSprite, 92f * s, sCol, additive: false );

					var sAnim = smokeGo.AddComponent<PostalVfxSpriteAnim>();
					sAnim.Duration = Game.Random.Float( 0.75f, 1.25f );
					sAnim.Sprite = sSr;
					sAnim.StartSize = 58f * s;
					sAnim.EndSize = 205f * s;
					sAnim.DriftVelocity = Vector3.Up * Game.Random.Float( 72f, 145f ) + Vector3.Random.WithZ( 0 ) * 55f;
					sAnim.StartColor = sCol;
					sAnim.EndColor = sCol.WithAlpha( 0f );
				}
			}

			// 5. Ground blast shockwave ring
			SpawnShockwave( scene, pos, radius * 1.02f, outerCol, duration: 0.30f, lineWidth: 8.5f * s );

			// 6. Flying glowing embers / sparks!
			SpawnSparkParticles( pos + Vector3.Up * 8f, Vector3.Up, isElectric ? coreCol : (isHoly ? Color.Yellow : new Color( 1f, 0.74f, 0.22f, 1f )), count: 16, speedMin: 260f, speedMax: 640f, size: 15f * s );
		}
	}

	/// <summary>
	/// Crisp metallic/ricochet spark burst on wall/prop impacts.
	/// </summary>
	public static void SpawnImpactSparks( Scene scene, Vector3 pos, Vector3 normal, Color color, int count = 6 )
	{
		if ( !scene.IsValid() ) return;
		using ( scene.Push() )
		{
			var n = normal.LengthSquared > 0.001f ? normal.Normal : Vector3.Up;

			var flash = new GameObject( true, "Vfx_ImpactFlash" );
			flash.Flags |= GameObjectFlags.NotSaved;
			flash.WorldPosition = pos + n * 2f;
			var sr = CreateSprite( flash, MuzzleStarSprite, 14f, Color.Lerp( color, Color.White, 0.55f ), additive: true );

			var anim = flash.AddComponent<PostalVfxSpriteAnim>();
			anim.Duration = 0.08f;
			anim.Sprite = sr;
			anim.StartSize = 16f;
			anim.EndSize = 2f;
			anim.StartColor = Color.Lerp( color, Color.White, 0.5f );
			anim.EndColor = color.WithAlpha( 0f );

			SpawnSparkParticles( pos + n * 2f, n, color, count, 140f, 360f, 9f );
		}
	}

	/// <summary>
	/// Juicy POSTAL: Brain Damaged crimson blood mist + flying arterial blood droplets + floor/wall blood decals!
	/// </summary>
	public static void SpawnBloodSplatter( Scene scene, Vector3 pos, Vector3 hitDir, bool heavy = false )
	{
		if ( !scene.IsValid() ) return;
		using ( scene.Push() )
		{
			var dir = hitDir.LengthSquared > 0.001f ? hitDir.Normal : Vector3.Up;

			// Project authentic PBD blood splat decal onto wall behind victim or floor underneath!
			var backTr = scene.Trace.Ray( pos, pos + dir * 135f )
				.WithoutTags( "trigger", "debris", "ragdoll", "npc", "playercontroller", "player" )
				.Run();
			if ( backTr.Hit )
			{
				SpawnSurfaceDecal( scene, backTr.HitPosition, backTr.Normal, PostalDecalKind.Blood, heavy ? Game.Random.Float( 40f, 62f ) : Game.Random.Float( 22f, 36f ) );
			}
			else if ( heavy )
			{
				var floorTr = scene.Trace.Ray( pos, pos - Vector3.Up * 110f )
					.WithoutTags( "trigger", "debris", "ragdoll", "npc", "playercontroller", "player" )
					.Run();
				if ( floorTr.Hit )
					SpawnSurfaceDecal( scene, floorTr.HitPosition, floorTr.Normal, PostalDecalKind.Blood, Game.Random.Float( 42f, 68f ) );
			}

			// Crimson arterial mist cloud
			int mists = heavy ? 3 : 1;
			for ( int m = 0; m < mists; m++ )
			{
				var mist = new GameObject( true, "Vfx_BloodMist" );
				mist.Flags |= GameObjectFlags.NotSaved;
				mist.WorldPosition = pos + Vector3.Random * (heavy ? 12f : 3f);
				var cStart = new Color( 0.68f, 0.02f, 0.02f, 0.92f );
				var mmr = CreateSprite( mist, BloodSplatSprite, heavy ? 36f : 18f, cStart, additive: false );

				var mAnim = mist.AddComponent<PostalVfxSpriteAnim>();
				mAnim.Duration = heavy ? 0.44f : 0.26f;
				mAnim.Sprite = mmr;
				mAnim.StartSize = heavy ? 22f : 12f;
				mAnim.EndSize = heavy ? 68f : 32f;
				mAnim.DriftVelocity = dir * 65f + Vector3.Up * 25f + Vector3.Random * 25f;
				mAnim.StartColor = cStart;
				mAnim.EndColor = new Color( 0.35f, 0.01f, 0.01f, 0f );
			}

			// Flying crimson blood splatters & gore droplets!
			int count = heavy ? 14 : 6;
			for ( int i = 0; i < count; i++ )
			{
				var drop = new GameObject( true, "Vfx_BloodDrop" );
				drop.Flags |= GameObjectFlags.NotSaved;
				drop.WorldPosition = pos + Vector3.Random * (heavy ? 10f : 4f);
				float sz = heavy ? Game.Random.Float( 9f, 19f ) : Game.Random.Float( 5f, 11f );
				var col = (i % 2 == 0) ? new Color( 0.78f, 0.02f, 0.02f, 0.96f ) : new Color( 0.48f, 0.01f, 0.02f, 0.96f );
				var dSr = CreateSprite( drop, BloodSplatSprite, sz, col, additive: false );

				var part = drop.AddComponent<PostalVfxBallistic>();
				var vDir = (dir * 0.65f + Vector3.Random * 0.85f + Vector3.Up * Game.Random.Float( 0.25f, 0.78f )).Normal;
				part.Velocity = vDir * Game.Random.Float( heavy ? 160f : 110f, heavy ? 430f : 270f );
				part.Gravity = 780f;
				part.Duration = Game.Random.Float( heavy ? 0.5f : 0.32f, heavy ? 0.95f : 0.56f );
				part.Sprite = dSr;
				part.StartSize = sz;
			}
		}
	}

	static void SpawnSparkParticles( Vector3 pos, Vector3 normal, Color color, int count, float speedMin, float speedMax, float size )
	{
		for ( int i = 0; i < count; i++ )
		{
			var sp = new GameObject( true, "Vfx_Spark" );
			sp.Flags |= GameObjectFlags.NotSaved;
			sp.WorldPosition = pos + Vector3.Random * 3f;

			var col = Color.Lerp( color, Color.White, Game.Random.Float( 0.25f, 0.75f ) );
			var sr = CreateSprite( sp, SoftGlowSprite, size, col, additive: true );

			var part = sp.AddComponent<PostalVfxBallistic>();
			var vDir = (normal * 0.7f + Vector3.Random * 0.9f + Vector3.Up * 0.25f).Normal;
			part.Velocity = vDir * Game.Random.Float( speedMin, speedMax );
			part.Gravity = 680f;
			part.Duration = Game.Random.Float( 0.18f, 0.44f );
			part.Sprite = sr;
			part.StartSize = size;
		}
	}

	/// <summary>
	/// Attaches native TrailRenderer + glowing sprite aura + PointLight to launched projectiles!
	/// </summary>
	public static void AttachProjectileVisuals( GameObject projGo, PostalProjectileVisualKind kind, Color color )
	{
		if ( !projGo.IsValid() ) return;

		var tr = projGo.AddComponent<TrailRenderer>();
		tr.Opaque = false;
		tr.CastShadows = false;
		tr.PointDistance = 6f;

		switch ( kind )
		{
			case PostalProjectileVisualKind.Rocket:
				{
					tr.LifeTime = 0.44f;
					tr.Width = new Curve( new Curve.Frame( 0f, 9f ), new Curve.Frame( 0.35f, 12f ), new Curve.Frame( 1f, 0.5f ) );
					tr.Color = MakeBeamGradient( new Color( 1f, 0.65f, 0.15f, 0.95f ), new Color( 0.45f, 0.4f, 0.38f, 0.45f ) );

					var glowGo = new GameObject( projGo, true, "RocketGlow" );
					CreateSprite( glowGo, FireExplosionSprite, 24f, new Color( 1f, 0.65f, 0.18f, 1f ), additive: true );

					var pl = projGo.AddComponent<PointLight>();
					pl.LightColor = new Color( 1f, 0.55f, 0.12f, 1f ) * 3.5f;
					pl.Radius = 220f;
					pl.Shadows = false;
					break;
				}

			case PostalProjectileVisualKind.Grenade:
				{
					tr.LifeTime = 0.28f;
					tr.Width = new Curve( new Curve.Frame( 0f, 5f ), new Curve.Frame( 1f, 0f ) );
					tr.Color = MakeBeamGradient( color, color.WithAlpha( 0.25f ) );

					var glowGo = new GameObject( projGo, true, "GrenadeGlow" );
					CreateSprite( glowGo, SoftGlowSprite, 20f, color, additive: true );

					var pl = projGo.AddComponent<PointLight>();
					pl.LightColor = color * 2.6f;
					pl.Radius = 155f;
					pl.Shadows = false;
					break;
				}

			case PostalProjectileVisualKind.Timebomb:
				{
					tr.LifeTime = 0.28f;
					tr.Width = new Curve( new Curve.Frame( 0f, 4.5f ), new Curve.Frame( 1f, 0f ) );
					tr.Color = MakeBeamGradient( new Color( 0.25f, 0.92f, 1f, 0.95f ), new Color( 0.1f, 0.45f, 1f, 0.25f ) );

					var glowGo = new GameObject( projGo, true, "BombGlow" );
					CreateSprite( glowGo, TimeBubbleCircleSprite, 20f, new Color( 0.3f, 0.92f, 1f, 0.95f ), additive: true );

					var pl = projGo.AddComponent<PointLight>();
					pl.LightColor = new Color( 0.22f, 0.85f, 1f, 1f ) * 3.2f;
					pl.Radius = 165f;
					pl.Shadows = false;
					break;
				}

			case PostalProjectileVisualKind.DildoBolt:
				{
					tr.LifeTime = 0.24f;
					tr.Width = new Curve( new Curve.Frame( 0f, 3.8f ), new Curve.Frame( 1f, 0f ) );
					tr.Color = MakeBeamGradient( new Color( 1f, 0.32f, 0.86f, 0.92f ), new Color( 1f, 0.68f, 0.95f, 0.25f ) );

					var glowGo = new GameObject( projGo, true, "DildoGlow" );
					CreateSprite( glowGo, SoftGlowSprite, 14f, new Color( 1f, 0.35f, 0.88f, 0.85f ), additive: true );

					var pl = projGo.AddComponent<PointLight>();
					pl.LightColor = new Color( 1f, 0.28f, 0.85f, 1f ) * 2.0f;
					pl.Radius = 120f;
					pl.Shadows = false;
					break;
				}

			case PostalProjectileVisualKind.Cat:
				{
					tr.LifeTime = 0.32f;
					tr.Width = new Curve( new Curve.Frame( 0f, 6.5f ), new Curve.Frame( 1f, 0f ) );
					tr.Color = MakeBeamGradient( new Color( 1f, 0.78f, 0.35f, 0.85f ), new Color( 1f, 0.45f, 0.15f, 0.25f ) );
					break;
				}

			case PostalProjectileVisualKind.LightningOrb:
				{
					tr.LifeTime = 0.38f;
					tr.Width = new Curve( new Curve.Frame( 0f, 15f ), new Curve.Frame( 1f, 0f ) );
					tr.Color = MakeBeamGradient( new Color( 0.65f, 0.96f, 1f, 0.95f ), new Color( 0.15f, 0.55f, 1f, 0.2f ) );

					var glowGo = new GameObject( projGo, true, "OrbGlow" );
					CreateSprite( glowGo, ElectricExplosionSprite, 52f, new Color( 0.42f, 0.92f, 1f, 1f ), additive: true );
					var ringGo = new GameObject( projGo, true, "OrbRing" );
					CreateSprite( ringGo, ElectricRingSprite, 40f, Color.White, additive: true );

					var pl = projGo.AddComponent<PointLight>();
					pl.LightColor = new Color( 0.3f, 0.88f, 1f, 1f ) * 5.5f;
					pl.Radius = 340f;
					pl.Shadows = false;
					break;
				}

			default:
				{
					tr.LifeTime = 0.18f;
					tr.Width = new Curve( new Curve.Frame( 0f, 3.8f ), new Curve.Frame( 1f, 0f ) );
					tr.Color = MakeBeamGradient( color, color.WithAlpha( 0.2f ) );
					var glowGo = new GameObject( projGo, true, "ProjGlow" );
					CreateSprite( glowGo, SoftGlowSprite, 14f, color, additive: true );
					break;
				}
		}
	}
}

/// <summary>
/// Authentic POSTAL: Brain Damaged stuck 3D Dildo Bow bolt that wobbles elastically ("boing-boing")
/// when suction-cupped to walls, floors, doors, props, or living NPCs!
/// </summary>
public sealed class PostalStuckDildo : Component
{
	public GameObject VisualChild { get; set; }
	public Rotation BaseRotation { get; set; }
	public GameObject AttachTarget { get; set; }
	public Vector3 LocalPosOffset { get; set; }
	public Rotation LocalRotOffset { get; set; }
	public float Duration { get; set; } = 28f;

	float _age;
	float _wobblePhase;

	protected override void OnStart()
	{
		_wobblePhase = Game.Random.Float( 0f, MathF.PI * 2f );
	}

	protected override void OnUpdate()
	{
		_age += Time.Delta;
		if ( _age >= Duration )
		{
			GameObject.Destroy();
			return;
		}

		if ( AttachTarget is not null )
		{
			if ( !AttachTarget.IsValid() )
			{
				GameObject.Destroy();
				return;
			}
			WorldPosition = AttachTarget.WorldTransform.PointToWorld( LocalPosOffset );
			BaseRotation = AttachTarget.WorldRotation * LocalRotOffset;
		}

		// Signature damped spring rubber wobble ("boing-boing") right after sticking!
		float decay = MathF.Exp( -_age * 2.4f );
		float amp = decay * 22f;
		float pitchWobble = MathF.Sin( _age * 26f + _wobblePhase ) * amp;
		float yawWobble = MathF.Cos( _age * 21f + _wobblePhase ) * (amp * 0.65f);
		WorldRotation = BaseRotation * Rotation.From( new Angles( pitchWobble, yawWobble, 0f ) );
	}
}

/// <summary>
/// Authentic POSTAL: Brain Damaged SMG Timebomb Temporal Slow-Mo Bubble Field!
/// Expands a translucent glowing cyan-blue dome with 3 rotating orbital clock rings, slows all NPCs,
/// projectiles, and physics objects inside to 0.15x time speed, then implodes and detonates with nail shrapnel.
/// </summary>
public sealed class PostalTimeBubble : Component
{
	public GameObject DomeVisual { get; set; }
	public SpriteRenderer CoreSprite { get; set; }
	public PointLight Light { get; set; }
	public float Radius { get; set; } = 245f;
	public float Duration { get; set; } = 3.5f;
	public GameObject Attacker { get; set; }
	public GameObject Weapon { get; set; }
	public float FinalDamage { get; set; } = 135f;

	readonly List<LineRenderer> _orbitRings = [];
	float _age;
	float _nextTickBeep;
	bool _detonated;

	protected override void OnStart()
	{
		var ringColors = new[]
		{
			new Color( 0.25f, 0.92f, 1f, 0.95f ),
			new Color( 0.55f, 0.82f, 1f, 0.85f ),
			new Color( 0.85f, 0.98f, 1f, 0.9f ),
		};

		for ( int i = 0; i < 3; i++ )
		{
			var ringGo = new GameObject( GameObject, true, $"OrbitRing_{i}" );
			ringGo.Flags |= GameObjectFlags.NotSaved;
			var lr = ringGo.AddComponent<LineRenderer>();
			lr.UseVectorPoints = true;
			lr.Additive = true;
			lr.Opaque = false;
			lr.Lighting = false;
			lr.CastShadows = false;
			lr.Color = new Gradient( new Gradient.ColorFrame( 0f, ringColors[i] ), new Gradient.ColorFrame( 1f, ringColors[i] ) );
			lr.Width = new Curve( new Curve.Frame( 0f, 4.5f ), new Curve.Frame( 1f, 4.5f ) );
			_orbitRings.Add( lr );
		}
	}

	protected override void OnUpdate()
	{
		if ( _detonated ) return;

		_age += Time.Delta;
		float remain = Duration - _age;
		if ( remain <= 0f )
		{
			DetonateBubble();
			return;
		}

		// Smooth pop-in expansion -> steady pulsing dome -> rapid collapse right before detonation!
		float scaleT;
		if ( _age < 0.22f )
		{
			float u = _age / 0.22f;
			scaleT = 1f - (1f - u) * (1f - u) * (1f - u);
		}
		else if ( remain < 0.24f )
		{
			scaleT = Math.Clamp( remain / 0.24f, 0.15f, 1f );
		}
		else
		{
			scaleT = 1f + MathF.Sin( _age * 7f ) * 0.03f;
		}

		float curRadius = Radius * scaleT;
		if ( DomeVisual.IsValid() )
		{
			DomeVisual.LocalScale = new Vector3( curRadius );
			DomeVisual.LocalRotation = Rotation.From( new Angles( _age * 16f, _age * 38f, _age * 11f ) );
		}

		if ( CoreSprite.IsValid() )
		{
			float coreSz = (36f + MathF.Sin( _age * 12f ) * 10f) * scaleT;
			CoreSprite.Size = new Vector2( coreSz, coreSz );
		}

		if ( Light.IsValid() )
			Light.LightColor = new Color( 0.2f, 0.85f, 1f, 1f ) * (4.6f + MathF.Sin( _age * 10f ) * 1.2f);

		// Update 3 rotating 3D orbital energy rings around the temporal dome
		for ( int r = 0; r < _orbitRings.Count; r++ )
		{
			var lr = _orbitRings[r];
			if ( !lr.IsValid() ) continue;
			var rot = r switch
			{
				0 => Rotation.From( new Angles( 25f, _age * 75f, 15f ) ),
				1 => Rotation.From( new Angles( -55f, -_age * 62f, 35f ) ),
				_ => Rotation.From( new Angles( 78f, _age * 48f, -45f ) )
			};
			const int segs = 24;
			var pts = new List<Vector3>( segs + 1 );
			float rr = curRadius * (0.96f - r * 0.06f);
			for ( int i = 0; i <= segs; i++ )
			{
				float a = (i / (float)segs) * MathF.PI * 2f;
				pts.Add( WorldPosition + rot * new Vector3( MathF.Cos( a ) * rr, MathF.Sin( a ) * rr, 0f ) );
			}
			lr.VectorPoints = pts;
		}

		// Accelerating clock tick sound as detonation approaches!
		if ( Time.Now >= _nextTickBeep )
		{
			_nextTickBeep = Time.Now + Math.Clamp( remain * 0.16f, 0.11f, 0.45f );
			float pitch = Math.Clamp( 1.0f + (1f - remain / Duration) * 0.55f, 1f, 1.6f );
			UnitySoundUtil.PlayAt( "mount://pbd/sounds/player/bells_handbell_small_reception_desk_bell_single_ring_01_snd14969_1.vsnd", WorldPosition, 0.78f, 1400f, pitch, pitch );
		}

		// TEMPORAL SLOW-MO FIELD: slow every NPC, projectile, and loose prop inside the bubble!
		foreach ( var npc in Scene.GetAllComponents<PostalNpc>() )
		{
			if ( !npc.IsValid() || npc.IsDead ) continue;
			if ( npc.WorldPosition.Distance( WorldPosition ) <= curRadius + 24f )
				npc.ApplyTemporalSlow( 0.15f, 0.25f );
		}

		foreach ( var proj in Scene.GetAllComponents<PostalProjectile>() )
		{
			if ( !proj.IsValid() ) continue;
			if ( proj.WorldPosition.Distance( WorldPosition ) <= curRadius + 18f )
				proj.ApplyTemporalSlow( 0.12f, 0.25f );
		}

		foreach ( var go in Scene.FindInPhysics( new Sphere( WorldPosition, curRadius ) ) )
		{
			if ( !go.IsValid() || go.Tags.Has( "world" ) || go.Tags.Has( "player" ) || go.Tags.Has( "playercontroller" ) ) continue;
			var rb = go.Components.GetInAncestorsOrSelf<Rigidbody>();
			if ( rb.IsValid() && !rb.IsProxy && rb.MotionEnabled && rb.Velocity.LengthSquared > 1600f )
				rb.Velocity *= MathF.Pow( 0.18f, Time.Delta * 6f );
		}
	}

	void DetonateBubble()
	{
		_detonated = true;
		var pos = WorldPosition;

		UnitySoundUtil.PlayAt( "mount://pbd/sounds/player/explosion_med_long_tail_01.vsnd", pos, 1.0f, 3400f );
		UnitySoundUtil.PlayAt( "mount://pbd/sounds/player/time_warp_stop_02_mono.vsnd", pos, 0.92f, 2200f, 0.85f, 0.95f );

		PostalNpc.AlertCiviliansNear( Scene, pos, 1400f );
		PostalVfx.SpawnExplosion( Scene, pos, Radius * 1.05f, new Color( 0.28f, 0.88f, 1f, 1f ), isElectric: true );

		// 360° flying nail shrapnel streaks erupting out of the collapsed TimeBubble!
		for ( int n = 0; n < 16; n++ )
		{
			var ndir = Vector3.Random.Normal;
			PostalVfx.SpawnTracerBeam( Scene, pos, pos + ndir * Game.Random.Float( 170f, 310f ), new Color( 0.55f, 0.95f, 1f, 1f ), width: 2.2f, duration: 0.18f );
		}

		var hitRoots = new HashSet<GameObject>();
		foreach ( var go in Scene.FindInPhysics( new Sphere( pos, Radius * 1.05f ) ) )
		{
			if ( !go.IsValid() || !hitRoots.Add( go.Root ) ) continue;
			if ( Attacker.IsValid() && go.Root == Attacker.Root ) continue;

			var delta = (go.WorldPosition + Vector3.Up * 24f) - pos;
			float dist = delta.Length;
			float falloff = Math.Clamp( 1f - dist / (Radius * 1.05f), 0.22f, 1f );
			var dir = dist > 1f ? delta / dist : Vector3.Up;

			var info = new DamageInfo( FinalDamage * falloff, Attacker, Weapon ) { Position = pos };
			info.Tags.Add( DamageTags.Explosion );
			foreach ( var d in go.Components.GetAll<Component.IDamageable>( FindMode.EverythingInSelfAndAncestors ) )
				d.OnDamage( info );

			var rb = go.Components.GetInAncestorsOrSelf<Rigidbody>();
			if ( rb.IsValid() && !rb.IsProxy )
				rb.ApplyImpulse( (dir + Vector3.Up * 0.45f).Normal * 3400f * falloff * Math.Clamp( rb.Mass, 1f, 60f ) );
		}

		GameObject.Destroy();
	}
}

public sealed class PostalVfxTimedDestroy : Component
{
	public float Duration { get; set; } = 25f;
	float _age;

	protected override void OnUpdate()
	{
		_age += Time.Delta;
		if ( _age >= Duration )
			GameObject.Destroy();
	}
}

public sealed class PostalVfxSpriteAnim : Component
{
	public float Duration { get; set; } = 0.25f;
	public SpriteRenderer Sprite { get; set; }
	public float StartSize { get; set; } = 24f;
	public float EndSize { get; set; } = 96f;
	public Vector3 DriftVelocity { get; set; }
	public Color StartColor { get; set; } = Color.White;
	public Color EndColor { get; set; } = Color.Transparent;
	public PointLight FadeLight { get; set; }

	float _age;
	Color _baseLight;

	protected override void OnStart()
	{
		if ( FadeLight.IsValid() ) _baseLight = FadeLight.LightColor;
	}

	protected override void OnUpdate()
	{
		_age += Time.Delta;
		float t = Math.Clamp( _age / MathF.Max( Duration, 0.01f ), 0f, 1f );
		float easeOut = 1f - (1f - t) * (1f - t);

		if ( Sprite.IsValid() )
		{
			float sz = StartSize.LerpTo( EndSize, easeOut );
			Sprite.Size = new Vector2( sz, sz );
			Sprite.Color = Color.Lerp( StartColor, EndColor, t );
		}

		if ( DriftVelocity.LengthSquared > 0.01f )
			WorldPosition += DriftVelocity * Time.Delta;

		if ( FadeLight.IsValid() )
			FadeLight.LightColor = _baseLight * (1f - t);

		if ( t >= 1f )
			GameObject.Destroy();
	}
}

public sealed class PostalVfxBeamFade : Component
{
	public float Duration { get; set; } = 0.08f;
	public LineRenderer Line { get; set; }
	public float BaseWidth { get; set; } = 2f;

	float _age;

	protected override void OnUpdate()
	{
		_age += Time.Delta;
		float t = Math.Clamp( _age / MathF.Max( Duration, 0.01f ), 0f, 1f );
		if ( Line.IsValid() )
		{
			float w = BaseWidth * (1f - t * 0.85f);
			Line.Width = new Curve( new Curve.Frame( 0f, w ), new Curve.Frame( 1f, w * 0.3f ) );
		}
		if ( t >= 1f )
			GameObject.Destroy();
	}
}

public sealed class PostalVfxRing : Component
{
	public LineRenderer Line { get; set; }
	public PointLight FadeLight { get; set; }
	public Vector3 Center { get; set; }
	public float StartRadius { get; set; } = 12f;
	public float EndRadius { get; set; } = 180f;
	public float Duration { get; set; } = 0.24f;
	public float BaseWidth { get; set; } = 5f;

	float _age;
	Color _baseLight;

	protected override void OnStart()
	{
		if ( FadeLight.IsValid() ) _baseLight = FadeLight.LightColor;
		UpdateRing( 0f );
	}

	protected override void OnUpdate()
	{
		_age += Time.Delta;
		float t = Math.Clamp( _age / MathF.Max( Duration, 0.01f ), 0f, 1f );
		UpdateRing( t );
		if ( t >= 1f )
			GameObject.Destroy();
	}

	void UpdateRing( float t )
	{
		if ( !Line.IsValid() ) return;
		float ease = 1f - (1f - t) * (1f - t);
		float r = StartRadius.LerpTo( EndRadius, ease );
		const int segs = 22;
		var pts = new List<Vector3>( segs + 1 );
		for ( int i = 0; i <= segs; i++ )
		{
			float ang = (i / (float)segs) * MathF.PI * 2f;
			pts.Add( Center + new Vector3( MathF.Cos( ang ) * r, MathF.Sin( ang ) * r, 0f ) );
		}
		Line.VectorPoints = pts;
		float w = BaseWidth * (1f - t);
		Line.Width = new Curve( new Curve.Frame( 0f, w ), new Curve.Frame( 1f, w ) );
		if ( FadeLight.IsValid() )
			FadeLight.LightColor = _baseLight * (1f - t);
	}
}

public sealed class PostalVfxBallistic : Component
{
	public Vector3 Velocity { get; set; }
	public float Gravity { get; set; } = 720f;
	public float Duration { get; set; } = 0.4f;
	public SpriteRenderer Sprite { get; set; }
	public float StartSize { get; set; } = 10f;

	float _age;
	Color _baseColor;

	protected override void OnStart()
	{
		if ( Sprite.IsValid() ) _baseColor = Sprite.Color;
	}

	protected override void OnUpdate()
	{
		_age += Time.Delta;
		float t = Math.Clamp( _age / MathF.Max( Duration, 0.01f ), 0f, 1f );

		Velocity += Vector3.Down * Gravity * Time.Delta;
		WorldPosition += Velocity * Time.Delta;

		if ( Sprite.IsValid() && t > 0.55f )
		{
			float fade = Math.Clamp( (1f - t) / 0.45f, 0f, 1f );
			float sz = StartSize * Math.Clamp( fade, 0.25f, 1f );
			Sprite.Size = new Vector2( sz, sz );
			Sprite.Color = _baseColor.WithAlpha( _baseColor.a * fade );
		}

		if ( t >= 1f )
			GameObject.Destroy();
	}
}

/// <summary>
/// Persistent physical cat launched by the Cat Cannon that stays in the world so the Cat Vacuum can pull &amp; reload it.
/// </summary>
public sealed class PostalWorldCat : Component
{
	public Rigidbody Body { get; set; }
	public float Age { get; private set; }
	float _nextMeowTime;

	protected override void OnStart()
	{
		_nextMeowTime = Time.Now + Game.Random.Float( 4f, 11f );
	}

	protected override void OnUpdate()
	{
		Age += Time.Delta;
		if ( Age >= 60f )
		{
			GameObject.Destroy();
			return;
		}

		if ( Time.Now >= _nextMeowTime )
		{
			_nextMeowTime = Time.Now + Game.Random.Float( 7f, 16f );
			UnitySoundUtil.PlayAt( "mount://pbd/sounds/player/vacuum_idle_meow-006.vsnd", WorldPosition, 0.5f, 700f, 0.92f, 1.12f );
		}
	}
}
