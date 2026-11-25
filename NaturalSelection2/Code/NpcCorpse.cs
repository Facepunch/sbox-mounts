using System;

namespace NS2;

public sealed class NpcCorpse : Component
{
	[Sync( SyncFlags.FromHost )] TimeSince Age { get; set; }
	[Sync( SyncFlags.FromHost )] float Lifetime { get; set; }
	[Sync( SyncFlags.FromHost )] TimeSince AnimationTime { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.BodyPose pose { get; set; }
	[Sync( SyncFlags.FromHost )] TimeUntil FlinchEnd { get; set; }
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

	static GameObject CreateAnimated( SkinnedModelRenderer source, SparkAnimationGraph.Playback graph, TimeSince animationTime, float lifetime, TimeUntil flinchEnd = default )
	{
		if ( !source.IsValid() || source.Model is null ) return null;
		var body = new GameObject( false, "Ragdoll" );
		body.WorldTransform = source.WorldTransform;
		body.Tags.Add( "ragdoll" );
		var renderer = body.AddComponent<SkinnedModelRenderer>();
		renderer.CopyFrom( source );
		renderer.UseAnimGraph = true;
		renderer.Animation.Graph = source.Animation.Graph;

		var corpse = body.AddComponent<NpcCorpse>();
		corpse.Lifetime = lifetime;
		corpse.FlinchEnd = flinchEnd;
		corpse.Age = 0;
		corpse.AnimationTime = animationTime;
		corpse.graph = graph;
		corpse.onTag = tag => corpse.deathEnded |= tag == "death_end";
		corpse.pose = graph.GetBodyPose();
		graph.Set( "alive", 0 );
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

	protected override void OnStart() => renderer = GetComponent<SkinnedModelRenderer>();

	void UpdateDeathAnimation()
	{
		if ( deathEnded || Networking.IsHost && graph is null )
		{
			// After a host migration the new host can hand off the last replicated pose.
			if ( Lifetime == 0 ) Lifetime = Age + 3;
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
		if ( Lifetime == 0 )
			output?.TrySet( "intensity", AlienWounds.Intensity( FlinchEnd ) );
		else
			output?.TrySet( "grow", 1f );
		if ( Networking.IsHost && graph is not null && Age >= nextGraphUpdate )
		{
			// NS2 schedules corpse transitions at 4 Hz and samples tags over the current server frame.
			nextGraphUpdate = Age + 0.25f;
			if ( Lifetime == 0 ) graph.Set( "flinch", FlinchEnd > 0 ? 1 : 0 );
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
		if ( Application.IsDedicatedServer ) return;
		float remaining = Math.Clamp( 1 - Age / 2.25f, 0, 1 );
		renderer.Attributes.Set( "Ns2Dissolve", 1 - remaining * remaining * remaining );
	}
}
