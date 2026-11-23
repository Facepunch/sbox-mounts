namespace Overgrowth;

public sealed class ColdBreath : Component
{
	internal void Exhale( SkinnedModelRenderer renderer, Vector3 velocity )
	{
		if ( CharacterLoader.AverageChainFrame( renderer, "head" ) is { } head &&
			Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount )
			mount.EmitBillboard( "Data/Particles/breath_fog.xml", head.Position, velocity + head.Up * 40f, Color.White );
	}
}
