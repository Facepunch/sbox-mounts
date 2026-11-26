using Halo2.Blam;
using Sandbox.Mounting;
using System;

namespace Halo2;

internal sealed class WeaponAmmoLoader( string title, int capacity ) : ResourceLoader<Halo2Mount>
{
	protected override object Load() => new BaseAmmoResource { Title = title, MaxReserve = capacity };
}

internal abstract partial class WeaponLoader( MapFile source, TagEntry tag ) : ResourceLoader<Halo2Mount>
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

	protected ProjectileSpeedProfile ReadSpeedProfile( int projectile )
	{
		int flags = map.ReadInt32( projectile + 0xbc );
		bool scaled = (flags & (1 << 10)) != 0;
		float initial = Float( projectile + 0x17c ), final = Float( projectile + 0x180 );
		float inverse = initial > 0 && final > 0 && initial != final ? Float( projectile + 0x194 ) / 120 : 0;
		if ( !scaled && inverse <= 0 ) return null;
		float npcScale = 1;
		if ( scaled && map.Tags.FirstOrDefault( t => t.PrimaryClass == "gtam" ) is {} globals )
		{
			int difficulty = Address( globals ) + 0xf8;
			if ( map.ReadInt32( difficulty ) > 0 ) npcScale = Float( Block( difficulty ) + 0xa4 );
		}
		return new( final * 120, Float( projectile + 0x18c ) * 120, inverse, scaled, npcScale, (flags & 0x41) != 0 );
	}

	protected void ConfigureKick( Weapon gun, int barrel )
	{
		if ( map.ReadInt32( barrel + 0xe4 ) == 0 || Reference( Block( barrel + 0xe4 ) + 0x1c ) is not { PrimaryClass: "!tpj", DataSize: >= 0x8c } effect ) return;
		int trigger = Address( effect );
		gun.KickDuration = Float( trigger + 0x74 );
		gun.KickDegrees = Float( trigger + 0x7c ).RadianToDegree();
		gun.KickDistance = Float( trigger + 0x80 ) * 120;
		gun.KickJitter = new Vector2( Float( trigger + 0x84 ), Float( trigger + 0x88 ) ) * 120;
	}

	protected SoundEvent FiringSound( int barrel, out string marker, int index = 0, bool dry = false )
	{
		marker = null;
		if ( index >= map.ReadInt32( barrel + 0xe4 ) ) return null;
		var effect = Reference( Block( barrel + 0xe4 ) + index * 52 + (dry ? 20 : 4) );
		if ( effect is { PrimaryClass: "!dns" } )
		{
			marker = "root";
			return GameResource.Load<SoundEvent>( Host.AssetPath( map, effect.TagPath, "sound" ) );
		}
		if ( effect is not { PrimaryClass: "effe", DataSize: > 0 } ) return null;
		int events = Address( effect ) + 20;
		if ( map.ReadInt32( events ) == 0 ) return null;
		int part = FirstSoundPart( Block( events ) );
		if ( part < 0 ) return null;
		int locations = Address( effect ) + 12;
		int location = Short( part + 4 );
		if ( (uint)location < map.ReadInt32( locations ) )
			marker = map.ResolveStringId( map.ReadInt32( Block( locations ) + location * 4 ) );
		return GameResource.Load<SoundEvent>( Host.AssetPath( map, Reference( part + 12 ).TagPath, "sound" ) );
	}

	protected int FirstSoundPart( int effectEvent )
	{
		int parts = effectEvent + 24;
		for ( int i = 0, part = Block( parts ); i < map.ReadInt32( parts ); i++, part += 56 )
		{
			if ( Reference( part + 12 ) is { PrimaryClass: "!dns" } sound && sound.TagPath.StartsWith( @"sound\", StringComparison.OrdinalIgnoreCase ) )
				return part;
		}
		return -1;
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

	void ConfigureMelee( Weapon gun, TagEntry globals )
	{
		gun.MeleeHalfExtents = new Vector2( Float( Header + 0x1b0 ), Float( Header + 0x1b4 ) ) * 120;
		gun.MeleeRange = Float( Header + 0x1b8 ) * 120;
		var strike = ReadMeleeDamage( Address( Reference( Header + 0x1bc ) ), globals );
		gun.MinimumMeleeDamage = strike.Minimum;
		gun.MeleeDamage = strike.Maximum;
		gun.MeleeBodyScales = strike.BodyScales;
		gun.MeleeExplosive = strike.Explosive;
		gun.MeleeForce = strike.Force;
		gun.ContactSounds = ReadMeleeSounds( Address( globals ) );
		gun.MeleeForwardSpeed = Float( Block( Address( globals ) + 0x130 ) + 0x2c );
		if ( gun is EnergySword sword && Reference( Header + 0x1ec ) is { PrimaryClass: "!tpj" } lunge )
			sword.LungeDamage = ReadMeleeDamage( Address( lunge ), globals );
	}

	MeleeContactSounds ReadMeleeSounds( int globals )
	{
		bool meleeOnly = (map.ReadInt32( Header + 0x12c ) & (1 << 24)) != 0;
		int count = map.ReadInt32( globals + 0x150 ), rows = Block( globals + 0x150 );
		var contacts = new Dictionary<string, int[]>();
		var cues = new List<CuePlayback>();
		var indices = new Dictionary<string, int>();
		var sounds = new List<int>( 3 );
		string SoundPath( TagEntry tag ) => tag?.PrimaryClass == "!dns" ? Host.AssetPath( map, tag.TagPath, "sound" ) : null;
		void Add( string path )
		{
			if ( path is null ) return;
			if ( !indices.TryGetValue( path, out int index ) )
			{
				if ( Host.GetByPath( path ) is not Halo2SoundLoader sound ) return;
				indices[path] = index = cues.Count;
				cues.Add( sound.Cue );
			}
			sounds.Add( index );
		}

		if ( !meleeOnly )
		{
			var damage = Reference( Header + 0x1bc );
			string strike = damage is null ? null : SoundPath( Reference( Address( damage ) + 0xa8 ) );
			foreach ( var (name, material) in Host.MeleeMaterialSounds )
			{
				sounds.Clear();
				Add( material );
				Add( strike );
				contacts[name] = sounds.ToArray();
			}
			return new( false, contacts, cues.ToArray() );
		}

		if ( Reference( Header + 0x54 ) is not { PrimaryClass: "toof" } foot || map.ReadInt32( Address( foot ) ) <= 15 ) return null;
		int bank = Block( Address( foot ) ) + 15 * 24 + 8;
		var footRows = new Dictionary<int, int>();
		for ( int i = 0, entry = Block( bank ); i < map.ReadInt32( bank ); i++, entry += 24 )
			footRows.TryAdd( Short( entry + 20 ), entry );
		for ( int i = 0; i < count; i++ )
		{
			int row = rows + i * 0xb4;
			if ( map.ResolveStringId( map.ReadInt32( row ) ) is not {} name ) continue;
			sounds.Clear();
			int index = i;
			for ( int depth = 0; (uint)index < count && depth < count; depth++ )
			{
				if ( footRows.TryGetValue( index, out var entry ) )
				{
					Add( SoundPath( Reference( entry ) ) );
					if ( ((map.ReadInt32( entry + 20 ) >> 16) & 255) == 1 ) Add( SoundPath( Reference( row + 0x38 ) ) );
					Add( SoundPath( Reference( entry + 8 ) ) );
					break;
				}
				index = Short( rows + index * 0xb4 + 8 );
			}
			contacts[name] = sounds.ToArray();
		}
		return new( true, contacts, cues.ToArray() );
	}

	MeleeDamageProfile ReadMeleeDamage( int effect, TagEntry globals ) => new(
		Float( effect + 0x1c ), Float( effect + 0x20 ), Float( effect + 0x40 ) * 120,
		BodyDamageScales( Address( globals ), effect ), (map.ReadInt32( effect + 0x14 ) & 0x20) != 0 );

	SoundEvent ZoomSound( int reference ) => Reference( reference ) is { PrimaryClass: "!dns" } sound
		? GameResource.Load<SoundEvent>( Host.AssetPath( map, sound.TagPath, "sound" ) ) : null;

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
		gun.NpcOnly = (map.ReadInt32( Header + 0x12c ) & (1 << 29)) != 0;
		(gun.AnimationClass, gun.AnimationType) = AnimationIdentifiers;
		gun.ZoomLevels = Short( Header + 0x1fe );
		if ( gun.ZoomLevels > 0 )
		{
			gun.ZoomRange = new Vector2( Float( Header + 0x200 ), Float( Header + 0x204 ) );
			gun.ZoomInSound = ZoomSound( Header + 0x26c );
			gun.ZoomOutSound = ZoomSound( Header + 0x274 );
		}
		var globals = map.Tags.First( t => t.PrimaryClass == "gtam" );
		int players = Address( globals ) + 0x138;
		if ( map.ReadInt32( players ) > 0 && Reference( Block( players ) + 0xb0 ) is { PrimaryClass: "dpib", DataSize: > 0 } body
			&& ModelPath( Host, map, body ) is {} path && Host.GetByPath( path ) is Halo2ModelLoader model
			&& model.GetAnimationContext( "combat", gun.AnimationClass, gun.AnimationType )?.Overlays.TryGetValue( "put_away", out int index ) == true
			&& model.GetClipInfo( index ) is {} clip )
		{
			gun.DefaultPutAwayFrames = clip.Frames;
			gun.DefaultPutAwayRelease = Weapon.ReleaseFrame( clip );
		}
		ConfigureMelee( gun, globals );
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
