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

	protected TagEntry Globals => map.Globals ?? throw new InvalidOperationException( $"Missing globals in {map.Header.MapName}" );
	protected int Header => map.VaToOffset( (int)weapon.DataVirtualAddress );
	protected int Block( int offset ) => map.VaToOffset( map.ReadInt32( offset + 4 ) );
	protected int Short( int offset ) => (short)map.ReadInt32( offset );
	protected float Float( int offset ) => BitConverter.Int32BitsToSingle( map.ReadInt32( offset ) );
	protected TagEntry Reference( int offset ) => map.GetTag( map.ReadInt32( offset + 4 ) & 0xffff );
	protected int Address( TagEntry tag ) => map.VaToOffset( (int)tag.DataVirtualAddress );
	internal int MaxReserve => Short( Block( Header + 0x2c0 ) + 0xc );

	protected (int Row, TagEntry Bitmap, int Index)? HudPage( string widget, bool split, bool elite = false ) =>
		HudPage( widget, split ? 1 : 0, elite );

	protected (int Row, TagEntry Bitmap, int Index)? HudPage( string widget, int layout, bool elite = false )
	{
		var visited = new HashSet<TagEntry>();
		for ( var definition = Reference( Header + 0x2b0 ); definition is { DataSize: >= 16 } && visited.Add( definition ); )
		{
			int hud = Address( definition );
			if ( FindHudPage( hud, widget, layout, elite ) is {} page ) return page;
			definition = Reference( hud );
		}
		return null;
	}

	(int Row, TagEntry Bitmap, int Index)? FindHudPage( int hud, string widget, int layout, bool elite )
	{
		int widgets = Block( hud + 8 );
		for ( int i = 0; i < map.ReadInt32( hud + 8 ); i++ )
		{
			int row = widgets + i * 100;
			if ( map.ResolveStringId( map.ReadInt32( row ) ) != widget ) continue;
			var bitmap = Reference( row + 0x20 );
			if ( bitmap is null ) return null;
			int sequence = map.ReadSpan( row + 0x30 + layout, 1 )[0];
			int sequences = Address( bitmap ) + 60;
			int count = map.ReadInt32( sequences );
			// HUD sequence zero addresses the first image when the bitmap has no sequences.
			int index = 0;
			if ( count == 0 )
			{
				if ( sequence != 0 || map.ReadInt32( sequences + 8 ) <= 0 ) return null;
			}
			else
			{
				if ( sequence >= count ) return null;
				int selected = Block( sequences ) + sequence * 60;
				index = Short( selected + 32 );
				if ( elite && Short( selected + 34 ) > 1 ) index++;
			}
			return (row, bitmap, index);
		}
		return null;
	}

	protected static ProjectileOrientation ReadOrientation( int flags ) => (ProjectileOrientation)flags &
		(ProjectileOrientation.AlongVelocity | ProjectileOrientation.SteeringAdjustsOrientation
			| ProjectileOrientation.CanTrackBehind | ProjectileOrientation.RobotronSteering);

	protected ProjectileSpeedProfile ReadSpeedProfile( int projectile )
	{
		int flags = map.ReadInt32( projectile + 0xbc );
		bool scaled = (flags & (1 << 10)) != 0;
		float initial = Float( projectile + 0x17c ), final = Float( projectile + 0x180 );
		float inverse = initial > 0 && final > 0 && initial != final ? Float( projectile + 0x194 ) / 120 : 0;
		if ( !scaled && inverse <= 0 ) return null;
		float npcScale = 1;
		if ( scaled && map.Globals is {} globals )
		{
			int difficulty = Address( globals ) + 0xf8;
			if ( map.ReadInt32( difficulty ) > 0 ) npcScale = Float( Block( difficulty ) + 0xa4 );
		}
		return new( final * 120, Float( projectile + 0x18c ) * 120, inverse, scaled, npcScale );
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
		var first = FiringSounds( barrel, index, dry ).FirstOrDefault();
		marker = first?.Marker;
		return first?.Sound;
	}

	protected IEnumerable<WeaponSound> FiringSounds( int barrel, int index = 0, bool dry = false )
	{
		if ( index >= map.ReadInt32( barrel + 0xe4 ) ) yield break;
		var effect = Reference( Block( barrel + 0xe4 ) + index * 52 + (dry ? 20 : 4) );
		SoundEvent LoadSound( TagEntry tag ) => GameResource.Load<SoundEvent>( Host.AssetPath( map, tag.TagPath, "sound" ) );
		if ( effect is { PrimaryClass: "!dns" } )
		{
			yield return new( LoadSound( effect ), "root" );
			yield break;
		}
		if ( effect is not { PrimaryClass: "effe", DataSize: > 0 } ) yield break;
		int events = Address( effect ) + 20;
		if ( map.ReadInt32( events ) == 0 ) yield break;
		int locations = Address( effect ) + 12;
		foreach ( var part in SoundParts( Block( events ) ) )
		{
			int location = Short( part + 4 );
			var marker = (uint)location < map.ReadInt32( locations )
				? map.ResolveStringId( map.ReadInt32( Block( locations ) + location * 4 ) ) : null;
			yield return new( LoadSound( Reference( part + 12 ) ), marker );
		}
	}

	protected int FirstSoundPart( int effectEvent ) => SoundParts( effectEvent ).FirstOrDefault( -1 );

	IEnumerable<int> SoundParts( int effectEvent )
	{
		int parts = effectEvent + 24;
		for ( int i = 0, part = Block( parts ); i < map.ReadInt32( parts ); i++, part += 56 )
		{
			if ( Reference( part + 12 ) is { PrimaryClass: "!dns" } sound && sound.TagPath.StartsWith( @"sound\", StringComparison.OrdinalIgnoreCase ) )
				yield return part;
		}
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
		gun.Assassination = strike.Assassination;
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
		BodyDamageScales( Address( globals ), effect ), (map.ReadInt32( effect + 0x14 ) & 0x20) != 0, Short( effect + 0x10 ) == 2 );

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
		if ( gun is HandheldWeapon handheld ) WeaponScreen.Attach( handheld, renderer );
		gun.DisplayName = title;
		gun.DamageAccelerationScale = Float( Header + 0x14 );
		int flags = map.ReadInt32( Header + 0x12c );
		gun.DualWieldable = (flags & (1 << 22)) != 0;
		gun.GripMarker = map.ResolveStringId( map.ReadInt32( Header + 0x284 ) );
		gun.NpcOnly = (flags & (1 << 29)) != 0;
		(gun.AnimationClass, gun.AnimationType) = AnimationIdentifiers;
		var globals = Globals;
		gun.ZoomLevels = Short( Header + 0x1fe );
		gun.AutoAimAngle = Float( Header + 0x208 );
		gun.AutoAimRange = Float( Header + 0x20c ) * 120;
		gun.MagnetismAngle = Float( Header + 0x210 );
		gun.MagnetismRange = Float( Header + 0x214 ) * 120;
		if ( map.ReadInt32( Address( globals ) + 0xf0 ) > 0 )
		{
			int control = Block( Address( globals ) + 0xf0 );
			gun.VerticalAimOffset = Float( control + 0x1c );
			gun.PickupHoldSeconds = Float( control + 0x7c );
			gun.MagnetismFriction = Float( control );
			gun.MagnetismAdhesion = Float( control + 4 );
			gun.InconsequentialTargetScale = Float( control + 8 );
		}
		if ( gun.ZoomLevels > 0 )
		{
			gun.ZoomRange = new Vector2( Float( Header + 0x200 ), Float( Header + 0x204 ) );
			gun.ZoomInSound = ZoomSound( Header + 0x26c );
			gun.ZoomOutSound = ZoomSound( Header + 0x274 );
		}
		else if ( (map.ReadInt32( Header + 0x12c ) & (1 << 16)) == 0 && map.ReadInt32( Address( globals ) + 0x130 ) > 0 )
		{
			int info = Block( Address( globals ) + 0x130 );
			gun.ZoomLevels = map.ReadInt32( info + 0xb8 );
			gun.ZoomRange = new Vector2( Float( info + 0xbc ), Float( info + 0xc0 ) );
			gun.UsesBinoculars = gun.ZoomLevels > 0;
		}
		int players = Address( globals ) + 0x138;
		if ( map.ReadInt32( players ) > 0 && Reference( Block( players ) + 0xb0 ) is { PrimaryClass: "dpib", DataSize: > 0 } body
			&& ModelPath( Host, map, body ) is {} path && Host.GetByPath( path ) is Halo2ModelLoader model )
		{
			gun.PlayerShieldMaterial = model.ShieldMaterial;
			if ( model.GetAnimationContext( "combat", gun.AnimationClass, gun.AnimationType )?.Overlays.TryGetValue( "put_away", out int index ) == true
				&& model.GetClipInfo( index ) is {} clip )
			{
				gun.DefaultPutAwayFrames = clip.Frames;
				gun.DefaultPutAwayRelease = Weapon.ReleaseFrame( clip );
			}
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

	internal static Halo2ModelLoader CreateRig( MapFile map, TagEntry weapon, TagEntry globals, int index, bool offhand = false )
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
		int flags = map.ReadInt32( map.VaToOffset( (int)weapon.DataVirtualAddress ) + 0x12c );
		return new Halo2ModelLoader( map, arms, biped: biped, graph: graph, firstPersonWeapon: gun, offhandRig: offhand,
			dualWieldable: (flags & (1 << 22)) != 0 );
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
