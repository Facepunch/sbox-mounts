namespace Source1.GarrysMod;

public sealed partial class PhysicsGun
{
	const int Segments = 16;
	[ConVar( "physgun_drawbeams", Help = "Draw active Physics Gun beams." )]
	public static bool DrawBeams { get; set; } = true;

	GameObject _beamRoot;
	GameObject _muzzle;
	SkinnedModelRenderer _beamModel;
	readonly LineRenderer[] _lines = new LineRenderer[3];
	Texture _searchTexture, _grabTexture;
	string _colorSetting;

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Application.IsDedicatedServer ) return;
		if ( !Active || !HasOwner || !BeamOn || !DrawBeams || !WeaponModel.IsValid() )
		{
			if ( _beamRoot.IsValid() ) _beamRoot.Enabled = false;
			return;
		}
		if ( WeaponModel is not Source1.ViewModel ) DrawBeam( null );
	}

	protected override void OnViewModelPlaced( ViewModel model )
	{
		if ( Active && HasOwner && BeamOn && DrawBeams ) DrawBeam( model );
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
		_beamRoot.Enabled = _muzzle.IsValid();
		if ( !_beamRoot.Enabled ) return;
		var start = view is null ? _muzzle.WorldPosition : view.DrawnPosition( _muzzle );
		var ray = AimRay;
		bool holding = HeldBody.IsValid();
		var end = holding ? HeldBody.WorldPosition + HeldBody.WorldRotation * GrabPoint
			: Scene.Trace.Ray( ray, MaxRange * 0.5f ).IgnoreGameObjectHierarchy( GameObject.Root ).WithoutTags( "trigger", "projectile" ).Run().EndPosition;
		float length = start.Distance( end );
		var control = holding ? ray.Position + ray.Forward * (length * 0.6f) : end;
		var setting = Owner.GetComponent<Player>()?.PhysgunColor ?? "";
		if ( setting != _colorSetting )
		{
			_colorSetting = setting;
			var tint = Color.Parse( setting ) ?? new Color( 0.30f, 1.80f, 2.10f );
			var rgb = new Vector3( tint.r, tint.g, tint.b ).Normal;
			var color = new Color( rgb.x, rgb.y, rgb.z );
			var gradient = new Gradient( new Gradient.ColorFrame( 0, Color.Black ), new Gradient.ColorFrame( 1f / Segments, color ),
				new Gradient.ColorFrame( 1 - 1f / Segments, color ), new Gradient.ColorFrame( 1, Color.Black ) );
			foreach ( var line in _lines )
				if ( line.IsValid() ) line.Color = gradient;
		}
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

	void CreateBeam( SkinnedModelRenderer renderer, bool firstPerson )
	{
		ClearBeam();
		_beamModel = renderer;
		var attachment = renderer.Model.Attachments.All.FirstOrDefault();
		_muzzle = attachment is null ? null : renderer.GetAttachmentObject( attachment.Name );
		_beamRoot = new GameObject( true, "Physics Gun beam" ) { NetworkMode = NetworkMode.Never };
		var mount = MountContent.Of( renderer.Model );
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
		_beamRoot = null;
		_beamModel = null;
		_colorSetting = null;
	}
}
