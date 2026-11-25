using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NS2;

[Title( "Fade" ), Category( "Natural Selection 2" )]
public sealed class Fade : BaseNpc
{
	const float Scale = MapLoader.Scale;
	static readonly SparkAnimationGraph.PoseParameters Locomotion = new( 0 ), ActionLayer = new( 1 );
	readonly AlienTargeting targeting = new();
	readonly AlienWounds wounds = new();
	[Sync( SyncFlags.FromHost )] public float Armor { get; private set; } = 80;
	[Sync( SyncFlags.FromHost )] TimeSince animationTime { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose movePose { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose attackPose { get; set; }
	SparkAnimationGraph.Playback world, weapon;
	Action<string> weaponTag;
	Rotation view;
	bool swiping;
	float energy = 100;
	TimeUntil repath;

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer )
	{
		const float radius = 0.4f * Scale, height = 2.1f * Scale;
		var child = new GameObject( root, true, "Collider" );
		child.Tags.Add( "playercontroller" );
		var collider = child.AddComponent<CapsuleCollider>();
		collider.Radius = radius;
		collider.Start = Vector3.Up * radius;
		collider.End = Vector3.Up * (height - radius);
		var body = root.AddComponent<Rigidbody>();
		body.MassOverride = 158;
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
		npc.Health = 250;
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
		if ( goal is null )
		{
			swiping = false;
			GameObject.GetOrAddComponent<AlienExploration>().Explore( Scale );
			return;
		}
		var touch = visible ? enemy.GetComponentInChildren<Collider>()?.FindClosestPoint( EyePosition ) ?? goal.Value : goal.Value;
		swiping = visible && EyePosition.Distance( touch ) <= (2.45f + Random.Shared.NextSingle() * 0.075f) * Scale;
		if ( repath <= 0 )
		{
			Movement.MoveTo( goal.Value, Scale, faceTarget: visible ? enemy : null );
			repath = 0.2f;
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		output?.Set( "move_speed", Animation.MoveVelocity.Length / (6.2f * Scale) );
		if ( !Networking.IsHost )
		{
			if ( output is null ) return;
			movePose.Write( Locomotion, output, animationTime );
			attackPose.Write( ActionLayer, output, animationTime );
			return;
		}

		if ( world is null || weapon is null || Health <= 0 ) return;
		var aim = Animation.LookObject.IsValid() ? CalculateAimPoint( Animation.LookObject ) - EyePosition : WorldRotation.Forward;
		view = AlienAim.Turn( view, aim, 5 );
		energy = Math.Min( 100, energy + 10 * Time.Delta );
		var activity = swiping && energy >= 7 ? "primary" : "none";
		world.Set( "attack_speed", 1 );
		world.Set( "alive", 1 );
		world.Set( "move", !Animation.IsGrounded ? "jump" : Movement.IsMoving ? "run" : "idle" );
		world.Set( "ability", "swipe" );
		world.Set( "activity", activity );
		world.Update( animationTime, output );
		movePose = world.GetPose( 0 );
		attackPose = world.GetPose( 1 );
		weapon.Set( "ability", "swipe" );
		weapon.Set( "attack_speed", 1 );
		weapon.Set( "activity", activity );
		weapon.Update( animationTime, tag: weaponTag );
	}

	void OnWeaponTag( string tag )
	{
		if ( tag != "hit" ) return;
		energy = Math.Max( 0, energy - 7 );
		var (hit, victim) = AlienMelee.Trace( this, view, 1.6f * Scale, 0.7f, 1.2f );
		if ( victim is null ) return;
		bool player = hit.GameObject.GetComponentInParent<PlayerController>().IsValid();
		var damage = new DamageInfo { Damage = player ? 75 : 37.5f, Origin = EyePosition, Position = hit.EndPosition, Attacker = GameObject, Weapon = GameObject };
		damage.Tags.Add( "puncture" );
		victim.OnDamage( damage );
	}

	protected override void OnDamaging( IDamageable.DamageEvent e )
	{
		if ( e.AcceptedDamage > 0 ) e.AcceptedDamage -= Math.Min( e.AcceptedDamage * 0.7f, Armor * AlienArmor.HealthPerArmor( e.DamageInfo ) );
	}

	protected override void OnDamaged( IDamageable.DamageEvent e )
	{
		if ( e.AcceptedDamage > 0 ) Armor -= wounds.ApplyDamage( e, Armor, MaxHealth, 80 );
	}

	protected override GameObject CreateRagdoll()
	{
		var corpse = base.CreateRagdoll();
		if ( corpse.IsValid() ) corpse.AddComponent<AlienCorpse>().Age = 0;
		return corpse;
	}
}
