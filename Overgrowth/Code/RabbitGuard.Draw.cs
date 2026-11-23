using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	const string DrawAcross = "r_knifeunsheathe", DrawSameSide = "r_knifeunsheathesameside";
	internal static readonly string[] DrawAnimations = [DrawAcross, DrawSameSide];
	internal static string DrawLayer( string animation, bool left ) => left ? animation + "_left" : animation + "_right";
	[Property, Sync] public LevelItem LeftHipWeapon { get; set; }
	[Property, Sync] public LevelItem RightHipWeapon { get; set; }
	[Sync] string DrawAnimation { get; set; }
	[Sync] bool DrawLeft { get; set; }
	[Sync( SyncFlags.Interpolate )] float DrawPhase { get; set; }
	[Sync( SyncFlags.Interpolate )] float DrawWeight { get; set; }
	float drawElapsed;
	LevelItem drawing;
	string appliedDrawLayer, appliedDrawAnimation;
	bool appliedDrawLeft;

	void BeginWeaponDraw()
	{
		if ( Downed || InThrow || !CanAttack( enemy ) || startled > 0f ) return;
		bool secondary = PrimaryItem.IsValid();
		bool left = secondary ? !LeftHanded : LeftHanded;
		if ( secondary && boundAnimations.Species != "cat" || (left ? LeftHandItem : RightHandItem).IsValid() ) return;
		var item = RightHipWeapon.IsValid() ? RightHipWeapon : LeftHipWeapon;
		if ( !item.IsValid() || item.Holder != Renderer ) return;

		DrawLeft = left;
		bool sameSide = left == (item == LeftHipWeapon);
		DrawAnimation = sameSide ? DrawSameSide : DrawAcross;
		drawElapsed = 0f;
		drawing = item;
	}

	void TakeDrawnWeapon()
	{
		if ( !drawing.IsValid() ) return;
		var item = drawing;
		drawing = null;
		if ( (DrawLeft ? LeftHandItem : RightHandItem).IsValid() || item.Holder != Renderer ||
			Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		var position = item.PhysicsPosition;
		LevelItem.Attach( mount, item.GameObject, item.GetComponent<ModelRenderer>().Model, this, 0, DrawLeft );
		if ( (DrawLeft ? LeftHandItem : RightHandItem) != item ) return;
		if ( item == RightHipWeapon ) RightHipWeapon = null;
		else LeftHipWeapon = null;
		PlayCombatSound( "Data/Sounds/weapon_foley/grab/weapon_grap_metal_leather_glove.xml", position, 0.5f );
	}

	void UpdateWeaponDraw()
	{
		if ( Networking.IsHost )
		{
			if ( DrawAnimation is null ) BeginWeaponDraw();
			if ( DrawAnimation is not null )
			{
				var clip = boundAnimations.Clips[DrawAnimation];
				drawElapsed += Time.Delta;
				DrawPhase = Math.Min( 1f, drawElapsed / clip.Duration );
				DrawWeight = Math.Clamp( Math.Min( (drawElapsed + 1f / 120f) * 8f, 1f - (drawElapsed - clip.Duration) * 8f ), 0f, 1f );
				if ( !Downed && clip.DrawTime is { } grab && drawElapsed >= grab ) TakeDrawnWeapon();
				if ( DrawWeight <= 0f || Downed )
				{
					DrawAnimation = null;
					drawing = null;
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
