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

	string ProjectileEffectPath( TagEntry projectile )
	{
		var tag = LightVolume( Address( projectile ) ) ?? projectile;
		return Host.AssetPath( map, tag.TagPath, "prefab" );
	}

	void RegisterProjectileEffects( MountContext context, HashSet<string> registered )
	{
		int barrels = Header + 0x2d0;
		for ( int i = 0, row = Block( barrels ); i < map.ReadInt32( barrels ); i++, row += 0xec )
		{
			var projectile = Reference( row + 0x8c );
			if ( projectile is null ) continue;
			var volume = LightVolume( Address( projectile ) );
			var path = ImpactPath( volume ?? projectile );
			if ( !registered.Add( path ) ) continue;
			context.Add( ResourceType.PrefabFile, path, new ProjectileEffectLoader( this, projectile, volume ) { Flags = ResourceFlags.DeveloperOnly | ResourceFlags.Effect } );
		}
	}

	sealed class ProjectileEffectLoader( HandheldWeaponLoader source, TagEntry projectile, TagEntry tag ) : ResourceLoader<Halo2Mount>
	{
		PrefabFile prefab;
		protected override object Load()
		{
			var builder = new PrefabBuilder().WithName( Path );
			using ( builder.Scope() )
			{
				var root = new GameObject();
				if ( tag is not null ) source.ReadVolume( tag, root.AddComponent<ProjectileLightVolume>() );
				source.ReadAttachments( projectile, root );
			}
			return prefab = builder.Create();
		}

		protected override void Shutdown()
		{
			PrefabBuilder.Destroy( prefab );
			base.Shutdown();
		}
	}

	void ReadAttachments( TagEntry projectile, GameObject root )
	{
		int header = Address( projectile );
		var markers = new Dictionary<string, GameObject>();
		GameObject AtMarker( string name )
		{
			if ( string.IsNullOrEmpty( name ) ) return root;
			if ( markers.TryGetValue( name, out var emitter ) ) return emitter;
			var path = ModelPath( Host, map, projectile );
			if ( path is null || Model.Load( path )?.Attachments.GetTransform( name ) is not {} pose ) return root;
			emitter = new GameObject( root, true, name );
			emitter.LocalTransform = pose;
			return markers[name] = emitter;
		}

		if ( ReadSoundTrack( header, "" ) is {} track )
			AtMarker( track.Marker ).AddComponent<ProjectileSound>().Track = track;
		int attachments = header + 0x94;
		for ( int i = 0, row = Block( attachments ); i < map.ReadInt32( attachments ); i++, row += 24 )
		{
			var tag = Reference( row );
			if ( tag is { PrimaryClass: "effe" } && root.GetComponent<ProjectileLightVolume>() is {} volume &&
				string.IsNullOrEmpty( map.ResolveStringId( map.ReadInt32( row + 16 ) ) ) )
				volume.Glow = ReadMuzzleFlash( tag, attachmentMarker: "" );
			if ( tag is { PrimaryClass: "effe" } && map.ResolveStringId( map.ReadInt32( row + 16 ) ) == "acceleration_range" &&
				ReadMuzzleFlash( tag, attachmentMarker: "" ) is { Length: > 0 } exhaust )
				AtMarker( map.ResolveStringId( map.ReadInt32( row + 8 ) ) ).AddComponent<ProjectileExhaust>().Effects = exhaust;
			if ( tag is not { PrimaryClass: "hgil" } ) continue;
			int light = Address( tag );
			if ( Short( light + 4 ) != 0 || Float( light + 0xb0 ) != 0 ) continue;
			var point = AtMarker( map.ResolveStringId( map.ReadInt32( row + 8 ) ) ).AddComponent<PointLight>();
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
			var curves = new VolumeCurves( offset, radius, width, height, alpha, tint, Tint( 4 ), Tint( 16 ) );
			int count = map.ReadInt32( row + 12 );
			var layer = new VolumeLayer( Host.AssetPath( map, bitmap.TagPath, "vtex" ), [], Float( aspect + 0x10 ), Float( aspect + 0x14 ), Float( aspect + 0x18 ) );
			int flags = map.ReadInt32( row + 0x5c );
			if ( (flags & 3) == 0 )
			{
				var phases = new Vector2[count];
				int first = Block( row + 0x60 );
				for ( int j = 0; j < count; j++ )
					phases[j] = new( Float( first + j * 8 ), Float( first + j * 8 + 4 ) );
				volume.Layers[i] = layer with { Phases = phases, Curves = curves };
				continue;
			}
			var sprites = new VolumeSprite[count];
			if ( (flags & 2) != 0 )
			{
				for ( int j = 0; j < sprites.Length; j++ )
					sprites[j] = curves.Sample( (float)j / (sprites.Length - 1), 0 );
				volume.Layers[i] = layer with { Sprites = sprites };
				continue;
			}
			// These volumes distribute sprites by a geometric progression of their authored radii.
			float start = radius.Evaluate( 0, 0 ), end = radius.Evaluate( 1, 0 );
			start = MathF.Max( start, end / 256 );
			end = MathF.Max( end, start / 256 );
			float current = start, growth = MathF.Pow( end / start, 1f / (sprites.Length - 1) );
			for ( int j = 0; j < sprites.Length; j++, current *= growth )
			{
				float phase = ((current - start) / (end - start)).Clamp( 0, 1 );
				sprites[j] = curves.Sample( phase, 0 );
			}
			volume.Layers[i] = layer with { Sprites = sprites };
		}
	}
}
