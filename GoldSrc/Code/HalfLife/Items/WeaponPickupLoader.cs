namespace GoldSrc.HalfLife;

internal sealed class WeaponPickupLoader( TypeDescription type, string modelPath, string previewPath, string iconPath ) : ResourceLoader<GameMount>
{
	PrefabFile _prefab;
	internal WeaponCrosshairs Crosshairs => field ??= new( Host, System.IO.Path.GetFileNameWithoutExtension( Path ) );
	string ModelPath => $"mount://{Host.Ident}/{modelPath}.vmdl";

	internal void BindPresentation( StudioWeapon weapon, SkinnedModelRenderer renderer )
	{
		if ( weapon.DisplayIcon is not { IsError: false } && Texture.Load( iconPath ) is { IsError: false } icon )
			weapon.DisplayIcon = icon;

		if ( renderer.IsValid() && renderer.Model is not { IsError: false } )
		{
			renderer.Model = Model.Load( ModelPath );
			weapon.PosePickup( renderer );
		}
	}

	protected override object Load()
	{
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", type.Title ).WithMetadata( "PreviewModel", $"mount://{Host.Ident}/{previewPath}.vmdl" );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, type.Title );
			root.NetworkMode = NetworkMode.Object;
			root.Tags.Add( "removable" );
			root.Tags.Add( "nocollide_props" );
			var renderer = root.AddComponent<SkinnedModelRenderer>();
			var weapon = (StudioWeapon)root.Components.Create( type );
			BindPresentation( weapon, renderer );
			var hull = root.AddComponent<BoxCollider>();
			hull.Scale = renderer.Model.Bounds.Size;
			hull.Center = renderer.Model.Bounds.Center;
			root.AddComponent<Rigidbody>();
			weapon.SetupPickup();
		}
		return _prefab = builder.Create();
	}

	internal static bool Drop<T>( Transform transform, bool toss = false ) where T : StudioWeapon
	{
		var definition = Game.TypeLibrary.GetType( typeof( T ) ).GetAttribute<WeaponPickupAttribute>();
		var prefab = GameObject.GetPrefab( $"mount://hl1/{definition.PrefabPath}" );
		if ( !prefab.IsValid() ) return false;
		var weapon = prefab.Clone( transform );
		var body = weapon.GetComponent<Rigidbody>();
		ApplyDropVelocity( body, toss );
		weapon.NetworkSpawn( true, null );
		return true;
	}

	internal static void ApplyDropVelocity( Rigidbody body, bool toss )
	{
		if ( toss ) body.Velocity = new Vector3( Game.Random.Float( -100, 100 ), Game.Random.Float( -100, 100 ), Game.Random.Float( 200, 300 ) );
		body.AngularVelocity = Vector3.Up * (toss ? Game.Random.Float( 200, 400 ) : Game.Random.Float( 0, 100 )).DegreeToRadian();
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( _prefab );
		_prefab = null;
		base.Shutdown();
	}
}
