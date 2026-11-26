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
	TimeUntil searchPatients, sprayAnimation, nextSpray;
	bool wantsSpray, previousSecondary, secondaryAttacking;

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

	bool NeedsHelp( Component c )
	{
		if ( !c.IsValid() || c is not IDamageable { Health: > 0 } hp || !Friendly( c ) ) return false;
		if ( c is AlienStructure { IsBuilt: false } ) return true;
		float fraction = (hp.Health + 2 * hp.Armour) / (hp.MaxHealth + 2 * hp.MaxArmour);
		return fraction < 0.85f || fraction < 0.98f && c.GameObject.Tags.Has( "ns2_lifeform" ) &&
			healing.TryGetValue( c, out var h ) && h.Sprayer == this && h.Last < 1.5f;
	}

	bool SupportAlly( bool seesEnemy )
	{
		wantsSpray = false;
		if ( searchPatients <= 0 )
		{
			searchPatients = 0.25f;
			patient = null;
			float best = 10 * Scale;
			FindPatients( WorldPosition, best );
			foreach ( var c in patients )
			{
				float distance = c.WorldPosition.Distance( WorldPosition );
				if ( distance > best || !NeedsHelp( c ) ) continue;
				best = distance;
				patient = c;
			}
		}
		if ( !NeedsHelp( patient ) ) return false;
		var point = patient.WorldPosition;
		float reach = 3.35f * Scale;
		bool near = point.DistanceSquared( EyePosition ) < reach * reach;
		if ( seesEnemy && !near ) return false;
		Animation.SetLookTarget( patient.GameObject );
		firing = false;
		wantsSpray = near && !WallBetween( EyePosition, point );
		if ( repath <= 0 )
		{
			var destination = wantsSpray ? HealingPosition( patient ) : point;
			if ( destination is { } goal ) Movement.MoveTo( goal, Scale, faceTarget: patient.GameObject );
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

	void UpdateSprayInput( Vector3 direction )
	{
		bool held = wantsSpray && Vector3.Dot( aim.Forward, direction.Normal ) > 0.98f;
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
		var velocity = GetComponent<Rigidbody>().Velocity;
		var start = eye + velocity.WithZ( velocity.z / 2.5f ) / 5.5f;
		var effect = Scene.Trace.Ray( start, start + forward * (3.15f * Scale) ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
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
