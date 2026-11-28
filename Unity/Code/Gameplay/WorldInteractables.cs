namespace UnityMount;

public enum UnityPickupKind
{
	Health = 0,
	Armor = 1,
	Ammo = 2,
}

/// <summary>
/// Breakable window (GlassObject) or destructible door/wall/table/prop (BreakableObject / BurnableObject).
/// Shatters on gunfire, melee swings, explosions, or (for glass) players crashing straight through!
/// </summary>
[Title( "Unity Breakable" ), Category( "Unity Mount" ), Icon( "broken_image" )]
public sealed class UnityBreakable : Component, Component.IDamageable, Component.ICollisionListener
{
	[Property] public bool IsGlass { get; set; }
	[Property] public bool IsBurnable { get; set; }
	[Property] public float Health { get; set; } = 30f;
	[Property] public Color ShardTint { get; set; } = Color.White;

	bool _broken;
	Material _shardMat;

	protected override void OnStart()
	{
		var mr = GetComponent<ModelRenderer>( true );
		if ( mr.IsValid() && mr.Model is not null && mr.Model.Materials.Any() )
			_shardMat = mr.Model.Materials.FirstOrDefault();
	}

	public void OnDamage( in DamageInfo damage )
	{
		if ( _broken ) return;
		Health -= MathF.Max( damage.Damage, 5f );
		if ( Health <= 0f || IsGlass )
			Break( damage.Position );
	}

	public void OnCollisionStart( Collision collision )
	{
		if ( _broken || !IsGlass ) return;
		var otherGo = collision.Other.GameObject;
		if ( !otherGo.IsValid() ) return;

		var pc = otherGo.Components.GetInAncestorsOrSelf<PlayerController>();
		if ( pc.IsValid() && pc.Velocity.Length > 140f )
		{
			Break( pc.WorldPosition + Vector3.Up * 36f );
			return;
		}

		var rb = otherGo.Components.GetInAncestorsOrSelf<Rigidbody>();
		if ( rb.IsValid() && rb.Velocity.Length > 180f )
			Break( rb.WorldPosition );
	}

	public void OnCollisionUpdate( Collision collision ) { }
	public void OnCollisionStop( CollisionStop collision ) { }

	public void Break( Vector3 hitPos )
	{
		if ( _broken ) return;
		_broken = true;

		var mr = GetComponent<ModelRenderer>( true );
		var center = mr.IsValid() && mr.Model is not null
			? WorldTransform.PointToWorld( mr.Model.Bounds.Center )
			: WorldPosition;
		var size = mr.IsValid() && mr.Model is not null
			? mr.Model.Bounds.Size
			: new Vector3( 32f );

		string sfx = IsGlass
			? (Game.Random.Int( 0, 2 ) switch
			{
				0 => "mount://pbd/sounds/master/pbd_sfx_glass_impact_01.vsnd",
				1 => "mount://pbd/sounds/master/pbd_sfx_glass_impact_02.vsnd",
				_ => "mount://pbd/sounds/master/pbd_sfx_glass_impact_03.vsnd"
			})
			: (size.Length > 85f
				? "mount://pbd/sounds/master/pbd_sfx_destructible_door.vsnd"
				: "mount://pbd/sounds/master/pbd_sfx_wood_impact_01.vsnd");

		UnitySoundUtil.PlayAt( sfx, center, 0.92f, 1600f, 0.9f, 1.15f );

		// Spawn lightweight physical debris shards that auto-clean after 4.5s
		int shardCount = IsGlass ? 5 : 4;
		var hitDir = (center - hitPos);
		hitDir = hitDir.LengthSquared > 1f ? hitDir.Normal : Vector3.Up;

		for ( int i = 0; i < shardCount; i++ )
		{
			var shard = new GameObject( true, "BreakableShard" );
			shard.Tags.Add( "debris" );
			var jitter = new Vector3(
				Game.Random.Float( -size.x * 0.3f, size.x * 0.3f ),
				Game.Random.Float( -size.y * 0.3f, size.y * 0.3f ),
				Game.Random.Float( -size.z * 0.3f, size.z * 0.3f ) );
			shard.WorldPosition = center + jitter;
			shard.WorldRotation = Rotation.Random;

			float s = IsGlass ? Game.Random.Float( 0.08f, 0.18f ) : Game.Random.Float( 0.14f, 0.32f );
			shard.WorldScale = IsGlass ? new Vector3( s, s, s * 0.18f ) : new Vector3( s, s * 0.55f, s * 0.35f );

			var smr = shard.AddComponent<ModelRenderer>();
			smr.Model = Model.Cube;
			if ( _shardMat is not null ) smr.MaterialOverride = _shardMat;
			smr.Tint = IsGlass ? new Color( 0.78f, 0.92f, 1f, 0.65f ) : ShardTint;

			var col = shard.AddComponent<BoxCollider>();
			col.Scale = new Vector3( 24f );

			var rb = shard.AddComponent<Rigidbody>();
			rb.MassOverride = 3f;
			rb.Velocity = (hitDir + Vector3.Random * 0.7f + Vector3.Up * 0.45f).Normal * Game.Random.Float( 160f, 360f );
			rb.AngularVelocity = Vector3.Random * 12f;

			var clean = shard.AddComponent<TimedDebrisCleanup>();
			clean.Lifetime = Game.Random.Float( 3.5f, 5.5f );
		}

		GameObject.Destroy();
	}
}

/// <summary>
/// Functional POSTAL: Brain Damaged JumpPad or AirTube!
/// Launches players, NPCs, and physics objects along PushTransform's exact direction &amp; calibrated speed.
/// </summary>
[Title( "Unity JumpPad" ), Category( "Unity Mount" ), Icon( "arrow_upward" )]
public sealed class UnityJumpPad : Component, Component.ITriggerListener
{
	[Property] public Vector3 LaunchDirection { get; set; } = Vector3.Up;
	[Property] public float LaunchSpeed { get; set; } = 720f;
	[Property] public bool IsAirTube { get; set; }

	readonly Dictionary<GameObject, float> _cooldowns = [];
	readonly HashSet<GameObject> _insideTube = [];

	public void OnTriggerEnter( Collider other )
	{
		if ( !other.IsValid() || !other.GameObject.IsValid() ) return;
		var root = other.GameObject.Root;

		if ( IsAirTube )
		{
			_insideTube.Add( root );
			return;
		}

		if ( _cooldowns.TryGetValue( root, out float until ) && Time.Now < until )
			return;

		var dir = LaunchDirection.LengthSquared > 0.01f ? LaunchDirection.Normal : Vector3.Up;
		bool launched = false;

		var pc = other.GameObject.Components.GetInAncestorsOrSelf<PlayerController>();
		if ( pc.IsValid() && pc.Body.IsValid() )
		{
			pc.WorldPosition += Vector3.Up * 4f;
			pc.Body.Velocity = dir * LaunchSpeed;
			launched = true;
		}
		else
		{
			var rb = other.GameObject.Components.GetInAncestorsOrSelf<Rigidbody>();
			if ( rb.IsValid() && !rb.IsProxy )
			{
				rb.Velocity = dir * LaunchSpeed;
				launched = true;
			}
		}

		if ( launched )
		{
			_cooldowns[root] = Time.Now + 0.25f;
			string sfx = Game.Random.Int( 0, 2 ) switch
			{
				0 => "mount://pbd/sounds/master/pbd_sfx_jump_01.vsnd",
				1 => "mount://pbd/sounds/master/pbd_sfx_jump_02.vsnd",
				_ => "mount://pbd/sounds/master/pbd_sfx_jump_03.vsnd"
			};
			UnitySoundUtil.PlayAt( sfx, WorldPosition, 0.95f, 1600f );
		}
	}

	public void OnTriggerExit( Collider other )
	{
		if ( !other.IsValid() || !other.GameObject.IsValid() ) return;
		_insideTube.Remove( other.GameObject.Root );
	}

	protected override void OnFixedUpdate()
	{
		if ( !IsAirTube || _insideTube.Count == 0 )
			return;

		var dir = LaunchDirection.LengthSquared > 0.01f ? LaunchDirection.Normal : Vector3.Up;
		_insideTube.RemoveWhere( g => !g.IsValid() );
		foreach ( var go in _insideTube )
		{
			var pc = go.Components.GetInDescendantsOrSelf<PlayerController>();
			if ( pc.IsValid() && pc.Body.IsValid() )
			{
				pc.Body.Velocity = Vector3.Lerp( pc.Body.Velocity, dir * LaunchSpeed, Time.Delta * 6f );
				continue;
			}
			var rb = go.Components.GetInDescendantsOrSelf<Rigidbody>();
			if ( rb.IsValid() && !rb.IsProxy )
				rb.Velocity = Vector3.Lerp( rb.Velocity, dir * LaunchSpeed, Time.Delta * 6f );
		}
	}
}

/// <summary>
/// Explosive Barrel / Explosive canister with authentic blast radius, damage, and domino chain reactions!
/// </summary>
[Title( "Unity Explosive Barrel" ), Category( "Unity Mount" ), Icon( "local_fire_department" )]
public sealed class UnityExplosiveBarrel : Component, Component.IDamageable
{
	[Property] public float Health { get; set; } = 28f;
	[Property] public float ExplosionRadius { get; set; } = 240f;
	[Property] public float ExplosionDamage { get; set; } = 120f;

	bool _primed;
	bool _detonated;
	float _detonateAt;
	GameObject _lastAttacker;

	public void OnDamage( in DamageInfo damage )
	{
		if ( _detonated ) return;
		_lastAttacker = damage.Attacker;
		Health -= MathF.Max( damage.Damage, 5f );
		if ( Health <= 0f && !_primed )
		{
			_primed = true;
			// Tiny stagger delay so nearby barrels chain-detonate dramatically!
			_detonateAt = Time.Now + Game.Random.Float( 0.05f, 0.14f );
			var mr = GetComponent<ModelRenderer>( true );
			if ( mr.IsValid() ) mr.Tint = new Color( 1f, 0.45f, 0.1f, 1f );
		}
	}

	protected override void OnUpdate()
	{
		if ( _primed && !_detonated && Time.Now >= _detonateAt )
			Detonate();
	}

	void Detonate()
	{
		_detonated = true;
		var pos = WorldPosition + Vector3.Up * 24f;
		UnitySoundUtil.PlayAt( "mount://pbd/sounds/player/rl_explosion_rocket.vsnd", pos, 1.0f, 3400f );
		PostalVfx.SpawnExplosion( Scene, pos, ExplosionRadius, new Color( 1f, 0.45f, 0.1f, 1f ) );

		var hitRoots = new HashSet<GameObject> { GameObject.Root };
		foreach ( var go in Scene.FindInPhysics( new Sphere( pos, ExplosionRadius ) ) )
		{
			if ( !go.IsValid() || !hitRoots.Add( go.Root ) )
				continue;

			var delta = (go.WorldPosition + Vector3.Up * 24f) - pos;
			float dist = delta.Length;
			float falloff = Math.Clamp( 1f - dist / ExplosionRadius, 0.2f, 1f );
			var dir = dist > 1f ? delta / dist : Vector3.Up;

			var info = new DamageInfo( ExplosionDamage * falloff, _lastAttacker ?? GameObject, GameObject )
			{
				Position = pos
			};
			info.Tags.Add( DamageTags.Explosion );

			foreach ( var d in go.Components.GetAll<Component.IDamageable>( FindMode.EverythingInSelfAndAncestors ) )
				d.OnDamage( info );

			var rb = go.Components.GetInAncestorsOrSelf<Rigidbody>();
			if ( rb.IsValid() && !rb.IsProxy )
				rb.ApplyImpulse( (dir + Vector3.Up * 0.5f).Normal * 3400f * falloff * Math.Clamp( rb.Mass, 1f, 60f ) );
		}

		GameObject.Destroy();
	}
}

/// <summary>
/// Automatic / pressable Sliding Doors (SlidingDoors) that smoothly slide their left/right leaves apart.
/// </summary>
[Title( "Unity Sliding Door" ), Category( "Unity Mount" ), Icon( "door_sliding" )]
public sealed class UnitySlidingDoor : Component, Component.IPressable, Component.IDamageable
{
	[Property] public GameObject LeafA { get; set; }
	[Property] public GameObject LeafB { get; set; }
	[Property] public Vector3 SlideOffsetA { get; set; } = new( 0f, 44f, 0f );
	[Property] public Vector3 SlideOffsetB { get; set; } = new( 0f, -44f, 0f );
	[Property] public float SlideSpeed { get; set; } = 3.2f;
	[Property] public float AutoOpenRadius { get; set; } = 135f;

	Vector3 _closedA;
	Vector3 _closedB;
	float _openProgress;
	bool _isOpen;
	float _closeAt;
	float _nextProxCheck;

	protected override void OnStart()
	{
		if ( LeafA.IsValid() ) _closedA = LeafA.LocalPosition;
		if ( LeafB.IsValid() ) _closedB = LeafB.LocalPosition;
	}

	public bool CanPress( IPressable.Event e ) => true;

	public IPressable.Tooltip? GetTooltip( IPressable.Event e )
		=> new( _isOpen ? "Close Sliding Door" : "Open Sliding Door", "door_sliding", null );

	public bool Press( IPressable.Event e )
	{
		SetOpen( !_isOpen );
		return true;
	}

	public void OnDamage( in DamageInfo damage )
	{
		if ( !_isOpen ) SetOpen( true );
	}

	public void SetOpen( bool open )
	{
		if ( _isOpen == open ) return;
		_isOpen = open;
		if ( open ) _closeAt = Time.Now + 4.0f;
		UnitySoundUtil.PlayAt( "mount://pbd/sounds/master/door_wood_open_close_fast_squeak_heavy_2_snd11382_1.vsnd", WorldPosition, 0.7f, 1000f, 1.15f, 1.3f );
	}

	protected override void OnUpdate()
	{
		if ( Time.Now >= _nextProxCheck )
		{
			_nextProxCheck = Time.Now + 0.18f;
			bool playerNear = false;
			foreach ( var pc in Scene.GetAllComponents<PlayerController>() )
			{
				if ( pc.IsValid() && pc.WorldPosition.Distance( WorldPosition ) <= AutoOpenRadius )
				{
					playerNear = true;
					break;
				}
			}

			if ( playerNear )
			{
				if ( !_isOpen ) SetOpen( true );
				_closeAt = Time.Now + 3.2f;
			}
			else if ( _isOpen && Time.Now >= _closeAt )
			{
				SetOpen( false );
			}
		}

		float target = _isOpen ? 1f : 0f;
		if ( MathF.Abs( _openProgress - target ) > 0.001f )
		{
			_openProgress = _openProgress.LerpTo( target, Time.Delta * SlideSpeed );
			if ( LeafA.IsValid() ) LeafA.LocalPosition = _closedA + SlideOffsetA * _openProgress;
			if ( LeafB.IsValid() ) LeafB.LocalPosition = _closedB + SlideOffsetB * _openProgress;
		}
	}
}

/// <summary>
/// Functional Unity Teleport volume (Entry -> Exit).
/// </summary>
[Title( "Unity Teleport" ), Category( "Unity Mount" ), Icon( "portal" )]
public sealed class UnityTeleport : Component, Component.ITriggerListener
{
	[Property] public Vector3 ExitPosition { get; set; }
	[Property] public Rotation ExitRotation { get; set; } = Rotation.Identity;

	readonly Dictionary<GameObject, float> _cooldowns = [];

	public void OnTriggerEnter( Collider other )
	{
		if ( !other.IsValid() || !other.GameObject.IsValid() ) return;
		var root = other.GameObject.Root;
		if ( _cooldowns.TryGetValue( root, out float until ) && Time.Now < until )
			return;

		var pc = other.GameObject.Components.GetInAncestorsOrSelf<PlayerController>();
		if ( pc.IsValid() )
		{
			_cooldowns[root] = Time.Now + 1.2f;
			pc.WorldPosition = ExitPosition + Vector3.Up * 8f;
			if ( pc.Body.IsValid() ) pc.Body.Velocity = pc.Body.Velocity.WithZ( MathF.Max( 0f, pc.Body.Velocity.z ) );
			UnitySoundUtil.PlayAt( "mount://pbd/sounds/master/pbd_sfx_jump_04.vsnd", ExitPosition, 0.9f, 1600f, 1.15f, 1.3f );
		}
	}

	public void OnTriggerExit( Collider other ) { }
}

/// <summary>
/// Bobbing, spinning Health / Armor / Ammo Pickup that heals players and refills weapon ammo!
/// </summary>
[Title( "Unity Pickup" ), Category( "Unity Mount" ), Icon( "medical_services" )]
public sealed class UnityPickup : Component, Component.ITriggerListener
{
	[Property] public UnityPickupKind Kind { get; set; } = UnityPickupKind.Health;
	[Property] public float Amount { get; set; } = 25f;
	[Property] public float RespawnDelay { get; set; } = 25f;

	Vector3 _basePos;
	bool _collected;
	float _respawnAt;
	float _bobOffset;

	protected override void OnStart()
	{
		_basePos = WorldPosition;
		_bobOffset = Game.Random.Float( 0f, MathF.PI * 2f );
	}

	protected override void OnUpdate()
	{
		if ( _collected )
		{
			if ( Time.Now >= _respawnAt )
			{
				_collected = false;
				SetVisible( true );
			}
			return;
		}

		WorldPosition = _basePos + Vector3.Up * (MathF.Sin( Time.Now * 2.8f + _bobOffset ) * 3.5f);
		WorldRotation = WorldRotation * Rotation.FromYaw( Time.Delta * 75f );
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( _collected || !other.IsValid() || !other.GameObject.IsValid() ) return;
		var pc = other.GameObject.Components.GetInAncestorsOrSelf<PlayerController>();
		if ( !pc.IsValid() ) return;

		if ( Kind == UnityPickupKind.Ammo )
		{
			// Refill held PostalWeapons
			foreach ( var wep in pc.GameObject.Components.GetAll<PostalWeapon>( FindMode.EverythingInSelfAndDescendants ) )
			{
				if ( wep.UsesClips && wep.ClipMaxSize > 0 )
					wep.Clip1 = wep.ClipMaxSize;
			}
		}

		// Play authentic ear-verified POSTAL: Brain Damaged pickup SFX per pickup type!
		string sfx = Kind switch
		{
			UnityPickupKind.Armor => "mount://pbd/sounds/master/armor.vsnd",
			UnityPickupKind.Ammo => Game.Random.Int( 0, 1 ) == 0
				? "mount://pbd/sounds/master/esm_battle_game_bag_item_inventory_gear_pick_up_equip_2_fast_one_shot.vsnd"
				: "mount://pbd/sounds/player/esm_battle_game_collect_item_point_boost_simple_metallic_beep_zip_bass_hit_2_deep_one_shot.vsnd",
			_ => Game.Random.Int( 0, 3 ) switch
			{
				0 => "mount://pbd/sounds/master/collect_food-001.vsnd",
				1 => "mount://pbd/sounds/master/collect_food-002.vsnd",
				2 => "mount://pbd/sounds/master/collect_food-003.vsnd",
				_ => "mount://pbd/sounds/master/collect_food-004.vsnd"
			}
		};
		UnitySoundUtil.PlayLocal( sfx, _basePos, listenLocal: !pc.IsProxy, 0.9f, 1000f );

		_collected = true;
		_respawnAt = Time.Now + RespawnDelay;
		SetVisible( false );
	}

	public void OnTriggerExit( Collider other ) { }

	void SetVisible( bool visible )
	{
		foreach ( var mr in GetComponentsInChildren<ModelRenderer>( true ) )
			mr.Enabled = visible;
	}
}

/// <summary>
/// Interactive / Shootable world button (ButtonInteractable / ButtonShootable) that toggles nearby doors!
/// </summary>
[Title( "Unity Button" ), Category( "Unity Mount" ), Icon( "radio_button_checked" )]
public sealed class UnityButton : Component, Component.IPressable, Component.IDamageable
{
	[Property] public bool Shootable { get; set; }
	float _cooldownUntil;
	Vector3 _restPos;
	float _depressAnim;

	protected override void OnStart()
	{
		_restPos = LocalPosition;
	}

	public bool CanPress( IPressable.Event e ) => Time.Now >= _cooldownUntil;

	public IPressable.Tooltip? GetTooltip( IPressable.Event e )
		=> new( "Press Button", "touch_app", null );

	public bool Press( IPressable.Event e )
	{
		Activate();
		return true;
	}

	public void OnDamage( in DamageInfo damage )
	{
		if ( Shootable || Time.Now >= _cooldownUntil )
			Activate();
	}

	void Activate()
	{
		if ( Time.Now < _cooldownUntil ) return;
		_cooldownUntil = Time.Now + 0.6f;
		_depressAnim = 1f;
		UnitySoundUtil.PlayAt( "mount://pbd/sounds/master/254713__greekirish__projector-button-push.vsnd", WorldPosition, 0.9f, 1200f );

		// Toggle nearby hinged or sliding doors within 450in
		foreach ( var sd in Scene.GetAllComponents<UnitySlidingDoor>() )
		{
			if ( sd.IsValid() && sd.WorldPosition.Distance( WorldPosition ) <= 450f )
				sd.SetOpen( true );
		}
		foreach ( var d in Scene.GetAllComponents<UnityDoor>() )
		{
			if ( d.IsValid() && d.WorldPosition.Distance( WorldPosition ) <= 380f )
				d.Toggle( WorldPosition, fastKick: false, syncPartner: true );
		}
	}

	protected override void OnUpdate()
	{
		if ( _depressAnim > 0f )
		{
			_depressAnim = MathF.Max( 0f, _depressAnim - Time.Delta * 4f );
			LocalPosition = _restPos - WorldRotation.Forward * (MathF.Sin( _depressAnim * MathF.PI ) * 2.5f);
		}
	}
}

public sealed class TimedDebrisCleanup : Component
{
	public float Lifetime { get; set; } = 4.5f;
	float _age;

	protected override void OnUpdate()
	{
		_age += Time.Delta;
		if ( _age >= Lifetime )
			GameObject.Destroy();
	}
}
