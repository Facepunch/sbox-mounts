namespace Diesel;

sealed class PaydayAmmoLoader : ResourceLoader<PaydayMount>
{
	protected override object Load() => new BaseAmmoResource { Title = "AMCAR-4 ammunition", MaxReserve = 100 };
}

sealed class PaydayWeaponPrefabLoader( bool view = false, bool world = false ) : ResourceLoader<PaydayMount>
{
	PrefabFile prefab;

	protected override async Task<object> LoadAsync()
	{
		bool part = view || world;
		string unit = view ? PaydayWeapons.ViewUnit : PaydayWeapons.WorldUnit;
		var model = await ResourceLibrary.LoadAsync<Model>( $"mount://payday/{unit}.unit.vmdl" );
		if ( model is null || model.IsError ) throw new InvalidDataException( $"Missing weapon geometry: {unit}" );
		var objectData = UnitModel.Xml( Host.Database.Find( "object", unit ) );
		string orientation = objectData.Element( "diesel" ).Attribute( "orientation_object" );
		var modelData = new ModelFile( Host.Database.Find( "model", unit ) );
		var muzzle = modelData.ReadAttachment( IdString.Hash( "fire" ), IdString.Hash( orientation ) )
			?? throw new InvalidDataException( $"Missing weapon fire attachment: {unit}" );
		BaseAmmoResource ammo = null;
		var sounds = new List<SoundEvent>();
		if ( part )
			foreach ( var sound in PaydayWeaponSoundLoader.Events )
				sounds.Add( await ResourceLibrary.LoadAsync<SoundEvent>( $"mount://payday/{PaydayWeaponSoundLoader.EventPath( sound )}" )
					?? throw new InvalidDataException( $"Missing AMCAR sound {sound}." ) );
		if ( view ) _ = Host.ViewAssets;
		if ( !part )
		{
			ammo = await ResourceLibrary.LoadAsync<BaseAmmoResource>( $"mount://payday/{PaydayWeapons.Ammo}" );
			await ResourceLibrary.LoadAsync<PrefabFile>( $"mount://payday/{PaydayWeapons.View}" );
			await ResourceLibrary.LoadAsync<PrefabFile>( $"mount://payday/{PaydayWeapons.World}" );
			if ( ammo is null ) throw new InvalidDataException( "Missing AMCAR ammunition resource." );
		}
		var builder = new PrefabBuilder().WithName( Path );
		if ( !part )
			builder.WithMetadata( "Title", "AMCAR-4" ).WithMetadata( "Group", Host.Title )
				.WithMetadata( "Description", "PAYDAY: The Heist AMCAR-4 — playable prototype" )
				.WithMetadata( "PreviewModel", $"mount://payday/{unit}.unit.vmdl" );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, part ? "AMCAR-4 model" : "AMCAR-4" );
			var visual = new GameObject( true, "Weapon geometry" ) { Parent = root };
			var renderer = visual.AddComponent<ModelRenderer>();
			renderer.Model = model;
			BaseWeaponModel presentation;
			if ( part )
			{
				var payday = visual.AddComponent<PaydayWeaponModel>();
				payday.AnimatedView = view;
				payday.Geometry = renderer;
				payday.Sounds = sounds;
				presentation = payday;
			}
			else presentation = visual.AddComponent<BaseWeaponModel>();
			presentation.EjectBrass = null;
			presentation.MuzzleGameObject = new GameObject( true, "Muzzle" ) { Parent = visual,
				LocalTransform = muzzle };
			if ( view )
			{
				root.Tags.Add( "firstperson", "viewmodel" );
				renderer.RenderOptions.Game = false;
				renderer.RenderOptions.Overlay = true;
			}
			if ( !part )
			{
				var weapon = root.AddComponent<PaydayWeapon>();
				PaydayWeapons.Configure( weapon );
				weapon.DroppedModel = visual;
				weapon.PrimaryAmmoType = ammo;
				weapon.ViewModelPrefab = GameObject.GetPrefab( $"mount://payday/{PaydayWeapons.View}" );
				weapon.WorldModelPrefab = GameObject.GetPrefab( $"mount://payday/{PaydayWeapons.World}" );
				var collider = root.AddComponent<BoxCollider>();
				collider.Center = model.Bounds.Center;
				collider.Scale = model.Bounds.Size;
				root.AddComponent<Rigidbody>().MassOverride = 2.5f;
			}
		}
		return prefab = builder.Create() ?? throw new InvalidDataException( "Could not build AMCAR prefab." );
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( prefab );
		prefab = null;
		base.Shutdown();
	}
}
