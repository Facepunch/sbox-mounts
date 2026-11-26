using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace NS2;

[Title( "Skulk" ), Category( "Natural Selection 2" )]
public sealed partial class Skulk : AlienLifeform, Component.IDamageable
{
	protected override Vector3 GetEngagementPoint() => WorldPosition + Vector3.Up * (0.5f * MapLoader.Scale);

	protected override int BaseHealth => 75;
	protected override int HealthPerBiomass => 3;

	float IDamageable.Armour { get => Armor; set => Armor = Math.Clamp( value, 0, MaxArmor ); }
	float IDamageable.MaxArmour => MaxArmor;

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer )
	{
		var hull = new GameObject( root, true, "Collider" );
		hull.Tags.Add( "playercontroller" );
		var sphere = hull.AddComponent<SphereCollider>();
		sphere.Center = Vector3.Up * (SkulkMovement.Radius + SkulkMovement.ControllerOffset);
		sphere.Radius = SkulkMovement.Radius;
		sphere.Friction = 0;
		var body = root.AddComponent<Rigidbody>();
		body.MassOverride = 45;
		body.CollisionUpdateEventsEnabled = true;
		body.EnableImpactDamage = false;
		body.Locking = new PhysicsLock { Pitch = true, Roll = true, Yaw = true };
		root.AddComponent<SkulkMovement>().RunSpeed = 7.25f * MapLoader.Scale;
		var senses = root.AddComponent<SensesLayer>();
		senses.SightRange = 20 * MapLoader.Scale;
		senses.FieldOfView = 360;
		var animation = root.AddComponent<AnimationLayer>();
		animation.EnableFootstepSounds = false;
		animation.TurnsBody = false;
		var npc = root.AddComponent<Skulk>();
		npc.Faction = Factions.Monster;
		npc.EyeHeight = 0.55f * MapLoader.Scale;
		npc.Health = npc.BaseHealth;
		npc.DisplayName = "Skulk";
		npc.Renderer = renderer;
	}

	const float BiteEnergy = 5.85f;
	const float LeapEnergy = 45;
	const float Reach = 1.42f * MapLoader.Scale;
	[Property] public bool LeapUnlocked { get; set; } = true;
	bool Biting { get; set; }
	bool Leaping { get; set; }
	bool secondaryHeld;
	SkulkMovement mover;
	TimeUntil leapEnds;
	float energy = 100;
	SparkAnimationGraph.Playback weapon;
	Action<string> weaponTag;

	protected override void OnStart()
	{
		base.OnStart();
		mover = GetComponent<SkulkMovement>();
		if ( !Networking.IsHost ) return;
		ViewRotation = BodyRotation = Renderer.WorldRotation;
		desiredBodyAngles = BodyRotation.Angles();
		standingYaw = runningYaw = BodyRotation.Angles().yaw;
		AnimationTime = 0;
		sneaky = Random.Shared.Next( 2 ) == 0;
		worldTag = OnWorldTag;
		PlayVoice( "spawn" );
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model )
			world = new( model.PropGraph, model );
		if ( mount?.GetByPath( "mount://ns2/ns2/models/alien/skulk/skulk_view.model.vmdl" ) is ModelLoader viewModel )
		{
			weapon = new( viewModel.PropGraph, viewModel );
			weaponTag = OnWeaponTag;
			weapon.Set( "ability", "bite" );
			weapon.Set( "attack_speed", 1 );
		}
	}

	protected override void OnUpdate()
	{
		if ( GameObject.IsDestroyed ) return;
		base.OnUpdate();
		if ( Networking.IsHost && Health > 0 )
		{
			if ( xenociding && detonation <= 0 )
			{
				Detonate();
				return;
			}
			Regenerate( underFire > 0 );
			UpdateFlinch();
			energy = Math.Min( 100, energy + GetEnergyRechargeRate( 27 ) * Time.Delta );
			if ( Leaping && leapEnds <= 0 ) Leaping = false;
		}

		UpdateDashSound();
		UpdateAnimation();
	}

	protected override void Think()
	{
		bool attackLeap = UpdateCombat();
		bool travelLeap = UpdateTravel();
		bool press = attackLeap || travelLeap;
		if ( press && !secondaryHeld ) Leap();
		secondaryHeld = press;
	}

	bool UpdateCombat()
	{
		FiringParasite = false;
		var target = FindEnemy( out var lastKnownPosition );
		var aimPoint = target.IsValid() ? CalculateAimPoint( target ) : default;
		bool aimReady = target.IsValid() && TrackAim( target, aimPoint );
		Animation.SetLookTarget( target );
		if ( !target.IsValid() )
		{
			rangedSight = null;
			primaryHeld = false;
			Investigate( lastKnownPosition );
			Biting = false;
			return false;
		}

		bool mobile = !target.Tags.Has( "structure" ) && (target.GetComponent<PlayerController>().IsValid() || target.GetComponent<BaseNpc>().IsValid());
		var contact = target.GetComponent<Rigidbody>() is { PhysicsBody.IsValid: true } body
			? body.FindClosestPoint( EyePosition )
			: target.GetComponentInChildren<Collider>()?.FindClosestPoint( EyePosition ) ?? target.WorldPosition;
		var distance = EyePosition.Distance( contact );
		if ( distance < 1.4f * MapLoader.Scale )
		{
			if ( mobile ) ApproachEnemy( target, aimPoint, distance );
			else Movement.Stop();
			bool xeno = mobile && TryXenocide();
			primaryHeld = xeno || !mobile || aimReady;
			Biting = !xeno && primaryHeld && energy >= BiteEnergy;
			return false;
		}

		Biting = false;
		var aim = (aimPoint - EyePosition).Normal;
		bool leap = mobile && LeapUnlocked && energy > LeapEnergy + 10 && distance is > (5 * MapLoader.Scale) and < (18 * MapLoader.Scale)
			&& Vector3.Dot( ViewRotation.Forward, aim ) > 0.95f;
		if ( distance < 45 * MapLoader.Scale ) rangedSight ??= 0;
		else rangedSight = null;
		bool parasite = !leap && !xenociding && rangedSight.HasValue && rangedSight.Value > 1 &&
			energy > (LeapUnlocked ? LeapEnergy + ParasiteEnergy : 60);
		if ( parasite && !primaryHeld ) TryParasite();
		primaryHeld = parasite;
		ApproachEnemy( target, aimPoint, distance );
		return leap;
	}

	void Leap()
	{
		var velocity = GetComponent<Rigidbody>().Velocity * 0.5f;
		var forward = ViewRotation.Forward;
		velocity += forward.WithZ( 0 ).Normal * (7.6f * MapLoader.Scale);
		velocity.z = (forward.z + 0.5f) * (10.8f * MapLoader.Scale) + Math.Min( 0, velocity.z );
		mover.Leap( velocity );
		Leaping = true;
		leapEnds = 0.2f;
		energy -= LeapEnergy;
		PlayVoice( "bite_alt" );
	}

	void OnWeaponTag( string tag )
	{
		if ( tag == "attack_end" ) parasiteAttacking = false;
		if ( tag != "hit" || parasiteAttacking ) return;
		energy = Math.Max( 0, energy - BiteEnergy );
		PlayVoice( "bite" );
		bool killed = false;
		foreach ( var (hit, victim) in AlienMelee.Swing( this, ViewRotation, Reach, 1.2f, 1.2f, AlienMelee.Attack.Bite ) )
		{
			AlienMelee.PlayImpact( hit, AlienMelee.Attack.Bite );
			if ( victim is null ) continue;
			var npc = victim as BaseNpc;
			bool alive = npc.IsValid() && npc.Health > 0;
			DealDamage( victim, 75, hit.EndPosition, "bite" );
			killed = alive && (!npc.IsValid() || npc.Health <= 0);
		}
		if ( killed ) PlayVoice( "bite_kill" );
	}
}
