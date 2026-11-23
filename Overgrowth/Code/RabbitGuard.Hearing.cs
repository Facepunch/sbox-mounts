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
			if ( range > 0f && guard.WorldPosition.DistanceSquared( position ) < range * range )
				guard.Hear( source, position, sound, awake );
		}
	}

	void Hear( GameObject source, Vector3 position, HeardSound sound, bool awake )
	{
		if ( !Active || GameObject == source || Health < 1 || Unconscious || StaticCharacter || hearingReady > 0 ) return;
		if ( enemy.IsValid() )
		{
			if ( Fleeing || enemy != source ) return;
			ObserveTarget( source, visible );
			lastSeen = TargetPosition( source );
			NoticeCharacter( source );
			sinceEnemySeen = 0;
			return;
		}
		if ( escortTarget.IsValid() || !awake ) return;
		bool investigating = Investigating;
		if ( investigating && sound == HeardSound.ItemImpact ) return;
		bool knownAlly = knownCharacters.ContainsKey( source ) && GetDisposition( source ) == Sandbox.Npcs.Disposition.Friendly;
		if ( knownAlly && (sound is HeardSound.Footstep or HeardSound.LoudFoley || investigating && (!investigatePoint.HasValue || urgentInvestigation)) ) return;
		suspicion = CurrentSuspicion +
			(sound == HeardSound.Footstep ? 0.3f : 1f);
		sinceSound = 0;
		if ( suspicion < 1f ) return;
		if ( !investigating )
		{
			LookAtNotice( position, source: source );
			Speak( "suspicious" );
			startled = sound == HeardSound.Voice ? Random.Shared.Float( 1.2f, 1.5f ) : 1f;
		}
		casualty = null;
		inspectPosition = null;
		inspectUntil = null;
		searchUntil = 0f;
		if ( sound == HeardSound.Voice )
		{
			escortTarget = source;
			investigatePoint = null;
		}
		else investigatePoint = Movement.ProjectPoint( position );
		urgentInvestigation = sound is HeardSound.Combat or HeardSound.ItemImpact;
		AlertStage = 1;
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
		if ( !IsLiveTarget( escortTarget ) || leader is { Unconscious: true } || escortTarget.GetComponent<CharacterRemains>().IsValid() )
		{
			LookAtNotice( position, 1f );
			casualty = escortTarget;
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
		if ( startled > 0f )
		{
			if ( Movement.MoveTarget.HasValue ) Movement.Stop();
			Stance = Ready;
			return;
		}
		Stance = MoveTowardPoint( position, 80f, 1f ) ? Locomotion : Ready;
	}
}
