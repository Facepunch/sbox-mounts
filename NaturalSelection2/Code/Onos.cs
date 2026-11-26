using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace NS2;

[Title( "Onos" ), Category( "Natural Selection 2" )]
public sealed partial class Onos : AlienLifeform, Component.IDamageable
{
	protected override Vector3 GetEngagementPoint() => WorldPosition + Vector3.Up * (1.3f * Scale);

	protected override int BaseHealth => 700;
	protected override int HealthPerBiomass => 50;

	float IDamageable.Armour { get => Armor; set => Armor = Math.Clamp( value, 0, MaxArmor ); }
	float IDamageable.MaxArmour => MaxArmor;

	const float Scale = MapLoader.Scale;
	internal static readonly float HorizontalExtents = new Vector2( 0.7f, 0.4f ).Length * Scale;
	const float RunSpeed = 6.6f * Scale;
	const float MaxArmor = 450;
	static readonly SparkAnimationGraph.PoseParameters Locomotion = new( 0 ), ActionLayer = new( 1 );
	[Sync( SyncFlags.FromHost )] public float Armor { get; private set; } = MaxArmor;
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose MovePose { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose AttackPose { get; set; }
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] Rotation ViewRotation { get; set; }
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] float BodyYaw { get; set; }
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] float Stoop { get; set; }
	bool stooping;
	TimeUntil nextStoopCheck = 0.3f;
	float standingYaw, fallSpeed;
	SparkAnimationGraph.Playback weapon;
	Action<string> weaponTag, worldTag;
	GameObject target;
	bool attacking;
	bool? smashSwing;
	float energy = 100;
	TimeUntil repath;
	readonly AlienTargeting targeting = new();

	internal static void RegisterSounds( MountContext context )
	{
		const string shieldBreak = "alien/common/armor_break";
		context.Add( ResourceType.GameResource, $"sounds/{shieldBreak}.sound", new SparkSoundLoader( shieldBreak ) );
		foreach ( var cue in new[] { "spawn", "gore", "gore_hit_marine", "gore_hit_thin_metal", "gore_hit_organic", "gore_hit_metal", "gore_hit_door", "onos_step", "wound_serious", "charge_hit_marine", "land", "stomp", "wound", "death", "rumble" } )
		{
			var id = $"alien/onos/{cue}";
			context.Add( ResourceType.GameResource, $"sounds/{id}.sound", new SparkSoundLoader( id ) );
		}
	}

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer, GameMount mount )
	{
		const float radius = 0.7f * Scale, height = 2.4f * Scale;
		var collider = new GameObject( root, true, "Collider" );
		collider.Tags.Add( "playercontroller" );
		var capsule = collider.AddComponent<CapsuleCollider>();
		capsule.Start = Vector3.Up * radius;
		capsule.End = Vector3.Up * (height - radius);
		capsule.Radius = radius;
		var body = root.AddComponent<Rigidbody>();
		body.MassOverride = OnosMass;
		body.EnableImpactDamage = false;
		var agent = root.AddComponent<NavMeshAgent>();
		agent.Radius = radius;
		agent.Height = height;
		agent.Enabled = false;
		var movement = root.AddComponent<OnosMovement>();
		movement.RunSpeed = RunSpeed;
		movement.TurnSpeed = 0;
		var senses = root.AddComponent<SensesLayer>();
		senses.SightRange = 20 * Scale;
		senses.FieldOfView = 360;
		var anim = root.AddComponent<AnimationLayer>();
		anim.EnableFootstepSounds = false;
		anim.TurnsBody = false;
		var npc = root.AddComponent<Onos>();
		npc.DisplayName = "Onos";
		npc.Faction = Factions.Monster;
		npc.EyeHeight = 2.5f * Scale;
		npc.Health = npc.BaseHealth;
		npc.Renderer = renderer;
		var layer = mount.FindSoundEvent( "sound/NS2.fev/alien/onos/rumble" )?.Layers.FirstOrDefault();
		var volume = layer?.Envelopes.FirstOrDefault( e => e.ParameterName == "speed" && e.Effect == SoundProject.Effect.Volume );
		if ( volume is not null )
			npc.RumbleVolume = new Curve( volume.Points.Select( p => new Curve.Frame( p.x, p.y ) { Mode = Curve.HandleMode.Linear } ) )
			{
				TimeRange = new Vector2( volume.Min, volume.Max )
			};
	}

	protected override void OnStart()
	{
		base.OnStart();
		if ( !Networking.IsHost ) return;
		PlayCue( "spawn" );
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

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		UpdatePropPusher();
		UpdateStompWave();
	}

	protected override void OnUpdate()
	{
		if ( GameObject.IsDestroyed ) return;
		base.OnUpdate();
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		var velocity = Animation.MoveVelocity.WithZ( 0 );
		float speedRatio = velocity.Length / RunSpeed;
		UpdateRumble( Animation.MoveVelocity.Length / RunSpeed );
		UpdateFootsteps();
		UpdateShieldRenderer();
		if ( Networking.IsHost )
		{
			if ( Animation.IsGrounded && fallSpeed > 5 * Scale ) PlayCue( "land" );
			fallSpeed = Animation.IsGrounded ? 0 : -Animation.MoveVelocity.z;

			var aim = target.IsValid() ? CalculateAimPoint( target ) - EyePosition
				: velocity.LengthSquared > 1 ? velocity : WorldRotation.Forward;
			ViewRotation = AlienAim.Turn( ViewRotation, aim, 5 );
			var yaw = ViewRotation.Angles().yaw;
			WorldRotation = Rotation.FromYaw( yaw );
			float offset = Angles.NormalizeAngle( standingYaw - yaw );
			BodyYaw = Animation.MoveVelocity.Length > 0.1f * Scale ? AlienAim.ApproachAngle( offset, 0, Time.Delta * 5 ) : Math.Clamp( offset, -85, 85 );
			standingYaw = yaw + BodyYaw;
			UpdateStoop();
		}
		output?.Set( "body_yaw", BodyYaw % 90 );
		output?.Set( "body_pitch", -ViewRotation.Angles().pitch );
		output?.Set( "move_speed", Shielding ? Math.Clamp( 1 - speedRatio, 0.75f, 1 ) : speedRatio );
		output?.Set( "stoop", Shielding ? 0.68f : Stoop );
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
			float attackSpeed = smashSwing == true ? 1.35f : 1;
			world.Set( "attack_speed", attackSpeed );
			world.Set( "alive", Health > 0 ? 1 : 0 );
			world.Set( "ability", ability );
			world.Set( "move", move );
			world.Set( "activity", activity );
			world.Update( AnimationTime, output, worldTag );
			MovePose = world.GetPose( 0 );
			AttackPose = world.GetPose( 1 );
			weapon?.Set( "attack_speed", attackSpeed );
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

	void UpdateStoop()
	{
		if ( nextStoopCheck <= 0 )
		{
			nextStoopCheck = 0.3f;
			var ahead = ViewRotation.Forward.WithZ( 0 ).Normal * (4 * Scale);
			bool Blocked( Vector3 start ) => Scene.Trace.Ray( start, start + ahead )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run().Hit;
			bool overhead = Blocked( WorldPosition + Vector3.Up * (3.3f * Scale) );
			if ( !stooping && overhead ) stooping = !Blocked( EyePosition );
			else if ( Stoop >= 1 && !overhead ) stooping = false;
		}
		Stoop = Math.Clamp( Stoop + (stooping ? 2 : -2) * Time.Delta, 0, 1 );
	}

	SkinnedModelRenderer shieldRenderer;

	void UpdateShieldRenderer()
	{
		if ( !shieldRenderer.IsValid() )
		{
			if ( !Shielding || !Graphics.IsAvailable ) return;
			var go = new GameObject( GameObject, false, "Bone Shield" ) { NetworkMode = NetworkMode.Never };
			shieldRenderer = go.AddComponent<SkinnedModelRenderer>();
			shieldRenderer.Model = Renderer.Model;
			shieldRenderer.BoneMergeTarget = Renderer;
			shieldRenderer.UseAnimGraph = false;
			shieldRenderer.MaterialOverride = Material.Load( "mount://ns2/ns2/cinematics/vfx_materials/boneshield.material.vmat" );
			shieldRenderer.RenderType = ModelRenderer.ShadowRenderType.Off;
			go.Enabled = true;
		}
		shieldRenderer.Enabled = Shielding;
	}

	SoundHandle rumble;
	float rumbleGain;
	[Property, Hide] internal Curve RumbleVolume { get; set; }

	void UpdateRumble( float speed )
	{
		if ( Application.IsDedicatedServer ) return;
		if ( rumble is null )
		{
			if ( speed <= 0 || RumbleVolume.Length == 0 ) return;
			var sound = GameResource.Load<SoundEvent>( "mount://ns2/sounds/alien/onos/rumble.sound" );
			if ( sound is null ) return;
			rumble = Sound.Play( sound, WorldPosition );
			if ( rumble is null ) return;
			rumbleGain = rumble.Volume;
		}
		rumble.Position = WorldPosition;
		rumble.Volume = rumbleGain * RumbleVolume.Evaluate( speed );
	}

	protected override void OnDisabled()
	{
		if ( propPusher.IsValid() ) propPusher.GameObject.Destroy();
		rumble?.Stop();
		rumble = null;
		base.OnDisabled();
	}

	void OnWeaponTag( string tag )
	{
		if ( stomping )
		{
			if ( tag == "stomp_hit" && !stompHit ) StartWave();
			// The outgoing Gore clip can finish after Stomp has been requested.
			if ( tag == "end" && stompHit ) wantsStomp = stomping = false;
			return;
		}
		if ( tag != "hit" || Shielding ) return;
		PlayCue( "gore" );
		energy = Math.Max( 0, energy - 10 );
		bool smash = smashSwing == true;
		smashSwing = null;
		var (hit, victim) = TraceGore();
		AlienMelee.PlayImpact( hit, AlienMelee.Attack.Gore );
		PushHitProp( hit, ViewRotation.Forward * RunSpeed );

		bool structural = victim is Component c && c.GameObject.Tags.Has( "structure" );
		if ( smash && hit.Hit && (structural || hit.HasTag( "world" ) && victim is null) )
			ShowSmashImpact( hit.EndPosition, ViewRotation );
		if ( victim is null ) return;
		DealDamage( victim, structural ? 180 : 90, hit.EndPosition, smash ? "smash" : "gore" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowSmashImpact( Vector3 position, Rotation facing ) => PlayCinematic( "door_hit", position, facing );

	static void PlayCinematic( string name, Vector3 position, Rotation facing )
	{
		CinematicLoader.Play( $"cinematics/alien/onos/{name}.cinematic", position, facing );
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

	void PlayCue( string cue ) => PlaySoundEvent( $"alien/onos/{cue}" );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlaySoundEvent( string id )
	{
		var sound = GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{id}.sound" );
		if ( sound is not null ) Sound.Play( sound, WorldPosition );
	}

	protected override void OnDamaging( IDamageable.DamageEvent e )
	{
		if ( !e.DamageInfo.Attacker.IsValid() && e.DamageInfo.Tags.Has( "fall" ) )
		{
			e.AcceptedDamage = 0;
			return;
		}
		base.OnDamaging( e );
	}

	protected override void OnDeath( IDamageable.DeathEvent e )
	{
		PlayCue( "death" );
		base.OnDeath( e );
	}
}
