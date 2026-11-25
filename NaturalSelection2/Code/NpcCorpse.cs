using System;

namespace NS2;

public sealed class NpcCorpse : Component
{
	[Sync( SyncFlags.FromHost )] TimeSince Age { get; set; }
	[Sync( SyncFlags.FromHost )] float Lifetime { get; set; }
	[Sync( SyncFlags.FromHost )] TimeSince AnimationTime { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.BodyPose pose { get; set; }
	SkinnedModelRenderer renderer;
	SparkAnimationGraph.Playback graph;
	Action<string> onTag;
	bool deathEnded;
	float nextGraphUpdate = -1;

	internal static GameObject Animate( SkinnedModelRenderer source, SparkAnimationGraph.Playback graph, TimeSince animationTime )
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
		corpse.Lifetime = 1.5f;
		corpse.Age = 0;
		corpse.AnimationTime = animationTime;
		corpse.graph = graph;
		corpse.onTag = tag => corpse.deathEnded |= tag == "death_end";
		corpse.pose = graph.GetBodyPose();
		graph.ClearInputs();
		graph.Set( "alive", 0 );
		graph.Set( "active", 1 );
		graph.Set( "built", 1 );
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
		if ( deathEnded )
		{
			// The preceding update submitted the final animated pose. Start the bodies from those bones.
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
		output?.TrySet( "grow", 1f );
		if ( Networking.IsHost && graph is not null && Age >= nextGraphUpdate )
		{
			// NS2 schedules corpse transitions at 4 Hz and samples tags over the current server frame.
			nextGraphUpdate = Age + 0.25f;
			graph.UpdateInterval( AnimationTime - Time.Delta, AnimationTime, output, onTag );
			pose = graph.GetBodyPose();
		}
		pose.Write( output, AnimationTime );
	}

	protected override void OnUpdate()
	{
		if ( Age >= Lifetime && Networking.IsHost )
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
