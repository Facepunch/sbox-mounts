using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NS2;

[Title( "Fade" ), Category( "Natural Selection 2" )]
public sealed partial class Fade : AlienLifeform
{
	protected override Vector3 GetEngagementPoint() => WorldPosition + Vector3.Up * (0.8f * Scale);

	protected override int BaseHealth => 250;
	protected override int HealthPerBiomass => 5;

	protected override int MaxArmor => GetArmorLimit( 80, 20, 2.5f );

	const float Scale = MapLoader.Scale;
	static readonly SparkAnimationGraph.PoseParameters Locomotion = new( 0 ), ActionLayer = new( 1 );
	internal GameObject assignedEnemy;
	[Property, Range( 1, 7 )] public int AimSkillTier { get; set; } = 1;
	readonly AlienAim.Acquisition acquisition = new( 90 );
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose movePose { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose attackPose { get; set; }
	SparkAnimationGraph.Playback weapon;
	Action<string> worldTag, weaponTag;
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] Rotation view { get; set; }
	float standingYaw;
	bool attacking, stabSelected, stabbing, drawingWeapon;
	TimeUntil weaponSwitch;
	[Property] public bool StabUnlocked { get; set; } = true;
	float energy = 100;
	TimeUntil repath, nextJuke;
	int jukeSign = 1;

	internal static void RegisterSounds( MountContext context )
	{
		AlienFootsteps.Register( context, "fade" );
		foreach ( var cue in new[] { "spawn", "swipe", "stab", "swipe_hit_marine", "swipe_hit_marine_glance", "blink", "blink_end", "blink_loop_3D", "metabolize", "wound", "wound_serious", "death" } )
		{
			var id = $"alien/fade/{cue}";
			context.Add( ResourceType.GameResource, $"sounds/{id}.sound", new SparkSoundLoader( id ) );
		}
		foreach ( var id in new[] { "materials/metal/metal_scrape", "materials/thin_metal/scrape", "materials/organic/scrape", "materials/rock/scrape" } )
			context.Add( ResourceType.GameResource, $"sounds/{id}.sound", new SparkSoundLoader( id ) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayCue( string cue )
	{
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/alien/fade/{cue}.sound" ) is { } sound )
			GameObject.PlaySound( sound );
	}

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer )
	{
		const float radius = 0.4f * Scale, height = StandingHeight;
		var child = new GameObject( root, true, "Collider" );
		child.Tags.Add( "playercontroller" );
		var collider = child.AddComponent<CapsuleCollider>();
		collider.Radius = radius;
		collider.Start = Vector3.Up * radius;
		collider.End = Vector3.Up * (height - radius);
		var body = root.AddComponent<Rigidbody>();
		body.MassOverride = 158;
		body.Locking = new PhysicsLock { Pitch = true, Roll = true };
		body.EnableImpactDamage = false;
		var agent = root.AddComponent<NavMeshAgent>();
		agent.Radius = radius;
		agent.Height = height;
		agent.Enabled = false;
		root.AddComponent<NavigationLayer>().RunSpeed = 6.2f * Scale;
		var senses = root.AddComponent<SensesLayer>();
		senses.SightRange = 20 * Scale;
		senses.FieldOfView = 360;
		root.AddComponent<AnimationLayer>().EnableFootstepSounds = false;
		var npc = root.AddComponent<Fade>();
		npc.Faction = Factions.Monster;
		npc.Health = npc.BaseHealth;
		npc.EyeHeight = 1.7f * Scale;
		npc.DisplayName = "Fade";
		npc.Renderer = renderer;
	}

	protected override void OnStart()
	{
		base.OnStart();
		standingYaw = WorldRotation.Angles().yaw;
		if ( !Networking.IsHost ) return;
		world = CreatePlayback( Renderer.Model.ResourcePath );
		weapon = CreatePlayback( "mount://ns2/ns2/models/alien/fade/fade_view.model.vmdl" );
		PlayCue( "spawn" );
		var gravity = MathF.Abs( Scene.PhysicsWorld.Gravity.z );
		if ( gravity > 0 ) GetComponent<Rigidbody>().GravityScale = 21.5f * Scale / gravity;
		view = WorldRotation;
		AnimationTime = 0;
		worldTag = tag => AlienFootsteps.OnTag( this, "fade", tag );
		weaponTag = OnWeaponTag;
	}

	protected override void Think()
	{
		if ( !locations.IsValid() ) locations = Scene.GetAllComponents<MapLocations>().FirstOrDefault();
		float health = (Health + 2 * Armor) / (MaxHealth + 2 * ((IDamageable)this).MaxArmour);
		var reason = RetreatThreshold( health, energy / 100, InCombat, locations?.AlienPlayerBalance( WorldPosition ) ?? 0 );
		Vector3? lastSeen = null;
		var enemy = reason == RetreatReason.None ? AlienTargeting.ForTeam( this ).FindEnemy( this, out lastSeen ) : null;
		float range = (health > 0.95f ? 50 : 15) * Scale;
		if ( enemy.IsValid() && WorldPosition.Distance( lastSeen ?? enemy.WorldPosition ) > range )
		{
			enemy = null;
			lastSeen = null;
		}
		if ( enemy != assignedEnemy ) attacking = false;
		assignedEnemy = enemy;
		Animation.SetLookTarget( enemy );
		if ( !enemy.IsValid() )
		{
			attacking = false;
			if ( !RetreatToHive( reason, health ) ) GameObject.GetOrAddComponent<AlienExploration>().Explore( Scale );
			if ( Movement.MoveTarget is { } destination ) TravelTo( destination );
			return;
		}
		if ( attacking )
		{
			MoveInMelee( enemy );
			return;
		}
		// Once a team sighting selects a live entity, the Fade follows its current position.
		var goal = Lerk.IsPlayerTarget( enemy ) ? ApproachPosition( enemy ) : enemy.WorldPosition;
		TravelTo( goal );
		if ( repath <= 0 )
		{
			Movement.MoveTo( goal, Scale, faceTarget: enemy );
			repath = 0.2f;
		}
	}

	void MoveInMelee( GameObject enemy )
	{
		if ( Lerk.IsPlayerTarget( enemy ) )
		{
			if ( nextJuke <= 0 )
			{
				jukeSign = -jukeSign;
				nextJuke = 0.35f + Random.Shared.NextSingle() * 0.65f;
			}
			var side = Vector3.Cross( EyePosition - CalculateAimPoint( enemy ), Vector3.Up ).Normal;
			if ( side.LengthSquared > 0 )
			{
				var goal = WorldPosition + side * (Scale * jukeSign);
				Movement.MoveTo( goal, 1, faceTarget: enemy );
			}
		}
		else if ( WorldPosition.Distance( enemy.WorldPosition ) < 2.4f * 0.915f * Scale )
		{
			Movement.Stop();
		}
	}

	Vector3 ApproachPosition( GameObject enemy )
	{
		var marine = enemy.GetComponent<Marine>();
		var exo = enemy.GetComponent<Exo>();
		var forward = marine?.AimDirection ?? exo?.AimDirection ?? enemy.GetComponent<PlayerController>()?.EyeAngles.Forward ?? enemy.WorldRotation.Forward;
		var eye = CalculateEyePosition( enemy );
		double cone = Math.Cos( (exo.IsValid() ? 95 : 90) * Math.PI / 180 );
		if ( Vector3.Dot( (WorldPosition - eye).Normal, forward ) <= cone )
			return CalculateAimPoint( enemy );

		if ( !Scene.NavMesh.IsEnabled ) return enemy.WorldPosition;
		float radius = enemy.GetComponentInChildren<CapsuleCollider>()?.Radius ?? 0;
		var flank = FlankPosition( WorldPosition, enemy.WorldPosition, forward, radius * MathF.Sqrt( 2 ) );
		return Scene.NavMesh.GetClosestPoint( flank ) ?? enemy.WorldPosition;
	}

	static Vector3 FlankPosition( Vector3 origin, Vector3 target, Vector3 forward, float extent )
	{
		var behind = target - forward * (extent + 2 * Scale);
		var left = behind + new Vector3( forward.y, -forward.x, forward.z ) * (2.4f * Scale);
		var right = behind + new Vector3( -forward.y, forward.x, forward.z ) * (2.4f * Scale);
		return origin.DistanceSquared( left ) < origin.DistanceSquared( right ) ? left : right;
	}

	static bool InAttackRange( float distance, float roll ) => distance / (double)Scale <= 2.4 + 0.05 + roll * 0.075;

	void UpdateAttack( GameObject enemy )
	{
		attacking = false;
		if ( ventBlink ) return;
		if ( PhysicsCarrier.IsValid() || !enemy.IsValid() || !AllowsAttack( enemy ) )
		{
			var direction = WorldRotation.Forward;
			if ( NpcConVars.Enabled && !PhysicsCarrier.IsValid() )
			{
				if ( Movement.MoveTarget is { } goal ) direction = TravelLook( goal );
				else if ( retreatHive.IsValid() ) direction = retreatHive.EngagementPoint - EyePosition;
				else if ( airGoal is { } destination ) direction = TravelLook( destination );
			}
			view = AlienAim.Turn( view, direction, acquisition.TurnRate );
			return;
		}

		var point = CalculateAimPoint( enemy );
		bool ready = acquisition.TrackView( enemy, EyePosition, point, view, InCombat ? 1.8f : 1, AimSkillTier );
		if ( acquisition.ViewTarget is { } viewTarget )
			view = AlienAim.Turn( view, viewTarget - EyePosition, acquisition.TurnRate );
		if ( !ready ) return;

		attacking = InAttackRange( WorldPosition.Distance( enemy.WorldPosition ), Random.Shared.NextSingle() );
		if ( attacking && !Blinking && !stabbing && weaponSwitch <= 0 )
		{
			bool wantsStab = StabUnlocked && energy >= 25 && OpenToStab( enemy );
			if ( wantsStab != stabSelected )
			{
				stabSelected = wantsStab;
				drawingWeapon = true;
				weaponSwitch = 1;
			}
		}
	}

	internal float AttackUrgency( BaseNpc target )
	{
		float distance = WorldPosition.Distance( target.WorldPosition ) / Scale;
		if ( distance > 50 || target is not (Marine or Exo) ) return 0;
		if ( distance < 15 ) return 1 + 1 / Math.Max( 1, distance );

		bool covered = AlienTargeting.HasResponders( this, target, 2 );
		float weight = target is Exo ? (covered ? 0.8f : 1.2f) : (covered ? 0.6f : 1);
		return weight * 40 / (40 + distance);
	}

	void TravelTo( Vector3 goal )
	{
		Travel( goal );
		if ( !Blinking ) TryMetabolize();
	}

	protected override void OnUpdate()
	{
		if ( GameObject.IsDestroyed ) return;
		base.OnUpdate();
		UpdateCrouch();
		UpdateBlinkPresentation();
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		if ( Networking.IsHost && Health > 0 ) UpdateAttack( assignedEnemy );
		var velocity = Animation.MoveVelocity.WithZ( 0 );
		float yaw = view.Angles().yaw;
		float offset = Angles.NormalizeAngle( standingYaw - yaw );
		offset = Animation.MoveVelocity.Length > 0.1f * Scale ? AlienAim.ApproachAngle( offset, 0, Time.Delta * 5 ) : Math.Clamp( offset, -85, 85 );
		standingYaw = yaw + offset;
		Renderer.WorldRotation = Rotation.FromYaw( yaw );
		output?.Set( "body_yaw", offset % 90 );
		output?.Set( "body_pitch", -view.Angles().pitch );
		output?.Set( "move_speed", velocity.Length / (6.2f * Scale) );
		output?.Set( "move_yaw", velocity.IsNearlyZero() ? 0f :
			MathF.Atan2( Vector3.Dot( velocity, view.Forward ), Vector3.Dot( velocity, view.Left ) ).RadianToDegree() );
		if ( !Networking.IsHost )
		{
			if ( output is null ) return;
			movePose.Write( Locomotion, output, AnimationTime );
			attackPose.Write( ActionLayer, output, AnimationTime );
			return;
		}

		if ( world is null || weapon is null || Health <= 0 ) return;
		Regenerate( underFire > 0 );
		if ( wounds.Update() is { } cue ) PlayCue( cue );
		energy = Math.Min( 100, energy + GetEnergyRechargeRate( 15 ) * Time.Delta );
		bool metabolizing = metabolizeAnimation > 0;
		var ability = metabolizing ? "vortex" : stabSelected ? "stab" : "swipe";
		var activity = metabolizing || attacking && !drawingWeapon && !Blinking && energy >= (stabSelected ? 25 : 7) ? "primary" : "none";
		float attackSpeed = (GameObject.Tags.Has( "enzymed" ) ? 1.25f : 1f) * (GameObject.Tags.Has( "electrified" ) ? 0.7f : 1f);
		world.Set( "attack_speed", attackSpeed );
		world.Set( "onfire", GameObject.Tags.Has( "burning" ) ? 1 : 0 );
		world.Set( "alive", 1 );
		if ( IsOnGround ) jumping = false;
		if ( !metabolizing )
		{
			bool moving = Movement.IsMoving || Animation.MoveVelocity.Length > 0.1f * Scale;
			world.Set( "move", Blinking ? "blink" : jumping ? "jump" : moving ? "run" : "idle" );
		}
		world.Set( "ability", ability );
		world.Set( "activity", activity );
		world.Update( AnimationTime, output, worldTag );
		movePose = world.GetPose( 0 );
		attackPose = world.GetPose( 1 );
		weapon.Set( "move", Blinking ? "blink" : "idle" );
		weapon.Set( "ability", ability );
		weapon.Set( "attack_speed", attackSpeed );
		weapon.Set( "activity", activity );
		weapon.Update( AnimationTime, tag: weaponTag );
		drawingWeapon = false;
	}

	void OnWeaponTag( string tag )
	{
		if ( tag == "metabolize" && metabolizeAnimation > 0 && metabolizeCooldown <= 0 ) RecoverFromMetabolize();
		if ( tag == "stab_start" && stabSelected )
		{
			stabbing = true;
			PlayCue( "stab" );
		}
		if ( tag != "hit" ) return;
		bool stab = stabbing;
		if ( !stab && (stabSelected || metabolizeAnimation > 0) ) return;
		stabbing = false;
		if ( !AllowsAttack( assignedEnemy ) ) return;
		if ( !stab ) PlayCue( "swipe" );
		energy = Math.Max( 0, energy - (stab ? 25 : 7) );
		var attack = stab ? AlienMelee.Attack.Stab : AlienMelee.Attack.Swipe;
		foreach ( var (hit, victim) in AlienMelee.Swing( this, view, (stab ? 1.9f : 1.6f) * Scale, 0.7f, 1.2f, attack ) )
		{
			AlienMelee.PlayImpact( hit, attack );
			if ( victim is not Component target ) continue;

			bool player = target.GameObject.Tags.HasAny( "ns2_marine", "ns2_lifeform", "ns2_exo", "ns2_exosuit" ) || target.GetComponentInParent<PlayerController>().IsValid();
			float amount = player && target is not (AlienLifeform or Marine or Exo) ? 75 : 37.5f;
			if ( stab )
			{
				amount = OpenToStab( target.GameObject ) ? 270 : 120;
				if ( target is not AlienStructure && target.GameObject.Tags.Has( "structure" ) ) amount *= 2;
			}
			var damage = new DamageInfo { Damage = amount, Origin = EyePosition, Position = hit.EndPosition, Attacker = GameObject, Weapon = GameObject };
			damage.Tags.Add( stab ? "structural" : "puncture" );
			if ( !stab ) LeechShield( victim, 0.0333f );
			victim.OnDamage( damage );
		}
	}

	bool OpenToStab( GameObject target )
	{
		if ( !target.Tags.Has( "ns2_exo" ) || target.Tags.Has( "nanoshielded" ) ) return false;
		var offset = (WorldPosition - target.WorldPosition).WithZ( 0 );
		var forward = target.GetComponent<Exo>()?.AimDirection ?? target.GetComponent<PlayerController>()?.EyeAngles.Forward ?? target.WorldRotation.Forward;
		return offset.LengthSquared > 0.01f * Scale * Scale && Vector3.Dot( forward, offset ) < 0;
	}

	protected override void OnDeath( IDamageable.DeathEvent e )
	{
		PlayCue( "death" );
		base.OnDeath( e );
	}
}
