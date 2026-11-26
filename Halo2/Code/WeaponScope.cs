using Sandbox.Rendering;

namespace Halo2;

[Hide]
public sealed class WeaponScope : Component
{
	[RequireComponent] public SkinnedModelRenderer Renderer { get; set; }

	static readonly TagSet Excluded = new( new[] { "viewmodel", "firstperson" } );
	Texture frame;

	internal void Capture( CameraComponent camera, in CameraView view, Vector2 center, Vector2 size )
	{
		var viewport = camera.ScreenRect.Size;
		if ( viewport.x <= 0 || viewport.y <= 0 ) return;
		if ( frame is null )
		{
			frame = Texture.CreateRenderTarget().WithSize( 64, 64 ).WithFormat( ImageFormat.RGBA8888 ).Create();
			Renderer.Attributes.Set( "ScopeFrame", frame );
			Renderer.Attributes.Set( "HasScopeFrame", true );
		}

		// The original 64-pixel scope takes its rectangle from the unobstructed world view.
		var clipCenter = new Vector2( center.x * 2 - 1, 1 - center.y * 2 );
		var bounds = new Vector4( clipCenter.x - size.x, clipCenter.y - size.y, clipCenter.x + size.x, clipCenter.y + size.y );
		var aspect = viewport.x / viewport.y;
		float fov = camera.FovAxis == CameraComponent.Axis.Vertical ? Screen.CreateVerticalFieldOfView( view.FieldOfView, aspect ) : view.FieldOfView;
		camera.RenderToTexture( frame, new ViewSetup
		{
			Transform = view.Transform,
			ProjectionMatrix = Matrix.CreateProjection( camera.ZNear, camera.ZFar, fov, aspect, bounds ),
			ExcludeTags = Excluded,
			EnablePostprocessing = false,
			EnableUI = false,
			EnableDebugOverlays = false,
			ViewHash = Id.GetHashCode()
		} );
	}

	protected override void OnDisabled()
	{
		if ( Renderer.IsValid() ) Renderer.Attributes.Set( "HasScopeFrame", false );
		frame?.Dispose();
		frame = null;
	}
}
