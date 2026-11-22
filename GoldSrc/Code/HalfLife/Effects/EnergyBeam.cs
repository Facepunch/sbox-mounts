namespace GoldSrc.HalfLife;

[Library]
internal static class EnergyBeam
{
	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void Flash( Vector3 start, Vector3 end, Color color, string texture, string name ) => Show( start, end, color, texture, name, 2, 0.3f );

	internal static void Show( Vector3 start, Vector3 end, Color color, string texture, string name, float width, float lifetime, GameMount mount = null )
	{
		var go = new GameObject( false, name );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = start;
		var beam = go.AddComponent<BeamEffect>();
		beam.TargetPosition = end;
		beam.Scale = width;
		beam.BeamColor = color;
		beam.Additive = true;
		beam.BeamLifetime = lifetime;
		mount ??= Sandbox.Mounting.Directory.Get( "hl1" ) as GameMount;
		if ( mount?.GetIndexedSprite( texture ) is { } sprite )
			beam.Texture = sprite.Animations[0].Frames[0].Texture;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = lifetime;
		go.Enabled = true;
	}
}
