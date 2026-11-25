using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NS2;

[Title( "Lerk" ), Category( "Natural Selection 2" )]
public sealed partial class Lerk : BaseNpc, Component.IDamageable
{
	float IDamageable.Armour { get => Armor; set => Armor = Math.Clamp( value, 0, 30 ); }
	float IDamageable.MaxArmour => 30;

	const float Scale = MapLoader.Scale;
	static readonly SparkAnimationGraph.PoseParameters Locomotion = new( 0 ), ActionLayer = new( 1 );
	readonly AlienTargeting targeting = new();
	readonly AlienWounds wounds = new();
	[Sync( SyncFlags.FromHost )] public float Armor { get; private set; } = 30;
	[Sync( SyncFlags.FromHost )] TimeSince animationTime { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose movePose { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose attackPose { get; set; }
	SparkAnimationGraph.Playback world, weapon;
	Action<string> weaponTag;
	Rotation view;
	bool attacking;
	TimeSince lastFlap = 1;
	float energy = 100;
	TimeUntil repath, nextHeal, underFire;

	internal static void RegisterSounds( MountContext context )
	{
		foreach ( var cue in new[] { "spawn", "bite", "flap", "death", "wound", "wound_serious" } )
		{
			var id = $"alien/lerk/{cue}";
			context.Add( ResourceType.GameResource, $"sounds/{id}.sound", new SparkSoundLoader( id ) );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayCue( string cue )
	{
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/alien/lerk/{cue}.sound" ) is { } sound )
			GameObject.PlaySound( sound );
	}

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer )
	{
		const float radius = 0.4f * Scale, height = 2 * radius;
		var child = new GameObject( root, true, "Collider" );
		child.Tags.Add( "playercontroller" );
		var collider = child.AddComponent<SphereCollider>();
		collider.Radius = radius;
		collider.Center = Vector3.Up * radius;
		var body = root.AddComponent<Rigidbody>();
		body.MassOverride = 54;
		body.EnableImpactDamage = false;
		var agent = root.AddComponent<NavMeshAgent>();
		agent.Radius = radius;
		agent.Height = height;
		agent.Enabled = false;
		root.AddComponent<NavigationLayer>().RunSpeed = 2.8f * Scale;
		var senses = root.AddComponent<SensesLayer>();
		senses.SightRange = 35 * Scale;
		senses.FieldOfView = 360;
		root.AddComponent<AnimationLayer>().EnableFootstepSounds = false;
		var npc = root.AddComponent<Lerk>();
		npc.Faction = Factions.Monster;
		npc.Health = 180;
		npc.EyeHeight = 0.5f * Scale;
		npc.DisplayName = "Lerk";
		npc.Renderer = renderer;
	}

	protected override void OnStart()
	{
		base.OnStart();
		if ( !Networking.IsHost ) return;
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model )
			world = new( model.PropGraph, model );
		if ( mount?.GetByPath( "mount://ns2/ns2/models/alien/lerk/lerk_view.model.vmdl" ) is ModelLoader viewModel )
			weapon = new( viewModel.PropGraph, viewModel );
		PlayCue( "spawn" );
		var gravity = MathF.Abs( Scene.PhysicsWorld.Gravity.z );
		if ( gravity > 0 ) GetComponent<Rigidbody>().GravityScale = 7 * Scale / gravity;
		view = WorldRotation;
		animationTime = 0;
		weaponTag = OnWeaponTag;
	}

	protected override void Think()
	{
		var enemy = targeting.FindEnemy( this, out var lastSeen );
		bool visible = enemy.IsValid() && lastSeen is null;
		Animation.SetLookTarget( visible ? enemy : null );
		var goal = lastSeen ?? enemy?.WorldPosition;
		var touch = visible ? enemy.GetComponentInChildren<Collider>()?.FindClosestPoint( EyePosition ) ?? enemy.WorldPosition : default;
		attacking = visible && EyePosition.Distance( touch ) < 1.5f * Scale;
		if ( goal is null )
			GameObject.GetOrAddComponent<AlienExploration>().Explore( Scale );
		else if ( attacking && enemy.Tags.Has( "structure" ) )
			Movement.Stop();
		else if ( repath <= 0 )
		{
			Movement.MoveTo( goal.Value, Scale, faceTarget: visible ? enemy : null );
			repath = 0.2f;
		}
		if ( !attacking && Movement.MoveTarget is { } destination && IsOnGround && lastFlap > 0.45f && energy > 3 && !PhysicsCarrier.IsValid() )
		{
			var velocity = Animation.MoveVelocity;
			if ( velocity.WithZ( 0 ).Length < 4.5f * Scale ) velocity += (destination - WorldPosition).Normal * (4.15f * Scale);
			velocity.z = velocity.z * 0.5f + 5 * Scale;
			Launch( velocity );
			energy -= 3;
			lastFlap = 0;
			PlayCue( "flap" );
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		var velocity = Animation.MoveVelocity;
		output?.Set( "move_speed", Animation.IsGrounded ? 2.8f : Math.Clamp( (velocity.Length / Scale - 2.8f) / 13, 0, 1 ) );
		output?.Set( "move_yaw", !Animation.IsGrounded ? 90f : velocity.IsNearlyZero() ? 0f :
			MathF.Atan2( Vector3.Dot( velocity, WorldRotation.Forward ), Vector3.Dot( velocity, WorldRotation.Left ) ).RadianToDegree() );
		if ( !Networking.IsHost )
		{
			if ( output is null ) return;
			movePose.Write( Locomotion, output, animationTime );
			attackPose.Write( ActionLayer, output, animationTime );
			return;
		}

		if ( world is null || weapon is null || Health <= 0 ) return;
		if ( nextHeal <= 0 )
		{
			nextHeal = 2;
			if ( underFire <= 0 ) Heal( Math.Clamp( MaxHealth * 0.02f, 1, 20 ) );
		}
		if ( wounds.Update() is { } cue ) PlayCue( cue );
		var aim = Animation.LookObject.IsValid() ? CalculateAimPoint( Animation.LookObject ) - EyePosition : WorldRotation.Forward;
		view = AlienAim.Turn( view, aim, 5 );
		energy = Math.Min( 100, energy + 10 * Time.Delta );
		var activity = attacking && energy >= 5 ? "primary" : "none";
		var move = !Animation.IsGrounded ? "fly" : Movement.IsMoving ? "run" : "idle";
		world.Set( "attack_speed", 1 );
		world.Set( "alive", 1 );
		world.Set( "move", move );
		world.Set( "flapping", lastFlap <= 0.5f ? 1 : 0 );
		world.Set( "ability", "bite" );
		world.Set( "activity", activity );
		world.Update( animationTime, output );
		movePose = world.GetPose( 0 );
		attackPose = world.GetPose( 1 );
		weapon.Set( "move", move );
		weapon.Set( "ability", "bite" );
		weapon.Set( "attack_speed", 1 );
		weapon.Set( "activity", activity );
		weapon.Update( animationTime, tag: weaponTag );
	}

	void OnWeaponTag( string tag )
	{
		if ( tag != "hit" ) return;
		PlayCue( "bite" );
		energy = Math.Max( 0, energy - 5 );
		var (hit, victim) = AlienMelee.Trace( this, view, 1.5f * Scale, 0.9f, 1.2f );
		if ( victim is not Component target ) return;
		victim.OnDamage( new DamageInfo { Damage = 60, Origin = EyePosition, Position = hit.EndPosition, Attacker = GameObject, Weapon = GameObject } );
		if ( target.IsValid() && victim.Health > 0 && !target.GameObject.Tags.HasAny( "ns2_exo", "ns2_exosuit" ) &&
			(target.GameObject.Tags.Has( "ns2_marine" ) || target.GetComponentInParent<PlayerController>().IsValid()) )
			target.GameObject.GetOrAddComponent<LerkPoison>().Apply( victim );
	}

	protected override void OnDamaging( IDamageable.DamageEvent e )
	{
		if ( e.AcceptedDamage > 0 ) e.AcceptedDamage -= Math.Min( e.AcceptedDamage * 0.7f, Armor * AlienArmor.HealthPerArmor( e.DamageInfo ) );
	}

	protected override void OnDamaged( IDamageable.DamageEvent e )
	{
		if ( e.AcceptedDamage <= 0 ) return;
		Armor -= wounds.ApplyDamage( e, Armor, MaxHealth, 30 );
		underFire = 3;
	}

	void Heal( float amount )
	{
		float health = Math.Clamp( MaxHealth - Health, 0, amount );
		Health += health;
		Armor = Math.Min( 30, Armor + (amount - health) * 0.5f );
	}

	protected override void OnDeath( IDamageable.DeathEvent e )
	{
		PlayCue( "death" );
		base.OnDeath( e );
	}

	protected override GameObject CreateRagdoll()
	{
		var corpse = base.CreateRagdoll();
		if ( corpse.IsValid() ) corpse.AddComponent<AlienCorpse>().Age = 0;
		return corpse;
	}
}
