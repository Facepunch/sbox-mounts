using System;

namespace NS2;

public sealed class NpcCorpse : Component
{
	[Sync( SyncFlags.FromHost )] TimeSince Age { get; set; }
	[Sync( SyncFlags.FromHost )] float Lifetime { get; set; }
	[Sync( SyncFlags.FromHost )] TimeSince AnimationTime { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.BodyPose pose { get; set; }
	[Sync( SyncFlags.FromHost )] TimeUntil FlinchEnd { get; set; }
	enum BodyKind { Structure, Player, RailgunVictim, Exo }
	[Sync( SyncFlags.FromHost )] BodyKind Kind { get; set; }
	SkinnedModelRenderer renderer;
	SparkAnimationGraph.Playback graph;
	Action<string> onTag;
	bool deathEnded;
	float nextGraphUpdate = -1;

	internal static GameObject Animate( SkinnedModelRenderer source, SparkAnimationGraph.Playback graph, TimeSince animationTime )
	{
		graph.ClearInputs();
		graph.Set( "active", 1 );
		graph.Set( "built", 1 );
		return CreateAnimated( source, graph, animationTime, lifetime: 1.5f );
	}

	// Actors using RagdollMixin start their three-second removal timer at the death_end marker.
	internal static GameObject AnimateUntilRagdoll( SkinnedModelRenderer source, SparkAnimationGraph.Playback graph, TimeSince animationTime, TimeUntil flinchEnd ) =>
		CreateAnimated( source, graph, animationTime, lifetime: 0, flinchEnd: flinchEnd );

	internal static GameObject AnimatePlayer( SkinnedModelRenderer source, SparkAnimationGraph.Playback graph, TimeSince animationTime, bool railgunDeath )
	{
		var body = CreateAnimated( source, graph, animationTime, lifetime: 2, kind: railgunDeath ? BodyKind.RailgunVictim : BodyKind.Player );
		return railgunDeath ? null : body;
	}

	// Dead Exo players remain in damage traces until the original server replaces them with spectators.
	internal static void KeepHitboxes( SkinnedModelRenderer source, SparkAnimationGraph.Playback graph, TimeSince animationTime ) =>
		CreateAnimated( source, graph, animationTime, lifetime: 2, kind: BodyKind.Exo );

	static GameObject CreateAnimated( SkinnedModelRenderer source, SparkAnimationGraph.Playback graph, TimeSince animationTime, float lifetime, TimeUntil flinchEnd = default, BodyKind kind = BodyKind.Structure )
	{
		if ( !source.IsValid() || source.Model is null ) return null;
		var body = new GameObject( false, kind == BodyKind.Exo ? "Exo hitboxes" : "Ragdoll" );
		body.WorldTransform = source.WorldTransform;
		if ( kind is BodyKind.Player or BodyKind.Structure ) body.Tags.Add( "ragdoll" );
		var renderer = body.AddComponent<SkinnedModelRenderer>();
		renderer.CopyFrom( source );
		renderer.UseAnimGraph = graph is not null;
		renderer.Animation.Graph = source.Animation.Graph;

		if ( kind == BodyKind.Exo )
		{
			renderer.RenderOptions.Game = false;
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		}
		var corpse = body.AddComponent<NpcCorpse>();
		corpse.Kind = kind;
		corpse.Lifetime = lifetime;
		corpse.FlinchEnd = flinchEnd;
		corpse.Age = 0;
		corpse.AnimationTime = animationTime;
		corpse.graph = graph;
		if ( kind != BodyKind.Exo ) corpse.onTag = tag => corpse.deathEnded |= tag == "death_end";
		corpse.pose = graph?.GetBodyPose() ?? default;
		graph?.Set( "alive", 0 );
		body.Enabled = true;
		body.NetworkSpawn();
		return body;
	}

	internal static GameObject Attach( GameObject ragdoll, float lifetime )
	{
		if ( ragdoll.IsValid() )
		{
			var corpse = ragdoll.AddComponent<NpcCorpse>();
			corpse.Age = 0;
			corpse.Lifetime = lifetime;
		}
		return ragdoll;
	}

	protected override void OnStart()
	{
		renderer = GetComponent<SkinnedModelRenderer>();
		if ( Kind != BodyKind.Exo ) return;

		// ModelHitboxes sizes its shapes from the current world-space bone transforms.
		renderer.SceneModel.Update( 0 );
		var hitboxes = GameObject.AddComponent<ModelHitboxes>();
		hitboxes.Flags |= ComponentFlags.NotNetworked;
		hitboxes.Renderer = renderer;
	}

	void UpdateDeathAnimation()
	{
		if ( Kind != BodyKind.Exo && (deathEnded || Networking.IsHost && graph is null) )
		{
			if ( Kind == BodyKind.RailgunVictim )
			{
				GameObject.Destroy();
				return;
			}

			// After a host migration the new host can hand off the last replicated pose.
			if ( Lifetime == 0 || Kind == BodyKind.Player ) Lifetime = Age + 3;
			var physics = GameObject.AddComponent<ModelPhysics>( false );
			physics.Model = renderer.Model;
			physics.Renderer = renderer;
			physics.CopyBonesFrom( renderer, true );
			renderer.UseAnimGraph = false;
			physics.Enabled = true;
			GameObject.Network.Refresh();
			return;
		}

		var output = renderer.Animation.IsValid ? renderer.Animation.Parameters : null;
		if ( Kind == BodyKind.Structure && Lifetime == 0 )
			output?.TrySet( "intensity", AlienWounds.Intensity( FlinchEnd ) );
		else if ( Kind == BodyKind.Structure )
			output?.TrySet( "grow", 1f );
		if ( Networking.IsHost && graph is not null && Age >= nextGraphUpdate )
		{
			// Structure graphs advance at 4 Hz. Player graphs advance every update.
			nextGraphUpdate = Age + (Kind == BodyKind.Structure ? 0.25f : 0);
			if ( Kind == BodyKind.Structure && Lifetime == 0 ) graph.Set( "flinch", FlinchEnd > 0 ? 1 : 0 );
			graph.UpdateInterval( AnimationTime - Time.Delta, AnimationTime, output, onTag );
			pose = graph.GetBodyPose();
		}
		pose.Write( output, AnimationTime );
	}

	protected override void OnUpdate()
	{
		if ( Lifetime > 0 && Age >= Lifetime && Networking.IsHost )
		{
			GameObject.Destroy();
			return;
		}
		if ( !renderer.IsValid() ) return;
		if ( renderer.UseAnimGraph ) UpdateDeathAnimation();
		if ( Kind is BodyKind.Exo or BodyKind.RailgunVictim || Application.IsDedicatedServer ) return;
		float remaining = Math.Clamp( 1 - Age / 2.25f, 0, 1 );
		renderer.Attributes.Set( "Ns2Dissolve", 1 - remaining * remaining * remaining );
	}
}
