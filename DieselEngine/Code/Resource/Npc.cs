using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace Diesel;

sealed class PaydayNpcPrefabLoader( NpcDefinition definition ) : ResourceLoader<PaydayMount>
{
	PrefabFile prefab;
	protected override async Task<object> LoadAsync()
	{
		var model = await ResourceLibrary.LoadAsync<Model>( $"mount://payday/{definition.Model}.vmdl" );
		if ( model is null || model.IsError ) throw new InvalidDataException( $"Missing skeletal model for {definition.Id}." );
		GameObject weapon = null;
		if ( definition.Weapon is { } id )
		{
			string path = $"mount://payday/{WeaponDefinition.Get( id ).Root}";
			await ResourceLibrary.LoadAsync<PrefabFile>( path );
			weapon = GameObject.GetPrefab( path );
		}
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", definition.Title )
			.WithMetadata( "Group", Host.Title ).WithMetadata( "PreviewModel", $"mount://payday/{definition.Model}.vmdl" );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, definition.Title );
			root.Tags.Add( "npc" );
			var renderer = new GameObject( true, "Character" ) { Parent = root }.AddComponent<SkinnedModelRenderer>();
			renderer.Model = model; renderer.UseAnimGraph = false; renderer.CreateBoneObjects = true;
			BaseNpc npc;
			if ( definition.Civilian ) npc = root.AddComponent<PaydayCivilian>();
			else
			{
				var combat = root.AddComponent<PaydayCombatNpc>();
				combat.InitiallyHostile = definition.Id == "swat";
				combat.WeaponChoices = [weapon];
				combat.IdleBehaviour = BaseCombatNpc.IdleBehaviours.HoldPost;
				npc = combat;
			}
			npc.Renderer = renderer; npc.DisplayName = definition.Title; npc.Health = definition.Health; npc.MaxHealth = definition.Health;
			root.AddComponent<AnimationLayer>();
			var senses = root.AddComponent<SensesLayer>(); senses.SightRange = 1600; senses.HearingRange = 1200; senses.FieldOfView = 140;
			root.AddComponent<NavigationLayer>();
			var agent = root.AddComponent<NavMeshAgent>(); agent.Radius = 12; agent.Height = 70;
			var capsule = root.AddComponent<CapsuleCollider>(); capsule.Radius = 12; capsule.Start = new Vector3( 0, 0, 12 ); capsule.End = new Vector3( 0, 0, 58 );
			var body = root.AddComponent<Rigidbody>(); body.MotionEnabled = false;
			var animation = root.AddComponent<PaydayNpcAnimator>(); animation.CharacterId = definition.Id; animation.Renderer = renderer;
		}
		return prefab = builder.Create();
	}
	protected override void Shutdown() { PrefabBuilder.Destroy( prefab ); prefab = null; base.Shutdown(); }
}

sealed class PaydayAlarmPrefabLoader : ResourceLoader<PaydayMount>
{
	PrefabFile prefab;
	const string Unit = "units/world/props/office/desk_accessories/desk_phone_02";
	protected override async Task<object> LoadAsync()
	{
		var model = await ResourceLibrary.LoadAsync<Model>( $"mount://payday/{Unit}.unit.vmdl" );
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", "Alarm phone" ).WithMetadata( "Group", Host.Title )
			.WithMetadata( "PreviewModel", $"mount://payday/{Unit}.unit.vmdl" );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, "Alarm phone" );
			root.AddComponent<ModelRenderer>().Model = model;
			var collider = root.AddComponent<BoxCollider>(); collider.Center = model.Bounds.Center; collider.Scale = model.Bounds.Size;
			var alarm = root.AddComponent<PaydayAlarm>();
			var lamp = new GameObject( false, "Alarm indicator" ) { Parent = root };
			lamp.LocalPosition = model.Bounds.Center + Vector3.Up * 5;
			alarm.Indicator = lamp.AddComponent<PointLight>();
			alarm.Indicator.LightColor = Color.Red * 3;
			alarm.Indicator.Radius = 80;
			alarm.Indicator.Enabled = false; lamp.Enabled = true;
		}
		return prefab = builder.Create();
	}
	protected override void Shutdown() { PrefabBuilder.Destroy( prefab ); prefab = null; base.Shutdown(); }
}
