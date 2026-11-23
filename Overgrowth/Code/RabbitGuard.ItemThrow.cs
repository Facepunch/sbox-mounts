using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string ThrowLayer = "r_knifethrowlayer";
	GameObject itemThrowTarget;

	bool BeginItemThrow()
	{
		if ( Downed || InThrow || Held || Holding || strike is not null || !Animation.IsGrounded ||
			!CanAttack( enemy ) || !PrimaryItem.IsValid() ) return false;
		string label = boundAnimations.WeaponLabel;
		if ( label is "big_sword" or "spear" or "staff" ) return false;
		bool secondary = (LeftHanded ? RightHandItem : LeftHandItem).IsValid();
		var delta = CharacterCenter( enemy ) - CharacterCenter( GameObject );
		if ( boundAnimations.Species == "cat" )
		{
			if ( !secondary && label != "knife" || Random.Shared.Float() >= Aggression * GameDifficulty * 0.04f ) return false;
			var velocity = GetVelocity( enemy );
			bool fleeing = delta.LengthSquared > 220f * 220f && velocity.LengthSquared >= 30f * 40f * 40f &&
				enemy.WorldRotation.Forward.Dot( WorldRotation.Forward ) >= 0f && delta.Dot( velocity ) >= 0f;
			if ( !fleeing && delta.LengthSquared <= 500f * 500f && !(delta.LengthSquared > 100f * 100f && TargetIsAirborne( enemy ) && enemy.GetComponent<RabbitGuard>() is not { Downed: true }) ) return false;
		}
		else if ( boundAnimations.Species != "dog" || delta.LengthSquared <= 400f * 400f ) return false;
		var target = SelectThrowTarget( label );
		if ( !target.IsValid() ) return false;
		DrawLeft = LeftHanded || secondary && label is ("sword" or "rapier");
		DrawAnimation = ThrowLayer;
		drawElapsed = 0f;
		itemThrowTarget = target;
		return true;
	}

	GameObject SelectThrowTarget( string label )
	{
		float range = label == "spear" ? 800f : 400f;
		var camera = Scene.Camera;
		var origin = camera.IsValid() ? camera.WorldPosition : EyePosition;
		var forward = camera.IsValid() ? camera.WorldRotation.Forward : WorldRotation.Forward;
		float best = 0.4f;
		GameObject chosen = null;
		foreach ( var (target, seen) in seenHostiles )
		{
			if ( Time.Now - seen >= 1f || !CanAttack( target ) || target.GetComponent<RabbitGuard>() is { Downed: true } ) continue;
			var center = CharacterCenter( target );
			var distance = center.DistanceSquared( CharacterCenter( GameObject ) );
			if ( distance <= 80f * 80f || distance > range * range ) continue;
			float dot = (center - origin).Normal.Dot( forward );
			if ( dot <= best ) continue;
			best = dot;
			chosen = target;
		}
		return chosen;
	}

	void ReleaseThrownItem( bool left, bool fullBody = false )
	{
		var target = itemThrowTarget;
		itemThrowTarget = null;
		var item = left ? LeftHandItem : RightHandItem;
		if ( !item.IsValid() ) item = left ? RightHandItem : LeftHandItem;
		if ( !item.IsValid() || item.Holder != Renderer ) return;
		var end = CharacterCenter( GameObject ) + WorldRotation.Forward * 4000f;
		var velocity = Vector3.Zero;
		if ( target.IsValid() )
		{
			end = CharacterCenter( target );
			velocity = GetVelocity( target );
			if ( target.GetComponent<RabbitGuard>() is { boundAnimations: { } rig } other )
			{
				Vector3 sum = default;
				int count = 0;
				foreach ( var bone in rig.TorsoBones )
					if ( other.Renderer.TryGetBoneTransform( bone, out var pose ) ) { sum += pose.Position; count++; }
				if ( count > 0 ) end = sum / count;
			}
		}
		if ( item == LeftHandItem ) LeftHandItem = null;
		else RightHandItem = null;
		seekTime = 0f;
		item.Throw( end, velocity, Animation.MoveVelocity, fullBody );
	}
}
