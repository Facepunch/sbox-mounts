using System;

namespace Source1;

/// <summary>Resolves local mounted art after loading, without baking mount URLs into prefab dependencies.</summary>
[Title( "Source Mounted Model" )]
[Category( "Source 1" )]
[Alias( "Source1.SourceMountedModel", "SourceMountedModel" )]
public sealed class MountedModel : Component
{
	[Property] public string MountName { get; set; } = "hl2";
	/// <summary>Source filename relative to the selected mount, such as models/police.mdl.</summary>
	[Property] public string ModelPath { get; set; }
	/// <summary>Used when the mount lacks <see cref="ModelPath"/>, such as a game's own viewmodel when an optional handless one is missing.</summary>
	[Property] public string FallbackModelPath { get; set; }
	[Property] public SkinnedModelRenderer Renderer { get; set; }
	[Property] public ModelCollider Collider { get; set; }
	bool _unavailable;
	BoxCollider _bounds;

	/// <summary>The model this mount provides: <see cref="ModelPath"/>, else <see cref="FallbackModelPath"/>.</summary>
	public string ResolvedModelPath => string.IsNullOrEmpty( FallbackModelPath )
		|| Sandbox.Mounting.Directory.Get( MountName )?.GetByPath( $"mount://{MountName}/{ModelPath}.vmdl" ) is not null ? ModelPath : FallbackModelPath;

	protected override void OnStart() => ChangeModel( ModelPath );

	internal async void ChangeModel( string requested )
	{
		ModelPath = requested;
		if ( !Renderer.IsValid() || string.IsNullOrEmpty( requested ) ) return;
		_unavailable = false;
		Renderer.Enabled = false;
		try
		{
			var mount = await Sandbox.Mounting.Directory.Mount( MountName );
			if ( !IsValid || requested != ModelPath ) return;
			if ( mount?.IsMounted != true )
			{
				_unavailable = true;
				return;
			}
			var path = $"mount://{MountName}/{ResolvedModelPath}.vmdl";
			var model = await Model.LoadAsync( path );
			if ( !IsValid || !Renderer.IsValid() || requested != ModelPath ) return;
			if ( !model.IsValid() || model.IsError )
			{
				_unavailable = true;
				return;
			}
			Renderer.Model = model;
			if ( Collider.IsValid() ) AssignCollider( model );
			Renderer.Enabled = true;
			// World models exist on the host as well as the owning client. Derive
			// gameplay reload timing there, not just on the owner-only viewmodel.
			var weapon = GetComponentInParent<BaseCombatWeapon>();
			if ( weapon.IsValid() ) await WeaponSetup.ConfigureWeapon( weapon );
		}
		catch ( Exception e )
		{
			if ( !IsValid || requested != ModelPath ) return;
			_unavailable = true;
			Log.Warning( $"Couldn't load {MountName} model '{ModelPath}': {e.Message}" );
		}
	}

	/// <summary>
	/// Source draws some models, such as the SMG grenade, without a collision model; Source's item code
	/// gives those a bounding box, so they get one from their render bounds.
	/// </summary>
	void AssignCollider( Model model )
	{
		Collider.Model = model;
		if ( model.Physics?.Parts.Count > 0 ) return;
		_bounds = Collider.GameObject.AddComponent<BoxCollider>();
		_bounds.Center = model.Bounds.Center;
		_bounds.Scale = model.Bounds.Size;
	}

	protected override void OnUpdate()
	{
		// Pickups and dropped weapons switch their collider off while loading or held.
		if ( _bounds.IsValid() ) _bounds.Enabled = Collider.Enabled;
		if ( _unavailable )
			DebugOverlay.Text( WorldPosition + Vector3.Up * 32, $"{MountName} content required", color: Color.White );
	}
}
