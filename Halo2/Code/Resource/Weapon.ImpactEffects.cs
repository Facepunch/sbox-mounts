using System;
using Halo2.Blam;
using Sandbox.Mounting;
using static Halo2.PelletImpactEffect;

namespace Halo2;

internal abstract partial class WeaponLoader
{
	protected Dictionary<string, TagEntry> ReadMaterialBank( int materials, TagEntry source, int referenceOffset, int action, int bankOffset )
	{
		var result = new Dictionary<string, TagEntry>( StringComparer.OrdinalIgnoreCase );
		if ( source is null || Reference( Address( source ) + referenceOffset ) is not { PrimaryClass: "toof" } foot ) return result;
		int actions = Address( foot );
		if ( map.ReadInt32( actions ) <= action ) return result;
		int bank = Block( actions ) + action * 24 + bankOffset;
		int entries = map.ReadInt32( bank );
		if ( entries == 0 ) return result;
		int count = map.ReadInt32( materials ), rows = Block( materials );
		var contacts = new Dictionary<int, TagEntry>();
		int entry = Block( bank );
		for ( int i = 0; i < entries; i++, entry += 24 ) contacts[Short( entry + 20 )] = Reference( entry );
		for ( int i = 0; i < count; i++ )
		{
			if ( map.ResolveStringId( map.ReadInt32( rows + i * 0xb4 ) ) is not {} name ) continue;
			int index = i;
			for ( int depth = 0; (uint)index < count && depth < count; depth++ )
			{
				if ( contacts.TryGetValue( index, out var sound ) )
				{
					result[name] = sound;
					break;
				}
				index = Short( rows + index * 0xb4 + 8 );
			}
		}
		return result;
	}

	protected Dictionary<string, int> ReadSoundBank( Dictionary<string, TagEntry> materials, List<CuePlayback> cues, Dictionary<TagEntry, int> indices )
	{
		int CueIndex( TagEntry tag )
		{
			if ( tag?.PrimaryClass != "!dns" ) return 0;
			if ( indices.TryGetValue( tag, out int index ) ) return index;
			if ( Host.GetByPath( Host.AssetPath( map, tag.TagPath, "sound" ) ) is not Halo2SoundLoader sound ) return 0;
			cues.Add( sound.Cue );
			return indices[tag] = cues.Count;
		}

		return materials?.ToDictionary( entry => entry.Key, entry => CueIndex( entry.Value ), StringComparer.OrdinalIgnoreCase ) ?? new();
	}

	protected string ImpactPath( TagEntry tag ) => $"{map.Header.MapName}/{tag.TagPath.Replace( '\\', '/' )}.prefab";

	protected sealed class ImpactLoader( WeaponLoader source, TagEntry tag, bool includeSounds = false ) : ResourceLoader<Halo2Mount>
	{
		PrefabFile prefab;
		protected override object Load()
		{
			var builder = new PrefabBuilder().WithName( Path );
			using ( builder.Scope() )
				new GameObject().AddComponent<PelletImpactEffect>().Visuals = source.ReadContactVisuals( tag, includeSounds );
			return prefab = builder.Create();
		}

		protected override void Shutdown()
		{
			PrefabBuilder.Destroy( prefab );
			base.Shutdown();
		}
	}

	protected ContactVisuals ReadContactVisuals( TagEntry contact, bool includeSounds, bool atMarker = false )
	{
		int events = Address( contact ) + 20;
		int eventCount = map.ReadInt32( events );
		if ( eventCount == 0 ) return default;
		int row = Block( events );
		var timeline = new ContactEvent[eventCount];
		for ( int i = 0; i < eventCount; i++, row += 56 )
		{
			int parts = map.ReadInt32( row + 24 );
			var marks = new List<ImpactDecal>();
			var lights = new List<ImpactLight>();
			var lenses = new List<ImpactLens>();
			var sounds = new List<SoundEvent>();
			if ( parts > 0 )
			{
				int part = Block( row + 24 );
				for ( int j = 0; j < parts; j++, part += 56 )
				{
					if ( ReadContactMark( contact, part ) is {} mark ) marks.Add( mark );
					else if ( ReadLight( part ) is {} light ) lights.Add( light );
					else if ( atMarker && ReadLens( contact, part ) is {} lens ) lenses.Add( lens );
					else if ( includeSounds && Reference( part + 12 ) is { PrimaryClass: "!dns" } sound &&
						GameResource.Load<SoundEvent>( Host.AssetPath( map, sound.TagPath, "sound" ) ) is {} cue ) sounds.Add( cue );
				}
			}
			timeline[i] = new ContactEvent( Float( row + 4 ), new Vector2( Float( row + 8 ), Float( row + 12 ) ),
				new Vector2( Float( row + 16 ), Float( row + 20 ) ), marks.ToArray(), map.ReadInt32( row + 48 ) > 0, ReadImpactEmitters( contact, row ) ) { Lights = lights.ToArray(), Lenses = lenses.ToArray(), Sounds = sounds.ToArray() };
		}
		return timeline.Any( e => e.Marks.Length > 0 || e.HasParticles || e.Lights.Length > 0 || e.Lenses.Length > 0 || e.Sounds.Length > 0 ) ? new ContactVisuals( timeline ) : null;
	}

	ImpactLens ReadLens( TagEntry contact, int part )
	{
		if ( Reference( part + 12 ) is not { PrimaryClass: "snel", DataSize: >= 100 } tag ) return null;
		int header = Address( tag );
		int rotationFunction = Short( header + 44 );
		if ( rotationFunction is not (0 or 1) ) return null;
		if ( Reference( header + 32 ) is not { PrimaryClass: "mtib" } bitmap ) return null;
		int locations = Address( contact ) + 12;
		int location = Short( part + 4 );
		if ( (uint)location >= map.ReadInt32( locations ) ) return null;
		var marker = map.ResolveStringId( map.ReadInt32( Block( locations ) + location * 4 ) );
		string path = Host.AssetPath( map, bitmap.TagPath, "vtex" );
		int count = map.ReadInt32( header + 64 );
		if ( count <= 0 ) return null;
		var reflections = new LensReflection[count];
		int row = Block( header + 64 );
		for ( int i = 0; i < count; i++, row += 48 )
		{
			int index = Short( row + 4 );
			if ( BitmapTag.Read( map, bitmap, index ) is null || Short( row ) != 0 || Float( row + 8 ) != 0 ) return null;
			reflections[i] = new( index == 0 ? path : $"{path}/{index}.vtex",
				new Vector2( Float( row + 16 ), Float( row + 20 ) ) * 120, new( Float( row + 24 ), Float( row + 28 ) ),
				new( Float( row + 36 ), Float( row + 40 ), Float( row + 44 ) ), Float( row + 32 ), Float( row + 12 ).DegreeToRadian() );
		}
		return new( marker, Float( header + 16 ) * 120, rotationFunction == 1 ? Float( header + 48 ) : 0, reflections );
	}

	ImpactLight ReadLight( int part )
	{
		if ( Reference( part + 12 ) is not { PrimaryClass: "hgil", DataSize: >= 228 } tag ) return null;
		int p = Address( tag );
		if ( Short( p + 4 ) != 0 || Short( p + 0xb6 ) != 0 || Float( p + 0xb0 ) <= 0 ||
			map.ReadInt32( p + 0x34 ) != 0 || map.ReadInt32( p + 0xc4 ) != 0 || map.ReadInt32( p + 0xcc ) != 0 || map.ReadInt32( p + 0xd4 ) != 0 ) return null;
		Color ColorAt( int i ) => new( Float( p + i ), Float( p + i + 4 ), Float( p + i + 8 ) );
		return new( Float( p + 0xb0 ), new Vector2( Float( p + 8 ), Float( p + 12 ) ) * (120 * Float( p + 24 )),
			ColorAt( 0x58 ), ColorAt( 0x64 ), new Vector2( Float( p + 0x70 ), Float( p + 0x74 ) ) * 2 );
	}

	ImpactDecal ReadContactMark( TagEntry contact, int part )
	{
		if ( Reference( part + 12 ) is not { PrimaryClass: "aced" } tag ) return default;
		int header = Address( tag );
		int layer = Short( header + 4 );
		if ( layer is not (2 or 3) || Reference( header + 8 ) is not null ) return default;
		DecalProjection projection = null;
		if ( layer == 3 )
		{
			int locations = Address( contact ) + 12;
			int index = Short( part + 4 );
			if ( (uint)index >= map.ReadInt32( locations ) ) return default;
			var marker = map.ResolveStringId( map.ReadInt32( Block( locations ) + index * 4 ) );
			if ( marker is not ("forward" or "gravity") ) return default;
			projection = new DecalProjection( marker, new Vector2( Float( part + 20 ), Float( part + 24 ) ) * 120, Float( part + 28 ) );
		}
		if ( Reference( header + 136 ) is not {} bitmap ) return default;
		int sequences = Address( bitmap ) + 60;
		int count = map.ReadInt32( sequences );
		string path = Host.AssetPath( map, bitmap.TagPath, "vtex" );
		if ( count > 0 ) path += "/sheet.vtex";
		var texture = Texture.Load( path );
		if ( !texture.IsValid() || texture.IsError || (count > 0 && texture.SequenceCount != count) ) return default;

		var sizes = new Vector2[System.Math.Max( count, 1 )];
		var centers = new Vector2[sizes.Length];
		sizes[0] = Vector2.One * 2;
		if ( count > 0 )
		{
			int sequence = Block( sequences );
			var scale = new Vector2( texture.Width, texture.Height ) / Float( header + 164 );
			for ( int i = 0; i < count; i++, sequence += 60 )
			{
				int sprite = Block( sequence + 52 );
				var bounds = Halo2TextureLoader.SpriteBounds( map, sprite + 8 );
				sizes[i] = bounds.Size * scale;
				centers[i] = (bounds.Size * 0.5f - new Vector2( Float( sprite + 24 ), Float( sprite + 28 ) )) * scale;
			}
		}
		float lifetime = Float( header + 52 );
		var tint = new ParticleGradient
		{
			Type = ParticleGradient.ValueType.Gradient,
			GradientA = new Gradient( new Gradient.ColorFrame( 0, Color.White ), new Gradient.ColorFrame( 1 - Float( header + 60 ) / lifetime, Color.White ),
				new Gradient.ColorFrame( 1, Color.White.WithAlpha( 0 ) ) )
		};
		float rejection = Float( header + 24 );
		return new ImpactDecal( tag.TagPath, tag.TagPath.Split( '\\' )[^1], path,
			new Vector2( Float( part + 40 ), Float( part + 44 ) ), new Vector2( Float( header + 16 ), Float( header + 20 ) ) * 120,
			sizes, centers, Short( header + 6 ), Float( header + 20 ) * 120 * (rejection == 0 ? 1 : rejection), lifetime, tint ) { Projection = projection, Layer = layer };
	}
}
