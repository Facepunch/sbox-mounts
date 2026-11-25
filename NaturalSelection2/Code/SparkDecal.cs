namespace NS2;

internal static class SparkDecal
{
	internal static void Spawn( string name, string material, Vector3 position, Rotation rotation, float roll, float size, float lifetime )
	{
		if ( !Graphics.IsAvailable ) return;
		var definition = GameResource.Load<DecalDefinition>( $"mount://ns2/ns2/cinematics/vfx_materials/decals/{material}.material.vmat/projected.decal" );
		if ( definition?.ColorTexture is null ) return;

		var effect = new GameObject( false, name )
		{
			NetworkMode = NetworkMode.Never,
			WorldPosition = position,
			WorldRotation = rotation
		};
		var decal = effect.AddComponent<Decal>();
		decal.Decals.Add( definition );
		decal.Size = size * MapLoader.Scale;
		decal.Rotation = roll;
		decal.LifeTime = lifetime;
		decal.Transient = true;
		effect.AddComponent<TemporaryEffect>();
		effect.Enabled = true;
	}
}
