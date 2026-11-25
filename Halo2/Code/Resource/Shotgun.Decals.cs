using Halo2.Blam;
using Sandbox.Mounting;
using static Halo2.PelletImpactEffect;

namespace Halo2;

internal sealed partial class ShotgunLoader
{
	internal void RegisterImpactEffects( MountContext context )
	{
		LoadMaterialEffects();
		if ( pelletEffects is null ) return;
		foreach ( var tag in pelletEffects.Values.OfType<TagEntry>().Distinct() )
			context.Add( ResourceType.PrefabFile, ImpactPath( tag ), new ImpactLoader( this, tag ) { Flags = ResourceFlags.DeveloperOnly | ResourceFlags.Effect } );
	}

}
