using Halo2.Blam;
using Sandbox.Mounting;
using System;

namespace Halo2;

internal sealed partial class HandheldWeaponLoader
{
	TagEntry LightVolume( int projectile )
	{
		int attachments = projectile + 0x94;
		for ( int i = 0, row = Block( attachments ); i < map.ReadInt32( attachments ); i++, row += 24 )
			if ( Reference( row ) is { PrimaryClass: "2SGM" } tag ) return tag;
		return null;
	}

	string VolumePath( int projectile ) => LightVolume( projectile ) is {} tag
		? Host.AssetPath( map, tag.TagPath, "prefab" ) : null;

	void RegisterLightVolumes( MountContext context, HashSet<string> registered )
	{
		int barrels = Header + 0x2d0;
		for ( int i = 0, row = Block( barrels ); i < map.ReadInt32( barrels ); i++, row += 0xec )
		{
			var projectile = Reference( row + 0x8c );
			if ( projectile is null || LightVolume( Address( projectile ) ) is not {} volume || !registered.Add( ImpactPath( volume ) ) ) continue;
			context.Add( ResourceType.PrefabFile, ImpactPath( volume ), new VolumeLoader( this, projectile, volume ) { Flags = ResourceFlags.DeveloperOnly | ResourceFlags.Effect } );
		}
	}

	sealed class VolumeLoader( HandheldWeaponLoader source, TagEntry projectile, TagEntry tag ) : ResourceLoader<Halo2Mount>
	{
		PrefabFile prefab;
		protected override object Load()
		{
			var builder = new PrefabBuilder().WithName( Path );
			using ( builder.Scope() )
			{
				var bolt = new GameObject().AddComponent<ProjectileLightVolume>();
				source.ReadVolume( tag, bolt );
				int header = source.Address( projectile );
				bolt.Sound = source.ReadSoundTrack( header, "" );
				source.ReadAttachments( header, bolt );
			}
			return prefab = builder.Create();
		}

		protected override void Shutdown()
		{
			PrefabBuilder.Destroy( prefab );
			base.Shutdown();
		}
	}

	void ReadAttachments( int projectile, ProjectileLightVolume bolt )
	{
		int attachments = projectile + 0x94;
		for ( int i = 0, row = Block( attachments ); i < map.ReadInt32( attachments ); i++, row += 24 )
		{
			var tag = Reference( row );
			if ( tag is { PrimaryClass: "effe" } )
				bolt.Glow = ReadMuzzleFlash( tag, attachmentMarker: "" );
			if ( tag is not { PrimaryClass: "hgil" } ) continue;
			int light = Address( tag );
			if ( Short( light + 4 ) != 0 || Float( light + 0xb0 ) != 0 ) continue;
			var point = bolt.GameObject.AddComponent<PointLight>();
			point.Radius = Float( light + 12 ) * Float( light + 24 ) * 120;
			point.LightColor = new Color( Float( light + 0x64 ), Float( light + 0x68 ), Float( light + 0x6c ) );
			point.Intensity = Float( light + 0x74 ) * 2;
			point.Shadows = false;
		}
	}

	void ReadVolume( TagEntry tag, ProjectileLightVolume volume )
	{
		int header = Address( tag );
		volume.Falloff = Float( header ) * 120;
		volume.Cutoff = Float( header + 4 ) * 120;
		volume.Layers = new VolumeLayer[map.ReadInt32( header + 8 )];
		for ( int i = 0, row = Block( header + 8 ); i < volume.Layers.Length; i++, row += 0x98 )
		{
			var bitmap = Reference( row + 4 ) ?? Reference( Block( Address( map.Tags.First( t => t.PrimaryClass == "gtam" ) ) + 0x108 ) + 0x30 );
			var offset = ReadCurveRange( Block( row + 0x10 ), 120 );
			var radius = ReadCurveRange( Block( row + 0x18 ), 1 );
			var alpha = ReadCurveRange( Block( row + 0x20 ), 1 );
			int color = Block( row + 0x28 );
			var tint = ReadCurveRange( color, 1, fraction: true );
			Color32 Tint( int field )
			{
				int rgb = map.ReadInt32( color + field );
				return new Color32( (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb );
			}
			int aspect = Block( row + 0x38 );
			var width = ReadCurveRange( Block( aspect ), 1 );
			var height = ReadCurveRange( Block( aspect + 8 ), 1 );
			var sprites = new VolumeSprite[map.ReadInt32( row + 12 )];
			// These volumes distribute sprites by a geometric progression of their authored radii.
			float start = radius.Evaluate( 0, 0 ), end = radius.Evaluate( 1, 0 );
			start = MathF.Max( start, end / 256 );
			end = MathF.Max( end, start / 256 );
			float current = start, growth = MathF.Pow( end / start, 1f / (sprites.Length - 1) );
			for ( int j = 0; j < sprites.Length; j++, current *= growth )
			{
				float phase = ((current - start) / (end - start)).Clamp( 0, 1 );
				sprites[j] = new( offset.Evaluate( phase, 0 ), radius.Evaluate( phase, 0 ) * 120,
					new( width.Evaluate( phase, 0 ), height.Evaluate( phase, 0 ) ), alpha.Evaluate( phase, 0 ),
					MuzzleFlash.InterpolateTint( Tint( 4 ), Tint( 16 ), tint.Evaluate( phase, 0 ) ) );
			}
			volume.Layers[i] = new( Host.AssetPath( map, bitmap.TagPath, "vtex" ), sprites, Float( aspect + 0x10 ), Float( aspect + 0x14 ), Float( aspect + 0x18 ) );
		}
	}
}
