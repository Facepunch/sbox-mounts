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

	internal static Halo2ModelLoader CreateRig( MapFile map, TagEntry weapon, TagEntry globals, int index )
	{
		if ( weapon.DataSize < 0x2b0 || globals is not { DataSize: >= 0x140 } ) return null;
		int players = map.VaToOffset( (int)globals.DataVirtualAddress ) + 0x138;
		int models = map.VaToOffset( (int)weapon.DataVirtualAddress ) + 0x2a8;
		if ( index >= map.ReadInt32( players ) || index >= map.ReadInt32( models ) ) return null;

		int player = map.VaToOffset( map.ReadInt32( players + 4 ) ) + index * 0xbc;
		int model = map.VaToOffset( map.ReadInt32( models + 4 ) ) + index * 16;
		TagEntry Reference( int offset ) => map.GetTag( map.ReadInt32( offset + 4 ) & 0xffff );
		if ( Reference( player ) is not { PrimaryClass: "edom", DataSize: > 0 } arms
			|| Reference( player + 0xb0 ) is not { PrimaryClass: "dpib", DataSize: > 0 } biped
			|| Reference( model ) is not { PrimaryClass: "edom", DataSize: > 0 } gun
			|| Reference( model + 8 ) is not { PrimaryClass: "damj", DataSize: > 0 } graph ) return null;
		return new Halo2ModelLoader( map, arms, biped: biped, graph: graph, firstPersonWeapon: gun );
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
