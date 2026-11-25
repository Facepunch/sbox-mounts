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

	string ImpactPath( TagEntry tag ) => $"{map.Header.MapName}/{tag.TagPath.Replace( '\\', '/' )}.prefab";

	sealed class ImpactLoader( ShotgunLoader shotgun, TagEntry tag ) : ResourceLoader<Halo2Mount>
	{
		PrefabFile prefab;
		protected override object Load()
		{
			var builder = new PrefabBuilder().WithName( Path );
			using ( builder.Scope() )
				new GameObject().AddComponent<PelletImpactEffect>().Visuals = shotgun.ReadContactVisuals( tag );
			return prefab = builder.Create();
		}

		protected override void Shutdown()
		{
			PrefabBuilder.Destroy( prefab );
			base.Shutdown();
		}
	}

	ContactVisuals ReadContactVisuals( TagEntry contact )
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
			if ( parts > 0 )
			{
				int part = Block( row + 24 );
				for ( int j = 0; j < parts; j++, part += 56 )
					if ( ReadContactMark( part ) is {} mark ) marks.Add( mark );
			}
			timeline[i] = new ContactEvent( Float( row + 4 ), new Vector2( Float( row + 8 ), Float( row + 12 ) ),
				new Vector2( Float( row + 16 ), Float( row + 20 ) ), marks.ToArray(), map.ReadInt32( row + 48 ) > 0, ReadImpactEmitters( contact, row ) );
		}
		return timeline.Any( e => e.Marks.Length > 0 || e.HasParticles ) ? new ContactVisuals( timeline ) : null;
	}

	ImpactDecal ReadContactMark( int part )
	{
		if ( Reference( part + 12 ) is not { PrimaryClass: "aced" } tag ) return default;
		int header = Address( tag );
		if ( Short( header + 4 ) != 2 || Reference( header + 8 ) is not null ) return default;
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
			sizes, centers, Short( header + 6 ), Float( header + 20 ) * 120 * (rejection == 0 ? 1 : rejection), lifetime, tint );
	}
}
