using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace NS2;

[Title( "Gorge" ), Category( "Natural Selection 2" )]
public sealed partial class Gorge : AlienLifeform, Component.IDamageable
{
	protected override int BaseHealth => 160;
	protected override int HealthPerBiomass => 2;

	float IDamageable.Armour { get => Armor; set => Armor = Math.Clamp( value, 0, 50 ); }
	float IDamageable.MaxArmour => 50;

	const float Scale = MapLoader.Scale;
	static readonly SparkAnimationGraph.PoseParameters Locomotion = new( 0 ), ActionLayer = new( 1 );
	readonly AlienTargeting targeting = new();
	[Sync( SyncFlags.FromHost )] public float Armor { get; private set; } = 50;
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose movePose { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose attackPose { get; set; }
	SparkAnimationGraph.Playback weapon;
	Action<string> worldTag, weaponTag;
	Rotation aim;
	float energy = 100;
	[Property] public bool BileBombUnlocked { get; set; } = true;
	bool firing, primaryHeld, bombarding;
	float ShotCost => bombarding ? 20 : 7;
	TimeUntil repath;

	internal static void RegisterSounds( MountContext context )
	{
		AlienFootsteps.Register( context, "gorge" );
		foreach ( var cue in new[] { "spit", "spit_hit", "bilebomb", "bilebomb_hit", "heal_spray", "slide_loop", "create_structure_start", "spawn", "wound", "wound_serious", "death" } )
		{
			var id = $"alien/gorge/{cue}";
			context.Add( ResourceType.GameResource, $"sounds/{id}.sound", new SparkSoundLoader( id ) );
		}
	}

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer )
	{
		renderer.CreateAttachments = true;
		var hull = new GameObject( root, true, "Collider" );
		hull.Tags.Add( "playercontroller" );
		var sphere = hull.AddComponent<SphereCollider>();
		sphere.Center = Vector3.Up * (0.475f * Scale);
		sphere.Radius = 0.5f * Scale;
		var body = root.AddComponent<Rigidbody>();
		body.MassOverride = 80;
		body.EnableImpactDamage = false;
		var agent = root.AddComponent<NavMeshAgent>();
		agent.Radius = sphere.Radius;
		agent.Height = 0.95f * Scale;
		agent.Enabled = false;
		root.AddComponent<NavigationLayer>().RunSpeed = 6 * Scale;
		var senses = root.AddComponent<SensesLayer>();
		senses.SightRange = 30 * Scale;
		senses.FieldOfView = 360;
		root.AddComponent<AnimationLayer>().EnableFootstepSounds = false;
		var npc = root.AddComponent<Gorge>();
		npc.Faction = Factions.Monster;
		npc.Health = npc.BaseHealth;
		npc.EyeHeight = 0.6f * Scale;
		npc.DisplayName = "Gorge";
		npc.Renderer = renderer;
	}

	protected override void OnStart()
	{
		base.OnStart();
		if ( !Networking.IsHost ) return;
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model )
			world = new( model.PropGraph, model );
		if ( mount?.GetByPath( "mount://ns2/ns2/models/alien/gorge/gorge_view.model.vmdl" ) is ModelLoader arms )
			weapon = new( arms.PropGraph, arms );
		worldTag = tag => AlienFootsteps.OnTag( this, "gorge", tag );
		weaponTag = OnWeaponTag;
		AnimationTime = 0;
		aim = WorldRotation;
		nextHatch = 2.5f;
		PlayCue( "spawn", WorldPosition );
	}

	protected override void Think()
	{
		var enemy = targeting.FindEnemy( this, out var lastSeen, static ( obj, _ ) =>
			obj.Tags.HasAny( "structure", "ns2_mac" ) );
		float combatWeight = AttackWeight( enemy, lastSeen );
		bool bombard = false;
		if ( BileBombUnlocked )
		{
			var target = targeting.FindEnemy( this, out var lastSeenBile, static ( obj, _ ) =>
				!obj.Tags.HasAny( "ns2_mac", "ns2_exo", "structure" ) || obj.GetComponent<AlienStructure>() is { IsBuilt: false } );
			if ( target.IsValid() )
			{
				float weight = BombardWeight( target );
				if ( weight > combatWeight )
				{
					enemy = target;
					lastSeen = lastSeenBile;
					combatWeight = weight;
					bombard = true;
				}
			}
		}
		bool visible = enemy.IsValid() && lastSeen is null;
		bombarding = false;
		assignedTarget = null;
		if ( SupportAlly( combatWeight, visible ) || PlantHydra( lastSeen ?? enemy?.WorldPosition ) )
		{
			escort = null;
			return;
		}
		assignedTarget = enemy;
		bombarding = bombard && WorldPosition.Distance( lastSeen ?? enemy.WorldPosition ) <= 12 * Scale;
		var goal = lastSeen ?? enemy?.WorldPosition;
		firing = visible && (bombard ? bombarding : EyePosition.Distance( goal.Value ) <= 20 * Scale);
		if ( goal is null )
		{
			if ( !FollowLifeform() )
			{
				Animation.SetLookTarget( null );
				GameObject.GetOrAddComponent<AlienExploration>().Explore( Scale );
			}
			return;
		}
		escort = null;
		Animation.SetLookTarget( visible ? enemy : null );
		if ( repath > 0 ) return;
		repath = 0.2f;
		float distance = EyePosition.Distance( goal.Value ) / Scale;
		if ( bombarding && distance < 8 && Random.Shared.NextSingle() < 0.9f )
			Movement.MoveTo( WorldPosition - WorldRotation.Forward * (0.75f * Scale), 0, faceTarget: enemy );
		else if ( !bombarding && visible && distance is > 5 and < 15 )
			Movement.MoveTo( WorldPosition - WorldRotation.Forward * (1.5f * Scale), 0, faceTarget: enemy );
		else if ( firing ) Movement.Stop();
		else Movement.MoveTo( goal.Value, Scale, faceTarget: visible ? enemy : null );
	}

	float BombardWeight( GameObject target )
	{
		float health = HealthFraction( this );
		float urgency = target.Tags.Has( "ns2_exo" ) ? 2.5f : target.Tags.Has( "ns2_mac" ) ? 0.4f : 0;
		float weight = 2.5f + urgency + personality.Aggression - (1 - health);
		return underFire > 0 ? weight * 0.25f * health : weight;
	}

	BaseNpc escort;
	TimeUntil checkEscort;

	bool FollowLifeform()
	{
		if ( checkEscort > 0 ) return escort.IsValid() && escort.Health > 0;
		checkEscort = 0.25f;
		escort = null;
		float nearest = 30 * Scale;
		foreach ( var npc in Scene.GetAllComponents<BaseNpc>() )
		{
			if ( npc is not (Skulk or Onos or Fade or Lerk) || npc.Health <= 0 || npc.Faction != Faction ||
				npc.GameObject.Tags.Has( "ns2_hallucination" ) ) continue;
			float distance = npc.WorldPosition.Distance( WorldPosition );
			if ( distance > nearest || Scene.GetAllComponents<Gorge>().Any( g => g != this && g.Health > 0 && g.escort == npc ) ) continue;
			nearest = distance;
			escort = npc;
		}
		if ( !escort.IsValid() ) return false;
		Animation.SetLookTarget( nearest >= 2.5f * Scale && nearest <= 6 * Scale ? escort.GameObject : null );
		if ( nearest > 6 * Scale ) Movement.MoveTo( escort.WorldPosition, 0 );
		else if ( nearest < 2.5f * Scale ) Movement.MoveTo( WorldPosition + (WorldPosition - escort.WorldPosition).Normal * (2 * Scale), 0 );
		else Movement.Stop();
		return true;
	}

	protected override void OnUpdate()
	{
		if ( GameObject.IsDestroyed ) return;
		base.OnUpdate();
		UpdateSlideSound();
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		output?.Set( "belly_yaw", bellyYaw * 45 );
		output?.Set( "move_speed", Animation.MoveVelocity.Length / (6 * Scale) );
		if ( !Networking.IsHost )
		{
			if ( output is null ) return;
			movePose.Write( Locomotion, output, AnimationTime );
			attackPose.Write( ActionLayer, output, AnimationTime );
			return;
		}
		if ( Health <= 0 ) return;
		UpdateBrood();
		if ( wounds.Update() is { } cue ) PlayCue( cue, WorldPosition );
		Regenerate( underFire > 0 );
		if ( world is null || weapon is null ) return;
		energy = Math.Min( 100, energy + 10 * Time.Delta );
		var direction = Animation.LookObject.IsValid() ? CalculateAimPoint( Animation.LookObject ) - EyePosition : WorldRotation.Forward;
		if ( wantsSpray && patient.IsValid() && patient.GetComponent<Rigidbody>() is { } body )
		{
			float distance = Math.Min( EyePosition.Distance( patient.WorldPosition ) / Scale, 1 );
			direction += body.Velocity * (distance * 0.6f);
		}
		if ( bombarding )
		{
			float speed = 11 * Scale + Animation.MoveVelocity.Length;
			float time = direction.WithZ( 0 ).Length / speed;
			direction.z += 0.5f * 9.81f * Scale * time * time;
		}
		aim = AlienAim.Turn( aim, direction, 5 );
		primaryHeld = firing && (bombarding ? energy > ShotCost : energy >= ShotCost) && Vector3.Dot( aim.Forward, direction.Normal ) > 0.98f;
		UpdateSprayInput( direction );
		var activity = secondaryAttacking && energy >= 10 || sprayAnimation > 0 ? "secondary" : primaryHeld ? "primary" : "none";
		var move = sliding ? "belly" : !Animation.IsGrounded ? "jump" : Movement.IsMoving ? "run" : "idle";
		world.Set( "move", move );
		world.Set( "alive", 1 );
		world.Set( "ability", bombarding ? "bomb" : "spit" );
		world.Set( "attack_speed", 1 );
		world.Set( "activity", activity );
		world.Update( AnimationTime, output, worldTag );
		movePose = world.GetPose( 0 );
		attackPose = world.GetPose( 1 );
		weapon.Set( "move", move );
		weapon.Set( "ability", bombarding ? "bomb" : "spit" );
		weapon.Set( "attack_speed", 1 );
		weapon.Set( "activity", activity );
		weapon.Update( AnimationTime, tag: weaponTag );
	}

	void OnWeaponTag( string tag )
	{
		if ( tag == "heal" && secondaryAttacking && energy >= 10 ) Spray();
		if ( tag != "shoot" || !primaryHeld || energy < ShotCost ) return;
		energy -= ShotCost;
		GorgeProjectile.Fire( this, aim, bombarding );
	}

	protected override void OnDeath( IDamageable.DeathEvent e )
	{
		while ( brood.Count > 0 ) BreakBabbler( brood[0] );
		PlayCue( "death", WorldPosition );
		base.OnDeath( e );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void PlayCue( string cue, Vector3 position )
	{
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/alien/gorge/{cue}.sound" ) is { } sound )
			Sound.Play( sound, position );
	}
}
