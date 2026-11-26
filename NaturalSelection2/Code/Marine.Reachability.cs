using Sandbox.Npcs;
using System;

namespace NS2;

public sealed partial class Marine : Component.IDamageable.IEvents
{
	GameObject trackedEnemy, ignoredEnemy;
	Vector3 ignoredPosition;
	Vector3? lastFiringPosition;
	MapLocations locations;
	bool unreachable;
	TimeUntil ignoreUntil;
	TimeSince sinceCheck, sinceProgress;
	float bestDistance;

	void IDamageable.IEvents.OnDamaged( IDamageable.DamageEvent hit )
	{
		if ( !unreachable || hit.Victim != trackedEnemy || hit.AcceptedDamage <= 0 || !hit.Instigator.IsValid() ) return;
		if ( hit.Instigator == GameObject )
			lastFiringPosition = WorldPosition;
		else if ( GetDisposition( hit.Instigator ) != Disposition.Friendly && hit.Instigator.GetComponent<BaseNpc>()?.Faction != Faction )
			return;

		sinceProgress = 0;
	}

	Func<GameObject, Vector3, bool> ignoreTarget;
	bool IsIgnoredTarget( GameObject target, Vector3 position ) =>
		target == ignoredEnemy && position.DistanceSquared( ignoredPosition ) < 9 * Scale * Scale;

	bool TrackReachability( GameObject enemy, Vector3 position )
	{
		if ( trackedEnemy != enemy || sinceCheck > 5 )
		{
			trackedEnemy = enemy;
			unreachable = false;
			sinceCheck = 0.5f;
		}
		if ( sinceCheck < 0.5f ) return unreachable;
		sinceCheck = 0;
		bool blocked = IsUnreachable( enemy, position );
		if ( blocked && !unreachable )
		{
			lastFiringPosition = null;
			bestDistance = float.PositiveInfinity;
			sinceProgress = 0;
		}
		unreachable = blocked;
		return blocked;
	}

	bool IsUnreachable( GameObject enemy, Vector3 position )
	{
		var player = enemy.GetComponentInParent<PlayerController>();
		if ( !player.IsValid() && !enemy.Tags.Has( "ns2_lifeform" ) ) return false;
		if ( !Scene.NavMesh.IsEnabled ) return false;
		var ground = Scene.NavMesh.GetClosestPoint( position );
		float offset = ground?.DistanceSquared( position ) ?? 0;
		if ( offset == 0 || offset > Scale * Scale )
		{
			if ( !locations.IsValid() ) locations = Scene.GetAllComponents<MapLocations>().FirstOrDefault();
			if ( locations.IsValid() && locations.IsVent( position ) ) return true;
		}
		if ( ground is not { } point ) return false;
		if ( position.z - point.z > 2 * Scale )
			return player.IsValid() ? player.IsOnGround : enemy.GetComponent<BaseNpc>()?.IsOnGround == true;
		return AlienTargeting.IsBehindFloor( Scene, position, point );
	}

	void ApproachUnreachable( Vector3 position )
	{
		if ( lastFiringPosition is { } from )
		{
			if ( WorldPosition.Distance( from ) > 1.5f * Scale ) Movement.MoveTo( from, Scale );
			else Movement.Stop();
			return;
		}
		float distance = (position - EyePosition).WithZ( 0 ).Length;
		if ( distance <= 8 * Scale )
		{
			Movement.Stop();
			return;
		}
		if ( distance < bestDistance - 0.5f * Scale )
		{
			bestDistance = distance;
			sinceProgress = 0;
		}
		Movement.MoveTo( position, Scale );
	}
}
