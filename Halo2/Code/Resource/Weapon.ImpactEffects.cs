using System;
using Halo2.Blam;
using Sandbox.Mounting;
using static Halo2.PelletImpactEffect;

namespace Halo2;

internal abstract partial class WeaponLoader
{
	protected Dictionary<string, TagEntry> ReadMaterialBank( int materials, TagEntry source, int referenceOffset, int action, int bankOffset, Dictionary<string, string> materialParents = null ) =>
		ReadMaterialRows( materials, source, referenceOffset, action, bankOffset, materialParents ).ToDictionary( e => e.Key, e => Reference( e.Value ), StringComparer.OrdinalIgnoreCase );

	protected Dictionary<string, string[]> ReadProjectileSounds( int materials, TagEntry projectile, int action, Dictionary<string, string> parents = null ) =>
		ReadProjectileBank( materials, projectile, action, false, parents ).ToDictionary( e => e.Key, e => e.Value.Select( tag => Host.AssetPath( map, tag.TagPath, "sound" ) ).ToArray() );

	protected Dictionary<string, TagEntry[]> ReadProjectileBank( int materials, TagEntry projectile, int action, bool visual, Dictionary<string, string> parents )
	{
		var entries = ReadMaterialRows( materials, projectile, 0x54, action, visual ? 16 : 8, parents );
		var surfaceTags = new Dictionary<string, TagEntry>( StringComparer.OrdinalIgnoreCase );
		for ( int i = 0, row = Block( materials ); i < map.ReadInt32( materials ); i++, row += 0xb4 )
			if ( map.ResolveStringId( map.ReadInt32( row ) ) is { Length: > 0 } name )
				surfaceTags[name] = Reference( row + (visual ? 0x70 : 0x38) );

		var result = new Dictionary<string, TagEntry[]>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (name, entry) in entries )
		{
			var surface = map.ReadSpan( entry + 22, 1 )[0] == 1 ? surfaceTags.GetValueOrDefault( name ) : null;
			result[name] = new[] { Reference( entry ), surface, Reference( entry + 8 ) }
				.Where( tag => tag?.PrimaryClass == (visual ? "effe" : "!dns") ).ToArray();
		}
		return result;
	}

	Dictionary<string, int> ReadMaterialRows( int materials, TagEntry source, int referenceOffset, int action, int bankOffset, Dictionary<string, string> materialParents = null )
	{
		var result = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		if ( source is null || Reference( Address( source ) + referenceOffset ) is not { PrimaryClass: "toof" } foot ) return result;
		int actions = Address( foot );
		int countActions = map.ReadInt32( actions ), first = Block( actions ) + bankOffset;
		while ( action >= countActions || map.ReadInt32( first + action * 24 ) == 0 )
		{
			action = action switch { 16 or 17 => 15, 19 => 16, 20 => 19, _ => -1 };
			if ( action < 0 ) return result;
		}
		int bank = first + action * 24;
		int entries = map.ReadInt32( bank );
		if ( entries == 0 ) return result;
		int count = map.ReadInt32( materials ), rows = Block( materials );
		bool useAuthoredNames = materialParents is not null;
		if ( materialParents is null )
		{
			materialParents = new( StringComparer.OrdinalIgnoreCase );
			for ( int i = 0; i < count; i++ )
			{
				int row = rows + i * 0xb4, parent = Short( row + 8 );
				if ( map.ResolveStringId( map.ReadInt32( row ) ) is { Length: > 0 } name )
					materialParents[name] = (uint)parent < count ? map.ResolveStringId( map.ReadInt32( rows + parent * 0xb4 ) ) : null;
			}
		}
		var contacts = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		for ( int i = 0, entry = Block( bank ); i < entries; i++, entry += 24 )
		{
			// Cutscene maps retain authored names for materials that have no local index.
			int index = Short( entry + 20 );
			int field = (uint)index < count ? rows + index * 0xb4 : useAuthoredNames ? entry + 16 : -1;
			if ( field >= 0 && map.ResolveStringId( map.ReadInt32( field ) ) is { Length: > 0 } name )
				contacts.TryAdd( name, entry );
		}
		foreach ( var material in materialParents.Keys )
		{
			string name = material;
			for ( int depth = 0; !string.IsNullOrEmpty( name ) && depth < materialParents.Count; depth++ )
			{
				if ( contacts.TryGetValue( name, out int entry ) )
				{
					result[material] = entry;
					break;
				}
				if ( !materialParents.TryGetValue( name, out name ) ) break;
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

		protected override ResourceLoader ResolveChild( string name, out ResourceType type )
		{
			type = ResourceType.Model;
			var parts = name.Split( '.' );
			if ( parts.Length != 3 || parts[2] != "vmdl" || !int.TryParse( parts[0], out int index ) || !int.TryParse( parts[1], out int variant ) ) return null;
			if ( source.map.GetTag( index ) is not { PrimaryClass: "MTRP", DataSize: >= 0xe0 } model ) return null;
			int count = source.map.ReadInt32( source.Address( model ) + 0x80 );
			return (uint)variant < count ? new ParticleMeshLoader( source, model, variant ) : null;
		}

		protected override void Shutdown()
		{
			PrefabBuilder.Destroy( prefab );
			base.Shutdown();
		}
	}

	sealed class ParticleMeshLoader( WeaponLoader source, TagEntry tag, int variant ) : ResourceLoader<Halo2Mount>
	{
		protected override object Load()
		{
			int header = source.Address( tag );
			int span = source.Block( header + 0x80 ) + variant * 8;
			int first = (ushort)source.Short( span + 4 ), count = (ushort)source.Short( span + 6 );
			var vertices = new Vertex[source.map.ReadInt32( header + 0x88 )];
			int row = source.Block( header + 0x88 );
			Vector3 Vector( int offset ) => new( source.Float( offset ), source.Float( offset + 4 ), source.Float( offset + 8 ) );
			for ( int i = 0; i < vertices.Length; i++, row += 0x38 )
				vertices[i] = new Vertex( Vector( row ), Vector( row + 12 ), Vector( row + 24 ), new Vector4( source.Float( row + 48 ), source.Float( row + 52 ), 0, 0 ) );
			int start = source.Block( header + 0x90 ) + first * 2;
			var indices = Enumerable.Range( 0, count ).Select( i => (int)(ushort)source.Short( start + i * 2 ) ).ToArray();
			var shader = source.Reference( header + 0x18 );
			var mesh = new Mesh( Material.Load( source.Host.AssetPath( source.map, shader.TagPath, "vmat" ) ) );
			mesh.CreateVertexBuffer( vertices.Length, vertices );
			mesh.CreateIndexBuffer( indices.Length, indices );
			var bounds = BBox.FromPositionAndSize( vertices[indices[0]].Position );
			foreach ( int index in indices ) bounds = bounds.AddPoint( vertices[index].Position );
			mesh.Bounds = bounds;
			return Model.Builder.WithName( Path ).AddMesh( mesh ).Create();
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

	protected ImpactLight ReadLight( int part )
	{
		if ( Reference( part + 12 ) is not { PrimaryClass: "hgil", DataSize: >= 228 } tag ) return null;
		int p = Address( tag );
		if ( Short( p + 4 ) != 0 || Short( p + 0xb6 ) is not (0 or 3 or 4) || Float( p + 0xb0 ) <= 0 ||
			map.ReadInt32( p + 0x34 ) != 0 || map.ReadInt32( p + 0xc4 ) != 0 || map.ReadInt32( p + 0xcc ) != 0 || map.ReadInt32( p + 0xd4 ) != 0 ) return null;
		Color ColorAt( int i ) => new( Float( p + i ), Float( p + i + 4 ), Float( p + i + 8 ) );
		return new( Float( p + 0xb0 ), new Vector2( Float( p + 8 ), Float( p + 12 ) ) * (120 * Float( p + 24 )),
			ColorAt( 0x58 ), ColorAt( 0x64 ), new Vector2( Float( p + 0x70 ), Float( p + 0x74 ) ) * 2 ) { FadePower = Short( p + 0xb6 ) switch { 3 => 2, 4 => 4, _ => 1 } };
	}

	ImpactDecal ReadContactMark( TagEntry contact, int part )
	{
		if ( Reference( part + 12 ) is not { PrimaryClass: "aced" } tag ) return default;
		int layer = Short( Address( tag ) + 4 );
		DecalProjection projection = null;
		if ( layer is 3 or 4 )
		{
			int locations = Address( contact ) + 12;
			int index = Short( part + 4 );
			if ( (uint)index >= map.ReadInt32( locations ) ) return default;
			var marker = map.ResolveStringId( map.ReadInt32( Block( locations ) + index * 4 ) );
			if ( marker is not ("forward" or "gravity") ) return default;
			projection = new DecalProjection( marker, new Vector2( Float( part + 20 ), Float( part + 24 ) ) * 120, Float( part + 28 ) );
		}
		var modifier = new Vector2( Float( part + 40 ), Float( part + 44 ) );
		ImpactDecal first = null, last = null;
		HashSet<TagEntry> visited = null;
		while ( tag?.PrimaryClass == "aced" )
		{
			int header = Address( tag );
			var next = Reference( header + 8 );
			if ( next is not null && !(visited ??= new()).Add( tag ) ) break;
			if ( Short( header + 4 ) is >= 2 and <= 5 && ReadDecalLayer( tag, header, modifier ) is {} mark )
			{
				if ( last is null ) first = mark;
				else last.Next = mark;
				last = mark;
			}
			tag = next;
		}
		if ( first is not null ) first.Projection = projection;
		return first;
	}

	ImpactDecal ReadDecalLayer( TagEntry tag, int header, Vector2 modifier )
	{
		int layer = Short( header + 4 );
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
		Color ColorAt( int i ) => new( Float( header + i ), Float( header + i + 4 ), Float( header + i + 8 ) );
		float rejection = Float( header + 24 );
		return new ImpactDecal( tag.TagPath, tag.TagPath.Split( '\\' )[^1], path,
			modifier, new Vector2( Float( header + 16 ), Float( header + 20 ) ) * 120,
			sizes, centers, Short( header + 6 ), Float( header + 20 ) * 120 * (rejection == 0 ? 1 : rejection),
			new( Float( header + 52 ), Float( header + 56 ) ), new( Float( header + 60 ), Float( header + 64 ) ),
			ColorAt( 28 ), ColorAt( 40 ) ) { Layer = layer };
	}
}
