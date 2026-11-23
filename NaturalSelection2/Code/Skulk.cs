using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace NS2;

internal sealed class SkulkPrefab : ResourceLoader<GameMount>
{
	const string ModelPath = "mount://ns2/ns2/models/alien/skulk/skulk.model.vmdl";
	const string GraphPath = "mount://ns2/ns2/models/alien/skulk/skulk.animation_graph.ag";
	PrefabFile prefab;

	internal static void Register( MountContext context )
	{
		var loader = new SkulkPrefab();
		loader.Tags.Add( "npc" );
		context.Add( ResourceType.PrefabFile, "entities/npcs/skulk.prefab", loader );
	}

	protected override object Load()
	{
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", "Skulk" )
			.WithMetadata( "Group", Host.Title ).WithMetadata( "PreviewModel", ModelPath )
			.WithMetadata( "PreviewSequence", "idle" );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, "Skulk" ) { NetworkMode = NetworkMode.Object };
			root.Tags.Add( "npc", "removable" );
			var renderer = new GameObject( root, true, "Model" ).AddComponent<SkinnedModelRenderer>();
			renderer.Model = Model.Load( ModelPath );
			renderer.UseAnimGraph = true;
			renderer.Animation.Graph = Sandbox.Animation.Graph.Load( GraphPath );
			root.AddComponent<ModelHitboxes>().Renderer = renderer;
			var hull = new GameObject( root, true, "Collider" );
			hull.Tags.Add( "playercontroller" );
			var box = hull.AddComponent<BoxCollider>();
			box.Center = Vector3.Up * 18;
			box.Scale = new Vector3( 36 );
			var body = root.AddComponent<Rigidbody>();
			body.MotionEnabled = false;
			body.EnableImpactDamage = false;
			body.Locking = new PhysicsLock { Pitch = true, Roll = true };
			var agent = root.AddComponent<NavMeshAgent>();
			agent.Radius = 18;
			agent.Height = 36;
			agent.Acceleration = 2000;
			root.AddComponent<NavigationLayer>().RunSpeed = 7.25f * MapLoader.Scale;
			root.AddComponent<SensesLayer>();
			root.AddComponent<AnimationLayer>().EnableFootstepSounds = false;
			var npc = root.AddComponent<Skulk>();
			npc.Faction = Factions.Monster;
			npc.EyeHeight = 0.55f * MapLoader.Scale;
			npc.Health = 75;
			npc.DisplayName = "Skulk";
			npc.Renderer = renderer;
		}
		return prefab = builder.Create();
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( prefab );
		base.Shutdown();
	}
}

[Title( "Skulk" ), Category( "Natural Selection 2" )]
public sealed class Skulk : BaseNpc
{
	const float Reach = 1.42f * MapLoader.Scale;
	[Sync( SyncFlags.FromHost )] string State { get; set; } = "idle";
	float biteDuration;
	TimeUntil biteEnds, repath;

	protected override void OnStart()
	{
		base.OnStart();
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		if ( mount?.GetByPath( "mount://ns2/ns2/models/alien/skulk/skulk_view.model.vmdl" ) is ModelLoader viewModel )
			biteDuration = viewModel.SequenceDuration( "bite_attack" );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Renderer.IsValid() || Renderer.Animation.Graph is null ) return;
		var inputs = Renderer.Animation.Parameters;
		inputs.Set( "alive", Health > 0 );
		inputs.Set( "move", State == "run" ? "run" : "idle" );
		inputs.Set( "activity", State == "bite" ? "primary" : "none" );
		var velocity = Animation.MoveVelocity;
		inputs.Set( "move_speed", velocity.Length / Movement.RunSpeed );
		inputs.Set( "move_yaw", velocity.IsNearlyZero() ? 0f :
			System.MathF.Atan2( Vector3.Dot( velocity, WorldRotation.Forward ), Vector3.Dot( velocity, WorldRotation.Right ) ).RadianToDegree() );
	}

	protected override void Think()
	{
		if ( biteDuration <= 0 || biteEnds > 0 ) return;

		var target = Senses.VisibleHostiles.FirstOrDefault();
		if ( !target.IsValid() )
		{
			if ( Movement.MoveTarget.HasValue ) Movement.Stop();
			State = "idle";
			return;
		}

		var direction = target.WorldPosition - WorldPosition;
		if ( direction.WithZ( 0 ).Length <= Reach && System.MathF.Abs( direction.z ) <= 36 )
		{
			Movement.Stop();
			if ( !direction.WithZ( 0 ).IsNearlyZero() ) WorldRotation = Rotation.LookAt( direction.WithZ( 0 ) );
			State = "bite";
			// skulk_view.animation_graph emits "hit" on entering bite_attack.
			biteEnds = biteDuration;
			Bite();
			return;
		}

		if ( repath <= 0 )
		{
			Movement.MoveTo( target.WorldPosition, Reach * 0.6f, Movement.RunSpeed, target );
			repath = 0.2f;
		}
		State = Movement.IsMoving ? "run" : "idle";
	}

	void Bite()
	{
		// BiteLeap checks a 1.2m square with nine box traces, center first, preferring players.
		const float step = 1.2f * MapLoader.Scale / 3;
		System.ReadOnlySpan<int> order = [4, 1, 3, 5, 7, 0, 2, 6, 8];
		SceneTraceResult hit = default;
		IDamageable victim = null;
		foreach ( var cell in order )
		{
			var start = EyePosition + WorldRotation.Right * ((cell % 3 - 1) * step) + WorldRotation.Up * ((cell / 3 - 1) * step);
			var trace = Scene.Trace.Ray( start, start + WorldRotation.Forward * Reach ).Size( step )
				.IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
			if ( trace.GameObject?.GetComponentInParent<IDamageable>() is not { } target || GetDisposition( trace.GameObject.Root ) != Disposition.Hostile ) continue;
			if ( Vector3.Dot( trace.HitPosition - EyePosition, WorldRotation.Forward ) <= 0.1f * MapLoader.Scale ) continue;
			bool player = trace.GameObject.GetComponentInParent<PlayerController>() is not null;
			if ( victim is null || player ) (hit, victim) = (trace, target);
			if ( player ) break;
		}
		if ( victim is null ) return;
		var damage = new DamageInfo { Damage = 75, Attacker = GameObject, Weapon = GameObject, Origin = EyePosition, Position = hit.HitPosition };
		damage.Tags.Add( "bite" );
		victim.OnDamage( damage );
	}
}
