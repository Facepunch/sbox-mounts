using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string CatchFront = "r_blockfronthard", CatchSide = "r_hitspinright";
	[Property, Range( 0f, 1f )] public float WeaponCatchSkill { get; set; } = 1f;

	internal bool DefendThrownItem( LevelItem item, Rigidbody body )
	{
		if ( !InMovementState || !IsOnGround || !HasCombatTarget || boundAnimations is null ) return false;
		float chance = boundAnimations.Species switch { "cat" => 1f, "wolf" => 0.3f, _ => 0.7f };
		if ( followerCount > 0 || groupLeader.IsValid() ) chance *= 0.4f;
		if ( Random.Shared.Float() >= chance * WeaponCatchSkill * (0.5f + 0.5f * GameDifficulty) ) return false;
		return CatchOrParry( item, body );
	}

	void CheckIncomingWeapons()
	{
		if ( Unconscious || IsTethered ) return;
		bool canCatch = activeBlock > 0f || boundAnimations.Species == "cat";
		if ( !canCatch && !MetalArmor ) return;
		var incoming = NearestIncomingWeapon();
		if ( canCatch && incoming is { } first && WorldRotation.Forward.Dot( first.Body.Velocity ) < 0f )
		{
			if ( CatchOrParry( first.Item, first.Body ) && MetalArmor ) incoming = NearestIncomingWeapon();
		}
		if ( MetalArmor && incoming is { } hit && hit.Item.ThrownBy != GameObject )
		{
			InvestigatePatrolDamage();
			PlayArmorImpact( hit.Item.PhysicsPosition, true );
			ParryItem( hit.Item, hit.Body );
		}
	}

	(LevelItem Item, Rigidbody Body)? NearestIncomingWeapon()
	{
		var center = CharacterCenter( GameObject );
		LevelItem nearest = null;
		float distance = 80f * 80f;
		foreach ( var obj in Scene.FindInPhysics( new Sphere( center, 80f ) ) )
		{
			if ( obj.GetComponent<LevelItem>() is not { ThrownBy: not null } item ) continue;
			float d = center.DistanceSquared( item.PhysicsPosition );
			if ( d >= distance ) continue;
			nearest = item;
			distance = d;
		}
		return nearest?.GetComponent<Rigidbody>() is { Enabled: true } body && body.Velocity.LengthSquared > 5f * 40f * 40f
			? (nearest, body) : null;
	}

	bool CatchOrParry( LevelItem item, Rigidbody body )
	{
		var position = item.PhysicsPosition;
		MirroredAction = MirroredStance;
		if ( FreeHands < item.Hands )
		{
			ClashWith( item );
			ParryItem( item, body );
			return true;
		}
		var incoming = body.Velocity.WithZ( 0f ).Normal;
		var toward = (position - WorldPosition).WithZ( 0f ).Normal;
		var recoil = (body.Velocity - Animation.MoveVelocity) * (body.Mass * 0.1f);
		string animation = CatchFront;
		if ( incoming.Dot( toward ) >= -0.95f )
		{
			animation = CatchSide;
			MirroredAction = Vector3.Up.Cross( toward ).Dot( incoming ) < 0f;
		}
		if ( !TakeWeapon( item ) ) return false;
		BeginReaction( null, position, animation, boundAnimations.Clips[animation] );
		combatVelocity = recoil.WithZ( 0f );
		PlayGrabSound( item, position, 1f );
		return true;
	}

	void ParryItem( LevelItem item, Rigidbody body )
	{
		item.MarkThrowSafe();
		MirroredAction = MirroredStance;
		EndRoll();
		body.Velocity = Animation.MoveVelocity;
		body.AngularVelocity = new Vector3( Random.Shared.Float( -20f, 20f ), Random.Shared.Float( -20f, 20f ), Random.Shared.Float( -20f, 20f ) );
		var height = Random.Shared.Next( 3 ) switch { 0 => "low", 1 => "med", _ => "high" };
		string guard = height + (Random.Shared.Next( 2 ) == 0 ? "leftblock" : "rightblock");
		BeginReaction( null, item.PhysicsPosition, guard, boundAnimations.Clips[guard], activeBlock: true );
		flinching = false;
		flinchFadeOut = 4f;
	}
}
