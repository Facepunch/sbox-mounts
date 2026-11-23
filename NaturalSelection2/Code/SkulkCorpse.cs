using System;

namespace NS2;

public sealed class SkulkCorpse : Component
{
	[Sync( SyncFlags.FromHost )] internal TimeSince Age { get; set; }
	SkinnedModelRenderer renderer;

	protected override void OnStart() => renderer = GetComponent<SkinnedModelRenderer>();

	protected override void OnUpdate()
	{
		if ( Age >= 3 && Networking.IsHost )
		{
			GameObject.Destroy();
			return;
		}
		if ( Application.IsDedicatedServer || !renderer.IsValid() ) return;
		float remaining = Math.Clamp( 1 - Age / 2.25f, 0, 1 );
		renderer.Attributes.Set( "Ns2Dissolve", 1 - remaining * remaining * remaining );
	}
}
