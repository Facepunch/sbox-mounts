using Sandbox.Npcs;
using System;
using System.Runtime.CompilerServices;

namespace NS2;

public sealed partial class Gorge
{
	sealed class Healing
	{
		public TimeSince Last;
		public Gorge Sprayer;
	}

	static readonly ConditionalWeakTable<Component, Healing> healing = new();
	readonly HashSet<Component> patients = [];
	Component patient;
	float patientDistance;
	AlienStructure construction;
	internal GameObject assignedTarget;
	// Recruit, Normal, Normal, Aggro, Veteran, Veteran in PlayerBot's distribution.
	readonly (float Help, float Aggression) personality = Random.Shared.Next( 6 ) switch
	{
		0 => (0.1f, 0.1f),
		1 or 2 => (0.9f, 0.4f),
		3 => (0, 0.9f),
		_ => (0.5f, 0.7f)
	};
	TimeUntil searchPatients, sprayAnimation, nextSpray;
	bool wantsSpray, previousSecondary, secondaryAttacking;

	internal static bool ShouldWaitForSpray( AlienLifeform patient )
	{
		if ( patient is Gorge || !healing.TryGetValue( patient, out var state ) || state.Last > 1.5f ) return false;
		var gorge = state.Sprayer;
		if ( !gorge.IsValid() || gorge.Health <= 0 || gorge.InCombat || patient.InCombat || patient.GameObject.Tags.Has( "ns2_hallucination" ) ) return false;
		var hp = (IDamageable)patient;
		if ( (hp.Health + 2 * hp.Armour) / (hp.MaxHealth + 2 * hp.MaxArmour) >= 0.99f ) return false;

		foreach ( var npc in patient.Scene.GetAllComponents<BaseNpc>() )
			if ( npc is Marine or Exo or AlienLifeform && npc.Health > 0 && NearbyEnemy( npc.GameObject ) ) return false;
		foreach ( var player in patient.Scene.GetAllComponents<PlayerController>() )
			if ( BaseNpc.IsLiveTarget( player.GameObject ) && !AlienTargeting.IsIgnoredPlayer( player.GameObject ) && NearbyEnemy( player.GameObject ) ) return false;
		return true;

		bool NearbyEnemy( GameObject obj )
		{
			const float range = 15 * Scale;
			return patient.GetDisposition( obj ) == Disposition.Hostile && patient.WorldPosition.DistanceSquared( obj.WorldPosition ) < range * range ||
				gorge.GetDisposition( obj ) == Disposition.Hostile && gorge.WorldPosition.DistanceSquared( obj.WorldPosition ) < range * range;
		}
	}

	bool Friendly( Component other )
	{
		var stance = GetDisposition( other.GameObject );
		return stance == Disposition.Friendly || stance == Disposition.Neutral &&
			other is BaseNpc npc && npc.Faction == Faction && AlienHealing.IsAlien( other );
	}

	void FindPatients( Vector3 origin, float range )
	{
		patients.Clear();
		foreach ( var obj in Scene.FindInPhysics( new Sphere( origin, range ) ) )
		{
			if ( obj.GetComponentInParent<IDamageable>() is Component c && c != this &&
				c is IDamageable { Health: > 0, MaxHealth: > 0 } && c.WorldPosition.DistanceSquared( origin ) <= range * range )
				patients.Add( c );
		}
	}

	static float HealthFraction( IDamageable hp ) =>
		(hp.Health + 2 * hp.Armour) / (hp.MaxHealth + 2 * hp.MaxArmour);

	bool ToppingUp( Component c, float fraction ) => fraction < 0.98f && c.GameObject.Tags.Has( "ns2_lifeform" ) &&
		healing.TryGetValue( c, out var h ) && h.Sprayer == this && h.Last < 1.5f;

	bool NeedsHelp( Component c )
	{
		if ( !c.IsValid() || c is not IDamageable { Health: > 0 } hp || !Friendly( c ) ) return false;
		float fraction = HealthFraction( hp );
		return fraction < 0.9f || ToppingUp( c, fraction );
	}

	float HealWeight( Component target, float distance )
	{
		if ( distance <= 0 || distance > 10 ) return 0;
		float fraction = HealthFraction( (IDamageable)target );
		if ( fraction >= 0.85f && !ToppingUp( target, fraction ) ||
			target is BaseNpc npc && AlienTargeting.HasResponders( this, npc, 1 ) ) return 3;
		if ( target is Gorge && underFire > 0 && AlienTargeting.HasResponders( this, this, 1 ) ) return 0;

		float weight = 10 - MathF.Log( distance ) + personality.Help - fraction * 1.25f;
		weight = target switch
		{
			Hive when fraction < 0.25f => weight * 25,
			Hive when fraction < 0.6f => weight * 1.3f,
			Onos => weight * 1.2f,
			Fade => weight * 1.125f,
			Lerk => weight * 1.1f,
			Skulk => weight + 0.5f,
			Gorge => weight * 0.01f,
			_ => weight
		};
		return target.GameObject.Tags.Has( "burning" ) ? weight + 0.25f : weight;
	}

	float AttackWeight( GameObject enemy )
	{
		if ( !enemy.IsValid() ) return 0;
		if ( bombarding ) return 3;
		float distance = WorldPosition.Distance( enemy.WorldPosition ) / Scale;
		if ( distance <= 0 ) return float.PositiveInfinity;
		float urgency = 1 + 1 / Math.Max( 1, distance );
		if ( distance > 20 )
		{
			var npc = enemy.GetComponent<BaseNpc>();
			bool exo = npc is Exo;
			bool covered = npc.IsValid() && AlienTargeting.HasResponders( this, npc, exo ? 6 : 4 );
			urgency = exo ? (covered ? 0.8f : 1.2f) : (covered ? 0.6f : 1);
		}
		return urgency * 20 / distance * HealthFraction( this );
	}

	float BuildWeight( AlienStructure structure )
	{
		if ( !structure.IsValid() || structure.IsBuilt || structure.Health <= 0 || InCombat ||
			structure is not Hive && AlienTargeting.HasResponders( this, structure, 1 ) ) return 0;
		float distance = WorldPosition.Distance( structure.WorldPosition ) / Scale;
		float weight = 2 + personality.Help + (structure switch { Hive => 5, Hydra => 4, Harvester => 3, Crag => 2.95f, _ => 0 });
		return distance <= 15 || assignedTarget == structure.GameObject ? weight + 15 / distance : weight;
	}

	bool SupportAlly( float competingWeight )
	{
		wantsSpray = false;
		if ( searchPatients <= 0 )
		{
			searchPatients = 0.25f;
			construction = null;
			float closest = float.MaxValue;
			foreach ( var structure in Scene.GetAllComponents<AlienStructure>() )
			{
				if ( structure.IsBuilt || structure.Health <= 0 || structure.Faction != Faction ) continue;
				float distance = WorldPosition.DistanceSquared( structure.WorldPosition );
				if ( distance >= closest ) continue;
				construction = structure;
				closest = distance;
			}
			patient = null;
			patientDistance = float.PositiveInfinity;
			FindPatients( WorldPosition, 10 * Scale );
			foreach ( var c in patients )
			{
				if ( !NeedsHelp( c ) ) continue;
				float distance = WorldPosition.Distance( c.WorldPosition );
				if ( distance >= patientDistance ) continue;
				patientDistance = distance;
				patient = c;
			}
		}
		float heal = NeedsHelp( patient ) ? HealWeight( patient, patientDistance / Scale ) : 0;
		float build = BuildWeight( construction );
		bool building = build > heal;
		if ( Math.Max( heal, build ) <= competingWeight )
		{
			assignedTarget = null;
			return false;
		}

		var target = building ? construction : patient;
		var point = target.WorldPosition;
		bool inRange = !building || WorldPosition.DistanceSquared( point ) <= (3.5f * 0.95f * Scale) * (3.5f * 0.95f * Scale);
		assignedTarget = target.GameObject;
		Animation.SetLookTarget( inRange ? target.GameObject : null );
		firing = false;
		wantsSpray = inRange && EyePosition.DistanceSquared( point ) < 3.35f * 3.35f * Scale * Scale && !WallBetween( EyePosition, point );
		if ( repath <= 0 )
		{
			var destination = wantsSpray ? HealingPosition( target ) : point;
			if ( destination is { } goal ) Movement.MoveTo( goal, Scale, faceTarget: inRange ? target.GameObject : null );
			else Movement.Stop();
			repath = 0.2f;
		}
		return true;
	}

	Vector3? HealingPosition( Component target )
	{
		if ( !target.GameObject.Tags.Has( "ns2_lifeform" ) && !target.GetComponent<PlayerController>().IsValid() ) return null;
		var forward = target.WorldRotation.Forward.WithZ( 0 ).Normal;
		var offset = (WorldPosition - target.WorldPosition).WithZ( 0 );
		if ( Vector3.Dot( forward, offset.Normal ) <= 0.866f && offset.Length >= 3 * Scale ) return null;
		var clearance = target is Onos ? Onos.HorizontalExtents :
			target.GetComponentInChildren<Collider>()?.LocalBounds.Size.WithZ( 0 ).Length * 0.5f ?? 0;
		var back = target.WorldPosition - forward * (clearance + 3.1f * Scale);
		var side = Vector3.Cross( Vector3.Up, forward ) * (2 * Scale);
		return WorldPosition.DistanceSquared( back - side ) < WorldPosition.DistanceSquared( back + side ) ? back - side : back + side;
	}

	void UpdateSprayInput( bool ready )
	{
		bool held = wantsSpray && ready;
		if ( !held ) secondaryAttacking = false;
		else if ( previousSecondary && nextSpray < 0 && energy >= 10 ) secondaryAttacking = true;
		previousSecondary = held;
	}

	bool WallBetween( Vector3 start, Vector3 end ) => Scene.Trace.Ray( start, end ).WithTag( "world" ).Run().Hit;

	void Spray()
	{
		energy -= 10;
		nextSpray = 0.8f;
		sprayAnimation = 0.5f;
		float range = 0;
		var eye = EyePosition;
		var forward = aim.Forward;
		ReadRange( eye );
		ReadRange( eye + aim.Up * (0.6f * Scale) );
		ReadRange( eye - aim.Up * (0.6f * Scale) );
		ReadRange( eye + aim.Left * (0.6f * Scale) );
		ReadRange( eye - aim.Left * (0.6f * Scale) );
		Restore( this );
		FindPatients( eye, Math.Min( 6 * Scale, range + 0.25f * Scale ) );
		foreach ( var c in patients )
		{
			var offset = c.WorldPosition - eye;
			float along = Vector3.Dot( offset, forward );
			if ( along < 0 || (offset - forward * along).LengthSquared > 9 * Scale * Scale ) continue;
			if ( offset.LengthSquared >= range * range && WallBetween( eye, c.WorldPosition ) ) continue;
			if ( Friendly( c ) )
				Restore( c );
			else if ( GetDisposition( c.GameObject ) == Disposition.Hostile && !c.GameObject.Tags.HasAny( "ns2_exosuit", "structure" ) &&
				(c is BaseNpc || c.GetComponentInParent<PlayerController>().IsValid()) )
			{
				var damage = new DamageInfo { Damage = 8, Origin = eye, Position = c.WorldPosition, Attacker = GameObject, Weapon = GameObject };
				damage.Tags.Add( "biological" );
				((IDamageable)c).OnDamage( damage );
			}
		}
		var velocity = Animation.MoveVelocity;
		var start = eye + velocity.WithZ( velocity.z / 2.5f ) / 5.5f;
		var effect = Scene.Trace.Ray( start, start + forward * (3.15f * Scale) ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).UseDamageHitboxes().Run();
		PlayCue( "heal_spray", WorldPosition );
		ShowSpray( effect.EndPosition, aim );

		void ReadRange( Vector3 from )
		{
			var trace = Scene.Trace.Ray( from, from + forward * (6 * Scale) ).WithTag( "world" ).Run();
			range = Math.Max( range, from.Distance( trace.EndPosition ) );
		}
	}

	void Restore( Component c )
	{
		var hp = (IDamageable)c;
		float amount = c.GameObject.Tags.Has( "structure" ) ? 60 : (8 + hp.MaxHealth * 0.03f) * (c == this ? 0.5f : 1);
		if ( c is AlienStructure structure ) amount = structure.SprayHealingLimit;
		if ( AlienHealing.Restore( c, amount, 1 ) && c != this && c.GameObject.Tags.Has( "ns2_lifeform" ) )
		{
			var state = healing.GetValue( c, static _ => new() );
			state.Last = 0;
			state.Sprayer = this;
		}
		if ( c is AlienStructure building ) building.OnHealSpray();
		if ( AlienHealing.IsAlien( c ) ) c.GameObject.Tags.Remove( "burning" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ShowSpray( Vector3 position, Rotation rotation ) =>
		CinematicLoader.Play( "cinematics/alien/gorge/healthspray.cinematic", position, rotation );
}
