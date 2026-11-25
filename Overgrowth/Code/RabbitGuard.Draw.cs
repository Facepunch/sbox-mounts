using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	const string DrawAcross = "r_knifeunsheathe", DrawSameSide = "r_knifeunsheathesameside";
	const string SwapHands = "r_heldweaponswap";
	internal static string[] HandAnimations => [DrawAcross, DrawSameSide, SwapHands, ThrowLayer, FlipThrowLayer];
	internal static string DrawLayer( string animation, bool left ) => left ? animation + "_left" : animation + "_right";
	[Property, Sync] public LevelItem LeftHipWeapon { get; set; }
	[Property, Sync] public LevelItem RightHipWeapon { get; set; }
	[Sync] string DrawAnimation { get; set; }
	[Sync] bool DrawLeft { get; set; }
	[Sync( SyncFlags.Interpolate )] float DrawPhase { get; set; }
	[Sync( SyncFlags.Interpolate )] float DrawWeight { get; set; }
	float drawElapsed;
	bool fadingThrow;
	string appliedDrawLayer, appliedDrawAnimation;
	bool appliedDrawLeft;

	bool BeginHandSwap()
	{
		var secondary = LeftHanded ? RightHandItem : LeftHandItem;
		if ( !secondary.IsValid() || Downed || Unconscious ) return false;
		if ( PrimaryItem.IsValid() && (boundAnimations.WeaponLabel != "knife" || secondary.Definition?.Element( "label" )?.Value is not ("sword" or "rapier")) ) return false;
		DrawAnimation = SwapHands;
		DrawLeft = MirroredStance;
		drawElapsed = 0f;
		return true;
	}

	void SwapHeldWeapons( int eventBone )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		var left = LeftHandItem;
		var right = RightHandItem;
		LeftHandItem = RightHandItem = null;
		if ( left.IsValid() ) LevelItem.Attach( mount, left.GameObject, left.GetComponent<ModelRenderer>().Model, this, 0, false );
		if ( right.IsValid() ) LevelItem.Attach( mount, right.GameObject, right.GetComponent<ModelRenderer>().Model, this, 0, true );
		var point = Renderer.TryGetBoneTransform( $"bone_{eventBone}", out var bone ) ? bone.Position : WorldPosition;
		PlayCombatSound( "Data/Sounds/weapon_foley/impact/weapon_drop_light_dirt.xml", point, 0.5f );
	}

	void BeginWeaponDraw()
	{
		if ( Unconscious || Downed || InThrow || !HasAttackGoal || startled > 0f ) return;
		bool secondary = PrimaryItem.IsValid();
		if ( IsTethered && !(Holding && secondary) ) return;
		bool left = secondary ? !LeftHanded : LeftHanded;
		if ( secondary && boundAnimations.Species != "cat" || (left ? LeftHandItem : RightHandItem).IsValid() ) return;
		var item = RightHipWeapon.IsValid() ? RightHipWeapon : LeftHipWeapon;
		if ( !item.IsValid() || item.Holder != Renderer ) return;

		DrawLeft = left;
		bool sameSide = left == (item == LeftHipWeapon);
		DrawAnimation = sameSide ? DrawSameSide : DrawAcross;
		drawElapsed = 0f;
	}

	void TakeDrawnWeapon( bool leftHand, bool leftHip )
	{
		var item = leftHip ? LeftHipWeapon : RightHipWeapon;
		if ( !item.IsValid() || (leftHand ? LeftHandItem : RightHandItem).IsValid() || item.Holder != Renderer ||
			Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		var position = item.PhysicsPosition;
		LevelItem.Attach( mount, item.GameObject, item.GetComponent<ModelRenderer>().Model, this, 0, leftHand );
		if ( (leftHand ? LeftHandItem : RightHandItem) != item ) return;
		PlayCombatSound( "Data/Sounds/weapon_foley/grab/weapon_grap_metal_leather_glove.xml", position, 0.5f );
	}

	void UpdateWeaponDraw()
	{
		if ( Networking.IsHost )
		{
			if ( DrawAnimation is null && !BeginHandSwap() && !BeginItemThrow() ) BeginWeaponDraw();
			if ( DrawAnimation is not null )
			{
				var clip = boundAnimations.Clips[DrawAnimation].ForSide( DrawLeft );
				var previous = drawElapsed;
				drawElapsed += Time.Delta;
				DrawPhase = Math.Min( 1f, drawElapsed / clip.Duration );
				if ( fadingThrow ) DrawWeight = Math.Max( 0f, DrawWeight - 4f * Time.Delta );
				else DrawWeight = Math.Clamp( Math.Min( (drawElapsed + 1f / 120f) * 8f, 1f - (drawElapsed - clip.Duration) * 8f ), 0f, 1f );
				if ( (!fadingThrow || DrawWeight > 0f) && !Downed && itemThrowTarget is not null && clip.WeaponRelease is { } release && drawElapsed >= release.Time )
					ReleaseThrownItem( release.Left );
				if ( !Downed && clip.DrawWeapon is { } draw && previous < draw.Time && drawElapsed >= draw.Time )
					TakeDrawnWeapon( draw.LeftHand, draw.LeftHip );
				if ( !Downed && clip.HandSwap is { } swap && previous < swap.Time && drawElapsed >= swap.Time )
					SwapHeldWeapons( swap.Bone );
				if ( DrawWeight <= 0f || Downed )
				{
					DrawAnimation = null;
					fadingThrow = false;
					handThrowLookTarget = null;
					itemThrowTarget = null;
				}
			}
		}
		if ( appliedDrawAnimation != DrawAnimation || appliedDrawLeft != DrawLeft )
		{
			if ( appliedDrawLayer is not null ) Renderer.Animation.Parameters.Set( appliedDrawLayer, 0f );
			appliedDrawAnimation = DrawAnimation;
			appliedDrawLeft = DrawLeft;
			appliedDrawLayer = DrawAnimation is null ? null : DrawLayer( DrawAnimation, DrawLeft );
		}
		if ( appliedDrawLayer is null ) return;
		Renderer.Animation.Parameters.Set( "draw_phase", DrawPhase );
		Renderer.Animation.Parameters.Set( appliedDrawLayer, DrawWeight );
	}
}
