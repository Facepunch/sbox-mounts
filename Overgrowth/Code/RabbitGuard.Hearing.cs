using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	enum HeardSound { Footstep, LoudFoley, Combat, Voice, ItemImpact }
	[Property] public float HearingModifier { get; set; } = 1f;
	GameObject escortTarget;
	float suspicion;
	TimeSince sinceSound;
	float CurrentSuspicion => Math.Max( 0f, suspicion - (float)sinceSound * 0.1f );

	void ReportFootstep( string step, Vector3 position )
	{
		if ( !Networking.IsHost ) return;
		if ( boundAnimations?.Species == "cat" && IsAudibleStep( step ) )
		{
			ReportSound( GameObject, position, HeardSound.Footstep, 2f );
			return;
		}
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

	static bool IsAudibleStep( string step ) => step is "leftrunstep" or "rightrunstep" or "leftwalkstep" or "rightwalkstep"
		or "leftstep" or "rightstep" or "leftwallstep" or "rightwallstep";

	internal void ReportItemImpact( Vector3 point ) => ReportSound( GameObject, point, HeardSound.ItemImpact, 10f );

	void ReportSound( GameObject source, Vector3 position, HeardSound sound, float radius )
	{
		if ( !source.IsValid() || IsIgnoredPlayer( source ) ) return;
		// AISound broadcasts to character spheres within twice the event radius.
		float broadcastRange = (sound == HeardSound.ItemImpact ? radius : 2f * radius + 0.5f) * 40f;
		bool awake = source.GetComponent<RabbitGuard>() is not { Unconscious: true } && IsLiveTarget( source );
		foreach ( var guard in Scene.GetAllComponents<RabbitGuard>() )
		{
			float range = Math.Min( broadcastRange, radius * 40f * (guard.boundAnimations?.Species == "rabbit" ? 2f : 1f) * guard.HearingModifier );
			if ( range <= 0f || !guard.Active || guard.GameObject == source || guard.Health < 1 || guard.Unconscious || guard.hearingReady > 0 ||
				guard.WorldPosition.DistanceSquared( position ) >= range * range ) continue;
			if ( guard.enemy.IsValid() )
			{
				if ( guard.Fleeing || guard.enemy != source ) continue;
				guard.ObserveTarget( source, guard.visible );
				guard.lastSeen = guard.TargetPosition( source );
				guard.knownCharacters[source] = guard.CharacterCenter( source );
				guard.sinceEnemySeen = 0;
				continue;
			}
			if ( guard.escortTarget.IsValid() || !awake ) continue;
			bool investigating = guard.Investigating;
			if ( investigating && sound == HeardSound.ItemImpact ) continue;
			bool knownAlly = guard.knownCharacters.ContainsKey( source ) && guard.GetDisposition( source ) == Sandbox.Npcs.Disposition.Friendly;
			if ( knownAlly && (sound is HeardSound.Footstep or HeardSound.LoudFoley || investigating && (!guard.investigatePoint.HasValue || guard.urgentInvestigation)) ) continue;
			guard.suspicion = guard.CurrentSuspicion +
				(sound == HeardSound.Footstep ? 0.3f : 1f);
			guard.sinceSound = 0;
			if ( guard.suspicion < 1f ) continue;
			if ( !investigating )
			{
				guard.LookAtNotice( position, source: source );
				guard.Speak( "suspicious" );
				guard.startled = sound == HeardSound.Voice ? Random.Shared.Float( 1.2f, 1.5f ) : 1f;
			}
			guard.casualty = null;
			guard.inspectPosition = null;
			guard.inspectUntil = null;
			guard.searchUntil = 0f;
			if ( sound == HeardSound.Voice )
			{
				guard.escortTarget = source;
				guard.investigatePoint = null;
			}
			else guard.investigatePoint = guard.Movement.ProjectPoint( position );
			guard.urgentInvestigation = sound is HeardSound.Combat or HeardSound.ItemImpact;
			guard.AlertStage = 1;
		}
	}

	bool FollowHeardVoice()
	{
		if ( !escortTarget.IsValid() ) return false;
		if ( IsIgnoredPlayer( escortTarget ) )
		{
			escortTarget = null;
			Movement.Stop();
			return false;
		}
		var position = TargetPosition( escortTarget );
		var leader = escortTarget.GetComponent<RabbitGuard>();
		if ( !IsLiveTarget( escortTarget ) || leader is { Unconscious: true } )
		{
			LookAtNotice( position, 1f );
			casualty = leader;
			inspectPosition = position;
			inspectUntil = null;
			escortTarget = null;
			return false;
		}

		FollowCharacter( position );
		return true;
	}

	void FollowCharacter( Vector3 position )
	{
		const float radius = 2f * 40f;
		float remaining = (position - WorldPosition).WithZ( 0 ).Length - radius;
		if ( startled > 0 || remaining <= 0 )
		{
			if ( Movement.MoveTarget.HasValue ) Movement.Stop();
			Stance = Ready;
		}
		else
		{
			if ( repath <= 0 )
			{
				Movement.MoveTo( position, radius, Math.Min( remaining / 40f, 1f ) * GroundRunSpeed );
				repath = 0.2f;
			}
			Stance = Locomotion;
		}
	}
}
