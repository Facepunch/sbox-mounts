using System;

namespace Source1;

/// <summary>
/// weapon_stunstick: CWeaponStunStick::ImpactEffect adds StunstickImpact to the melee impact. A metrocop switches it on and off
/// (SetStunState): Weapon_StunStick.Activate with sparks at the tip, or Deactivate. While on, C_WeaponStunStick::DrawModel flickers
/// two effects/stunstick halos an inch behind the tip, a dim one 8 to 12 units across and a bright one 4 to 6.
/// Approximation: the tesla beams its ClientThink adds now and then are not drawn.
/// </summary>
[Title( "HL2 Stunstick" )]
[Category( "Half-Life 2" )]
public sealed class Hl2Stunstick : SourceMeleeWeapon
{
	[Sync( SyncFlags.FromHost )] public bool StunActive { get; private set; }

	bool _glowLoaded;
	SpriteRenderer _halo;
	SpriteRenderer _core;

	protected override void OnShootImpact( in ShotEffect shot )
	{
		base.OnShootImpact( shot );
		if ( shot.Hit && !Application.IsDedicatedServer )
			SourceWeaponEffects.StunstickImpact( shot.HitPosition, shot.Normal, SourceMount.Of( GameObject ) );
	}

	/// <summary>Switches the stick on or off, on the host.</summary>
	public void SetStunState( bool active )
	{
		if ( StunActive == active )
			return;

		StunActive = active;
		StunStateChanged( active );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void StunStateChanged( bool active )
	{
		var mount = SourceMount.Of( GameObject );
		if ( SourceSounds.Sound( active ? "Weapon_StunStick.Activate" : "Weapon_StunStick.Deactivate", mount ) is { } sound )
			GameObject.PlaySound( sound );

		if ( active && Tip() is { } tip )
			SourceWeaponEffects.ElectricSpark( tip.Position, 1, Vector3.Zero, mount );
	}

	/// <summary>The weapon model's first attachment, GetAttachment( 1 ).</summary>
	Transform? Tip()
	{
		var renderer = WeaponModel?.Renderer;
		var name = renderer?.Model?.Attachments.All.FirstOrDefault()?.Name;
		return name is null ? null : renderer.GetAttachment( name );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		var tip = StunActive && !Application.IsDedicatedServer ? Tip() : null;
		if ( tip.HasValue && !_glowLoaded )
		{
			_glowLoaded = true;
			var mount = SourceMount.Of( GameObject );
			_halo = SourceMountedEffects.AddSprite( GameObject, "effects/stunstick", mount );
			_core = SourceMountedEffects.AddSprite( GameObject, "effects/stunstick", mount );
		}

		Glow( _halo, tip, 4, 6, 0.1f, 0.2f );
		Glow( _core, tip, 2, 3, 0.9f, 1 );
	}

	/// <summary>DrawHalo: a quad reaching the radius out from the point.</summary>
	static void Glow( SpriteRenderer sprite, Transform? tip, float minRadius, float maxRadius, float minBrightness, float maxBrightness )
	{
		if ( !sprite.IsValid() )
			return;

		sprite.Enabled = tip.HasValue;
		if ( tip is not { } at )
			return;

		sprite.WorldPosition = at.Position - at.Rotation.Forward;
		sprite.Size = 2 * Game.Random.Float( minRadius, maxRadius );
		sprite.Color = (Color.White * Game.Random.Float( minBrightness, maxBrightness )).WithAlpha( 1 );
	}
}
