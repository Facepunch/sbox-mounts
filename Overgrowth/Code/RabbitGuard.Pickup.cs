using System;
using Sandbox.Navigation;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	LevelItem soughtItem;
	TimeUntil weaponLookTime;
	Vector3 weaponLookPoint;
	float seekDelay = 0.4f, seekTime, searchRemainder;
	bool reaching;
	int reachAttempts;
	[Sync] bool ReachLeft { get; set; }
	[Sync] float ReachWeight { get; set; }
	[Sync] float ReachPhase { get; set; }

	int FreeHands => 2 - (LeftHandItem.IsValid() ? LeftHandItem.Hands : 0) - (RightHandItem.IsValid() ? RightHandItem.Hands : 0);

	bool CanPickUp( LevelItem item )
	{
		if ( !item.IsValid() || !item.Active || item.Definition?.Element( "type" )?.Value == "misc" || item.Hands > FreeHands ) return false;
		if ( !item.Holder.IsValid() ) return true;
		return item.Holder.GetComponentInParent<RabbitGuard>() is { Unconscious: true } ||
			item.Holder.GetComponentInParent<CharacterRemains>().IsValid();
	}

	bool PickupLeft => RightHandItem.IsValid() || (LeftHanded && !LeftHandItem.IsValid());

	LevelItem WeaponAtHand( bool left )
	{
		var palm = CharacterLoader.FromModel( Renderer.Model )?.ChainOrigin( this, left ? "leftarm" : "rightarm" );
		return palm.HasValue ? NearestWeapon( palm.Value, 36f ) : null;
	}

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

	bool FindWeapon()
	{
		if ( boundAnimations.Species == "wolf" || seekTime >= 15f ) return false;
		if ( PrimaryItem.IsValid() )
		{
			seekDelay = 0.4f;
			return false;
		}
		const float tick = 4f / 120f;
		searchRemainder += Time.Delta;
		int polls = (int)(searchRemainder / tick);
		if ( polls == 0 ) return false;
		searchRemainder -= polls * tick;
		var candidate = NearestWeapon( CharacterCenter( GameObject ), 400f );
		if ( candidate?.StuckIn?.GetComponentInParent<RabbitGuard>() is { Unconscious: false } ) candidate = null;
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
		if ( !WantsToSeekWeapon( polls ) ) return false;
		soughtItem = candidate;
		return SeekWeapon();
	}

	bool SeekWeapon()
	{
		var center = CharacterCenter( GameObject );
		if ( PrimaryItem.IsValid() || seekTime > 15f || !CanPickUp( soughtItem ) || center.DistanceSquared( soughtItem.PhysicsPosition ) > 600f * 600f )
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

		if ( RollProgress >= 1f && !reaching && WeaponAtHand( PickupLeft ) is not null )
		{
			ReachLeft = PickupLeft;
			reaching = true;
			reachAttempts = 0;
			ReachPhase = 0f;
		}
		return true;
	}

	bool WantsToSeekWeapon( int polls )
	{
		// CheckForNearbyWeapons subtracts one physics step per four-step NPC brain update.
		for ( int i = 0; i < polls; i++ )
		{
			if ( seekDelay < 0f ) return true;
			seekDelay -= 1f / 120f;
		}
		return false;
	}

	void StopSeekingWeapon()
	{
		if ( soughtItem.IsValid() && Movement.MoveTarget.HasValue ) ReleaseMovementInput();
		soughtItem = null;
		reaching = false;
		searchRemainder = 0f;
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
		if ( FreeHands <= 0 || Unconscious || IsTethered ) return;
		if ( !GrabWeaponAtHand() && ++reachAttempts > 4 ) reaching = false;
	}

	bool GrabWeaponAtHand( bool rolling = false )
	{
		if ( WeaponAtHand( PickupLeft ) is not { } item ) return false;
		// HandlePickUp extracts an embedded weapon during a flip. Only a loose item
		// can enter the hand on that update. The grabitem animation event does both.
		if ( rolling && item.StuckIn.IsValid() )
		{
			if ( item.StuckIn != Renderer ) ExtractWeapon( item, lift: true );
			return false;
		}
		if ( !TakeWeapon( item ) ) return false;
		StopSeekingWeapon();
		return true;
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

