using System;

namespace NS2;

public sealed class NpcCorpse : Component
{
	[Sync( SyncFlags.FromHost )] TimeSince Age { get; set; }
	[Sync( SyncFlags.FromHost )] float Lifetime { get; set; }
	SkinnedModelRenderer renderer;

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

	protected override void OnUpdate()
	{
		if ( Age >= Lifetime && Networking.IsHost )
		{
			GameObject.Destroy();
			return;
		}
		if ( Application.IsDedicatedServer || !renderer.IsValid() ) return;
		float remaining = Math.Clamp( 1 - Age / 2.25f, 0, 1 );
		renderer.Attributes.Set( "Ns2Dissolve", 1 - remaining * remaining * remaining );
	}
}
