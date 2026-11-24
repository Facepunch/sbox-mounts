using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace NS2;

[Title( "Onos" ), Category( "Natural Selection 2" )]
public sealed partial class Onos : BaseNpc
{
	const float Scale = MapLoader.Scale;
	const float RunSpeed = 6.6f * Scale;
	const float MaxArmor = 450;
	static readonly SparkAnimationGraph.PoseParameters Locomotion = new( 0 ), ActionLayer = new( 1 );
	[Sync( SyncFlags.FromHost )] public float Armor { get; private set; } = MaxArmor;
	[Sync( SyncFlags.FromHost )] TimeSince AnimationTime { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose MovePose { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose AttackPose { get; set; }
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] Rotation ViewRotation { get; set; }
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] float BodyYaw { get; set; }
	float standingYaw, fallSpeed;
	SparkAnimationGraph.Playback world, weapon;
	Action<string> weaponTag, worldTag;
	GameObject target;
	bool attacking;
	bool? smashSwing;
	float energy = 100;
	TimeUntil repath;
	readonly AlienTargeting targeting = new();

	internal static void RegisterSounds( MountContext context )
	{
		foreach ( var cue in new[] { "gore", "onos_step", "wound_serious", "charge_hit_marine", "land", "stomp", "wound", "death", "rumble" } )
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
		body.EnableImpactDamage = false;
		var agent = root.AddComponent<NavMeshAgent>();
		agent.Radius = radius;
		agent.Height = height;
		agent.Enabled = false;
		var movement = root.AddComponent<OnosMovement>();
		movement.RunSpeed = RunSpeed;
		var senses = root.AddComponent<SensesLayer>();
		senses.SightRange = 20 * Scale;
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
		var gravity = MathF.Abs( Scene.PhysicsWorld.Gravity.z );
		if ( gravity > 0 ) GetComponent<Rigidbody>().GravityScale = 21.5f * Scale / gravity;
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model )
			world = new( model.PropGraph, model );
		if ( mount?.GetByPath( "mount://ns2/ns2/models/alien/onos/onos_view.model.vmdl" ) is ModelLoader view )
		{
			weapon = new( view.PropGraph, view );
			stompDuration = view.SequenceDuration( view.SequenceIndex( "stomp" ) );
		}
		AnimationTime = 0;
		ViewRotation = WorldRotation;
		standingYaw = WorldRotation.Angles().yaw;
		weaponTag = OnWeaponTag;
		worldTag = OnWorldTag;
		weapon?.Set( "attack_speed", 1 );
	}

	protected override void Think()
	{
		var enemy = targeting.FindEnemy( this, out _ );
		if ( target != enemy ) repath = 0;
		target = enemy;
		Animation.SetLookTarget( enemy );
		if ( !enemy.IsValid() )
		{
			attacking = false;
			SetShield( false );
			GameObject.GetOrAddComponent<AlienExploration>().Explore( Scale );
			UpdateCharge();
			return;
		}
		var point = enemy.GetComponentInChildren<Collider>()?.FindClosestPoint( EyePosition ) ?? enemy.WorldPosition;
		float distance = EyePosition.Distance( point );
		attacking = distance < 4 * Scale;
		ChooseAbility( enemy, distance );
		if ( distance < Scale ) Movement.Stop();
		else Pursue( enemy.WorldPosition, enemy );
	}

	void Pursue( Vector3 point, GameObject faceTarget = null )
	{
		if ( repath > 0 ) return;
		Movement.MoveTo( point, Scale, faceTarget: faceTarget );
		repath = 0.2f;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		var velocity = Animation.MoveVelocity.WithZ( 0 );
		float speedRatio = velocity.Length / RunSpeed;
		UpdateRumble( Animation.MoveVelocity.Length / RunSpeed );
		if ( Networking.IsHost )
		{
			if ( Animation.IsGrounded && fallSpeed > 5 * Scale ) PlayCue( "land" );
			fallSpeed = Animation.IsGrounded ? 0 : -Animation.MoveVelocity.z;

			var aim = attacking && target.IsValid() ? CalculateAimPoint( target ) - EyePosition : WorldRotation.Forward;
			ViewRotation = AlienAim.Turn( ViewRotation, aim, 5 );
			var yaw = WorldRotation.Angles().yaw;
			float offset = Angles.NormalizeAngle( standingYaw - yaw );
			BodyYaw = velocity.Length > 0.1f * Scale ? AlienAim.ApproachAngle( offset, 0, Time.Delta * 5 ) : Math.Clamp( offset, -85, 85 );
			standingYaw = yaw + BodyYaw;
		}
		output?.Set( "body_yaw", BodyYaw % 90 );
		output?.Set( "body_pitch", -ViewRotation.Angles().pitch );
		output?.Set( "move_speed", Shielding ? Math.Clamp( 1 - speedRatio, 0.75f, 1 ) : speedRatio );
		output?.Set( "stoop", Shielding ? 0.68f : 0f );
		output?.Set( "move_yaw", velocity.IsNearlyZero() ? 0f :
			MathF.Atan2( Vector3.Dot( velocity, ViewRotation.Forward ), Vector3.Dot( velocity, ViewRotation.Left ) ).RadianToDegree() );
		if ( Networking.IsHost && world is not null )
		{
			UpdateAbilities( velocity );
			if ( wounds.Update() is { } cue ) PlayCue( cue );
			var ability = Shielding ? "boneshield" : "gore";
			var move = Shielding ? "shield" : !Animation.IsGrounded ? "jump" : velocity.Length > 0.1f * Scale ? "run" : "idle";
			bool canSwing = attacking && energy >= 10 && !Shielding && !stomping;
			if ( canSwing ) smashSwing ??= TraceGore().Victim is Component c && c.GameObject.Tags.Has( "structure" );
			else smashSwing = null;
			var activity = stomping ? (sinceStomp < stompDuration * 0.95f ? "secondary" : "none")
				: Shielding ? "primary" : canSwing ? (smashSwing == true ? "smash" : "primary") : "none";
			world.Set( "alive", Health > 0 ? 1 : 0 );
			world.Set( "ability", ability );
			world.Set( "move", move );
			world.Set( "activity", activity );
			world.Update( AnimationTime, output, worldTag );
			MovePose = world.GetPose( 0 );
			AttackPose = world.GetPose( 1 );
			weapon?.Set( "ability", ability );
			weapon?.Set( "move", Shielding ? (velocity.Length > 0.1f * Scale ? "run" : "idle") : move );
			weapon?.Set( "activity", activity );
			weapon?.Update( AnimationTime, tag: weaponTag );
			return;
		}
		if ( output is null ) return;
		MovePose.Write( Locomotion, output, AnimationTime );
		AttackPose.Write( ActionLayer, output, AnimationTime );
	}

	SoundHandle rumble;
	float rumbleGain;
	SoundProject.Envelope rumbleVolume;

	void UpdateRumble( float speed )
	{
		if ( Application.IsDedicatedServer ) return;
		if ( rumble is null )
		{
			if ( speed <= 0 ) return;
			var mount = Sandbox.Mounting.Directory.Get( "ns2" ) as GameMount;
			var layer = mount?.FindSoundEvent( "sound/NS2.fev/alien/onos/rumble" )?.Layers.FirstOrDefault();
			rumbleVolume = layer?.Envelopes.FirstOrDefault( e => e.ParameterName == "speed" && e.Effect == SoundProject.Effect.Volume );
			if ( rumbleVolume is null ) return;
			var sound = GameResource.Load<SoundEvent>( "mount://ns2/sounds/alien/onos/rumble.sound" );
			if ( sound is null ) return;
			rumble = Sound.Play( sound, WorldPosition );
			if ( rumble is null ) return;
			rumbleGain = rumble.Volume;
		}
		rumble.Position = WorldPosition;
		rumble.Volume = rumbleGain * rumbleVolume.Evaluate( speed );
	}

	protected override void OnDisabled()
	{
		rumble?.Stop();
		rumble = null;
		base.OnDisabled();
	}

	void OnWeaponTag( string tag )
	{
		if ( stomping )
		{
			if ( tag == "stomp_hit" && !stompHit ) StartWave();
			if ( tag == "end" && sinceStomp >= stompDuration * 0.25f ) stomping = false;
			return;
		}
		if ( tag != "hit" || Shielding ) return;
		PlayCue( "gore" );
		energy = Math.Max( 0, energy - 10 );
		bool smash = smashSwing == true;
		smashSwing = null;
		var (hit, victim) = TraceGore();
		if ( victim is null ) return;
		bool structural = victim is Component c && c.GameObject.Tags.Has( "structure" );
		DealDamage( victim, structural ? 180 : 90, hit.EndPosition, smash ? "smash" : "gore" );
	}

	(SceneTraceResult Hit, IDamageable Victim) TraceGore()
	{
		var view = ViewRotation;
		float range = (1.7f + Math.Max( 0, -view.Forward.z ) * 0.9f) * Scale;
		return AlienMelee.Trace( this, view, range, 1, 1.4f );
	}

	void DealDamage( IDamageable victim, float amount, Vector3 position, string tag )
	{
		var damage = new DamageInfo { Damage = amount, Attacker = GameObject, Weapon = GameObject, Origin = EyePosition, Position = position };
		damage.Tags.Add( tag );
		victim.OnDamage( damage );
	}

	void OnWorldTag( string tag )
	{
		if ( tag is "step" or "step_run" or "step_sprint" && Animation.IsGrounded && Animation.MoveVelocity.Length > 0.75f * Scale )
			PlayCue( "onos_step" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayCue( string cue )
	{
		var sound = GameResource.Load<SoundEvent>( $"mount://ns2/sounds/alien/onos/{cue}.sound" );
		if ( sound is not null ) Sound.Play( sound, WorldPosition );
	}

	protected override void OnDamaging( IDamageable.DamageEvent e )
	{
		if ( !e.DamageInfo.Attacker.IsValid() && e.DamageInfo.Tags.Has( "fall" ) )
		{
			e.AcceptedDamage = 0;
			return;
		}
		if ( e.AcceptedDamage <= 0 ) return;
		sinceDamage = 0;
		BlockDamage( e );
		e.AcceptedDamage -= Math.Min( e.AcceptedDamage * 0.7f, Armor * AlienArmor.HealthPerArmor( e.DamageInfo ) );
	}

	protected override void OnDamaged( IDamageable.DamageEvent e )
	{
		if ( e.AcceptedDamage <= 0 ) return;
		float ratio = AlienArmor.HealthPerArmor( e.DamageInfo );
		float absorbed = ratio == 0 ? 0 : Math.Min( Armor, e.AcceptedDamage * (0.7f / 0.3f) / ratio );
		Armor -= absorbed;
		float damage = e.AcceptedDamage + absorbed * ratio;
		wounds.Hit( damage / MaxHealth, (e.AcceptedDamage + absorbed * 2) / (MaxHealth + MaxArmor * 2) );
	}

	readonly AlienWounds wounds = new();

	protected override GameObject CreateRagdoll()
	{
		var corpse = base.CreateRagdoll();
		if ( corpse.IsValid() ) corpse.AddComponent<AlienCorpse>().Age = 0;
		return corpse;
	}

	protected override void OnDeath( IDamageable.DeathEvent e )
	{
		PlayCue( "death" );
		base.OnDeath( e );
	}
}
