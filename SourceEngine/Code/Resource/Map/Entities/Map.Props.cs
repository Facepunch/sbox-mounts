using System;

namespace Source1;

partial class MapLoader
{
	private readonly List<Model> _propModels = [];
	private sealed partial class EntityBuilder( MapLoader owner, GameObject world, BspFile bsp, bool skybox = false )
	{
		public GameObject World { get; } = world;

		private readonly Dictionary<string, Model> models = new( StringComparer.OrdinalIgnoreCase );
		private readonly Dictionary<string, SourceModel> sourceModels = new( StringComparer.OrdinalIgnoreCase );
		private readonly GameObject parent = new( false, "props" )
		{
			Parent = world
		};
		public GameObject BuildProp( MapProp prop, MapEntity entity = null, bool animated = false )
		{
			if ( string.IsNullOrWhiteSpace( prop.Model ) || !prop.Model.EndsWith( ".mdl", StringComparison.OrdinalIgnoreCase ) )
			{
				Log.Trace( $"Prop '{prop.Name}' has no supported MDL model; omitted." );
				return null;
			}

			if ( !float.IsFinite( prop.Scale ) || prop.Scale <= 0 || prop.Skin < 0 )
				throw new InvalidDataException( $"{owner.FullPath}: prop '{prop.Name}' has invalid scale or skin." );
			if ( !float.IsFinite( prop.MassScale ) )
				throw new InvalidDataException( $"{owner.FullPath}: prop '{prop.Name}' has invalid mass scale." );
			var path = prop.Model.Replace( '\\', '/' ).TrimStart( '/' );
			Model model = null;
			if ( prop.StaticPropIndex >= 0 )
			{
				var resource = owner.Host.GetContentPath( path, owner.ContentRoot );
				if ( resource is null )
				{
					Log.Trace( $"Prop model '{path}' is missing; its instances were omitted." );
					return null;
				}

				try
				{
					if ( !sourceModels.TryGetValue( path, out var source ) )
						sourceModels.Add( path, source = owner.Host.ReadSourceModel( path, owner.ContentRoot, readLighting: true ) );
					var litModel = source.BuildStaticProp( owner.ContentRoot, prop.StaticPropIndex, prop.StaticPropFlags, bsp.Hdr && (bsp.MapFlags & 2) != 0, (bsp.MapFlags & 3) != 0, new Transform( prop.Position, Rotation.From( prop.Angles ), prop.Scale ), (prop.StaticPropFlags & 2) != 0 ? prop.LightingOrigin : null, origin => bsp.GetPropLighting( origin, owner._lighting ), owner._bakedShadowBindings, owner._vertexLitMaterials );
					if ( litModel != null )
					{
						owner._propModels.Add( litModel );
						model = litModel;
					}
				}
				catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException or InvalidOperationException )
				{
					Log.Trace( $"Prop model '{path}' could not be imported: {e.Message}; its instances were omitted." );
					return null;
				}
			}

			model ??= LoadModel( path );
			if ( model is null )
				return null;
			ReportPhysicsSettings( prop, model, path );
			var useGameplay = !skybox && CanBuildGameplayProp( prop, model, path );
			var isStatic = prop.Physics == PropPhysics.Static && !useGameplay;
			var instance = new GameObject( true, prop.Name )
			{
				Parent = parent,
				IsStatic = isStatic,
				LocalPosition = prop.Position,
				LocalRotation = Rotation.From( prop.Angles ),
				LocalScale = new Vector3( prop.Scale )
			};
			if ( isStatic )
				instance.Tags.Add( "world" );
			if ( prop.StaticPropIndex < 0 )
				instance.Tags.Add( "source1_entity" );
			if ( !skybox && prop.Solid != 0 && (prop.Gameplay || prop.Physics is PropPhysics.Rigid or PropPhysics.Ragdoll) )
				MakeNetworkRoot( instance );
			if ( entity is not null )
			{
				instance.Tags.Add( ActivatorFilter.ClassTag + entity.Get( "classname" ) );
				TrackParent( entity, instance );
				GarrysMod.PhysicsGun.TagMapEntity( instance, entity );
				// A physics prop flagged as debris, which characters walk through.
				if ( prop.Physics == PropPhysics.Rigid && (entity.Integer( "spawnflags" ) & 4) != 0 )
					instance.Tags.Add( Hl2.ZombieProps.DebrisTag );
			}
			if ( prop.EnableOnPhyscannon )
				instance.Tags.Add( Hl2.GravityWeapon.EnableMotionTag );
			var materialGroup = GetSkin( model, prop.Skin, path );
			if ( useGameplay )
			{
				AddGameplayProp( prop, instance, model, materialGroup );
				if ( entity?.Get( "OnBreak" ) is not null )
					RegisterEntityIO( entity, instance.GetOrAddComponent<PropDamage>() );
				return instance;
			}

			var ragdollRenderer = prop.Physics == PropPhysics.Ragdoll ? instance.AddComponent<SkinnedModelRenderer>() : null;
			ModelRenderer renderer = ragdollRenderer ?? (animated ? instance.AddComponent<SkinnedModelRenderer>() : instance.AddComponent<ModelRenderer>());
			renderer.Model = model;
			renderer.Tint = prop.Tint;
			renderer.Enabled = prop.Visible;
			renderer.MaterialGroup = materialGroup;
			if ( prop.StaticPropIndex >= 0 && (prop.StaticPropFlags & 0x10) != 0 )
				renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
			AddPhysics( prop, renderer, ragdollRenderer, path );
			return instance;
		}

		public void Finish()
		{
			if ( !skybox )
			{
				ParentMovers();
				if ( propDoors.Count > 1 ) LinkPropDoors();
				BuildPostProcessing();
				WireEntityIO();
			}
			parent.Enabled = true;
			foreach ( var (entity, projected) in authoredDecals )
				BuildAuthoredDecal( entity, projected );
		}

		private Model LoadModel( string path )
		{
			if ( models.TryGetValue( path, out var model ) )
				return model;
			var resource = owner.Host.GetContentPath( path, owner.ContentRoot );
			model = resource is null ? null : Model.Load( $"{resource}.vmdl" );
			if ( model is null || !model.IsValid || model.IsError )
			{
				Log.Trace( $"Prop model '{path}' is missing or could not be loaded; its instances were omitted." );
				model = null;
			}

			models.Add( path, model );
			return model;
		}

		private string GetSkin( Model model, int index, string path )
		{
			if ( index == 0 )
				return null;
			var skin = $"Skin {index}";
			var groupCount = model.MaterialGroupCount;
			for ( var i = 0; i < groupCount; i++ )
			{
				if ( model.GetMaterialGroupName( i ) == skin )
				{
					return skin;
				}
			}

			Log.Trace( $"Prop model '{path}' has no skin {index}; using the default skin." );
			return null;
		}
	}
}
