using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NS2;

[Title( "Fade" ), Category( "Natural Selection 2" )]
public sealed partial class Fade : AlienLifeform, Component.IDamageable
{
	protected override int BaseHealth => 250;
	protected override int HealthPerBiomass => 5;

	float IDamageable.Armour { get => Armor; set => Armor = Math.Clamp( value, 0, 80 ); }
	float IDamageable.MaxArmour => 80;

	const float Scale = MapLoader.Scale;
	static readonly SparkAnimationGraph.PoseParameters Locomotion = new( 0 ), ActionLayer = new( 1 );
	internal GameObject assignedEnemy;
	readonly AlienAim.Acquisition acquisition = new( 90 );
	[Sync( SyncFlags.FromHost )] public float Armor { get; private set; } = 80;
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose movePose { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose attackPose { get; set; }
	SparkAnimationGraph.Playback weapon;
	Action<string> worldTag, weaponTag;
	Rotation view;
	bool attacking, stabSelected, stabbing, drawingWeapon;
	TimeUntil weaponSwitch;
	[Property] public bool StabUnlocked { get; set; } = true;
	float energy = 100;
	TimeUntil repath;

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
		if ( !Networking.IsHost ) return;
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model )
			world = new( model.PropGraph, model );
		if ( mount?.GetByPath( "mount://ns2/ns2/models/alien/fade/fade_view.model.vmdl" ) is ModelLoader viewModel )
			weapon = new( viewModel.PropGraph, viewModel );
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
		if ( enemy.IsValid() && WorldPosition.DistanceSquared( lastSeen ?? enemy.WorldPosition ) > range * range )
		{
			enemy = null;
			lastSeen = null;
		}
		assignedEnemy = enemy;
		bool visible = enemy.IsValid() && lastSeen is null;
		Animation.SetLookTarget( visible ? enemy : null );
		var goal = lastSeen ?? enemy?.WorldPosition;
		if ( goal is null )
		{
			attacking = false;
			if ( !RetreatToHive( reason, health ) ) GameObject.GetOrAddComponent<AlienExploration>().Explore( Scale );
			if ( Movement.MoveTarget is { } destination ) TravelTo( destination );
			return;
		}
		if ( !attacking ) TravelTo( goal.Value );
		if ( repath <= 0 )
		{
			Movement.MoveTo( goal.Value, Scale, faceTarget: visible ? enemy : null );
			repath = 0.2f;
		}
	}

	void UpdateAttack( GameObject enemy )
	{
		attacking = false;
		if ( ventBlink ) return;
		if ( !NpcConVars.Enabled || PhysicsCarrier.IsValid() || !enemy.IsValid() || !Senses.VisibleHostiles.Contains( enemy ) )
		{
			view = AlienAim.Turn( view, WorldRotation.Forward, 5 );
			return;
		}

		var aim = CalculateAimPoint( enemy ) - EyePosition;
		view = AlienAim.Turn( view, aim, 5 );
		if ( !acquisition.Ready( enemy, aim, view ) ) return;

		var touch = enemy.GetComponentInChildren<Collider>()?.FindClosestPoint( EyePosition ) ?? enemy.WorldPosition;
		attacking = EyePosition.Distance( touch ) <= (2.45f + Random.Shared.NextSingle() * 0.075f) * Scale;
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
		output?.Set( "move_speed", Animation.MoveVelocity.Length / (6.2f * Scale) );
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
		UpdateAttack( assignedEnemy );
		energy = Math.Min( 100, energy + 10 * Time.Delta );
		bool metabolizing = metabolizeAnimation > 0;
		var ability = metabolizing ? "vortex" : stabSelected ? "stab" : "swipe";
		var activity = metabolizing || attacking && !drawingWeapon && !Blinking && energy >= (stabSelected ? 25 : 7) ? "primary" : "none";
		world.Set( "attack_speed", 1 );
		world.Set( "alive", 1 );
		world.Set( "move", Blinking ? "blink" : !Animation.IsGrounded ? "jump" : Movement.IsMoving ? "run" : "idle" );
		world.Set( "ability", ability );
		world.Set( "activity", activity );
		world.Update( AnimationTime, output, worldTag );
		movePose = world.GetPose( 0 );
		attackPose = world.GetPose( 1 );
		weapon.Set( "move", Blinking ? "blink" : "idle" );
		weapon.Set( "ability", ability );
		weapon.Set( "attack_speed", 1 );
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
		if ( !stab ) PlayCue( "swipe" );
		energy = Math.Max( 0, energy - (stab ? 25 : 7) );
		var (hit, victim) = AlienMelee.Trace( this, view, (stab ? 1.9f : 1.6f) * Scale, 0.7f, 1.2f );
		AlienMelee.PlayImpact( hit, stab ? AlienMelee.Attack.Stab : AlienMelee.Attack.Swipe );
		if ( victim is not Component target ) return;

		bool player = target.GameObject.Tags.HasAny( "ns2_marine", "ns2_lifeform", "ns2_exo", "ns2_exosuit" ) || target.GetComponentInParent<PlayerController>().IsValid();
		float amount = player && target is not (AlienLifeform or Marine or Exo) ? 75 : 37.5f;
		if ( stab )
		{
			amount = OpenToStab( target.GameObject ) ? 270 : 120;
			if ( target is not AlienStructure && target.GameObject.Tags.Has( "structure" ) ) amount *= 2;
		}
		var damage = new DamageInfo { Damage = amount, Origin = EyePosition, Position = hit.EndPosition, Attacker = GameObject, Weapon = GameObject };
		damage.Tags.Add( stab ? "structural" : "puncture" );
		victim.OnDamage( damage );
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
