using Sandbox.Npcs.Layers;

namespace GoldSrc.HalfLife;

internal sealed class NpcPrefabLoader( TypeDescription component, NpcDefinitionAttribute definition, string model, string projectile, string edition = null ) : ResourceLoader<GameMount>
{
	PrefabFile _prefab;

	protected override object Load()
	{
		var modelPath = $"mount://{Host.Ident}/{model}.vmdl";
		var builder = new PrefabBuilder().WithName( Path )
			.WithMetadata( "Title", edition is null ? component.Title : $"{component.Title} ({edition})" )
			.WithMetadata( "Group", Host.Title )
			.WithMetadata( "PreviewModel", modelPath );

		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, component.Title );
			root.Tags.Add( "npc" );
			root.NetworkMode = NetworkMode.Object;

			var entry = root.Components.Create( component );
			var pose = (entry as PosedCorpse)?.PoseName ?? definition.IdleSequence;
			if ( pose is not null ) builder.WithMetadata( "PreviewSequence", pose );
			if ( entry is Npc npc ) ConfigureNpc( root, npc, modelPath );
			else if ( entry is GruntRepel rappel && edition == "SD" )
				rappel.GruntPrefab = rappel.GruntPrefab.Replace( "/npcs/", "/npcs/sd/" );
			else if ( entry is PosedCorpse corpse )
			{
				root.Tags.Add( "ragdoll", "removable" );
				corpse.ModelPath = modelPath;
				corpse.Renderer = root.AddComponent<SkinnedModelRenderer>();
				corpse.Renderer.Model = Model.Load( modelPath );
				corpse.Renderer.UseAnimGraph = true;
				var hitboxes = root.AddComponent<ModelHitboxes>();
				hitboxes.Renderer = corpse.Renderer;
				hitboxes.Target = root;
				CorpseDamage.Attach( root, corpse.Renderer, definition.Health * 0.5f, definition.Gibs == GibSet.None ? GibSet.Human : definition.Gibs,
					definition.Gibs == GibSet.None ? Npc.BloodColor.Red : definition.Blood );
			}
		}

		return _prefab = builder.Create();
	}

	void ConfigureNpc( GameObject root, Npc npc, string modelPath )
	{
		var visuals = new GameObject( root, true, "model" );
		var renderer = visuals.AddComponent<SkinnedModelRenderer>();
		renderer.Model = Model.Load( modelPath );
		renderer.UseAnimGraph = true;
		renderer.CreateAttachments = true;
		var hitboxes = visuals.AddComponent<ModelHitboxes>();
		hitboxes.Renderer = renderer;
		hitboxes.Target = root;

		var hull = new GameObject( root, true, "collider" );
		hull.Tags.Add( "playercontroller" );
		var collider = hull.AddComponent<BoxCollider>();
		collider.Center = Vector3.Up * (definition.Height * 0.5f);
		collider.Scale = new Vector3( definition.Radius * 2, definition.Radius * 2, definition.Height );
		var body = root.AddComponent<Rigidbody>();
		body.MotionEnabled = false;
		body.EnableImpactDamage = false;
		body.Locking = new PhysicsLock { Pitch = true, Roll = true };
		body.Gravity = definition.Movement == NpcMovement.Ground;

		if ( definition.Movement == NpcMovement.Ground )
		{
			var agent = root.AddComponent<NavMeshAgent>();
			agent.Radius = definition.Radius;
			agent.Height = definition.Height;
			agent.Acceleration = 2000;
			root.AddComponent<NavigationLayer>().RunSpeed = definition.Speed;
		}
		else if ( definition.Movement is NpcMovement.Flying or NpcMovement.Swimming )
		{
			var flight = root.AddComponent<FlyingLayer>();
			flight.Bounds = definition.Movement == NpcMovement.Swimming ? FlyingLayer.FlyingBounds.WaterOnly : FlyingLayer.FlyingBounds.Anywhere;
			flight.HoverHeight = definition.Height;
			flight.RunSpeed = definition.Speed;
		}

		var senses = root.AddComponent<SensesLayer>();
		senses.SightRange = npc.SightRange;
		senses.HearingRange = 700;
		senses.FieldOfView = 2 * System.MathF.Acos( npc.ViewDot ).RadianToDegree();
		root.AddComponent<SpeechLayer>();
		var animation = root.AddComponent<AnimationLayer>();
		animation.EnableFootstepSounds = false;

		npc.ClassName = definition.ClassName;
		npc.ModelPath = modelPath;
		npc.ProjectilePath = projectile is null ? null : $"mount://{Host.Ident}/{projectile}.vmdl";
		npc.DisplayName = component.Title;
		npc.Renderer = renderer;
		npc.Animation = animation;
		npc.Health = definition.Health;
		npc.EyeHeight = definition.Height * 0.8f;
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( _prefab );
		_prefab = null;
		base.Shutdown();
	}
}
