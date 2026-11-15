using System;

namespace Source1;

/// <summary>
/// Counter-Strike: Source's knife: attack1 slashes, attack2 stabs. Damage, reach and recovery are
/// CKnife's code constants; the weapon script's Damage goes unused, as in the game.
/// </summary>
[Title( "CS:S Knife" )]
[Category( "Counter-Strike: Source" )]
public sealed class CssKnife : SourceMeleeWeapon
{
	const float SlashDamage = 15;
	const float StabDamage = 65;
	const float SlashRange = 48;
	const float StabRange = 32;
	const float SlashHitDelay = 0.4f;
	const float SlashMissDelay = 0.5f;
	const float StabHitDelay = 1.1f;
	const float StabMissDelay = 1;

	public override bool CanSecondaryAttack() => NextSecondaryFire <= 0 && NextPrimaryFire <= 0;

	protected override bool OnAdding( BaseInventoryComponent inventory )
	{
		SourceWeaponSetup.ConfigureAmmo( this );
		return base.OnAdding( inventory );
	}

	protected override async void OnStart()
	{
		base.OnStart();
		try
		{
			await SourceWeaponSetup.ConfigureWeapon( this );
			// The script's single_shot is a leftover Desert Eagle shot; CKnife plays its own sounds.
			if ( IsValid ) AttackSound = null;
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't configure mounted knife: {e.Message}" );
		}
	}

	public override void PrimaryAttack() => Swing( false );

	public override void SecondaryAttack() => Swing( true );

	void Swing( bool stab )
	{
		var tr = Scene.Trace.Ray( AimRay, stab ? StabRange : SlashRange )
			.IgnoreGameObjectHierarchy( AimIgnoreRoot )
			.WithoutTags( "playercontroller" )
			.UseHitboxes()
			.Run();
		if ( !tr.Hit )
			tr = Scene.Trace.Ray( AimRay, stab ? StabRange : SlashRange ).Radius( SwingRadius )
				.IgnoreGameObjectHierarchy( AimIgnoreRoot ).WithoutTags( "playercontroller" ).UseHitboxes().Run();
		var hit = tr.GameObject.IsValid();
		SetNextFire( stab ? (hit ? StabHitDelay : StabMissDelay) : (hit ? SlashHitDelay : SlashMissDelay) );
		SwingEffects( new ShotEffect( tr.HitPosition, tr.Hit, tr.Normal, tr.GameObject, tr.Surface ), stab );
		ShootBullet( tr, stab ? StabDamage : SlashDamage, SwingForce, hitboxTags: true );
		KickView();
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void SwingEffects( ShotEffect shot, bool stab )
	{
		OnShootEffects( shot );
		if ( Application.IsDedicatedServer ) return;
		var flesh = shot.Surface?.ResourceName?.Contains( "flesh" ) == true;
		var sound = !shot.Hit ? "Weapon_Knife.Slash" : !flesh ? "Weapon_Knife.HitWall" : stab ? "Weapon_Knife.Stab" : "Weapon_Knife.Hit";
		if ( SourceSounds.Sound( sound, SourceMount.OfWeapon( this ) ) is { } swing ) GameObject.PlaySound( swing );
		// The base played the slash clip; a stab replaces it on the same frame.
		if ( stab && WeaponModel is SourceViewModel view ) view.OnAlternateAttack();
	}
}
