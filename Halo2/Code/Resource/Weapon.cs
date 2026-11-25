using Halo2.Blam;
using Sandbox.Mounting;

namespace Halo2;

internal abstract class WeaponLoader( MapFile source, TagEntry tag ) : ResourceLoader<Halo2Mount>
{
	protected readonly MapFile map = source;
	protected readonly TagEntry weapon = tag;

	internal string WorldModelPath => ModelPath( Host, map, weapon );

	internal (string Class, string Type) AnimationIdentifiers
	{
		get
		{
			int header = map.VaToOffset( (int)weapon.DataVirtualAddress );
			return (map.ResolveStringId( map.ReadInt32( header + 0x288 ) ), map.ResolveStringId( map.ReadInt32( header + 0x28c ) ));
		}
	}

	internal static string ModelPath( Halo2Mount mount, MapFile map, TagEntry weapon )
	{
		int header = map.VaToOffset( (int)weapon.DataVirtualAddress );
		if ( map.GetTag( map.ReadInt32( header + 0x38 ) & 0xffff ) is not { PrimaryClass: "tmlh", DataSize: >= 8 } model ) return null;
		int definition = map.VaToOffset( (int)model.DataVirtualAddress );
		return map.GetTag( map.ReadInt32( definition + 4 ) & 0xffff ) is { PrimaryClass: "edom", DataSize: > 0 } render
			? mount.AssetPath( map, render.TagPath, "vmdl" ) : null;
	}
}
