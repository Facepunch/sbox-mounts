namespace Source1.Hl2;

/// <summary>An electrified melee weapon with sparks and flickering electrodes.</summary>
[Title( "HL2 Stunstick" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2Stunstick", "Hl2Stunstick" )]
public sealed class Stunstick : MeleeWeapon
{
	[Sync( SyncFlags.FromHost )] public bool StunActive { get; private set; }

	GameObject _effects;
	SkinnedModelRenderer _renderer;
	GameObject _tip;
	(GameObject Point, SpriteRenderer Glow)[] _electrodes = [];
	LineRenderer _arc;
	GameObject _arcStart;
	Vector3 _arcOffset;
	TimeUntil _arcExpires;
	TimeSince _swing = 1;
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

	protected override void OnShootEffects( ShotEffect shot )
	{
		if ( !shot.NoEvents ) _swing = 0;
		base.OnShootEffects( shot );
	}

	protected override void OnShootImpact( in ShotEffect shot )
	{
		if ( shot.Hit && !Application.IsDedicatedServer )
			WeaponEffects.StunstickImpact( shot.HitPosition, shot.Normal, MountContent.Of( GameObject ) );
		ImpactEffect( shot, GetShotDirection( shot ) );
	}


	/// <summary>Switches the stick on or off on the host.</summary>
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

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Application.IsDedicatedServer ) return;
		if ( !StunActive || !Active || !HasOwner || !WeaponModel.IsValid() )
		{
			if ( _effects.IsValid() ) _effects.Enabled = false;
			return;
		}
		if ( WeaponModel is not Source1.ViewModel ) DrawEffects( null );
	}

	protected override void OnViewModelPlaced( ViewModel model )
	{
		if ( StunActive && Active && HasOwner ) DrawEffects( model );
	}

	void DrawEffects( ViewModel view )
	{
		var renderer = WeaponModel?.Renderer;
		if ( !renderer.IsValid() || !renderer.Model.IsValid() ) return;
		if ( renderer != _renderer || !_effects.IsValid() ) CreateEffects( renderer, view );
		_effects.Enabled = _tip.IsValid();
		if ( !_effects.Enabled ) return;

		Vector3 Position( GameObject point ) => view is null ? point.WorldPosition : view.DrawnPosition( point );
		var tip = Position( _tip );
		if ( _sparkOnActivation )
		{
			_sparkOnActivation = false;
			WeaponEffects.ElectricSpark( tip, 1, Vector3.Zero, MountContent.OfWeapon( this ), view?.GameObject );
		}

		if ( view is null )
		{
			var center = tip - _tip.WorldRotation.Forward;
			Glow( _halo, center, Game.Random.Float( 8, 12 ), Game.Random.Float( 0.1f, 0.2f ) );
			Glow( _core, center, Game.Random.Float( 4, 6 ), Game.Random.Float( 0.9f, 1 ) );
		}
		else
		{
			var fade = _swing < SwingDelay ? 1 : 0.1f;
			Glow( _halo, tip, fade == 1 ? 22 : 20, 0.4f * fade );
			foreach ( var (point, glow) in _electrodes )
				Glow( glow, Position( point ), Game.Random.Float( 4, 5 ) * fade, Game.Random.Float( 0.05f, 0.5f ) * fade );
		}

		if ( !_arc.IsValid() ) return;
		if ( Game.Random.Int( 0, view is null ? 5 : 3 ) == 0 )
		{
			_arcStart = _electrodes.Length > 0 ? _electrodes[Game.Random.Int( 0, _electrodes.Length - 1 )].Point : _tip;
			_arcOffset = Vector3.Random * Game.Random.Float( 4, 10 );
			_arcExpires = view is null ? 0.01f : 0.05f;
		}
		_arc.Enabled = _arcStart.IsValid() && _arcExpires > 0;
		if ( !_arc.Enabled ) return;
		var start = Position( _arcStart );
		_arc.VectorPoints[0] = start;
		_arc.VectorPoints[1] = start + _arcOffset * 0.5f + Vector3.Random * 2;
		_arc.VectorPoints[2] = start + _arcOffset;
	}

	void CreateEffects( SkinnedModelRenderer renderer, ViewModel view )
	{
		_effects?.Destroy();
		_renderer = renderer;
		bool firstPerson = view.IsValid();
		_effects = new GameObject( view?.GameObject ?? renderer.GameObject, true, "Stunstick electricity" ) { NetworkMode = NetworkMode.Never };
		var mount = MountContent.OfWeapon( this );
		var points = renderer.Model.Attachments.All;
		var center = firstPerson ? renderer.GetAttachmentObject( "Sparkrear" ) : null;
		_tip = center ?? (points.FirstOrDefault() is { } first ? renderer.GetAttachmentObject( first.Name ) : null);
		var material = firstPerson ? "sprites/light_glow02" : "effects/stunstick";
		_halo = MountedEffects.AddSprite( _effects, material, mount );
		_core = firstPerson ? null : MountedEffects.AddSprite( _effects, material, mount );
		_electrodes = firstPerson ? points.Select( a => renderer.GetAttachmentObject( a.Name ) )
			.Where( p => p.IsValid() && p != _tip ).Select( p => (p, MountedEffects.AddSprite( _effects, material, mount )) ).ToArray() : [];
		_arcStart = null;
		_arc = MountedEffects.AddBeam( _effects, "sprites/lgtning", mount );
		if ( _arc.IsValid() )
		{
			_arc.VectorPoints = [Vector3.Zero, Vector3.Zero, Vector3.Zero];
			_arc.Width = 1;
		}
	}

	static void Glow( SpriteRenderer sprite, Vector3 position, float size, float brightness )
	{
		if ( !sprite.IsValid() ) return;
		sprite.WorldPosition = position;
		sprite.Size = size;
		sprite.Color = (Color.White * brightness).WithAlpha( 1 );
	}
}
