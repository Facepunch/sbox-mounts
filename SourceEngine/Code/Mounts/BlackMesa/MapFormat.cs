using System;

namespace Source1.BlackMesa;

internal sealed partial class MapFormat : BspFormat
{
	private readonly Dictionary<string, CubemapVolume> cubemaps = new( StringComparer.OrdinalIgnoreCase );

	internal override int? MultiblendLump => 61;
	internal override PropLayout StaticProps( int version ) => version == 11
		? new( 80, FlagsOffset: 64, TintOffset: 72 ) : base.StaticProps( version );

	internal override void IndexMap( string path, IReadOnlyDictionary<string, SourceGameMount.SourceFile> files )
	{
		IndexThumbnail( path, files );
		if ( !files.TryGetValue( Path.ChangeExtension( path, ".pcv" ), out var file ) ) return;
		try
		{
			using var stream = file.Open();
			var map = Path.GetFileNameWithoutExtension( path );
			foreach ( var volume in CubemapVolume.Read( stream ) )
			{
				var p = volume.Origin;
				cubemaps[$"maps/{map}/c{(int)p.x}_{(int)p.y}_{(int)p.z}"] = volume;
			}
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException )
		{
			Log.Warning( $"{path}: could not read cubemap volumes: {e.Message}" );
		}
	}

	internal override void BindCubemap( Material material, string path )
	{
		if ( !cubemaps.TryGetValue( path, out var volume ) ) return;
		material.Set( "g_vSourceEnvOrigin", new Vector4( volume.Origin, 1 ) );
		material.Set( "g_vSourceEnvMins", volume.Mins - volume.Origin );
		material.Set( "g_vSourceEnvMaxs", volume.Maxs - volume.Origin );
	}

	internal override void ReadVertexLighting( SourceGameMount mount, StaticPropLighting lighting, int index, int checksum, bool hdr )
	{
		if ( !hdr || !lighting.ReadVertices( mount, $"sp_rgbe_{index}.vhv", checksum ) )
			base.ReadVertexLighting( mount, lighting, index, checksum, hdr );
	}
}
