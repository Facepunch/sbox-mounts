using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace NS2;

[Title( "Onos" ), Category( "Natural Selection 2" )]
public sealed class Onos : BaseNpc
{
	const float Scale = MapLoader.Scale;
	const float RunSpeed = 6.6f * Scale;
	const float MaxArmor = 450;
	static readonly SparkAnimationGraph.PoseParameters Locomotion = new( 0 ), ActionLayer = new( 1 );
	[Sync( SyncFlags.FromHost )] public float Armor { get; private set; } = MaxArmor;
	[Sync( SyncFlags.FromHost )] TimeSince AnimationTime { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose MovePose { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose AttackPose { get; set; }
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] float AimPitch { get; set; }
	SparkAnimationGraph.Playback world, weapon;
	Action<string> weaponTag, worldTag;
	GameObject target;
	bool attacking;
	float energy = 100;
	TimeUntil repath;

	internal static void RegisterSounds( MountContext context )
	{
		foreach ( var cue in new[] { "gore", "onos_step" } )
		{
			var id = $"alien/onos/{cue}";
			context.Add( ResourceType.GameResource, $"sounds/{id}.sound", new SparkSoundLoader( id ) );
		}
	}

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer )
	{
		const float radius = 0.7f * Scale, height = 2.4f * Scale;
		var collider = new GameObject( root, true, "Collider" );
		collider.Tags.Add( "playercontroller" );
		var capsule = collider.AddComponent<CapsuleCollider>();
		capsule.Start = Vector3.Up * radius;
		capsule.End = Vector3.Up * (height - radius);
		capsule.Radius = radius;
		var body = root.AddComponent<Rigidbody>();
		body.MassOverride = 453;
		body.MotionEnabled = false;
		body.EnableImpactDamage = false;
		var agent = root.AddComponent<NavMeshAgent>();
		agent.Radius = radius;
		agent.Height = height;
		var movement = root.AddComponent<NavigationLayer>();
		movement.RunSpeed = RunSpeed;
		var senses = root.AddComponent<SensesLayer>();
		senses.FieldOfView = 360;
		var anim = root.AddComponent<AnimationLayer>();
		anim.EnableFootstepSounds = false;
		var npc = root.AddComponent<Onos>();
		npc.DisplayName = "Onos";
		npc.Faction = Factions.Monster;
		npc.EyeHeight = 2.5f * Scale;
		npc.Health = 700;
		npc.Renderer = renderer;
	}

	protected override void OnStart()
	{
		base.OnStart();
		if ( !Networking.IsHost ) return;
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model )
			world = new( model.PropGraph, model );
		if ( mount?.GetByPath( "mount://ns2/ns2/models/alien/onos/onos_view.model.vmdl" ) is ModelLoader view )
			weapon = new( view.PropGraph, view );
		AnimationTime = 0;
		weaponTag = OnWeaponTag;
		worldTag = OnWorldTag;
		weapon?.Set( "attack_speed", 1 );
	}

	protected override void Think()
	{
		var enemy = Senses.FindBestTarget();
		if ( target != enemy ) repath = 0;
		target = enemy;
		Animation.SetLookTarget( enemy );
		if ( !enemy.IsValid() )
		{
			attacking = false;
			Movement.Stop();
			return;
		}
		var point = enemy.GetComponentInChildren<Collider>()?.FindClosestPoint( EyePosition ) ?? enemy.WorldPosition;
		float distance = EyePosition.Distance( point );
		attacking = distance < 4 * Scale;
		if ( distance < Scale ) Movement.Stop();
		else if ( repath <= 0 )
		{
			Movement.MoveTo( enemy.WorldPosition, Scale, RunSpeed, enemy );
			repath = 0.2f;
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		var velocity = Animation.MoveVelocity.WithZ( 0 );
		output?.Set( "move_speed", velocity.Length / RunSpeed );
		output?.Set( "move_yaw", velocity.IsNearlyZero() ? 0f :
			MathF.Atan2( Vector3.Dot( velocity, WorldRotation.Forward ), Vector3.Dot( velocity, WorldRotation.Left ) ).RadianToDegree() );
		if ( Networking.IsHost && world is not null )
		{
			energy = Math.Min( 100, energy + Time.Delta * 10 );
			var aim = target.IsValid() ? CalculateAimPoint( target ) - EyePosition : WorldRotation.Forward;
			AimPitch = aim.IsNearlyZero() ? 0 : Rotation.LookAt( aim ).Angles().pitch;
			output?.Set( "body_pitch", -AimPitch );
			var move = !Animation.IsGrounded ? "jump" : velocity.Length > 0.1f * Scale ? "run" : "idle";
			var activity = attacking && energy >= 10 ? "primary" : "none";
			world.Set( "alive", Health > 0 ? 1 : 0 );
			world.Set( "ability", "gore" );
			world.Set( "move", move );
			world.Set( "activity", activity );
			world.Update( AnimationTime, output, worldTag );
			MovePose = world.GetPose( 0 );
			AttackPose = world.GetPose( 1 );
			weapon?.Set( "ability", "gore" );
			weapon?.Set( "move", move );
			weapon?.Set( "activity", activity );
			weapon?.Update( AnimationTime, tag: weaponTag );
			return;
		}
		if ( output is null ) return;
		output.Set( "body_pitch", -AimPitch );
		MovePose.Write( Locomotion, output, AnimationTime );
		AttackPose.Write( ActionLayer, output, AnimationTime );
	}

	void OnWeaponTag( string tag )
	{
		if ( tag != "hit" ) return;
		PlayCue( "gore" );
		energy = Math.Max( 0, energy - 10 );
		var view = Rotation.From( AimPitch, WorldRotation.Angles().yaw, 0 );
		float range = (1.7f + Math.Max( 0, -view.Forward.z ) * 0.9f) * Scale;
		var (hit, victim) = AlienMelee.Trace( this, view, range, 1, 1.4f );
		if ( victim is null ) return;
		var damage = new DamageInfo { Damage = 90, Attacker = GameObject, Weapon = GameObject, Origin = EyePosition, Position = hit.EndPosition };
		damage.Tags.Add( "gore" );
		victim.OnDamage( damage );
	}

	void OnWorldTag( string tag )
	{
		if ( tag is "step" or "step_run" or "step_sprint" && Animation.IsGrounded && Animation.MoveVelocity.Length > 0.75f * Scale )
			PlayCue( "onos_step" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayCue( string cue ) => GameObject.PlaySound( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/alien/onos/{cue}.sound" ) );

	protected override void OnDamaging( IDamageable.DamageEvent e )
	{
		if ( e.AcceptedDamage > 0 ) e.AcceptedDamage -= Math.Min( e.AcceptedDamage * 0.7f, Armor * 2 );
	}

	protected override void OnDamaged( IDamageable.DamageEvent e )
	{
		if ( e.AcceptedDamage > 0 ) Armor = Math.Max( 0, Armor - e.AcceptedDamage * (0.7f / 0.3f / 2) );
	}
}
