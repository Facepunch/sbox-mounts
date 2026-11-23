using System;
using Sandbox.Navigation;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	LevelItem soughtItem;
	TimeUntil weaponLookTime;
	Vector3 weaponLookPoint;
	float seekDelay = 0.4f, seekTime;
	bool reaching;
	int reachAttempts;
	[Sync] bool ReachLeft { get; set; }
	[Sync] float ReachWeight { get; set; }
	[Sync] float ReachPhase { get; set; }

	int FreeHands => 2 - (LeftHandItem.IsValid() ? LeftHandItem.Hands : 0) - (RightHandItem.IsValid() ? RightHandItem.Hands : 0);

	bool CanPickUp( LevelItem item ) => item.IsValid() && item.Active && !item.Holder.IsValid() &&
		item.Definition?.Element( "type" )?.Value != "misc" && item.Hands <= FreeHands;

	LevelItem NearestWeapon( Vector3 position, float radius )
	{
		LevelItem closest = null;
		float distance = radius * radius;
		foreach ( var item in Scene.GetAllComponents<LevelItem>() )
		{
			if ( !CanPickUp( item ) ) continue;
			float d = item.PhysicsPosition.DistanceSquared( position );
			if ( d >= distance ) continue;
			distance = d;
			closest = item;
		}
		return closest;
	}

	bool SeekWeapon()
	{
		if ( PrimaryItem.IsValid() || boundAnimations.Species == "wolf" || seekTime > 15f || Downed || Unconscious )
		{
			StopSeekingWeapon();
			return false;
		}
		var center = CharacterCenter( GameObject );
		if ( !soughtItem.IsValid() )
		{
			if ( !enemy.IsValid() && !investigatePoint.HasValue ) return false;
			var candidate = NearestWeapon( center, 400f );
			if ( candidate is not null )
			{
				var path = Scene.NavMesh.CalculatePath( new() { Start = WorldPosition, Target = candidate.PhysicsPosition, Agent = GetComponent<NavMeshAgent>() } );
				if ( path.Status != NavMeshPathStatus.Complete ) candidate = null;
			}
			if ( candidate is null )
			{
				seekDelay = 0.4f;
				return false;
			}
			seekDelay -= Time.Delta;
			if ( seekDelay >= 0f ) return false;
			soughtItem = candidate;
		}
		if ( !CanPickUp( soughtItem ) || center.DistanceSquared( soughtItem.PhysicsPosition ) > 600f * 600f )
		{
			StopSeekingWeapon();
			return false;
		}

		seekTime += Time.Delta * (Animation.MoveVelocity.LengthSquared < 40f * 40f ? 11f : 1f);
		var point = soughtItem.PhysicsPosition;
		MoveTowardPoint( point, 0f, 1f );
		Stance = Animation.MoveVelocity.WithZ( 0f ).Length < 5f ? Threat : Locomotion;
		if ( NearestWeapon( center, 80f ) is { } nearby )
		{
			weaponLookTime = 2f / 30f;
			weaponLookPoint = nearby.PhysicsPosition;
			duckAmount = Math.Max( duckAmount, 1f - (weaponLookPoint - center).WithZ( 0f ).LengthSquared / (40f * 40f) );
		}

		ReachLeft = RightHandItem.IsValid() || (LeftHanded && !LeftHandItem.IsValid());
		var hand = boundAnimations.Arms[ReachLeft ? 0 : 1][0].Bone;
		if ( !reaching && Renderer.TryGetBoneTransform( hand, out var palm ) && NearestWeapon( palm.Position, 36f ) is not null )
		{
			reaching = true;
			reachAttempts = 0;
			ReachPhase = 0f;
		}
		return true;
	}

	void StopSeekingWeapon()
	{
		if ( soughtItem.IsValid() && Movement.MoveTarget.HasValue ) Movement.Stop();
		soughtItem = null;
		reaching = false;
		seekDelay = 0.4f;
	}

	void UpdatePickupLayer()
	{
		if ( Networking.IsHost )
		{
			if ( Downed || Unconscious || Health < 1 ) StopSeekingWeapon();
			ReachWeight = Math.Clamp( ReachWeight + (reaching ? 4f : -4f) * Time.Delta, 0f, 1f );
			if ( reaching || ReachWeight > 0f )
			{
				var clip = boundAnimations.Clips["pickup"];
				float next = ReachPhase + Time.Delta / clip.Duration;
				if ( reaching && clip.GrabTime is { } time )
				{
					float eventPhase = time / clip.Duration;
					float crossing = eventPhase > ReachPhase ? eventPhase : 1f + eventPhase;
					if ( next >= crossing ) TryGrab();
				}
				ReachPhase = next % 1f;
			}
		}
		var parameters = Renderer.Animation.Parameters;
		parameters.Set( "pickup_phase", ReachPhase );
		parameters.Set( "pickup_left", ReachLeft ? ReachWeight : 0f );
		parameters.Set( "pickup_right", ReachLeft ? 0f : ReachWeight );
	}

	void TryGrab()
	{
		var hand = boundAnimations.Arms[ReachLeft ? 0 : 1][0].Bone;
		if ( Renderer.TryGetBoneTransform( hand, out var palm ) && NearestWeapon( palm.Position, 36f ) is { } item && TakeWeapon( item ) )
		{
			StopSeekingWeapon();
			return;
		}
		if ( ++reachAttempts > 4 ) reaching = false;
	}

	bool TakeWeapon( LevelItem item )
	{
		if ( item.Hands > FreeHands || Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return false;
		var position = item.PhysicsPosition;
		if ( item.StuckIn.IsValid() && item.StuckIn != Renderer ) ExtractWeapon( item, lift: true );
		bool left = PrimaryItem.IsValid() ? !LeftHanded : LeftHanded;
		LevelItem.Attach( mount, item.GameObject, item.GetComponent<ModelRenderer>().Model, this, 0, left );
		if ( item.Holder != Renderer ) return false;
		PlayGrabSound( item, position, 0.5f );
		return true;
	}

	void PlayGrabSound( LevelItem item, Vector3 position, float gain )
	{
		bool soft = item.Definition.Element( "physics" )?.Attribute( "sound_modifier" ) == "soft";
		PlayCombatSound( soft ? "Data/Sounds/weapon_foley/impact/soft_bag_on_soft.xml" : "Data/Sounds/weapon_foley/grab/weapon_grap_metal_leather_glove.xml", position, gain );
	}
}

