using System;

namespace Source1.Hl2;

/// <summary>
/// A hit adds StunstickImpact to the melee impact. A metrocop switches the stick on and off
/// with activation sparks at the tip and matching sounds. While on, it flickers
/// two effects/stunstick halos an inch behind the tip, a dim one 8 to 12 units across and a bright one 4 to 6.
/// Approximation: intermittent tesla beams are omitted.
/// </summary>
[Title( "HL2 Stunstick" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2Stunstick", "Hl2Stunstick" )]
public sealed class Stunstick : MeleeWeapon
{
	[Sync( SyncFlags.FromHost )] public bool StunActive { get; private set; }

	bool _glowLoaded;
	bool _sparkOnActivation;
	SpriteRenderer _halo;
	SpriteRenderer _core;

	protected override void OnEquipped()
	{
		base.OnEquipped();
		if ( Networking.IsHost && IsValid && IsActive && HasOwner ) SetStunState( true );
	}

	protected override void OnHolstered()
	{
		if ( Networking.IsHost && HasOwner ) SetStunState( false );
		base.OnHolstered();
	}

	protected override void OnShootImpact( in ShotEffect shot )
	{
		if ( shot.Hit && !Application.IsDedicatedServer )
			WeaponEffects.StunstickImpact( shot.HitPosition, shot.Normal, MountContent.Of( GameObject ) );
		ImpactEffect( shot, GetShotDirection( shot ) );
	}

	/// <summary>Switches the stick on or off, on the host.</summary>
	public void SetStunState( bool active )
	{
		if ( StunActive == active )
			return;

		StunActive = active;
		// The multiplayer player weapon requests Deactive; the NPC uses Deactivate.
		var sound = active ? "Weapon_StunStick.Activate" : Holder is Sandbox.Npcs.BaseNpc ? "Weapon_StunStick.Deactivate" : "Weapon_StunStick.Deactive";
		StunStateChanged( active, sound );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void StunStateChanged( bool active, string soundName )
	{
		var mount = MountContent.Of( GameObject );
		if ( Sounds.Sound( soundName, mount ) is { } sound )
			GameObject.PlaySound( sound );

		// The equipped model may still be loading its attachments on this peer.
		_sparkOnActivation = active;
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
		if ( _sparkOnActivation && tip is { } at )
		{
			_sparkOnActivation = false;
			WeaponEffects.ElectricSpark( at.Position, 1, Vector3.Zero, MountContent.OfWeapon( this ) );
		}
		if ( tip.HasValue && !_glowLoaded )
		{
			_glowLoaded = true;
			var mount = MountContent.Of( GameObject );
			_halo = MountedEffects.AddSprite( GameObject, "effects/stunstick", mount );
			_core = MountedEffects.AddSprite( GameObject, "effects/stunstick", mount );
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
