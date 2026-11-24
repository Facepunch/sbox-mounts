namespace Diesel;

sealed class PaydayAmmoLoader( WeaponDefinition definition ) : ResourceLoader<PaydayMount>
{
	protected override object Load() => new BaseAmmoResource { Title = $"{definition.Title} ammunition", MaxReserve = definition.Reserve };
}

sealed class PaydayWeaponPrefabLoader( WeaponDefinition definition, bool view = false, bool world = false ) : ResourceLoader<PaydayMount>
{
	PrefabFile prefab;

	protected override async Task<object> LoadAsync()
	{
		bool part = view || world;
		string unit = view ? definition.ViewUnit : definition.WorldUnit;
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
			foreach ( var sound in Host.WeaponAudio[definition.Id].Events.Keys )
				sounds.Add( await ResourceLibrary.LoadAsync<SoundEvent>( $"mount://payday/{PaydayWeaponSoundLoader.EventPath( sound )}" )
					?? throw new InvalidDataException( $"Missing weapon sound {sound}." ) );
		if ( view ) _ = Host.GetViewAssets( definition.Id );
		if ( !part )
		{
			ammo = await ResourceLibrary.LoadAsync<BaseAmmoResource>( $"mount://payday/{definition.Ammo}" );
			await ResourceLibrary.LoadAsync<PrefabFile>( $"mount://payday/{definition.View}" );
			await ResourceLibrary.LoadAsync<PrefabFile>( $"mount://payday/{definition.WorldPrefab}" );
			if ( ammo is null ) throw new InvalidDataException( $"Missing {definition.Title} ammunition resource." );
			if ( GameObject.GetPrefab( $"mount://payday/{definition.View}" ) is null || GameObject.GetPrefab( $"mount://payday/{definition.WorldPrefab}" ) is null )
				throw new InvalidDataException( $"Missing {definition.Title} presentation prefab." );
		}
		var builder = new PrefabBuilder().WithName( Path );
		if ( !part )
			builder.WithMetadata( "Title", definition.Title ).WithMetadata( "Group", Host.Title )
				.WithMetadata( "Description", $"PAYDAY: The Heist {definition.Title}" )
				.WithMetadata( "PreviewModel", $"mount://payday/{unit}.unit.vmdl" );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, part ? $"{definition.Title} model" : definition.Title );
			var visual = new GameObject( true, "Weapon geometry" ) { Parent = root };
			var renderer = visual.AddComponent<ModelRenderer>();
			renderer.Model = model;
			BaseWeaponModel presentation;
			if ( part )
			{
				var payday = visual.AddComponent<PaydayWeaponModel>();
				payday.AnimatedView = view;
				payday.WeaponId = definition.Id;
				payday.SoundNames = Host.WeaponAudio[definition.Id].Events.Keys.ToList();
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
				PaydayWeapons.Configure( weapon, definition );
				weapon.DroppedModel = visual;
				weapon.PrimaryAmmoType = ammo;
				weapon.ViewModelPrefab = GameObject.GetPrefab( $"mount://payday/{definition.View}" );
				weapon.WorldModelPrefab = GameObject.GetPrefab( $"mount://payday/{definition.WorldPrefab}" );
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
