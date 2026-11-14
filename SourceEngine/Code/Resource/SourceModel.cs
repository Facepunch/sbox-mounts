using System;

namespace Source1;

internal sealed class SourceModel( BaseMount mount, string path, (MdlFile Mdl, VtxFile Vtx, PhyFile Physics) source,
	Transform[] referencePose = null, MdlFile.ImportedAnimations animations = null )
{
	// Included animation models, shared by every model of a mount that includes them.
	// Entries live as long as a model still has clips to decode from them.
	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BaseMount, Dictionary<string, WeakReference<MdlFile.AnimationFile>>> AnimationFiles = new();

	internal (MdlFile Mdl, VtxFile Vtx, PhyFile Physics) Source { get; } = source;
	internal Transform[] ReferencePose { get; } = referencePose;

	internal Model Build( string name ) =>
		new SourceModelBuilder( mount, path ).BuildModel( Source.Mdl, Source.Vtx, Source.Physics, name, ReferencePose, animations: animations );

	internal Model BuildStaticProp( string contentRoot, int index, uint flags, bool hdr, bool diskLighting,
		Transform transform, Vector3? lightingOrigin, Func<Vector3, PropLightingState> fallback, SourceMaterialOverrides shadows,
		Dictionary<Material, Material> vertexLitMaterials )
		=> new SourceModelBuilder( mount, path ).CreateStaticPropModel( Source, contentRoot, index, flags, hdr,
			diskLighting, transform, lightingOrigin, fallback, shadows, ReferencePose, vertexLitMaterials );

	internal static SourceModel Read( BaseMount mount, string filename, bool readLighting = false )
		=> mount.ReadSourceModel( filename, readLighting: readLighting );

	internal static SourceModel Read( BaseMount mount, string filename, bool readLighting,
		Func<string, Stream> open, Func<string, Stream> tryOpen, bool animated = false )
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
		if ( animated )
		{
			try
			{
				var shared = mount is null ? null : AnimationFiles.GetValue( mount, _ => new( StringComparer.OrdinalIgnoreCase ) );
				animations = mdl.ReadAnimations( mdlStream, filename, open, shared );
			}
			catch ( Exception e ) when ( MdlFile.IsDataError( e ) )
			{
				Log.Warning( $"{filename}: animations were not imported: {e.Message}" );
				animations = new( [], [], [], [] );
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
