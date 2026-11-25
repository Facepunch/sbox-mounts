using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace NS2;

[Title( "Gorge" ), Category( "Natural Selection 2" )]
public sealed partial class Gorge : BaseNpc, Component.IDamageable
{
	float IDamageable.Armour { get => Armor; set => Armor = Math.Clamp( value, 0, 50 ); }
	float IDamageable.MaxArmour => 50;

	const float Scale = MapLoader.Scale;
	static readonly SparkAnimationGraph.PoseParameters Locomotion = new( 0 ), ActionLayer = new( 1 );
	readonly AlienTargeting targeting = new();
	readonly AlienWounds wounds = new();
	[Sync( SyncFlags.FromHost )] public float Armor { get; private set; } = 50;
	[Sync( SyncFlags.FromHost )] TimeSince animationTime { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose movePose { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose attackPose { get; set; }
	SparkAnimationGraph.Playback world, weapon;
	Action<string> worldTag, weaponTag;
	Rotation aim;
	float energy = 100;
	[Property] public bool BileBombUnlocked { get; set; } = true;
	bool firing, primaryHeld, bombarding;
	float ShotCost => bombarding ? 20 : 7;
	TimeUntil repath, nextHeal, underFire;

	internal static void RegisterSounds( MountContext context )
	{
		AlienFootsteps.Register( context, "gorge" );
		foreach ( var cue in new[] { "spit", "spit_hit", "bilebomb", "bilebomb_hit", "heal_spray", "slide_loop", "spawn", "wound", "wound_serious", "death" } )
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
		npc.Health = 160;
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
		animationTime = 0;
		aim = WorldRotation;
		nextHatch = 2.5f;
		PlayCue( "spawn", WorldPosition );
	}

	protected override void Think()
	{
		var enemy = targeting.FindEnemy( this, out var lastSeen );
		bool visible = enemy.IsValid() && lastSeen is null;
		bombarding = false;
		if ( SupportAlly( visible ) ) return;
		bombarding = visible && BileBombUnlocked && enemy.Tags.HasAny( "structure", "ns2_exo", "ns2_mac" );
		Animation.SetLookTarget( visible ? enemy : null );
		var goal = lastSeen ?? enemy?.WorldPosition;
		firing = visible && EyePosition.Distance( goal.Value ) <= (bombarding ? 12 : 20) * Scale;
		if ( goal is null )
		{
			GameObject.GetOrAddComponent<AlienExploration>().Explore( Scale );
			return;
		}
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

	protected override void OnUpdate()
	{
		base.OnUpdate();
		UpdateSlideSound();
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		output?.Set( "belly_yaw", bellyYaw * 45 );
		output?.Set( "move_speed", Animation.MoveVelocity.Length / (6 * Scale) );
		if ( !Networking.IsHost )
		{
			if ( output is null ) return;
			movePose.Write( Locomotion, output, animationTime );
			attackPose.Write( ActionLayer, output, animationTime );
			return;
		}
		if ( Health <= 0 ) return;
		UpdateBrood();
		if ( wounds.Update() is { } cue ) PlayCue( cue, WorldPosition );
		if ( nextHeal <= 0 )
		{
			nextHeal = 2;
			if ( underFire <= 0 )
			{
				float heal = Math.Clamp( MaxHealth * 0.02f, 1, 20 );
				float missing = Math.Clamp( MaxHealth - Health, 0, heal );
				Health += missing;
				Armor = Math.Min( 50, Armor + (heal - missing) * 0.5f );
			}
		}
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
		world.Update( animationTime, output, worldTag );
		movePose = world.GetPose( 0 );
		attackPose = world.GetPose( 1 );
		weapon.Set( "move", move );
		weapon.Set( "ability", bombarding ? "bomb" : "spit" );
		weapon.Set( "attack_speed", 1 );
		weapon.Set( "activity", activity );
		weapon.Update( animationTime, tag: weaponTag );
	}

	void OnWeaponTag( string tag )
	{
		if ( tag == "heal" && secondaryAttacking && energy >= 10 ) Spray();
		if ( tag != "shoot" || !primaryHeld || energy < ShotCost ) return;
		energy -= ShotCost;
		GorgeProjectile.Fire( this, aim, bombarding );
	}

	protected override void OnDamaging( IDamageable.DamageEvent e )
	{
		AbsorbWithBabblers( e );
		AlienArmor.Absorb( e, Armor );
	}

	protected override void OnDamaged( IDamageable.DamageEvent e )
	{
		if ( e.AcceptedDamage <= 0 ) return;
		Armor -= wounds.ApplyDamage( e, Armor, MaxHealth, 50 );
		underFire = 3;
	}

	protected override void OnDeath( IDamageable.DeathEvent e )
	{
		while ( brood.Count > 0 ) BreakBabbler();
		PlayCue( "death", WorldPosition );
		base.OnDeath( e );
	}

	protected override GameObject CreateRagdoll()
	{
		var corpse = base.CreateRagdoll();
		if ( corpse.IsValid() ) corpse.AddComponent<AlienCorpse>().Age = 0;
		return corpse;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void PlayCue( string cue, Vector3 position )
	{
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/alien/gorge/{cue}.sound" ) is { } sound )
			Sound.Play( sound, position );
	}
}
