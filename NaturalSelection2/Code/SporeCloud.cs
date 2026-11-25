using Sandbox.Npcs;
using System;
using System.Runtime.CompilerServices;

namespace NS2;

public sealed class SporeCloud : LerkCloud
{
	internal const float Radius = 4 * MapLoader.Scale;
	internal Lerk Owner;
	TimeSince age;
	TimeUntil nextPulse;
	bool arrived;
	static readonly ConditionalWeakTable<GameObject, Exposure> exposures = new();
	sealed class Exposure { public float NextHit; }

	protected override void OnStart()
	{
		age = 0;
		CinematicLoader.Play( "cinematics/alien/lerk/spore_projection.cinematic", WorldPosition, WorldRotation, GameObject );
		CinematicLoader.Play( "cinematics/alien/lerk/spore_trail_alien.cinematic", WorldPosition, Rotation.Identity, GameObject );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( !arrived && TravelToDestination() )
		{
			arrived = true;
			Lerk.PlayCloudSound( "alien/lerk/spores_hit", WorldPosition );
		}
		if ( age > 6 ) { GameObject.Destroy(); return; }
		if ( age >= 4 || nextPulse >= 0 ) return;
		nextPulse = 0.25f;
		float radius = Math.Min( 1, age * 2 ) * Radius;
		foreach ( var player in Scene.GetAllComponents<PlayerController>() )
			Expose( player.GameObject, player.EyeTransform.Position, radius );
		foreach ( var alien in Scene.GetAllComponents<BaseNpc>() )
			if ( alien.Health > 0 && alien.GameObject.Tags.HasAny( "ns2_lifeform", "ns2_marine" ) )
				Expose( alien.GameObject, alien.EyePosition, radius );
	}

	void Expose( GameObject target, Vector3 eye, float radius )
	{
		if ( target.Tags.HasAny( "ns2_exo", "ns2_exosuit", "ns2_commander" ) || WorldPosition.DistanceSquared( eye ) >= radius * radius ) return;
		if ( Owner.IsValid() ? Owner.GetDisposition( target ) != Disposition.Hostile : target.GetComponent<BaseNpc>()?.Faction == Faction ) return;
		if ( exposures.TryGetValue( target, out var exposure ) && exposure.NextHit > Time.Now ) return;
		if ( Scene.Trace.Ray( WorldPosition, eye ).WithTag( "world" ).Run().Hit ) return;
		var victim = target.GetComponentInChildren<IDamageable>();
		if ( victim is null || victim.MaxHealth > 0 && victim.Health <= 0 ) return;
		(exposure ?? exposures.GetValue( target, _ => new() )).NextHit = Time.Now + 0.25f;
		var hit = new DamageInfo { Damage = 3.75f, Position = eye, Origin = WorldPosition, Weapon = GameObject, Attacker = Owner.IsValid() ? Owner.GameObject : null };
		hit.Tags.Add( "gas" );
		victim.OnDamage( hit );
	}
}
