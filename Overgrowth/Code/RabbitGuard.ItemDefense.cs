using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string CatchFront = "r_blockfronthard", CatchSide = "r_hitspinright";
	[Property, Range( 0f, 1f )] public float WeaponCatchSkill { get; set; } = 1f;

	internal bool DefendThrownItem( LevelItem item, Rigidbody body, GameObject attacker )
	{
		if ( !InMovementState || !IsOnGround || !CanAttack( enemy ) || boundAnimations is null ) return false;
		float chance = boundAnimations.Species switch { "cat" => 1f, "wolf" => 0.3f, _ => 0.7f };
		if ( followerCount > 0 || groupLeader.IsValid() ) chance *= 0.4f;
		if ( Random.Shared.Float() >= chance * WeaponCatchSkill * (0.5f + 0.5f * GameDifficulty) ) return false;
		var position = item.PhysicsPosition;
		MirroredAction = MirroredStance;
		if ( FreeHands < item.Hands )
		{
			if ( PrimaryItem.IsValid() && PrimaryItem.TryContact( item, out var contact, out var a, out var b ) )
				PlayWeaponClash( contact, a, b );
			body.Velocity = Animation.MoveVelocity;
			body.AngularVelocity = new Vector3( Random.Shared.Float( -20f, 20f ), Random.Shared.Float( -20f, 20f ), Random.Shared.Float( -20f, 20f ) );
			var height = Random.Shared.Next( 3 ) switch { 0 => "low", 1 => "med", _ => "high" };
			string guard = height + (Random.Shared.Next( 2 ) == 0 ? "leftblock" : "rightblock");
			BeginReaction( attacker, position, guard, boundAnimations.Clips[guard], activeBlock: true );
			flinching = false;
			flinchFadeOut = 4f;
			return true;
		}
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return false;
		var incoming = body.Velocity.WithZ( 0f ).Normal;
		var toward = (position - WorldPosition).WithZ( 0f ).Normal;
		var recoil = (body.Velocity - Animation.MoveVelocity) * (body.Mass * 0.1f);
		string animation = CatchFront;
		if ( incoming.Dot( toward ) >= -0.95f )
		{
			animation = CatchSide;
			MirroredAction = Vector3.Up.Cross( toward ).Dot( incoming ) < 0f;
		}
		BeginReaction( attacker, position, animation, boundAnimations.Clips[animation] );
		blockVelocity = recoil.WithZ( 0f );
		bool left = PrimaryItem.IsValid() ? !LeftHanded : LeftHanded;
		LevelItem.Attach( mount, item.GameObject, item.GetComponent<ModelRenderer>().Model, this, 0, left );
		var sound = item.Definition.Element( "physics" )?.Attribute( "sound_modifier" ) == "soft"
			? "Data/Sounds/weapon_foley/impact/soft_bag_on_soft.xml" : "Data/Sounds/weapon_foley/grab/weapon_grap_metal_leather_glove.xml";
		PlayCombatSound( sound, position, 1f );
		PlayCombatSound( sound, position, 0.5f );
		return true;
	}
}
