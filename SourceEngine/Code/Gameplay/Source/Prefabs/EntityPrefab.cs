using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace Source1;

/// <summary>Common object layouts used by the mounted games' prefab definitions.</summary>
internal sealed class EntityPrefab( GameObject root, SourceGameMount mount, EntityDefinition entity )
{
	SourceGameMount Mount => mount;
	public GameObject Root => root;
	public SkinnedModelRenderer Renderer { get; private set; }
	public MountedModel Model { get; private set; }

	public static IEnumerable<EntityDefinition> WeaponSet<T>( string game, string name, string title, string worldModel, string viewModel,
		Action<T> configure, Action<ViewModel> configureView = null, string fallback = null, string skin = null, bool worldEffects = false ) where T : Weapon, new()
	{
		string view = $"{name}_vm", world = $"{name}_wm";
		var viewPath = $"entities/parts/{view}.prefab";
		var worldPath = $"entities/parts/{world}.prefab";
		yield return new( game, EntityKind.Weapon, name, title, worldModel, null )
		{
			RequiredPrefabs = [viewPath, worldPath],
			Build = p =>
			{
				var weapon = p.MakeWeapon<T>( skin );
				weapon.ViewModelPrefab = p.Prefab( viewPath );
				weapon.WorldModelPrefab = p.Prefab( worldPath );
				configure( weapon );
				var script = p.Mount.FindWeaponScript( viewModel, worldModel, fallback, $"weapon_{name}" );
				if ( script?.DroppedModel is { } dropped )
				{
					p.Model.ModelPath = dropped;
					p.Model.FallbackModelPath = worldModel;
				}
				WeaponSetup.ApplyInventoryData( weapon, script );
			}
		};
		yield return new( game, EntityKind.ViewModel, view, null, null, null )
		{
			ModelPath = viewModel, FallbackModel = fallback,
			Build = p =>
			{
				var view = p.MakeViewModel();
				if ( p.Mount.FindWeaponScript( viewModel, worldModel, fallback, $"weapon_{name}" ) is { } script )
				{
					view.WeaponClass = script.ClassName;
					view.FieldOfView = script.ViewModelFov ?? view.FieldOfView;
					var offset = script.ViewModelOffset;
					view.PositionOffset = new( offset.x, -offset.y, offset.z );
				}
				configureView?.Invoke( view );
			}
		};
		yield return new( game, EntityKind.WorldModel, world, null, null, null )
		{
			ModelPath = worldModel, Build = p => p.MakeWorldModel( skin, worldEffects )
		};
	}

	GameObject Prefab( string path ) => GameObject.GetPrefab( $"mount://{mount.Ident}/{path}" );

	public List<GameObject> WeaponChoices( params string[] names )
	{
		var result = new List<GameObject>();
		foreach ( var name in names )
		{
			var path = $"entities/weapons/{name}.prefab";
			if ( mount.GetByPath( $"mount://{mount.Ident}/{path}" ) is not null ) result.Add( Prefab( path ) );
		}
		return result;
	}

	public BaseCombatWeapon Equip( string name )
	{
		root.GetOrAddComponent<BaseNpcInventory>();
		var weapon = Prefab( $"entities/weapons/{name}.prefab" ).Clone( new CloneConfig { Parent = root, Transform = Transform.Zero, StartEnabled = true } );
		return weapon.GetComponent<BaseCombatWeapon>( true );
	}

	public void AddModel( GameObject target, bool bones = false )
	{
		Renderer = target.AddComponent<SkinnedModelRenderer>();
		Renderer.UseAnimGraph = false;
		if ( bones )
		{
			Renderer.CreateAttachments = true;
			Renderer.CreateBoneObjects = true;
			Renderer.Sequence.Blending = true;
		}
		Model = target.AddComponent<MountedModel>();
		Model.MountName = mount.Ident;
		Model.ModelPath = entity.ModelPath;
		Model.FallbackModelPath = entity.FallbackModel;
		Model.Renderer = Renderer;
	}

	T MakeWeapon<T>( string skin ) where T : Weapon, new()
	{
		root.NetworkMode = NetworkMode.Object;
		var weapon = root.AddComponent<T>();
		if ( weapon is BulletWeapon )
		{
			weapon.CanCancelReload = false;
			weapon.DeployTime = 0.2f;
			weapon.PreferredSlot = 0;
			weapon.SecondaryDelay = 1;
			weapon.Value = 10;
		}
		var collider = root.AddComponent<ModelCollider>();
		root.AddComponent<Rigidbody>();
		var dropped = new GameObject( root, true, "standalone_wm" );
		var model = dropped.AddComponent<BaseWeaponModel>();
		AddModel( dropped, bones: true );
		Renderer.Enabled = false;
		if ( skin is not null ) Renderer.MaterialGroup = skin;
		Model.Collider = collider;
		model.EjectBrass = model.TracerEffect = null;
		model.Renderer = Renderer;
		weapon.DroppedGameObject = dropped;
		return weapon;
	}

	ViewModel MakeViewModel()
	{
		root.NetworkMode = NetworkMode.Never;
		var view = root.AddComponent<ViewModel>();
		AddModel( new GameObject( root, true, "Viewmodel" ), bones: true );
		Renderer.RenderOptions.Game = false;
		// The viewmodel enables the overlay when it initializes its first pose.
		Renderer.RenderOptions.Overlay = false;
		Renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		view.Renderer = Renderer;
		view.MuzzleEffect = view.TracerEffect = null;
		return view;
	}

	void MakeWorldModel( string skin, bool effects )
	{
		root.NetworkMode = NetworkMode.Never;
		var world = root.AddComponent<WeaponWorldModel>();
		AddModel( new GameObject( root, true, "model" ), bones: true );
		if ( skin is not null ) Renderer.MaterialGroup = skin;
		world.Renderer = Renderer;
		world.EjectBrass = null;
		if ( !effects ) world.MuzzleEffect = world.TracerEffect = null;
	}

	public T Npc<T>() where T : BaseNpc, new()
	{
		root.Tags.Add( "npc" );
		root.AddComponent<AnimationLayer>();
		root.AddComponent<SpeechLayer>();
		var senses = root.AddComponent<SensesLayer>();
		senses.FieldOfView = 300;
		senses.HearingRange = 1200;
		senses.SightRange = 2048;
		var body = root.AddComponent<Rigidbody>();
		body.AngularDamping = 2;
		body.EnableImpactDamage = false;
		body.LinearDamping = 0.5f;
		body.Locking = new() { Pitch = true, Roll = true };
		body.MotionEnabled = false;
		var npc = root.AddComponent<T>();
		var model = new GameObject( root, true, "model" );
		AddModel( model, bones: true );
		Renderer.MaterialGroup = "default";
		npc.Renderer = Renderer;
		npc.DisplayName = entity.Title;
		var hitboxes = model.AddComponent<ModelHitboxes>();
		hitboxes.Renderer = Renderer;
		hitboxes.Target = root;
		return npc;
	}

	public void Navigation( float radius = 13, float height = 72, float acceleration = 2000, float? run = null, float? walk = null, bool opensDoors = false )
	{
		var agent = root.AddComponent<NavMeshAgent>();
		agent.Radius = radius;
		agent.Height = height;
		agent.Acceleration = acceleration;
		var navigation = root.AddComponent<DoorNavigation>();
		navigation.OpensDoors = opensDoors;
		if ( run.HasValue ) navigation.RunSpeed = run.Value;
		if ( walk.HasValue ) navigation.WalkSpeed = walk.Value;
	}

	public NpcDefaults Skill( string health, string damage = null, string playerDamage = null, string config = null )
	{
		var defaults = root.AddComponent<NpcDefaults>();
		defaults.HealthSetting = health;
		defaults.DamageSetting = damage;
		defaults.PlayerDamageSetting = playerDamage;
		defaults.AdditionalConfig = config;
		return defaults;
	}

	public void Box( float width, float height ) => BoxAt( Vector3.Up * (height * 0.5f), new( width, width, height ) );

	public GameObject ColliderObject()
	{
		var collider = new GameObject( root, true, "collider" );
		collider.Tags.Add( "playercontroller" );
		return collider;
	}

	public void BoxAt( Vector3 center, Vector3 scale )
	{
		var box = ColliderObject().AddComponent<BoxCollider>();
		box.Center = center;
		box.Scale = scale;
	}

	public T Effect<T>( float lifetime ) where T : Component, new()
	{
		var effect = root.AddComponent<TemporaryEffect>();
		effect.BecomeOrphan = true;
		effect.DestroyAfterSeconds = lifetime;
		var component = root.AddComponent<T>();
		if ( component is MountedEffect particle ) particle.MountName = mount.Ident;
		if ( component is ExplosionEffect explosion ) explosion.MountName = mount.Ident;
		return component;
	}
}
