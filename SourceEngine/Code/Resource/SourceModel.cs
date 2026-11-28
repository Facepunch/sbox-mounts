using System;

namespace Source1;

internal sealed class SourceModel( SourceGameMount mount, string path, (MdlFile Mdl, VtxFile Vtx, PhyFile Physics) source,
	Transform[] referencePose = null, MdlFile.ImportedAnimations animations = null )
{
	internal (MdlFile Mdl, VtxFile Vtx, PhyFile Physics) Source { get; } = source;
	internal Transform[] ReferencePose { get; } = referencePose;

	internal Model Build( string name )
	{
		// Procedural helpers deform the authored mesh after animation. Pre-skinning it
		// into the first sequence pose distorts vertices shared by multiple bones.
		var bindPose = Source.Mdl.QuaternionBones.Length > 0 && animations?.Clips.Count > 0 ? null : ReferencePose;
		return new SourceModelBuilder( mount, path ).BuildModel( Source.Mdl, Source.Vtx, Source.Physics, name, bindPose, animations: animations );
	}

	internal Model BuildStaticProp( string contentRoot, int index, uint flags, bool hdr, bool diskLighting,
		Transform transform, Vector3? lightingOrigin, Func<Vector3, PropLightingState> fallback, SourceMaterialOverrides shadows,
		Dictionary<Material, Material> vertexLitMaterials )
		=> new SourceModelBuilder( mount, path ).CreateStaticPropModel( Source, contentRoot, index, flags, hdr,
			diskLighting, transform, lightingOrigin, fallback, shadows, ReferencePose, vertexLitMaterials );

	internal static SourceModel Read( SourceGameMount mount, string filename, bool readLighting,
		Func<string, Stream> open, Func<string, Stream> tryOpen, Dictionary<string, WeakReference<MdlFile.AnimationFile>> animationFiles )
	{
		filename = filename.Replace( '\\', '/' );
		using var mdlStream = open( filename );
		var mdl = new MdlFile( mdlStream );
		PhyFile physics = null;
		var phyPath = System.IO.Path.ChangeExtension( filename, ".phy" );
		if ( tryOpen( phyPath ) is { } phyStream )
		{
			using ( phyStream )
			{
				try
				{
					physics = new PhyFile( phyStream, phyPath, mdl.Checksum );
				}
				catch ( Exception e ) when ( e is InvalidDataException or NotSupportedException )
				{
					Log.Trace( $"{filename}: physics was not imported: {e.Message}" );
				}
			}
		}
		Transform[] referencePose = null;
		if ( !mdl.IsStaticProp && (physics is null || physics is { Solids.Count: 1, Constraints.Count: 0 }) )
		{
			try
			{
				Stream OpenAnimation( string path ) =>
					open( path.StartsWith( "models/", StringComparison.OrdinalIgnoreCase )
						? path : filename[..(filename.LastIndexOf( '/' ) + 1)] + path );
				referencePose = physics is null
					? mdl.ReadSequenceReferencePose( mdlStream, OpenAnimation )
					: mdl.ReadPhysicsReferencePose( mdlStream, OpenAnimation );
			}
			catch ( Exception e ) when ( e is InvalidDataException or NotSupportedException or FileNotFoundException )
			{
				Log.Trace( $"{filename}: initial model pose is unavailable; authored bind pose retained: {e.Message}" );
			}
		}
		MdlFile.ImportedAnimations animations = null;
		if ( animationFiles is not null )
		{
			try
			{
				animations = mdl.ReadAnimations( mdlStream, filename, open, animationFiles );
			}
			catch ( Exception e ) when ( MdlFile.IsDataError( e ) )
			{
				Log.Trace( $"{filename}: animations were not imported: {e.Message}" );
				animations = new( [], [], [], [], [], [] );
			}
		}
		if ( mdl.TotalVertices == 0 )
			return new SourceModel( mount, filename, (mdl, null, physics), referencePose, animations );
		using var vvdStream = open( System.IO.Path.ChangeExtension( filename, ".vvd" ) );
		using var vtxStream = open( System.IO.Path.ChangeExtension( filename, ".dx90.vtx" ) );
		var vvd = new VvdFile( vvdStream );
		return new SourceModel( mount, filename, (mdl, new VtxFile( vtxStream, mdl, vvd, readLighting: readLighting ), physics), referencePose, animations );
	}
}
