namespace Diesel;

/// <summary>Mount-cached inventory art, rendered with the sandbox weapon icon creator's framing and outline.</summary>
sealed class PaydayWeaponIconLoader( WeaponDefinition definition ) : ResourceLoader<PaydayMount>
{
	protected override async Task<object> LoadAsync()
	{
		if ( Application.IsDedicatedServer ) return null;
		var model = await ResourceLibrary.LoadAsync<Model>( $"mount://payday/{definition.WorldUnit}.unit.vmdl" );
		if ( model is null || model.IsError ) throw new InvalidDataException( $"Missing icon model for {definition.Title}." );
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			scene.Enabled = true;
			var camera = new GameObject( true, "Icon camera" ).AddComponent<CameraComponent>();
			camera.Orthographic = true;
			camera.IsMainCamera = true;
			camera.BackgroundColor = Color.Transparent;
			camera.ZNear = 1;
			camera.GameObject.AddComponent<Highlight>();
			var renderer = new GameObject( true, "Icon model" ).AddComponent<ModelRenderer>();
			renderer.Model = model;
			renderer.MaterialOverride = Material.Load( "materials/default/hud_icon.vmat" );
			var outline = renderer.GameObject.AddComponent<HighlightOutline>();
			outline.Color = Color.White;
			outline.ObscuredColor = Color.Transparent;
			outline.Width = 1.5f;
			var rotation = Rotation.From( 0, 90, 0 );
			renderer.WorldRotation = rotation;
			renderer.WorldPosition = -(rotation * model.Bounds.Center);
			var extents = Vector3.Zero;
			for ( int i = 0; i < 8; i++ )
			{
				var corner = new Vector3(
					(i & 1) == 0 ? model.Bounds.Mins.x : model.Bounds.Maxs.x,
					(i & 2) == 0 ? model.Bounds.Mins.y : model.Bounds.Maxs.y,
					(i & 4) == 0 ? model.Bounds.Mins.z : model.Bounds.Maxs.z );
				extents = Vector3.Max( extents, (rotation * (corner - model.Bounds.Center)).Abs() );
			}
			camera.OrthographicHeight = Math.Max( extents.z * 2, extents.y ) * 1.1f;
			camera.WorldPosition = new Vector3( -(extents.x + 64), 0, 0 );
			camera.ZFar = extents.x * 2 + 128;
			using var bitmap = new Bitmap( 512, 256 );
			// Flush component/render state in this isolated scene, never tick the player's scene.
			scene.EditorTick( RealTime.Now, 0 );
			camera.RenderToBitmap( bitmap, false );
			return bitmap.ToTexture();
		}
		finally { scene.Destroy(); }
	}
}
