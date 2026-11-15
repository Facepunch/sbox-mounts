using System;

namespace Source1;

/// <summary>
/// Source viewmodels are authored for a separate lens. Keep the whole mounted
/// weapon and its replacement hands under that lens, without altering their poses.
/// </summary>
[Title( "Source View Model Camera" )]
[Category( "Source 1" )]
public sealed class SourceViewModelCamera : Component
{
	const string ViewTag = "hl2_viewmodel_lens";
	// Source's viewmodel_fov reference, horizontal at 4:3.
	const float ReferenceFieldOfView = 54;
	CameraComponent _worldCamera;
	CameraComponent _camera;
	IDisposable _hook;

	/// <summary>The lens-space muzzle attachment mirrored into <see cref="WorldMuzzle"/>.</summary>
	public GameObject Muzzle { get; set; }

	/// <summary>
	/// The muzzle where the world camera shows it, for tracers, projectiles and world effects.
	/// Outside the viewmodel, so nothing parented to it is drawn through the lens.
	/// </summary>
	public GameObject WorldMuzzle { get; private set; }

	/// <summary>
	/// Maps a world point drawn through the viewmodel lens (such as a viewmodel attachment)
	/// to the world point at the same screen position and view depth through the world camera,
	/// like Source's FormatViewModelAttachment.
	/// </summary>
	public Vector3 ToWorld( Vector3 point )
	{
		if ( !_camera.IsValid() || !_camera.Enabled || !_worldCamera.IsValid() ) return point;
		var world = _worldCamera;
		var eye = _camera.WorldTransform;
		var local = eye.PointToLocal( point );
		var rect = world.ScreenRect;
		var aspect = rect.Height > 0 ? rect.Width / rect.Height : Screen.Aspect;
		var lensTangent = MathF.Tan( _camera.FieldOfView.DegreeToRadian() * 0.5f );
		var worldTangent = MathF.Tan( world.View.FieldOfView.DegreeToRadian() * 0.5f );
		if ( world.FovAxis == CameraComponent.Axis.Horizontal ) worldTangent /= aspect;
		var scale = worldTangent / lensTangent;
		return eye.PointToWorld( new Vector3( local.x, local.y * scale, local.z * scale ) );
	}

	protected override void OnEnabled()
	{
		// Tags are inherited, so this also covers the hands, attachments and effects spawned later.
		GameObject.Tags.Add( ViewTag );
		_hook = Scene.AddHook( GameObjectSystem.Stage.FinishUpdate, 200, UpdateView, nameof( SourceViewModelCamera ), "Place mounted viewmodel lens" );
	}

	void UpdateView()
	{
		var viewModel = GetComponent<SourceViewModel>();
		var world = Scene.Camera;
		if ( !world.IsValid() || world == _camera || !viewModel.IsValid() || !viewModel.Renderer.IsValid() ) return;
		if ( _worldCamera != world )
		{
			ReleaseCamera();
			_worldCamera = world;
		}
		world.RenderExcludeTags.Add( ViewTag );
		if ( !_camera.IsValid() )
		{
			var go = new GameObject( GameObject, false, "Mounted viewmodel lens" );
			go.NetworkMode = NetworkMode.Never;
			_camera = go.AddComponent<CameraComponent>();
			// Main cameras render untargeted screen panels, so the HUD is redrawn above the gun.
			// Scene.Camera picks the lowest priority, so it stays the world camera.
			_camera.IsMainCamera = true;
			_camera.ClearFlags = ClearFlags.Depth | ClearFlags.Stencil;
			_camera.RenderTags.Add( ViewTag );
			_camera.FovAxis = CameraComponent.Axis.Vertical;
			// Source's 54-degree reference is horizontal at 4:3.
			_camera.FieldOfView = Screen.CreateVerticalFieldOfView( ReferenceFieldOfView, 0.75f );
			_camera.ZNear = 1;
			_camera.ZFar = 512;
			// The lens draws over the world camera's painter HUD (crosshair), so repeat it on top.
			_camera.AddCommandList( world.HudCommandList, Sandbox.Rendering.Stage.AfterPostProcess, 5000 );
			go.Enabled = true;
		}
		// A scoped sniper rifle hides the viewmodel while looking through the scope overlay.
		_camera.Enabled = world.Active && viewModel.Renderer.Enabled && !world.RenderExcludeTags.Has( "firstperson" ) && !GameObject.Tags.Has( "sniper_scoped" );
		_camera.Priority = world.Priority + 1;
		_camera.Viewport = world.Viewport;
		var view = world.View;
		var effects = CameraEffectSystem.Get( Scene );
		if ( effects is not null )
		{
			effects.QueryOffsets( world, out var offset, out var angles, out _ );
			view.Position += view.Rotation * offset;
			view.Rotation *= angles;
		}
		_camera.WorldTransform = view.Transform;
		if ( !Muzzle.IsValid() ) return;
		if ( !WorldMuzzle.IsValid() )
		{
			WorldMuzzle = new GameObject( GameObject.Parent, true, "Mounted viewmodel world muzzle" );
			WorldMuzzle.NetworkMode = NetworkMode.Never;
			WorldMuzzle.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		}
		WorldMuzzle.WorldTransform = new Transform( ToWorld( Muzzle.WorldPosition ), Muzzle.WorldRotation );
	}

	void ReleaseCamera()
	{
		if ( _worldCamera.IsValid() ) _worldCamera.RenderExcludeTags.Remove( ViewTag );
		if ( _camera.IsValid() ) _camera.GameObject.Destroy();
		_camera = null;
		_worldCamera = null;
	}

	protected override void OnDisabled()
	{
		_hook?.Dispose();
		_hook = null;
		ReleaseCamera();
		WorldMuzzle?.Destroy();
		WorldMuzzle = null;
		GameObject.Tags.Remove( ViewTag );
	}
}
