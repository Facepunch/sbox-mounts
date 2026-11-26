namespace Halo2;

[Hide]
public sealed class MarkerEmission : Component, Component.ITemporaryEffect
{
	[RequireComponent] public ParticleEffect Effect { get; set; }
	int pending;
	bool Component.ITemporaryEffect.IsActive => pending > 0;
	internal GameObject Attachment { get; set; }
	internal Transform Offset { get; set; }

	internal static void Queue( ParticleEffect effect, int count = 1 )
	{
		if ( count > 0 ) effect.GameObject.GetOrAddComponent<MarkerEmission>().pending += count;
	}

	protected override void OnEnabled()
	{
		Effect.OnPreStep += Emit;
	}

	protected override void OnDisabled()
	{
		if ( Effect.IsValid() ) Effect.OnPreStep -= Emit;
		pending = 0;
	}

	// The camera and animated attachment have their frame transforms when particles step.
	void Emit( float delta )
	{
		if ( pending == 0 ) return;
		if ( Attachment.IsValid() ) Effect.WorldTransform = Attachment.WorldTransform.ToWorld( Offset );
		for ( int i = 0; i < pending; i++ ) Effect.Emit( Effect.WorldPosition, 0 );
		pending = 0;
	}
}
