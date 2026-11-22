using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	enum HeardSound { Footstep, LoudFoley, Combat, Voice }
	GameObject escortTarget;
	float suspicion;
	TimeSince sinceSound;

	void ReportFootstep( string step, Vector3 position )
	{
		if ( !Networking.IsHost ) return;
		switch ( step )
		{
			case "leftrunstep": case "rightrunstep":
				ReportSound( GameObject, position, HeardSound.LoudFoley, 5f );
				break;
			case "leftwalkstep": case "rightwalkstep": case "leftstep": case "rightstep":
				ReportSound( GameObject, position, HeardSound.Footstep, 5f );
				break;
			case "leftwallstep": case "rightwallstep":
				ReportSound( GameObject, position, HeardSound.Footstep, 2f );
				break;
		}
	}

	void ReportSound( GameObject source, Vector3 position, HeardSound sound, float radius )
	{
		// Rabbit hearing doubles the event radius. Overgrowth expresses the radius in metres.
		float range = radius * 2f * 40f;
		foreach ( var guard in Scene.GetAllComponents<RabbitGuard>() )
		{
			if ( !guard.Active || guard.GameObject == source || guard.Health < 1 || guard.Downed || guard.hearingReady > 0 ||
				guard.WorldPosition.DistanceSquared( position ) >= range * range ) continue;
			if ( guard.enemy.IsValid() )
			{
				if ( guard.enemy != source ) continue;
				guard.lastSeen = guard.TargetPosition( source );
				guard.sinceEnemySeen = 0;
				continue;
			}
			if ( guard.escortTarget.IsValid() || (sound != HeardSound.Voice && !guard.CanAttack( source )) ) continue;
			guard.suspicion = Math.Max( 0f, guard.suspicion - (float)guard.sinceSound * 0.1f ) +
				(sound == HeardSound.Footstep ? 0.3f : 1f);
			guard.sinceSound = 0;
			if ( guard.suspicion < 1f ) continue;
			if ( !guard.investigatePoint.HasValue )
			{
				guard.Speak( "suspicious" );
				guard.startled = sound == HeardSound.Voice ? Random.Shared.Float( 1.2f, 1.5f ) : 1f;
			}
			if ( sound == HeardSound.Voice )
			{
				guard.escortTarget = source;
				guard.investigatePoint = null;
			}
			else guard.investigatePoint = guard.Movement.ProjectPoint( position );
			guard.urgentInvestigation = sound == HeardSound.Combat;
			guard.AlertStage = 1;
		}
	}

	bool FollowHeardVoice()
	{
		if ( !escortTarget.IsValid() ) return false;
		var position = TargetPosition( escortTarget );
		if ( !IsLiveTarget( escortTarget ) )
		{
			investigatePoint = Movement.ProjectPoint( position );
			urgentInvestigation = true;
			escortTarget = null;
			return false;
		}

		const float radius = 2f * 40f;
		float remaining = (position - WorldPosition).WithZ( 0 ).Length - radius;
		if ( startled > 0 || remaining <= 0 )
		{
			if ( Movement.MoveTarget.HasValue ) Movement.Stop();
			Stance = Watch;
		}
		else
		{
			if ( repath <= 0 )
			{
				Movement.MoveTo( position, radius, Math.Min( remaining / 40f, 1f ) * 8f * 40f );
				repath = 0.2f;
			}
			Stance = Locomotion;
		}
		return true;
	}
}
