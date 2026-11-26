using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NS2;

[Title( "Lerk" ), Category( "Natural Selection 2" )]
public sealed partial class Lerk : AlienLifeform
{
	protected override Vector3 GetEngagementPoint() => WorldPosition + Vector3.Up * (0.6f * Scale);

	protected override int BaseHealth => 180;
	protected override int HealthPerBiomass => 2;

	protected override int MaxArmor => GetArmorLimit( 30, 12, 1 );

	const float Scale = MapLoader.Scale;
	const float BodyRadius = 0.4f * Scale;
	static readonly SparkAnimationGraph.PoseParameters Locomotion = new( 0 ), ActionLayer = new( 1 );
	internal GameObject assignedEnemy;
	[Property, Range( 1, 7 )] public int AimSkillTier { get; set; } = 1;
	readonly BotWeaponAim acquisition = new( 100 );
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose movePose { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose attackPose { get; set; }
	SparkAnimationGraph.Playback weapon;
	Action<string> worldTag, weaponTag;
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] Rotation View { get; set; }
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] Rotation BodyRotation { get; set; }
	bool attacking, wantsSpikes, spiking;
	TimeSince lastFlap = 1;
	float energy = 100;
	TimeUntil repath;

	internal static void RegisterSounds( MountContext context )
	{
		AlienFootsteps.Register( context, "lerk" );
		const string umbra = "alien/structures/crag/umbra";
		context.Add( ResourceType.GameResource, $"sounds/{umbra}.sound", new SparkSoundLoader( umbra ) );
		foreach ( var cue in new[] { "spawn", "bite", "spikes", "flap", "flying_3D", "spores_hit", "spore_spray_once", "death", "wound", "wound_serious" } )
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
		var child = new GameObject( root, true, "Collider" );
		child.Tags.Add( "playercontroller" );
		var collider = child.AddComponent<SphereCollider>();
		collider.Radius = BodyRadius;
		collider.Center = Vector3.Up * BodyRadius;
		var body = root.AddComponent<Rigidbody>();
		body.MassOverride = 54;
		body.EnableImpactDamage = false;
		var agent = root.AddComponent<NavMeshAgent>();
		agent.Radius = BodyRadius;
		agent.Height = 2 * BodyRadius;
		agent.Enabled = false;
		root.AddComponent<NavigationLayer>().RunSpeed = 2.8f * Scale;
		var senses = root.AddComponent<SensesLayer>();
		senses.SightRange = 35 * Scale;
		senses.FieldOfView = 360;
		root.AddComponent<AnimationLayer>().EnableFootstepSounds = false;
		var npc = root.AddComponent<Lerk>();
		npc.Faction = Factions.Monster;
		npc.Health = npc.BaseHealth;
		npc.EyeHeight = 0.5f * Scale;
		npc.DisplayName = "Lerk";
		npc.Renderer = renderer;
	}

	protected override void OnStart()
	{
		base.OnStart();
		if ( !Networking.IsHost ) return;
		if ( FindModel( Renderer.Model.ResourcePath ) is { } model )
		{
			modelOrigin = model.BoundsCenter;
			world = model.CreatePlayback();
		}
		weapon = CreatePlayback( "mount://ns2/ns2/models/alien/lerk/lerk_view.model.vmdl" );
		PlayCue( "spawn" );
		var gravity = MathF.Abs( Scene.PhysicsWorld.Gravity.z );
		if ( gravity > 0 ) GetComponent<Rigidbody>().GravityScale = 7 * Scale / gravity;
		View = BodyRotation = WorldRotation;
		AnimationTime = 0;
		worldTag = tag => AlienFootsteps.OnTag( this, "lerk", tag );
		weaponTag = OnWeaponTag;
	}

	protected override void Think()
	{
		umbraTarget = ChooseUmbraTarget();
		sporeTarget = umbraTarget.IsValid() ? null : ChooseSporeTarget();
		if ( umbraTarget.IsValid() || sporeTarget.IsValid() )
		{
			assignedEnemy = null;
			Animation.SetLookTarget( umbraTarget.IsValid() ? umbraTarget.GameObject : sporeTarget );
			attacking = wantsSpikes = false;
			return;
		}
		Vector3? lastSeen = null;
		var enemy = energy > 35 && Health + 2 * Armor > (MaxHealth + 2 * MaxArmor) * 0.6f
			? AlienTargeting.ForTeam( this ).FindEnemy( this, out lastSeen ) : null;
		if ( enemy.IsValid() && WorldPosition.DistanceSquared( enemy.WorldPosition ) > 50 * 50 * Scale * Scale ) enemy = null;
		assignedEnemy = enemy;
		bool visible = enemy.IsValid() && lastSeen is null;
		Animation.SetLookTarget( visible ? enemy : null );
		var goal = enemy?.WorldPosition;
		var touch = visible ? enemy.GetComponentInChildren<Collider>()?.FindClosestPoint( EyePosition ) ?? enemy.WorldPosition : default;
		float distance = visible ? EyePosition.Distance( touch ) / Scale : float.PositiveInfinity;
		attacking = distance < 1.5f;
		wantsSpikes = !attacking && distance < 35 && IsPlayerTarget( enemy );
		if ( goal is null )
		{
			if ( !RetreatToHive() ) GameObject.GetOrAddComponent<AlienExploration>().Explore( Scale );
		}
		else if ( distance < 1.75f && !IsPlayerTarget( enemy ) )
			Movement.Stop();
		else if ( repath <= 0 )
		{
			Movement.MoveTo( goal.Value, Scale, faceTarget: visible ? enemy : null );
			repath = 0.2f;
		}
	}

	internal float AttackUrgency( BaseNpc target )
	{
		float distance = WorldPosition.Distance( target.WorldPosition ) / Scale;
		if ( distance < 10 && target is Marine or Exo ) return 1 + 1 / Math.Max( 1, distance );
		bool covered = AlienTargeting.HasResponders( this, target, 2 );
		float weight = target switch
		{
			Marine => covered ? 0.6f : 1,
			Exo => covered ? 0.8f : 1.2f,
			Mac => covered ? 0.2f : 0.4f,
			_ => 0
		};
		return weight * 40 / (40 + distance);
	}

	// Spark rotates the model about the center of the movement sphere.
	void UpdateBodyTransform()
	{
		var rotation = WorldRotation.Inverse * BodyRotation;
		Renderer.LocalRotation = rotation;
		Renderer.LocalPosition = (Vector3.Up - rotation.Up) * BodyRadius;
	}

	static Vector2 MovementPose( Vector3 velocity, Rotation head )
	{
		var planar = velocity.WithZ( 0 );
		float yaw = planar.LengthSquared > 0 ? MathF.Atan2( Vector3.Dot( head.Forward, planar ), Vector3.Dot( head.Left, planar ) ).RadianToDegree() : 0;
		return new( yaw, planar.Length / (2.8f * Scale) );
	}

	protected override void OnUpdate()
	{
		if ( GameObject.IsDestroyed ) return;
		base.OnUpdate();
		UpdateFlightSound();
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		var aim = flying ? flightDirection : WorldRotation.Forward;
		bool ready = false;
		if ( Networking.IsHost )
		{
			var target = attacking || wantsSpikes ? assignedEnemy : Animation.LookObject;
			if ( target.IsValid() && AllowsAttack( target ) )
			{
				var point = CalculateAimPoint( target );
				if ( attacking || wantsSpikes )
				{
					var group = wantsSpikes ? BotWeaponAim.Weapon.LerkSpikes : BotWeaponAim.Weapon.LerkBite;
					ready = acquisition.Track( this, target, point, View, AimSkillTier, group, out var sampled, InCombat ? 1.5f : 1 );
					if ( sampled is { } viewTarget ) aim = viewTarget - EyePosition;
				}
				else aim = point - EyePosition;
			}
			View = AlienAim.Turn( View, aim, acquisition.TurnRate );
			BodyRotation = FlightBodyRotation( BodyRotation.Angles(), View.Angles(), Animation.IsGrounded, Time.Delta );
		}
		UpdateBodyTransform();
		var moveInputs = MovementPose( Animation.MoveVelocity, View );
		output?.Set( "move_speed", moveInputs.y );
		output?.Set( "move_yaw", moveInputs.x );
		output?.Set( "body_pitch", -View.Angles().pitch );
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
		energy = Math.Min( 100, energy + GetEnergyRechargeRate( 13 ) * Time.Delta );
		spiking = wantsSpikes && ready && energy >= SpikeCost;
		if ( !spiking ) firingSpikes = false;
		bool usingUmbra = umbraTarget.IsValid() && NpcConVars.Enabled;
		bool usingSpores = sporeTarget.IsValid() && AllowsAttack( sporeTarget );
		var ability = usingUmbra ? "umbra" : usingSpores ? "spores" : "bite";
		bool primary = usingUmbra || usingSpores ? energy >= CloudCost && (!usingSpores || Vector3.Dot( View.Forward, aim.Normal ) > 0.95f) : attacking && ready && energy >= 5;
		var activity = primary ? "primary" : spiking ? "secondary" : "none";
		var move = !Animation.IsGrounded ? "fly" : Movement.IsMoving ? "run" : "idle";
		world.Set( "attack_speed", 1 );
		world.Set( "alive", 1 );
		world.Set( "move", move );
		world.Set( "flapping", lastFlap <= 0.5f ? 1 : 0 );
		world.Set( "ability", ability );
		world.Set( "activity", activity );
		world.Update( AnimationTime, output, worldTag );
		movePose = world.GetPose( 0 );
		attackPose = world.GetPose( 1 );
		weapon.Set( "move", move );
		weapon.Set( "ability", ability );
		weapon.Set( "attack_speed", 1 );
		weapon.Set( "activity", activity );
		weapon.Update( AnimationTime, tag: weaponTag );
		if ( firingSpikes && nextSpikeSound < 0 )
		{
			nextSpikeSound = 0.1f;
			PlayCue( "spikes" );
		}
	}

	void OnWeaponTag( string tag )
	{
		if ( tag == "shoot" && spiking && energy > SpikeCost ) FireSpike();
		if ( tag != "hit" || !AllowsAttack( sporeTarget ?? assignedEnemy ) ) return;
		if ( umbraTarget.IsValid() || sporeTarget.IsValid() )
		{
			FireCloud( sporeTarget.IsValid() );
			return;
		}
		PlayCue( "bite" );
		energy = Math.Max( 0, energy - 5 );
		foreach ( var (hit, victim) in AlienMelee.Swing( this, View, 1.5f * Scale, 0.9f, 1.2f, AlienMelee.Attack.Bite ) )
		{
			if ( hit.Hit )
			{
				var contact = hit.GameObject?.GetComponentInParent<IDamageable>();
				if ( contact is { Health: > 0 } || hit.GameObject?.Tags.Has( "world" ) == true )
					acquisition.Record( victim is not null );
			}
			if ( victim is not Component target ) continue;
			LeechShield( victim, 0.02f );
			victim.OnDamage( new DamageInfo { Damage = 60, Origin = EyePosition, Position = hit.EndPosition, Attacker = GameObject, Weapon = GameObject } );
			if ( target.IsValid() && victim.Health > 0 && !target.GameObject.Tags.HasAny( "ns2_exo", "ns2_exosuit" ) &&
				(target.GameObject.Tags.Has( "ns2_marine" ) || target.GetComponentInParent<PlayerController>().IsValid()) )
				target.GameObject.GetOrAddComponent<LerkPoison>().Apply( victim );
		}
	}

	protected override void OnDeath( IDamageable.DeathEvent e )
	{
		PlayCue( "death" );
		base.OnDeath( e );
	}
}
