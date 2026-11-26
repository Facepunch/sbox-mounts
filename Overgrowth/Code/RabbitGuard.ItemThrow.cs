using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string ThrowLayer = "r_knifethrowlayer", FlipThrowLayer = "r_knifethrowfliplayer";
	internal const string SpearThrow = "r_spearthrow", SwordThrow = "r_dogswordthrow";
	GameObject itemThrowTarget;
	GameObject pendingRollThrow;
	TimeUntil rollThrowExpires;
	bool HasThrowLayer => DrawAnimation is ThrowLayer or FlipThrowLayer;
	bool IsThrowingItem => Stance is SpearThrow or SwordThrow && ReactionPhase < 1f;

	bool BeginItemThrow()
	{
		if ( Downed || InThrow || IsTethered || IsThrowingItem || IsGetUp( Stance ) || strike is { AsLayer: false } ||
			!PrimaryItem.IsValid() ) return false;
		string label = boundAnimations.WeaponLabel;
		bool secondary = (LeftHanded ? RightHandItem : LeftHandItem).IsValid();
		bool airFlip = !AirFlipAxis.IsNearlyZero() && airFlipProgress <= 1f;
		float flipProgress = airFlip ? airFlipProgress : RollProgress;
		bool flipping = airFlip || RollProgress < 1f;
		if ( HasCombatTarget && (IsOnGround || flipping) && WantsItemThrow( label, secondary ) && SelectThrowTarget( label ) is { } selected )
		{
			pendingRollThrow = selected;
			rollThrowExpires = 1f;
		}
		var target = pendingRollThrow;
		if ( rollThrowExpires <= 0f || !target.IsValid() ) return false;
		if ( flipping && flipProgress <= 0.5f ) return true;
		pendingRollThrow = null;
		itemThrowTarget = target;
		if ( IsOnGround && !flipping && label is ("big_sword" or "spear" or "staff") )
		{
			Stance = label == "big_sword" ? SwordThrow : SpearThrow;
			MirroredAction = MirroredStance;
			MirroredStance = !MirroredStance;
			ReactionPhase = 0f;
			Movement.Stop();
			return true;
		}
		DrawLeft = LeftHanded || secondary && label is ("sword" or "rapier");
		DrawAnimation = flipping ? FlipThrowLayer : ThrowLayer;
		drawElapsed = 0f;
		return true;
	}

	bool WantsItemThrow( string label, bool secondary )
	{
		var delta = CharacterCenter( enemy ) - CharacterCenter( GameObject );
		if ( boundAnimations.Species == "cat" )
		{
			if ( !secondary && label != "knife" || Random.Shared.Float() >= Aggression * GameDifficulty * 0.04f ) return false;
			var velocity = TargetVelocity( enemy );
			bool fleeing = delta.LengthSquared > 220f * 220f && velocity.LengthSquared >= 30f * 40f * 40f &&
				enemy.WorldRotation.Forward.Dot( WorldRotation.Forward ) >= 0f && delta.Dot( velocity ) >= 0f;
			if ( !fleeing && delta.LengthSquared <= 500f * 500f && !(delta.LengthSquared > 100f * 100f && TargetIsAirborne( enemy ) && enemy.GetComponent<RabbitGuard>() is not { Downed: true }) ) return false;
		}
		else if ( boundAnimations.Species != "dog" || delta.LengthSquared <= 400f * 400f ) return false;
		return true;
	}

	bool AdvanceItemThrow()
	{
		if ( !IsThrowingItem ) return false;
		if ( itemThrowTarget.IsValid() ) TurnDuringStrike( TargetPosition( itemThrowTarget ) - WorldPosition );
		var clip = boundAnimations.Clips[Stance].ForSide( MirroredAction );
		float previous = ReactionPhase * clip.Duration;
		var from = clip.Animation.GetRootMotion( ReactionPhase );
		ReactionPhase = Math.Min( 1f, ReactionPhase + Time.Delta / clip.Duration );
		ApplyRootMotion( from, clip.Animation.GetRootMotion( ReactionPhase ) );
		ReleaseAtMarker( clip, previous, ReactionPhase * clip.Duration, fullBody: true );
		if ( ReactionPhase >= 1f )
		{
			itemThrowTarget = null;
			StartAttackRecoveryFlip();
			Stance = Ready;
			EndGroupAttack();
		}
		return true;
	}

	GameObject SelectThrowTarget( string label )
	{
		// GetCharactersInSphere tests the character sphere against the throw radius.
		float range = (label == "spear" ? 800f : 400f) + CharacterQueryRadius;
		var camera = Scene.Camera;
		var origin = camera.IsValid() ? camera.WorldPosition : EyePosition;
		var forward = camera.IsValid() ? camera.WorldRotation.Forward : WorldRotation.Forward;
		float best = 0.4f;
		GameObject chosen = null;
		var centerOfThrower = CharacterCenter( GameObject );
		foreach ( var (target, memory) in knownCharacters )
		{
			if ( Time.Now - memory.SeenTime >= 1f || !CanAttack( target ) || target.GetComponent<RabbitGuard>() is { Downed: true } ) continue;
			var center = CharacterCenter( target );
			var distance = center.DistanceSquared( centerOfThrower );
			if ( distance <= 80f * 80f || distance > range * range ) continue;
			float dot = (center - origin).Normal.Dot( forward );
			if ( dot <= best ) continue;
			best = dot;
			chosen = target;
		}
		return chosen;
	}

	void ReleaseAtMarker( RabbitClip clip, float previous, float elapsed, bool fullBody = false )
	{
		if ( clip.WeaponRelease is not { } release || elapsed <= previous ) return;
		if ( elapsed >= release.Time && (previous == 0f || previous < release.Time) )
			ReleaseThrownItem( release.Left, fullBody );
	}

	void ReleaseThrownItem( bool left, bool fullBody = false )
	{
		var target = itemThrowTarget;
		var item = left ? LeftHandItem : RightHandItem;
		if ( !item.IsValid() ) item = left ? RightHandItem : LeftHandItem;
		if ( !item.IsValid() || item.Holder != Renderer ) return;
		var end = CharacterCenter( GameObject ) + WorldRotation.Forward * 4000f;
		var velocity = Vector3.Zero;
		if ( target.IsValid() )
		{
			end = CharacterCenter( target );
			velocity = TargetVelocity( target );
			var renderer = target.GetComponent<RabbitGuard>()?.Renderer ?? target.GetComponent<SkinnedModelRenderer>();
			end = CharacterLoader.AverageChainPosition( renderer, "torso" ) ?? end;
		}
		item.Throw( this, end, velocity, fullBody );
		if ( item == LeftHandItem ) LeftHandItem = null;
		else RightHandItem = null;
		seekTime = 0f;
	}
}
