using System;

namespace Overgrowth;

[Library( "overgrowth_turner" )]
public sealed class RabbitGuard : Component
{
	internal const string ModelName = "entities/npcs/turner";
	internal const string ActorPath = "Data/Objects/IGF_Characters/IGF_TurnerActor.xml";
	internal static readonly string[] Poses = ["r_idle", "r_dialogue_shade", "r_combatidlethreat"];

	[Property] public SkinnedModelRenderer Renderer { get; set; }
	[Sync] public int AlertStage { get; private set; }
	TimeUntil nextVisionCheck = 1f;
	float enemySeen;
	int playing = -1;

	protected override void OnUpdate()
	{
		if ( !Renderer.IsValid() || Renderer.Model is null ) return;
		if ( Renderer.Animation.Graph is null )
		{
			var mount = Sandbox.Mounting.Directory.Get( "overgrowth" );
			if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is not CharacterLoader loader || loader.GuardGraph is null ) return;
			Renderer.Animation.Graph = loader.GuardGraph;
			playing = -1;
		}
		if ( !IsProxy && nextVisionCheck <= 0 )
		{
			nextVisionCheck = Random.Shared.Float( 0.2f, 0.3f );
			UpdateAwareness();
		}
		if ( playing == AlertStage ) return;
		playing = AlertStage;
		Renderer.Animation.Parameters.Set( "stance", Poses[playing] );
	}

	// enemycontrol.as accumulates sightings on vision checks and fades by 0.05 per missed check.
	void UpdateAwareness()
	{
		var eye = WorldPosition + WorldRotation.Up * (Renderer.Model.Bounds.Maxs.z * 0.8f);
		float gain = 0;
		foreach ( var player in Scene.GetAllComponents<PlayerController>() )
		{
			var target = player.EyeTransform.Position;
			var delta = target - eye;
			var distance = delta.Length;
			var direction = WorldRotation.Inverse * delta;
			var angles = direction.EulerAngles.Normal;
			bool focus = distance <= 4000 && MathF.Abs( angles.yaw ) <= 36 && MathF.Abs( angles.pitch ) <= 36;
			bool peripheral = distance <= 160 && MathF.Abs( angles.yaw ) <= 81 && MathF.Abs( angles.pitch ) <= 81;
			if ( !focus && !peripheral ) continue;
			if ( Scene.Trace.Ray( eye, target ).IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( player.GameObject ).WithoutTags( "trigger" ).Run().Hit ) continue;
			gain += 0.5f - 0.4f * distance / 4000f;
		}
		enemySeen = Math.Clamp( enemySeen + (gain > 0 ? gain : -0.05f), 0, 1 );
		AlertStage = enemySeen >= 1 ? 2 : enemySeen > 0 ? 1 : 0;
	}
}

internal sealed class RabbitGuardPrefab : ResourceLoader<GameMount>
{
	PrefabFile prefab;

	protected override object Load()
	{
		var modelPath = Host.MountedPath( RabbitGuard.ModelName ) + ".vmdl";
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", "Turner" )
			.WithMetadata( "Group", Host.Title ).WithMetadata( "PreviewModel", modelPath );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var go = new GameObject( true, "Turner" );
			Configure( go, Model.Load( modelPath ) );
		}
		return prefab = builder.Create();
	}

	internal static void Configure( GameObject go, Model model )
	{
		go.Tags.Add( "npc" );
		go.NetworkMode = NetworkMode.Object;
		var renderer = go.AddComponent<SkinnedModelRenderer>();
		renderer.Model = model;
		renderer.UseAnimGraph = true;
		go.AddComponent<RabbitGuard>().Renderer = renderer;
		var collider = go.AddComponent<CapsuleCollider>();
		collider.Radius = 0.45f * 40f;
		collider.Start = Vector3.Up * (model.Bounds.Mins.z + collider.Radius);
		collider.End = Vector3.Up * (model.Bounds.Maxs.z - collider.Radius);
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( prefab );
		prefab = null;
	}
}
