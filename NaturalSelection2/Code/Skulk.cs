using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace NS2;

internal sealed class SkulkPrefab : ResourceLoader<GameMount>
{
	const string ModelFile = "ns2/models/alien/skulk/skulk.model";
	const string ModelPath = $"mount://ns2/{ModelFile}.vmdl";
	const string WorldGraph = "ns2/models/alien/skulk/skulk.animation_graph";
	PrefabFile prefab;

	internal static void Register( MountContext context )
	{
		context.Add( ResourceType.AnimationGraph, WorldGraph, new AnimationGraphLoader( ModelFile, WorldGraph, npcPlayback: true ) );
		Skulk.RegisterSounds( context );
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
			renderer.Animation.Graph = Sandbox.Animation.Graph.Load( $"mount://ns2/{WorldGraph}.ag" );
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
public sealed partial class Skulk : BaseNpc
{
	const float Reach = 1.42f * MapLoader.Scale;
	[Sync( SyncFlags.FromHost )] string State { get; set; } = "idle";
	SparkAnimationGraph.Playback world, weapon;
	System.Action<string> weaponTag;
	TimeUntil repath;

	protected override void OnStart()
	{
		base.OnStart();
		worldTag = OnWorldTag;
		if ( Networking.IsHost ) PlayVoice( "spawn" );
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model )
			world = new( model.PropGraph, model );
		if ( Networking.IsHost && mount?.GetByPath( "mount://ns2/ns2/models/alien/skulk/skulk_view.model.vmdl" ) is ModelLoader viewModel )
		{
			weapon = new( viewModel.PropGraph, viewModel );
			weaponTag = OnWeaponTag;
			weapon.Set( "ability", "bite" );
			weapon.Set( "attack_speed", 1 );
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Networking.IsHost && Health > 0 ) UpdateFlinch();

		var move = State == "run" ? "run" : "idle";
		var activity = State == "bite" ? "primary" : "none";
		world?.Set( "alive", Health > 0 ? 1 : 0 );
		world?.Set( "move", move );
		world?.Set( "activity", activity );
		if ( weapon is not null && Health > 0 )
		{
			weapon.Set( "move", move );
			weapon.Set( "activity", activity );
			weapon.Update( Time.Delta, tag: weaponTag );
		}
		var inputs = Renderer.IsValid() && Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;

		var velocity = Animation.MoveVelocity;
		inputs?.Set( "move_speed", velocity.Length / Movement.RunSpeed );
		inputs?.Set( "move_yaw", velocity.IsNearlyZero() ? 0f :
			System.MathF.Atan2( Vector3.Dot( velocity, WorldRotation.Forward ), Vector3.Dot( velocity, WorldRotation.Right ) ).RadianToDegree() );
		world?.Update( Time.Delta, inputs, Health > 0 && !Application.IsDedicatedServer && velocity.Length > 0.75f * MapLoader.Scale ? worldTag : null );
	}

	protected override void Think()
	{
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
			return;
		}

		if ( repath <= 0 )
		{
			Movement.MoveTo( target.WorldPosition, Reach * 0.6f, Movement.RunSpeed, target );
			repath = 0.2f;
		}
		State = Movement.IsMoving ? "run" : "idle";
	}

	void OnWeaponTag( string tag )
	{
		if ( tag != "hit" ) return;
		// BiteLeap checks a 1.2m square with nine box traces, center first, preferring players.
		const float step = 1.2f * MapLoader.Scale / 3;
		System.ReadOnlySpan<int> order = [4, 1, 3, 5, 7, 0, 2, 6, 8];
		PlayVoice( "bite" );
		SceneTraceResult hit = default, center = default;
		IDamageable victim = null;
		foreach ( var cell in order )
		{
			var start = EyePosition + WorldRotation.Right * ((cell % 3 - 1) * step) + WorldRotation.Up * ((cell / 3 - 1) * step);
			var trace = Scene.Trace.Ray( start, start + WorldRotation.Forward * Reach ).Size( step )
				.IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
			if ( cell == 4 ) center = trace;
			if ( trace.GameObject?.GetComponentInParent<IDamageable>() is not { } target || GetDisposition( trace.GameObject.Root ) != Disposition.Hostile ) continue;
			if ( Vector3.Dot( trace.HitPosition - EyePosition, WorldRotation.Forward ) <= 0.1f * MapLoader.Scale ) continue;
			bool player = trace.GameObject.GetComponentInParent<PlayerController>() is not null;
			if ( victim is null || player ) (hit, victim) = (trace, target);
			if ( player ) break;
		}
		if ( victim is null ) hit = center;
		if ( hit.Hit ) PlayBiteImpact( hit );
		if ( victim is null ) return;
		var npc = victim as BaseNpc;
		bool alive = npc.IsValid() && npc.Health > 0;
		var damage = new DamageInfo { Damage = 75, Attacker = GameObject, Weapon = GameObject, Origin = EyePosition, Position = hit.HitPosition };
		damage.Tags.Add( "bite" );
		victim.OnDamage( damage );
		if ( alive && (!npc.IsValid() || npc.Health <= 0) ) PlayVoice( "bite_kill" );
	}
}
