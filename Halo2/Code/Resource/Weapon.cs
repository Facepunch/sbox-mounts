using Halo2.Blam;
using Sandbox.Mounting;
using System;

namespace Halo2;

internal sealed class WeaponAmmoLoader( string title, int capacity ) : ResourceLoader<Halo2Mount>
{
	protected override object Load() => new BaseAmmoResource { Title = title, MaxReserve = capacity };
}

internal abstract class WeaponLoader( MapFile source, TagEntry tag ) : ResourceLoader<Halo2Mount>
{
	protected readonly MapFile map = source;
	protected readonly TagEntry weapon = tag;

	protected int Header => map.VaToOffset( (int)weapon.DataVirtualAddress );
	protected int Block( int offset ) => map.VaToOffset( map.ReadInt32( offset + 4 ) );
	protected int Short( int offset ) => (short)map.ReadInt32( offset );
	protected float Float( int offset ) => BitConverter.Int32BitsToSingle( map.ReadInt32( offset ) );
	protected TagEntry Reference( int offset ) => map.GetTag( map.ReadInt32( offset + 4 ) & 0xffff );
	protected int Address( TagEntry tag ) => map.VaToOffset( (int)tag.DataVirtualAddress );
	internal int MaxReserve => Short( Block( Header + 0x2c0 ) + 0xc );

	protected SoundEvent FiringSound( int barrel )
	{
		if ( map.ReadInt32( barrel + 0xe4 ) == 0 || Reference( Block( barrel + 0xe4 ) + 4 ) is not { PrimaryClass: "effe", DataSize: > 0 } effect ) return null;
		int events = Address( effect ) + 20;
		if ( map.ReadInt32( events ) == 0 ) return null;
		int parts = Block( events ) + 0x18;
		int count = map.ReadInt32( parts );
		if ( count == 0 ) return null;
		int first = Block( parts );
		for ( int i = 0; i < count; i++ )
		{
			var tag = Reference( first + i * 0x38 + 0xc );
			if ( tag?.PrimaryClass == "!dns" && tag.TagPath.StartsWith( @"sound\", StringComparison.OrdinalIgnoreCase ) )
				return GameResource.Load<SoundEvent>( Host.AssetPath( map, tag.TagPath, "sound" ) );
		}
		return null;
	}

	protected Dictionary<string, float> BodyDamageScales( int globals, int effect )
	{
		var result = new Dictionary<string, float>();
		if ( map.ReadInt32( globals + 0xd0 ) <= 0 ) return result;
		int table = Block( globals + 0xd0 );
		int groupA = map.ReadInt32( effect + 0x50 ), groupB = map.ReadInt32( effect + 0x54 );
		var modifiers = new Dictionary<(int Group, int Armor), float>();
		for ( int i = 0, count = map.ReadInt32( table ), row = Block( table ); i < count; i++, row += 12 )
		{
			int group = map.ReadInt32( row );
			if ( group != groupA && group != groupB ) continue;
			for ( int j = 0, entries = map.ReadInt32( row + 4 ), entry = Block( row + 4 ); j < entries; j++, entry += 8 )
				modifiers[(group, map.ReadInt32( entry ))] = Float( entry + 4 );
		}

		float Modifier( int group, int armor ) => modifiers.GetValueOrDefault( (group, armor), 1f );
		for ( int i = 0, count = map.ReadInt32( globals + 0x150 ), row = Block( globals + 0x150 ); i < count; i++, row += 0xb4 )
		{
			int armorA = map.ReadInt32( row + 0x10 ), armorB = map.ReadInt32( row + 0x14 );
			float scale = Modifier( groupA, armorA ) * Modifier( groupA, armorB ) * Modifier( groupB, armorA ) * Modifier( groupB, armorB );
			if ( scale != 1 && map.ResolveStringId( map.ReadInt32( row ) ) is { Length: > 0 } name )
				result[$"mount://{Host.Ident}/surfaces/{name}.surface"] = scale;
		}
		return result;
	}

	void ConfigureMelee( Weapon gun )
	{
		var globals = map.Tags.First( t => t.PrimaryClass == "gtam" );
		gun.MeleeHalfExtents = new Vector2( Float( Header + 0x1b0 ), Float( Header + 0x1b4 ) ) * 120;
		gun.MeleeRange = Float( Header + 0x1b8 ) * 120;
		int strike = Address( Reference( Header + 0x1bc ) );
		gun.MinimumMeleeDamage = Float( strike + 0x1c );
		gun.MeleeDamage = Float( strike + 0x20 );
		gun.MeleeBodyScales = BodyDamageScales( Address( globals ), strike );
		gun.MeleeExplosive = (map.ReadInt32( strike + 0x14 ) & 0x20) != 0;
		gun.MeleeForwardSpeed = Float( Block( Address( globals ) + 0x130 ) + 0x2c );
		gun.MeleeForce = Float( strike + 0x40 ) * 120;
	}

	protected T ConfigurePickup<T>( GameObject root, string world, string title ) where T : Weapon, new()
	{
		root.NetworkMode = NetworkMode.Object;
		root.Tags.Add( "removable", "nocollide_props" );
		var renderer = root.AddComponent<SkinnedModelRenderer>();
		renderer.Model = Model.Load( world );
		var collider = root.AddComponent<BoxCollider>();
		collider.Center = renderer.Model.Bounds.Center;
		collider.Scale = renderer.Model.Bounds.Size;
		root.AddComponent<Rigidbody>().GravityScale = 4.1712594f * 120 / 850;
		var gun = root.AddComponent<T>();
		gun.DisplayName = title;
		(gun.AnimationClass, gun.AnimationType) = AnimationIdentifiers;
		ConfigureMelee( gun );
		return gun;
	}

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
