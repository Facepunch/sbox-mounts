using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;
using System.Runtime.CompilerServices;

namespace NS2;

[Title( "Exo" ), Category( "Natural Selection 2" )]
public sealed class Exo : Ns2Npc, Component.IDamageable
{
	protected override Vector3 GetEngagementPoint() => WorldPosition + Vector3.Up * (1.5f * Scale);

	const float Scale = MapLoader.Scale;
	const string DeathSound = "marine/structures/generic_death";
	[Property, Range( 0, 3 ), Sync( SyncFlags.FromHost )] public int WeaponsLevel { get; set; }
	[Property] public bool Railguns { get; set; }
	bool HoldingRailCharge => Railguns && (leftPressed && left.Charge >= 1 || rightPressed && right.Charge >= 1);
	float GroundSpeed => (Railguns ? 4.879f : 4.522f) * Scale * (HoldingRailCharge ? 0.8f : 1f);
	[Property, Range( 0, 3 ), Sync( SyncFlags.FromHost ), Change( nameof( OnArmorLevelChanged ) )]
	public int ArmorLevel { get; set; }
	float ArmorCapacity => (Railguns ? 260 : 320) + 40 * Math.Clamp( ArmorLevel, 0, 3 );
	// BaseNpc dies below one health. Reserve that interval so even fractional armor keeps the suit alive.
	float HealthFromArmor( float armor ) => armor <= 0 ? 0 : 1 + Math.Min( armor, ArmorCapacity );
	float RemainingArmor => Math.Max( 0, Health - 1 );
	float IDamageable.MaxArmour => ArmorCapacity;
	float IDamageable.Armour
	{
		get => RemainingArmor;
		set => Health = HealthFromArmor( value );
	}

	void OnArmorLevelChanged( int before, int after )
	{
		if ( Networking.IsHost && MaxHealth > 0 ) UpdateArmorCapacity();
	}

	void UpdateArmorCapacity()
	{
		if ( MaxHealth == ArmorCapacity + 1 ) return;
		// Older prefabs stored armor as a percentage of 100 health.
		float fraction = MaxHealth == 100 ? Health / 100 : RemainingArmor / Math.Max( 1, MaxHealth - 1 );
		MaxHealth = ArmorCapacity + 1;
		if ( Health > 0 ) Health = HealthFromArmor( fraction * ArmorCapacity );
	}

	sealed class Arm
	{
		internal float Heat;
		internal bool Shooting;
		internal TimeSince Charge, Released, Flash;
		internal SoundHandle Loop;
	}

	sealed class Resonance
	{
		internal Guid Source;
		internal bool LeftArm;
		internal TimeUntil Expires = -1;

		internal float Hit( Guid source, bool leftArm )
		{
			if ( Expires >= 0 && (Source != source || LeftArm != leftArm) )
			{
				Expires = -1;
				return 1.5f;
			}

			Source = source;
			LeftArm = leftArm;
			Expires = 2;
			return 1;
		}
	}

	static readonly ConditionalWeakTable<GameObject, Resonance> marks = new();
	readonly Arm left = new(), right = new();
	internal GameObject assignedEnemy;
	[Property, Range( 1, 7 )] public int AimSkillTier { get; set; } = 1;
	Vector3? viewTarget;
	readonly BotWeaponAim acquisition = new( 95 );
	[Sync( SyncFlags.FromHost )] bool leftPressed { get; set; }
	[Sync( SyncFlags.FromHost )] bool rightPressed { get; set; }
	SparkAnimationGraph.Playback graph, weapon;
	Action<string> worldTag, onTag;
	static readonly SparkAnimationGraph.PoseParameters Locomotion = new( 0 ), ActionLayer = new( 1 );
	// Keep the clock of existing Exos when hotloading the replicated property.
	TimeSince age;
	[Sync( SyncFlags.FromHost )] TimeSince AnimationTime { get => age; set => age = value; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose movePose { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose attackPose { get; set; }
	TimeUntil repath, fireUntil, nextJuke;
	int strafeSide = 1;
	Rotation aim;
	internal Vector3 AimDirection => aim.Forward;
	bool attack, closePlayer, inRailRange;

	internal static void RegisterSounds( MountContext context )
	{
		context.Add( ResourceType.GameResource, $"sounds/{DeathSound}.sound", new SparkSoundLoader( DeathSound, 0 ) );
		foreach ( var cue in new[] { "spin", "spin_2", "spin_up", "spin_up_2", "spin_down", "spin_down_2", "tail", "tail_2", "railgun_fire", "step" } )
		{
			var path = $"marine/heavy/{cue}";
			context.Add( ResourceType.GameResource, $"sounds/{path}.sound", new SparkSoundLoader( path ) );
		}
	}

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer, bool railguns )
	{
		if ( railguns ) renderer.LocalScale = Vector3.One * 0.95f;
		var capsule = root.AddComponent<CapsuleCollider>();
		capsule.Radius = 0.55f * Scale;
		capsule.Start = Vector3.Up * capsule.Radius;
		capsule.End = Vector3.Up * (2.4f * Scale - capsule.Radius);
		root.AddComponent<Rigidbody>().EnableImpactDamage = false;
		var agent = root.AddComponent<NavMeshAgent>();
		agent.Radius = capsule.Radius;
		agent.Height = 2.4f * Scale;
		agent.Enabled = false;
		root.AddComponent<ExoMovement>();
		root.AddComponent<SensesLayer>().SightRange = 45 * Scale;
		root.AddComponent<AnimationLayer>().EnableFootstepSounds = false;
		var npc = root.AddComponent<Exo>();
		npc.Railguns = railguns;
		npc.DisplayName = railguns ? "Railgun Exo" : "Minigun Exo";
		npc.EyeHeight = (railguns ? 2.185f : 2.3f) * Scale;
		npc.MaxHealth = npc.Health = npc.ArmorCapacity + 1;
		npc.Faction = Factions.Ally;
		npc.Renderer = renderer;
	}

	protected override void OnStart()
	{
		base.OnStart();
		if ( !Networking.IsHost ) return;
		UpdateArmorCapacity();
		AnimationTime = 0;
		aim = WorldRotation;
		worldTag = OnWorldTag;
		left.Released = right.Released = 1;
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		var path = Renderer.Model.ResourcePath;
		if ( mount?.GetByPath( path ) is ModelLoader body )
			graph = new( body.PropGraph, body );
		if ( mount?.GetByPath( path.Replace( ".model.vmdl", "_view.model.vmdl" ) ) is ModelLoader view )
		{
			weapon = new( view.PropGraph, view );
			onTag = OnWeaponTag;
		}
	}

	internal float AttackUrgency( BaseNpc target, Vector3 position, bool visible )
	{
		var (responders, weight) = target switch
		{
			Onos => (4, 7f),
			Fade => (3, 6f),
			Lerk => (2, 5f),
			Skulk => (2, 4f),
			Gorge => (2, 3f),
			Hydra => (2, 2f),
			Drifter => (1, 1f),
			Hive => (6, 0.9f),
			Harvester => (2, 0.8f),
			Crag => (2, 0.95f),
			_ => (0, 0f)
		};
		if ( responders == 0 ) return 0;
		bool passive = target is Hive or Harvester or Crag;
		if ( !passive && !visible && Scene.NavMesh.IsEnabled && Scene.NavMesh.GetClosestPoint( position ) is { } ground &&
			AlienTargeting.IsBehindFloor( Scene, position, ground ) ) return 0;

		float distance = WorldPosition.Distance( position ) / Scale;
		if ( (passive || distance >= 15) && AlienTargeting.HasResponders( this, target, responders ) )
			weight = target switch { Crag => 0.2f, Hive => 0.5f, Harvester => 0.4f, _ => 0.1f };
		return distance < 15 ? weight + (passive ? 3 : 10) / Math.Max( 1, distance ) : weight;
	}

	// Spark uses gameplay extents for touch range. Some differ from the movement collider.
	static float HorizontalExtent( GameObject target ) => target.GetComponent<BaseNpc>() switch
	{
		Onos => Onos.HorizontalExtents,
		Hive => 2 * MathF.Sqrt( 2 ) * Scale,
		Harvester => MathF.Sqrt( 2 ) * Scale,
		Hydra or Crag or Babbler => 0.5f * MathF.Sqrt( 2 ) * Scale,
		_ => ((target.GetComponentInChildren<Collider>()?.LocalBounds.Size ?? Vector3.Zero) * target.WorldScale).WithZ( 0 ).Length * 0.5f
	};

	protected override void Think()
	{
		var targeting = AlienTargeting.ForTeam( this );
		var enemy = targeting.FindEnemy( this, out var memory );
		assignedEnemy = enemy;
		bool visible = enemy.IsValid() && !memory.HasValue;
		Animation.SetLookTarget( visible ? enemy : null );
		var goal = memory ?? enemy?.WorldPosition;
		float distance = enemy.IsValid() ? Math.Max( 0, targeting.WalkDistanceFromEye( this, enemy.WorldPosition ) - HorizontalExtent( enemy ) ) / Scale : float.MaxValue;
		bool player = enemy.IsValid() && (enemy.Tags.HasAny( "ns2_lifeform", "ns2_marine", "ns2_exo" ) || enemy.GetComponentInParent<PlayerController>().IsValid());
		closePlayer = player && distance < 10;
		inRailRange = distance <= 32;
		var aimPoint = visible ? CalculateAimPoint( enemy ) : memory.GetValueOrDefault();
		viewTarget = visible ? aimPoint : null;
		var weapon = Railguns ? BotWeaponAim.Weapon.Railgun : BotWeaponAim.Weapon.Minigun;
		bool aimed = visible && distance < 45 && acquisition.Track( this, enemy, aimPoint, aim, AimSkillTier, weapon, out viewTarget );
		if ( aimed ) fireUntil = 0.5f;
		if ( !aimed && fireUntil <= 0 && distance < 15 ) viewTarget = aimPoint;
		attack = enemy.IsValid() && fireUntil > 0;
		if ( repath > 0 ) return;
		repath = 0.2f;
		if ( goal is { } destination && (!visible || distance > 10) )
			Movement.MoveTo( destination, Scale, faceTarget: visible ? enemy : null );
		else if ( visible && player )
		{
			if ( nextJuke <= 0 )
			{
				strafeSide = -strafeSide;
				nextJuke = Random.Shared.Float( 0.35f, 1 );
			}
			// Spark tests a step beside the eye, so an ordinary flat floor is too far below it.
			var side = Vector3.Cross( EyePosition - CalculateAimPoint( enemy ), Vector3.Up ).Normal;
			var step = EyePosition + side * (strafeSide * 0.5f * Scale);
			if ( side.LengthSquared > 0 && Scene.NavMesh.GetClosestPoint( step ) is { } point && point.Distance( step ) < 0.2f * Scale )
				Movement.MoveTo( point, 1, faceTarget: enemy );
			else Movement.Stop();
		}
		else Movement.Stop();
	}

	protected override void OnUpdate()
	{
		if ( GameObject.IsDestroyed ) return;
		base.OnUpdate();
		if ( Health <= 0 ) return;
		var output = PlayerGait.UpdatePose( this, 5.95f );
		if ( Networking.IsHost )
		{
			Movement.WalkSpeed = Movement.RunSpeed = GroundSpeed;
			if ( viewTarget is { } point )
				aim = AlienAim.Turn( aim, point - EyePosition, acquisition.TurnRate );
			leftPressed = UpdateArm( left, leftPressed );
			rightPressed = UpdateArm( right, rightPressed );
			var move = Animation.MoveVelocity.Length > 0.1f * Scale ? "run" : "idle";
			if ( graph is not null )
			{
				UpdateInputs( graph, move );
				graph.Update( age, output, worldTag );
				movePose = graph.GetPose( 0 );
				attackPose = graph.GetPose( 1 );
			}
			UpdateInputs( weapon, move );
			weapon?.Update( age, tag: onTag );
		}
		else if ( output is not null )
		{
			movePose.Write( Locomotion, output, age );
			attackPose.Write( ActionLayer, output, age );
		}
		if ( !leftPressed ) StopSound( left );
		if ( !rightPressed ) StopSound( right );
	}

	bool UpdateArm( Arm arm, bool pressed )
	{
		bool request = attack && NpcConVars.Enabled;
		if ( !Railguns )
		{
			arm.Heat = Math.Clamp( arm.Heat + Time.Delta * (arm.Shooting ? 0.3f : -0.4f), 0, 1 );
			request &= arm.Heat < 0.95f;
			if ( !request && arm.Shooting )
			{
				PlayMinigunCue( arm == left ? "tail_2" : "tail" );
				arm.Shooting = false;
			}
			return request;
		}

		request &= pressed ? arm.Charge < (closePlayer ? 0.8f : 1) : inRailRange && arm.Released >= 1;
		if ( request && !pressed ) arm.Charge = 0;
		if ( !request && pressed ) arm.Released = 0;
		return request;
	}

	void UpdateInputs( SparkAnimationGraph.Playback playback, string move )
	{
		if ( playback is null ) return;
		playback.Set( "move", move );
		playback.Set( "weapon", "exo_weapon_holder" );
		playback.Set( "alive", 1 );
		playback.Set( "activity_left", leftPressed ? "primary" : "none" );
		playback.Set( "activity_right", rightPressed ? "primary" : "none" );
		playback.Set( "overheat_speed", 1.1666666269302368 / 2 );
	}

	void OnWorldTag( string tag )
	{
		if ( tag == "step" && PlayerGait.CanPlayStep( this ) )
			PlayFootstep();
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	void PlayFootstep()
	{
		if ( !Application.IsHeadless ) PlayHeavySound( "step" );
	}

	void OnWeaponTag( string tag )
	{
		if ( !Railguns )
		{
			var cue = tag switch
			{
				"left_minigun_attack_start" => "spin_up_2",
				"right_minigun_attack_start" => "spin_up",
				"left_minigun_attack_end" => "spin_down_2",
				"right_minigun_attack_end" => "spin_down",
				_ => null
			};
			if ( cue is not null )
			{
				PlayMinigunCue( cue );
				return;
			}
		}
		bool isLeft = tag == "l_shoot";
		if ( !isLeft && tag != "r_shoot" || !NpcConVars.Enabled ) return;
		var arm = isLeft ? left : right;
		if ( !Railguns && !(isLeft ? leftPressed : rightPressed) ) return;
		if ( !Railguns ) arm.Shooting = true;
		float charge = Math.Clamp( (float)arm.Charge, 0, 1 );
		Fire( Railguns ? 120 * charge : 6, isLeft, Railguns && charge >= 1 );
		if ( Railguns || arm.Flash >= 0.15f )
		{
			arm.Flash = 0;
			ShowShot( isLeft );
		}
	}

	void Fire( float damage, bool isLeft, bool fullCharge )
	{
		var direction = Railguns ? aim.Angles().Forward : aim.Forward;
		if ( !Railguns )
		{
			float angle = Random.Shared.NextSingle() * MathF.Tau;
			float radius = Random.Shared.NextSingle() * MathF.Tan( 2.5f.DegreeToRadian() );
			direction = (direction + radius * (aim.Left * MathF.Cos( angle ) + aim.Up * MathF.Sin( angle ))).Normal;
		}
		var eye = EyePosition;
		float range = Railguns ? 32 : 45;
		var end = eye + direction * (range * Scale);
		var ray = Scene.Trace.Ray( eye, end ).WithoutTags( "trigger" ).IgnoreGameObjectHierarchy( GameObject ).UseDamageHitboxes();
		float pierceStep = 0;
		if ( Railguns )
		{
			// Spark builds the sweep from the two endpoints in map metres.
			var origin = eye / Scale;
			var railEnd = origin + direction * range;
			direction = (railEnd - origin).Normal;
			var extents = new Vector3( direction.WithX( 0 ).Length, direction.WithY( 0 ).Length, direction.WithZ( 0 ).Length ) * (0.3f * Scale);
			// Spark rejects box sweeps with a zero half-extent.
			if ( extents.x == 0 || extents.y == 0 || extents.z == 0 )
			{
				acquisition.Record( false );
				return;
			}

			end = Scene.Trace.Ray( eye, railEnd * Scale ).WithTag( "world" ).Run().EndPosition;
			ray = ray.FromTo( eye, end ).Size( -extents, extents );
			pierceStep = extents.y * 3;
		}
		bool hitEntity = false;
		for ( int i = 0; i < 20; i++ )
		{
			var hit = ray.Run();
			var target = hit.GetBulletTarget( out var hp, out bool pass );
			if ( !target.IsValid() || Railguns && damage <= 0 ) break;
			if ( target.Tags.Has( "world" ) )
			{
				// Spark clips the shot by its centerline. Its wider sweep can graze cover on the way.
				if ( pierceStep <= 0 ) break;
				var next = hit.EndPosition + direction * pierceStep;
				if ( (end - next).Dot( direction ) <= 0 ) break;
				ray = ray.FromTo( next, end );
				continue;
			}
			hitEntity = true;
			if ( !Railguns ) acquisition.Record( hp is not null );
			var onos = hp as Onos;
			bool blocked = Railguns && onos is not null && onos.BlocksShotAt( hit.HitPosition );
			if ( hp is { Health: > 0 } && GetDisposition( target ) == Disposition.Hostile )
			{
				float amount = Marine.UpgradedWeaponDamage( damage, WeaponsLevel, target );
				if ( fullCharge && target.Tags.Has( "ns2_lifeform" ) && !(onos?.Shielding ?? false) )
				{
					var mark = marks.GetValue( target, _ => new() );
					amount *= mark.Hit( GameObject.Id, isLeft );
				}
				var info = new DamageInfo { Damage = amount, Origin = eye, Position = hit.HitPosition, Attacker = GameObject, Weapon = GameObject };
				info.Tags.Add( Railguns ? "railgun" : "heavy" );
				info.Tags.Add( "bullet" );
				hp.OnDamage( info );
			}
			if ( blocked ) break;
			if ( !Railguns && !pass ) break;
			if ( Railguns && hp is not null && target.Tags.Has( "ns2_lifeform" ) ) damage *= 0.5f;
			ray = ray.IgnoreGameObjectHierarchy( target );
		}
		if ( Railguns || !hitEntity ) acquisition.Record( hitEntity );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowShot( bool isLeft )
	{
		string muzzle = Railguns ? (isLeft ? "fxnode_lrailgunmuzzle" : "fxnode_rrailgunmuzzle") : (isLeft ? "fxnode_l_minigun_muzzle" : "fxnode_r_minigun_muzzle");
		if ( Renderer.GetAttachment( muzzle ) is { } tx )
		{
			string effect = Railguns ? "railgun/muzzle_flash" : isLeft ? "minigun/muzzle_flash_left" : "minigun/muzzle_flash";
			CinematicLoader.Play( $"cinematics/marine/{effect}.cinematic", tx.Position, tx.Rotation );
		}
		var arm = isLeft ? left : right;
		if ( !Railguns && arm.Loop is not null ) return;
		var cue = Railguns ? "railgun_fire" : isLeft ? "spin_2" : "spin";
		var handle = PlayHeavySound( cue );
		if ( !Railguns ) arm.Loop = handle;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayMinigunCue( string cue )
	{
		if ( !Application.IsHeadless ) PlayHeavySound( cue );
	}

	SoundHandle PlayHeavySound( string cue ) =>
		GameResource.Load<SoundEvent>( $"mount://ns2/sounds/marine/heavy/{cue}.sound" ) is { } sound ? GameObject.PlaySound( sound ) : null;

	static void StopSound( Arm arm )
	{
		arm.Loop?.Stop();
		arm.Loop = null;
	}

	protected override GameObject CreateRagdoll() => null;

	protected override void OnDeath( IDamageable.DeathEvent e )
	{
		NpcCorpse.KeepHitboxes( Renderer, graph, age );
		ShowDeath( WorldPosition, WorldRotation );
		base.OnDeath( e );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ShowDeath( Vector3 position, Rotation rotation )
	{
		CinematicLoader.Play( "cinematics/marine/exo/explosion.cinematic", position, rotation );
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{DeathSound}.sound" ) is { } sound )
			Sound.Play( sound, position );
	}

	protected override void OnDisabled()
	{
		assignedEnemy = null;
		StopSound( left );
		StopSound( right );
		base.OnDisabled();
	}

	protected override void OnDamaging( IDamageable.DamageEvent hit )
	{
		var tags = hit.DamageInfo.Tags;
		if ( hit.AcceptedDamage <= 0 || tags.HasAny( "gas", "nervegas", "biological", "structuresonly", "splash" ) )
		{
			hit.Cancelled = true;
			return;
		}

		// Falling bypasses armor, and NS2 discards health damage on an Exo.
		if ( tags.Has( "falling" ) )
		{
			hit.AcceptedDamage = 0;
			return;
		}

		float damage = hit.AcceptedDamage * (tags.Has( "corrode" ) ? 0.4f : AlienArmor.PlayerDamageScale( hit.DamageInfo ));
		float ratio = AlienArmor.HealthPerArmor( hit.DamageInfo );
		float armorLost = damage / ratio;
		hit.AcceptedDamage = armorLost >= RemainingArmor ? Health : armorLost;
	}
}
