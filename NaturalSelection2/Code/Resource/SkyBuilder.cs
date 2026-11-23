using Sandbox;
using System;
using System.IO;
using System.Linq;

namespace NS2;

/// <summary>
/// Builds a sky material from a Spark skybox cinematic.
///
/// Spark has no cubemaps. Client.lua treats an entity of class "skybox" as a cinematic placed in
/// RenderScene.Zone_SkyBox, and that cinematic holds a "Sky Box" node naming six materials, one per
/// box face. Each uses core/shaders/SkyBox.surface_shader - just tex2D( emissiveMap, texCoord ) -
/// so Spark draws six textured quads. s&amp;box wants a cubemap, so they get assembled into one here.
/// </summary>
internal static class SkyBuilder
{
	/// <summary>
	/// The Spark property naming each cube face, in D3D order: +X, -X, +Y, -Y, +Z, -Z.
	/// </summary>
	/// <remarks>
	/// Spark is Y-up and the map loader converts with Swizzle(v) = (v.z, v.x, v.y), so spark +X lands
	/// on +Y, spark +Y on +Z and spark +Z on +X. Faces must be looked up by property, never by
	/// filename: tanith's "Right Material" is literally named tanith_bk.material.
	/// </remarks>
	private static readonly string[] FaceProperty =
	[
		"Front Material",   // +X  (spark +Z)
		"Back Material",    // -X
		"Right Material",   // +Y  (spark +X)
		"Left Material",    // -Y
		"Up Material",      // +Z  (spark +Y)
		"Down Material",    // -Z
	];

	/// <summary>
	/// How each face image has to be transformed, as a CCW quarter turn repeated (k &amp; 3) times
	/// followed by a horizontal mirror when k >= 4. -1 means solve it at load.
	/// </summary>
	/// <remarks>
	/// Measured, not guessed: every orientation of every face of all 13 shipped skyboxes was scored
	/// by how well the images line up along the twelve cube edges. Five faces agree unanimously,
	/// with a seam error of 1-5 of 255 - DXT noise. The Down face genuinely disagrees between art
	/// sets, so it falls to <see cref="SolveDownFace"/>.
	/// </remarks>
	private static readonly int[] FaceOrientation = [7, 5, 6, 4, 6, -1];

	/// <summary>Faces are capped to this; the 2048 sets would otherwise cost 200MB as fp16.</summary>
	private const int MaxFaceSize = 1024;

	public static Material Build( GameMount mount, string cinematicPath )
	{
		var fs = mount.FileSystem;
		if ( !GameMount.TryResolve( fs, cinematicPath, out var filePath ) )
			return null;

		var name = Path.GetFileNameWithoutExtension( filePath );
		if ( mount.SkyMaterials.TryGetValue( filePath, out var cached ) && cached.IsValid )
			return cached;

		var sky = ReadSkyNode( fs, filePath );
		if ( sky is null )
			return null;

		var faces = new Color32[6][];
		var size = 0;

		for ( var i = 0; i < 6; i++ )
		{
			var texture = LoadFaceTexture( fs, sky.Get( FaceProperty[i] ).AsString(), mount.Ident );
			if ( texture is null )
				return null;

			faces[i] = ReadPixels( texture, ref size );
			if ( faces[i] is null )
				return null;

			faces[i] = Orient( faces[i], size, FaceOrientation[i] );
		}

		faces[5] = Orient( faces[5], size, SolveDownFace( faces, size ) );

		var cube = Texture.CreateCube( size, size, ImageFormat.RGBA16161616F )
			.WithName( $"ns2sky/{name}" )
			.WithData( ToLinearHalf( faces, size ) )
			.Finish();

		if ( cube is null )
			return null;

		// Named rather than anonymous, and with a material extension, otherwise the scene can't
		// resolve it by name again when it deserializes SkyBox2D.SkyMaterial.
		var material = Material.Create( $"ns2sky/{name}.vmat", "shaders/sky.shader", false );
		if ( material is null )
			return null;

		material.Set( "g_tSkyTexture", cube );
		material.Set( "SkyTexture", cube );

		mount.SkyMaterials[filePath] = material;
		return material;
	}

	private static CinematicNode ReadSkyNode( BaseFileSystem fs, string filePath )
	{
		try
		{
			using var stream = fs.OpenRead( filePath );
			return CinematicFile.Read( stream ).Find( "Sky Box" );
		}
		catch
		{
			return null;
		}
	}

	private static Texture LoadFaceTexture( BaseFileSystem fs, string materialPath, string ident )
	{
		if ( !GameMount.TryResolve( fs, materialPath, out var filePath ) )
			return null;

		var map = ReadEmissiveMap( fs, filePath );
		if ( !GameMount.TryResolve( fs, map, out var texturePath ) )
			return null;

		return Texture.Load( $"mount://{ident}/{texturePath}.vtex", false );
	}

	/// <summary>
	/// Pulls emissiveMap out of a face .material, which is a flat list of key = "value" lines.
	/// Every face of all 13 shipped skyboxes names one, so there's no default to fall back to.
	/// </summary>
	private static string ReadEmissiveMap( BaseFileSystem fs, string filePath )
	{
		using var reader = new StreamReader( fs.OpenRead( filePath ) );

		while ( reader.ReadLine() is { } line )
		{
			if ( !line.Contains( "emissiveMap", StringComparison.OrdinalIgnoreCase ) )
				continue;

			var parts = line.Split( '"' );
			if ( parts.Length >= 2 )
				return parts[1].Replace( '\\', '/' );
		}

		return null;
	}

	/// <summary>
	/// Reads a face at the smallest mip that is still at least <see cref="MaxFaceSize"/> so the 2048
	/// sets come down the mip chain, then matches whatever size the first face settled on.
	/// </summary>
	private static Color32[] ReadPixels( Texture texture, ref int size )
	{
		var width = texture.Width;
		var mip = 0;

		while ( mip + 1 < texture.Mips && width / 2 >= MaxFaceSize )
		{
			width /= 2;
			mip++;
		}

		var pixels = texture.GetPixels( mip );
		if ( pixels is null || pixels.Length == 0 )
			return null;

		var side = (int)MathF.Round( MathF.Sqrt( pixels.Length ) );
		if ( side * side != pixels.Length )
			return null;

		if ( size == 0 )
			size = Math.Min( side, MaxFaceSize );

		return side == size ? pixels : Resample( pixels, side, size );
	}

	private static Color32[] Resample( Color32[] src, int side, int size )
	{
		var dst = new Color32[size * size];

		for ( var r = 0; r < size; r++ )
		{
			var sr = r * side / size;
			for ( var c = 0; c < size; c++ )
				dst[r * size + c] = src[sr * side + c * side / size];
		}

		return dst;
	}

	/// <summary>Rotates CCW (k &amp; 3) times, then mirrors horizontally when k >= 4.</summary>
	private static Color32[] Orient( Color32[] src, int n, int k )
	{
		if ( k <= 0 )
			return src;

		var dst = new Color32[n * n];

		for ( var r = 0; r < n; r++ )
		{
			for ( var c = 0; c < n; c++ )
			{
				var rc = k >= 4 ? n - 1 - c : c;

				var (sr, sc) = (k & 3) switch
				{
					1 => (rc, n - 1 - r),
					2 => (n - 1 - r, n - 1 - rc),
					3 => (n - 1 - rc, r),
					_ => (r, rc),
				};

				dst[r * n + c] = src[sr * n + sc];
			}
		}

		return dst;
	}

	/// <summary>
	/// Picks the -Z orientation whose edges line up best against the four already-oriented side
	/// faces - the same scoring as the offline solve, restricted to the one ambiguous face.
	/// </summary>
	private static int SolveDownFace( Color32[][] faces, int size )
	{
		const int samples = 128;

		var best = 0;
		var bestError = float.MaxValue;

		for ( var k = 0; k < 8; k++ )
		{
			var candidate = Orient( faces[5], size, k );
			var error = 0f;

			for ( var side = 0; side < 4; side++ )
			{
				// The edge shared by two faces sweeps from one of their corners to the other.
				var corner = Axis[5] + Axis[side];
				var along = Vector3.Cross( Axis[5], Axis[side] );

				for ( var i = 0; i < samples; i++ )
				{
					var direction = corner + along * (-0.99f + 1.98f * i / (samples - 1));
					var a = SampleFace( candidate, size, 5, direction );
					var b = SampleFace( faces[side], size, side, direction );
					error += MathF.Abs( a.r - b.r ) + MathF.Abs( a.g - b.g ) + MathF.Abs( a.b - b.b );
				}
			}

			if ( error < bestError )
				(bestError, best) = (error, k);
		}

		return best;
	}

	private static readonly Vector3[] Axis =
	[
		new( 1, 0, 0 ), new( -1, 0, 0 ),
		new( 0, 1, 0 ), new( 0, -1, 0 ),
		new( 0, 0, 1 ), new( 0, 0, -1 ),
	];

	/// <summary>
	/// Standard D3D cube face projection: u = (sc/ma + 1)/2, v = (tc/ma + 1)/2, v counting down.
	/// </summary>
	private static Color32 SampleFace( Color32[] face, int n, int index, Vector3 d )
	{
		var (sc, tc, ma) = index switch
		{
			0 => (-d.z, -d.y, d.x),
			1 => (d.z, -d.y, -d.x),
			2 => (d.x, d.z, d.y),
			3 => (d.x, -d.z, -d.y),
			4 => (d.x, -d.y, d.z),
			_ => (-d.x, -d.y, -d.z),
		};

		var col = Math.Clamp( (int)((sc / ma + 1f) * 0.5f * n), 0, n - 1 );
		var row = Math.Clamp( (int)((tc / ma + 1f) * 0.5f * n), 0, n - 1 );
		return face[row * n + col];
	}

	private static readonly ushort[] SrgbToLinearHalf = BuildSrgbTable();

	private static ushort[] BuildSrgbTable()
	{
		var table = new ushort[256];

		for ( var i = 0; i < 256; i++ )
		{
			var c = i / 255f;
			var linear = c <= 0.04045f ? c / 12.92f : MathF.Pow( (c + 0.055f) / 1.055f, 2.4f );
			table[i] = BitConverter.HalfToUInt16Bits( (Half)linear );
		}

		return table;
	}

	/// <summary>
	/// The sky shader reads its cubemap with SrgbRead( false ), so the face data has to be
	/// linearised on the way in.
	/// </summary>
	private static byte[] ToLinearHalf( Color32[][] faces, int n )
	{
		var data = new byte[6 * n * n * 8];
		var at = 0;

		foreach ( var face in faces )
		{
			foreach ( var pixel in face )
			{
				WriteHalf( data, ref at, SrgbToLinearHalf[pixel.r] );
				WriteHalf( data, ref at, SrgbToLinearHalf[pixel.g] );
				WriteHalf( data, ref at, SrgbToLinearHalf[pixel.b] );
				WriteHalf( data, ref at, SrgbToLinearHalf[255] );
			}
		}

		return data;
	}

	private static void WriteHalf( byte[] data, ref int at, ushort bits )
	{
		data[at++] = (byte)bits;
		data[at++] = (byte)(bits >> 8);
	}
}
