namespace GoldSrc.HalfLife;

[Library]
internal static class ImpactDecal
{
	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void Gunshot( Vector3 position, Vector3 normal, int variant ) => Place( position, normal, $"shot{variant}", 10 );

	internal static void Place( Vector3 position, Vector3 normal, string name, float size )
	{
		var texture = Texture.Load( $"mount://hl1/valve/textures/decals/{{{name}.vtex" );
		if ( !texture.IsValid() || texture.IsError ) return;
		var go = new GameObject( false, $"{name} decal" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position + normal;
		go.WorldRotation = Rotation.LookAt( -normal );
		var decal = go.AddComponent<Decal>();
		decal.Decals = [new DecalDefinition { ColorTexture = texture, Width = size > 0 ? size : texture.Width, Height = size > 0 ? size : texture.Height }];
		decal.Depth = 4;
		decal.Transient = true;
		go.AddComponent<TemporaryEffect>();
		go.Enabled = true;
	}
}
