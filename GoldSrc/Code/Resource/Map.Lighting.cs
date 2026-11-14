using System;
using System.IO;
using GoldSrc.Bsp;

namespace GoldSrc;

partial class MapLoader
{
	private const int MaxProbeVolumes = 24;
	private const int MaxProbesPerVolume = 1024;
	private const float ProbeSpacing = 64;
	private readonly List<Texture> _probeTextures = [];

	private void SpawnLightProbes( BspFile bsp )
	{
		_probeTextures.Clear();
		var open = bsp.Leaves.Where( leaf => !leaf.Solid && leaf.HasVolume ).Select( leaf => leaf.Bounds ).ToArray();
		if ( open.Length == 0 )
		{
			Log.Warning( $"{FilePath}: no open BSP leaves; model light probes were not created." );
			return;
		}
		var world = BBox.FromBoxes( open );
		var cells = new Dictionary<Vector3Int, BBox>();
		for ( float cellSize = 1024; ; cellSize *= 2 )
		{
			cells.Clear();
			foreach ( var leaf in open )
			{
				var p = (leaf.Center - world.Mins) / cellSize;
				var key = new Vector3Int( (int)MathF.Floor( p.x ), (int)MathF.Floor( p.y ), (int)MathF.Floor( p.z ) );
				cells[key] = cells.TryGetValue( key, out var bounds ) ? bounds.AddBBox( leaf ) : leaf;
			}
			if ( cells.Count <= MaxProbeVolumes ) break;
		}

		var root = new GameObject( false, "GoldSrc model lighting" );
		if ( bsp.Lighting.Length == 0 )
			Log.Warning( $"{FilePath}: BSP has no lightmap samples; using fullbright model probes." );
		int totalProbes = 0;
		int reflectionProbes = 0;
		foreach ( var cell in cells.Values )
		{
			var bounds = cell.Grow( 16 );
			var counts = ProbeCounts( bounds.Size );
			int probes = counts.x * counts.y * counts.z;
			var data = new byte[probes * 48];
			var spacing = bounds.Size / new Vector3( counts.x, counts.y, counts.z );
			var capturePosition = bounds.Center;
			float captureDistance = float.PositiveInfinity;
			for ( int z = 0; z < counts.z; z++ )
				for ( int y = 0; y < counts.y; y++ )
					for ( int x = 0; x < counts.x; x++ )
					{
						var position = bounds.Mins + spacing * new Vector3( x + 0.5f, y + 0.5f, z + 0.5f );
						if ( !FindProbePosition( bsp, ref position, spacing ) ) continue;
						float distance = (position - bounds.Center).LengthSquared;
						if ( distance < captureDistance )
						{
							capturePosition = position;
							captureDistance = distance;
						}
						var color = SampleFloorLight( bsp, position );
						int probe = x + counts.x * (y + counts.y * z);
						// Native ambient layers are +X, +Y, +Z, -X, -Y, -Z, not interleaved.
						for ( int side = 0; side < 6; side++ )
						{
							float lightcos = side == 2 ? -1 : side == 5 ? 1 : 0;
							float shade = bsp.Lighting.Length == 0 ? 1 : 1 - 0.9f * Math.Max( 0, (lightcos + 0.4953241f) / 1.4953241f );
							int offset = (side * probes + probe) * 8;
							WriteProbeHalf( data, offset, ProbeLinear( color.x * shade ) );
							WriteProbeHalf( data, offset + 2, ProbeLinear( color.y * shade ) );
							WriteProbeHalf( data, offset + 4, ProbeLinear( color.z * shade ) );
							WriteProbeHalf( data, offset + 6, 1 );
						}
					}
			var texture = Texture.CreateVolume( counts.x, counts.y, counts.z * 6, ImageFormat.RGBA16161616F )
				.WithName( $"{WorldModelName}/model_probes_{_probeTextures.Count}" ).WithData( data ).Finish();
			if ( texture is null || !texture.IsValid || texture.IsError )
				throw new InvalidOperationException( $"{FilePath}: failed to create model light probe texture." );
			_probeTextures.Add( texture );
			var go = new GameObject( false, $"model light probes {_probeTextures.Count}" )
			{
				Parent = root,
				LocalPosition = bounds.Center
			};
			var volume = go.AddComponent<LightProbeVolume>();
			volume.Bounds = new BBox( bounds.Mins - bounds.Center, bounds.Maxs - bounds.Center );
			volume.Priority = -(int)Math.Ceiling( Math.Log2( Math.Max( 1, bounds.Size.LengthSquared ) ) );
			volume.Texture = texture;
			if ( float.IsFinite( captureDistance ) )
			{
				var reflections = new GameObject( false, "reflections" )
				{
					Parent = go,
					LocalPosition = capturePosition - bounds.Center
				};
				var cubemap = reflections.AddComponent<EnvmapProbe>();
				cubemap.Bounds = new BBox( bounds.Mins - capturePosition, bounds.Maxs - capturePosition );
				cubemap.Priority = Math.Clamp( 100 + volume.Priority, 0, 100 );
				cubemap.Projection = SceneCubemap.ProjectionMode.Box;
				cubemap.Resolution = EnvmapProbe.CubemapResolution.Small;
				cubemap.ZNear = 1;
				cubemap.ZFar = Math.Clamp( bounds.Size.Length, 4096, 16384 );
				cubemap.UpdateStrategy = EnvmapProbe.CubemapDynamicUpdate.OnEnabled;
				cubemap.MultiBounce = false;
				cubemap.Mode = EnvmapProbe.EnvmapProbeMode.Realtime;
				reflections.Enabled = true;
				reflectionProbes++;
			}
			else
			{
				Log.Warning( $"{FilePath}: model probe volume {_probeTextures.Count} has no open capture position; no reflection probe was created." );
			}
			go.Enabled = true;
			totalProbes += probes;
		}
		root.Enabled = true;
		Log.Info( $"{FilePath}: {cells.Count} native model probe volumes, {totalProbes} probes, {totalProbes * 48L / 1048576f:F2} MiB ambient texture data, {reflectionProbes} startup reflection captures." );
	}

	private static Vector3Int ProbeCounts( Vector3 size )
	{
		var counts = new Vector3Int( Count( size.x ), Count( size.y ), Count( size.z ) );
		while ( counts.x * counts.y * counts.z > MaxProbesPerVolume )
		{
			int axis = 0;
			float spacing = float.MaxValue;
			for ( int i = 0; i < 3; i++ )
				if ( counts[i] > 1 && size[i] / counts[i] < spacing )
				{
					axis = i;
					spacing = size[i] / counts[i];
				}
			counts[axis]--;
		}
		return counts;

		static int Count( float extent ) => Math.Clamp( (int)Math.Ceiling( extent / ProbeSpacing ), 1, 16 );
	}

	private static void WriteProbeHalf( byte[] data, int offset, float value ) =>
		BitConverter.TryWriteBytes( data.AsSpan( offset, 2 ), (Half)value );

	private static float ProbeLinear( float value )
	{
		value = Math.Clamp( value / 255, 0, 1 );
		return value <= 0.04045f ? value / 12.92f : MathF.Pow( (value + 0.055f) / 1.055f, 2.4f );
	}
}
