using System;

namespace Source1.GarrysMod;

public sealed partial class PhysicsGun
{
	const int Segments = 16;
	[ConVar( "physgun_drawbeams", Help = "Draw active Physics Gun beams." )]
	public static bool DrawBeams { get; set; } = true;
	[ConVar( "physgun_halo", ConVarFlags.Saved, Help = "Outline the object held by a Physics Gun." )]
	public static bool DrawHalo { get; set; } = true;

	GameObject _beamRoot;
	HighlightOutline _outline;
	GameObject _outlined;
	GameObject _muzzle;
	SkinnedModelRenderer _beamModel;
	readonly LineRenderer[] _lines = new LineRenderer[3];
	readonly SpriteRenderer[] _muzzleGlows = new SpriteRenderer[7], _contactGlows = new SpriteRenderer[7];
	Sprite _glowSprite;
	Color _tint;
	Texture _searchTexture, _grabTexture;
	string _colorSetting;

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Application.IsDedicatedServer ) return;
		if ( !Active || !HasOwner || !WeaponModel.IsValid() )
		{
			if ( _beamRoot.IsValid() ) _beamRoot.Enabled = false;
			return;
		}
		if ( WeaponModel is not Source1.ViewModel ) DrawBeam( null );
	}

	protected override void OnViewModelPlaced( ViewModel model )
	{
		if ( Active && HasOwner ) DrawBeam( model );
	}

	void DrawBeam( ViewModel view )
	{
		var renderer = WeaponModel?.Renderer;
		if ( !renderer.IsValid() || !renderer.Model.IsValid() || !renderer.Enabled )
		{
			if ( _beamRoot.IsValid() ) _beamRoot.Enabled = false;
			return;
		}
		if ( renderer != _beamModel || !_beamRoot.IsValid() ) CreateBeam( renderer, view is not null );
		_beamRoot.Enabled = _muzzle.IsValid() && (!BeamOn || DrawBeams);
		if ( !_beamRoot.Enabled ) return;
		var start = view is null ? _muzzle.WorldPosition : view.DrawnPosition( _muzzle );
		UpdateColor();
		bool holding = BeamOn && HeldTarget.IsValid();
		UpdateOutline( holding && DrawHalo ? HeldTarget.GameObject : null );
		var forward = _muzzle.WorldRotation.Forward;
		if ( view is not null ) forward = view.DrawnDirection( forward );
		int activeGlows = holding ? 7 : 2;
		DrawGlows( BeamOn ? activeGlows : 3, start, forward * (BeamOn ? 2 : 5), muzzle: true );
		foreach ( var line in _lines )
			if ( line.IsValid() ) line.Enabled = BeamOn;
		if ( !BeamOn )
		{
			DrawGlows( 0, default, default, muzzle: false );
			return;
		}
		var ray = AimRay;
		var end = holding ? HeldTarget.WorldPosition + HeldTarget.WorldRotation * GrabPoint
			: TraceTarget( ray, MaxRange * 0.5f ).EndPosition;
		float length = start.Distance( end );
		var control = holding ? ray.Position + ray.Forward * (length * 0.6f) : end;
		DrawGlows( activeGlows, end, default, muzzle: false );
		for ( int i = 0; i < _lines.Length; i++ )
		{
			var line = _lines[i];
			if ( !line.IsValid() ) continue;
			line.Width = i == 0 ? 2 : Game.Random.Float( 2, 4 + i );
			var uv = MountedEffects.LineTexture( holding ? _grabTexture : _searchTexture );
			uv.Scale = -length * 0.02f;
			uv.Offset = (Time.Now * (holding ? 10 : -2) * (i == 0 ? 1 : i == 1 ? 1.1f : -0.9f)) % 1;
			line.Texturing = uv;
		}
		for ( int j = 0; j <= Segments; j++ )
		{
			float t = j / (float)Segments;
			var point = start * ((1 - t) * (1 - t)) + control * (2 * (1 - t) * t) + end * (t * t);
			for ( int i = 0; i < _lines.Length; i++ )
			{
				if ( _lines[i].IsValid() ) _lines[i].VectorPoints[i == 0 ? j : Segments - j] = point;
			}
		}
	}

	void UpdateColor()
	{
		var setting = Owner.GetComponent<Player>()?.PhysgunColor ?? "";
		if ( setting == _colorSetting ) return;
		_colorSetting = setting;
		var tint = Color.Parse( setting ) ?? new Color( 0.30f, 1.80f, 2.10f );
		var rgb = new Vector3( tint.r, tint.g, tint.b ).Normal;
		_tint = new Color( rgb.x, rgb.y, rgb.z );
		if ( _outline.IsValid() ) _outline.Color = _tint;
		var gradient = new Gradient( new Gradient.ColorFrame( 0, Color.Black ), new Gradient.ColorFrame( 1f / Segments, _tint ),
			new Gradient.ColorFrame( 1 - 1f / Segments, _tint ), new Gradient.ColorFrame( 1, Color.Black ) );
		foreach ( var line in _lines )
			if ( line.IsValid() ) line.Color = gradient;
	}

	void UpdateOutline( GameObject target )
	{
		if ( target == _outlined ) return;
		_outlined = target;
		if ( !target.IsValid() )
		{
			_outline.Hide();
			return;
		}
		_outline ??= ObjectHighlight.Create( _beamRoot );
		_outline.Show( ObjectHighlight.GetTarget( target ), new HighlightStyle( _tint, Color.Transparent, HighlightStyle.Selected.Width ) );
	}

	void DrawGlows( int count, Vector3 position, Vector3 step, bool muzzle )
	{
		if ( _glowSprite is null ) return;
		var glows = muzzle ? _muzzleGlows : _contactGlows;
		for ( int i = 0; i < glows.Length; i++ )
		{
			var sprite = glows[i];
			if ( i >= count )
			{
				if ( sprite.IsValid() ) sprite.Enabled = false;
				continue;
			}
			if ( !sprite.IsValid() )
			{
				var go = new GameObject( _beamRoot, true, muzzle ? "Muzzle glow" : "Contact glow" ) { NetworkMode = NetworkMode.Never };
				sprite = glows[i] = go.AddComponent<SpriteRenderer>();
				sprite.Sprite = _glowSprite;
				sprite.Additive = true;
				sprite.AlphaCutoff = 0;
				bool firstPerson = WeaponModel is Source1.ViewModel;
				sprite.RenderOptions.Game = !firstPerson;
				sprite.RenderOptions.Overlay = firstPerson;
			}
			sprite.WorldPosition = position + step * i;
			sprite.Size = !BeamOn ? 48 + MathF.Sin( (Time.Now + i * 3) * 5 ) * 8
				: muzzle ? Game.Random.Float( 16, 64 ) : Game.Random.Float( 1, 16 );
			float alpha = Game.Random.Int( BeamOn ? 100 : 120, 255 ) / 255f;
			sprite.Color = _tint.WithAlpha( alpha * (muzzle ? 0.3f : 0.5f) );
			sprite.Enabled = true;
		}
	}

	void CreateBeam( SkinnedModelRenderer renderer, bool firstPerson )
	{
		ClearBeam();
		_beamModel = renderer;
		var attachment = renderer.Model.Attachments.All.FirstOrDefault();
		_muzzle = attachment is null ? null : renderer.GetAttachmentObject( attachment.Name );
		_beamRoot = new GameObject( true, "Physics Gun beam" ) { NetworkMode = NetworkMode.Never };
		var mount = MountContent.Of( renderer.Model );
		var glow = MountedEffects.SpriteTexture( "sprites/physgun_glow", mount );
		_glowSprite = glow is null ? null : Sprite.FromTexture( glow );
		_searchTexture = MountedEffects.SpriteTexture( "sprites/physbeam_white", mount );
		_grabTexture = MountedEffects.SpriteTexture( "sprites/physbeam_active_white", mount );
		for ( int i = 0; i < _lines.Length; i++ )
		{
			var line = _lines[i] = MountedEffects.AddBeam( _beamRoot, "sprites/physbeam_white", mount );
			if ( !line.IsValid() ) continue;
			line.VectorPoints = new( new Vector3[Segments + 1] );
			line.RenderOptions.Game = !firstPerson;
			line.RenderOptions.Overlay = firstPerson;
		}
	}

	void ClearBeam()
	{
		_beamRoot?.Destroy();
		Array.Clear( _muzzleGlows );
		Array.Clear( _contactGlows );
		_glowSprite = null;
		_beamRoot = null;
		_outline = null;
		_outlined = null;
		_beamModel = null;
		_colorSetting = null;
	}
}
