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
	Vector3 _arcEnd;
	float _arcAmplitude;
	const int ArcSegments = 16;
	readonly float[] _arcNoise = new float[ArcSegments + 1];
	TimeUntil _arcExpires;
	TimeSince _sinceSwing = 1;
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

		bool swinging = view is null || view.IsSwinging;
		if ( view is null )
		{
			var center = tip;
			Glow( _halo, center, 22, 0.4f );
			Glow( _core, center, Game.Random.Float( 4, 6 ), Game.Random.Float( 0.6f, 0.8f ) );
		}
		else
		{
			if ( swinging ) _sinceSwing = 0;
			var fade = 1 - 0.9f * ((float)_sinceSwing / 0.25f).Clamp( 0, 1 );
			Glow( _halo, tip, swinging ? 22 : 20, 0.4f * fade );
			foreach ( var (point, glow) in _electrodes )
				Glow( glow, Position( point ), Game.Random.Float( 4, 5 ) * fade, Game.Random.Float( 0.05f, 0.5f ) * fade );
		}

		if ( !_arc.IsValid() ) return;
		if ( swinging && Game.Random.Int( 0, view is null ? 5 : 3 ) == 0 )
		{
			_arcStart = _electrodes.Length > 0 ? _electrodes[Game.Random.Int( 0, _electrodes.Length - 1 )].Point : _tip;
			_arcEnd = Position( _arcStart ) + (view is null ? Particles.InCube( 10 ) + Vector3.One * 2 : Particles.InCube( 8 ));
			_arcAmplitude = view is null ? Game.Random.Float( 1, 2 ) : Game.Random.Float( 16, 32 );
			var width = Game.Random.Float( 1, view is null ? 3 : 2 );
			_arc.Width = new Curve( new Curve.Frame( 0, width ) { Mode = Curve.HandleMode.Linear }, new Curve.Frame( 1, 0 ) );
			_arcExpires = view is null ? 0.01f : 0.05f;
		}
		_arc.Enabled = _arcStart.IsValid() && _arcExpires > 0;
		if ( !_arc.Enabled ) return;
		if ( _arc.VectorPoints.Count != ArcSegments + 1 )
			_arc.VectorPoints = new( new Vector3[ArcSegments + 1] );
		var start = Position( _arcStart );
		var delta = _arcEnd - start;
		var side = delta.Cross( Scene.Camera.WorldPosition - start ).Normal * (delta.Length * 0.01f * _arcAmplitude);
		for ( int step = ArcSegments; step > 1; step /= 2 )
		{
			for ( int i = 0; i < ArcSegments; i += step )
				_arcNoise[i + step / 2] = (_arcNoise[i] + _arcNoise[i + step]) * 0.5f + Game.Random.Float( -1, 1 ) * step / ArcSegments;
		}
		for ( int i = 0; i <= ArcSegments; i++ )
			_arc.VectorPoints[i] = start + delta * (i / (float)ArcSegments) + side * _arcNoise[i];
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
		const string material = "sprites/light_glow02";
		_halo = MountedEffects.AddSprite( _effects, material, mount );
		_core = firstPerson ? null : MountedEffects.AddSprite( _effects, "effects/blueflare1", mount );
		_electrodes = firstPerson ? points.Select( a => renderer.GetAttachmentObject( a.Name ) )
			.Where( p => p.IsValid() && p != _tip ).Select( p => (p, MountedEffects.AddSprite( _effects, material, mount )) ).ToArray() : [];
		_arcStart = null;
		_arc = MountedEffects.AddBeam( _effects, "sprites/lgtning", mount );
		if ( _arc.IsValid() )
		{
			_arc.Width = 1;
			_arc.Color = Color.White;
		}
	}

	static void Glow( SpriteRenderer sprite, Vector3 position, float size, float brightness )
	{
		if ( !sprite.IsValid() ) return;
		sprite.WorldPosition = position;
		sprite.Size = 2 * size;
		sprite.Color = (Color.White * System.MathF.Pow( brightness, 2.2f )).WithAlpha( 1 );
	}
}
