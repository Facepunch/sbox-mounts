using Sandbox.Mounting;
using System;
using System.IO;

namespace Overgrowth;

public class CharacterLoader( string filePath, bool guard = false ) : ResourceLoader<GameMount>, IHotloadManaged
{
	RabbitGuardAnimations compiledAnimations;
	internal ModelHitboxes.ConvexHull[] HitVolumes { get; private set; } = [];
	const float Scale = 40f;
	(XmlAsset Groups, float Pitch)? voice;

	internal (string Group, float Pitch) GetVoice( string cue )
	{
		if ( voice is null )
		{
			var definition = XmlAsset.Read( Host.FileSystem, filePath, "character" ).Element( "voice" );
			var groups = definition?.Attribute( "path" ) is { } path
				? XmlAsset.Read( Host.FileSystem, Host.Resolve( path ), "voice" )
				: null;
			voice = (groups, definition?.Number( "pitch" ) ?? 1f);
		}
		return (voice.Value.Groups?.Element( cue )?.Attribute( "path" ), voice.Value.Pitch);
	}

	XmlAsset ReadAppearance() => XmlAsset.Read( Host.FileSystem, filePath, "character" ).Element( "appearance" )
		?? throw new InvalidDataException( $"Character has no appearance: {filePath}" );

	(SkeletonData Skeleton, XmlAsset Rig, ObjLoader.ObjData Reference, string ModelPath) ReadSkeleton( XmlAsset appearance )
	{
		var rig = XmlAsset.Read( Host.FileSystem, Host.Resolve( appearance.Attribute( "skeleton" ) ), "rig" );
		var modelPath = Host.Resolve( rig.Attribute( "model_path" ) );
		var reference = ObjLoader.Load( Host.FileSystem, modelPath );
		using var stream = Host.FileSystem.OpenRead( Host.Resolve( rig.Attribute( "bone_path" ) ) );
		return (PhxbnLoader.Load( stream, reference.FaceVertexCount ), rig, reference, modelPath);
	}

	void ReadBoneMasses( SkeletonData skeleton, XmlAsset rig )
	{
		if ( rig.Attribute( "mass_path" ) is { } massPath )
		{
			var massRig = XmlAsset.Read( Host.FileSystem, Host.Resolve( massPath ), "rig" );
			var massReference = ObjLoader.Load( Host.FileSystem, Host.Resolve( (string)massRig.Attribute( "model_path" ) ) );
			using var stream = Host.FileSystem.OpenRead( Host.Resolve( (string)massRig.Attribute( "bone_path" ) ) );
			var massSkeleton = PhxbnLoader.Load( stream, massReference.FaceVertexCount );
			for ( int i = 0; i < Math.Min( skeleton.BoneMass.Count, massSkeleton.BoneMass.Count ); i++ )
				skeleton.BoneMass[i] = massSkeleton.BoneMass[i];
		}
	}

	float ScaleSkeleton( XmlAsset appearance, SkeletonData skeleton )
	{
		float defaultScale = appearance.Number( "default_scale" ) ?? 1f;
		float modelScale = appearance.Number( "model_scale" ) ?? 1f;
		if ( !float.IsFinite( defaultScale ) || !float.IsFinite( modelScale ) || defaultScale <= 0 || modelScale <= 0 )
			throw new InvalidDataException( $"Invalid character scale: {filePath}" );
		float scale = defaultScale / modelScale;
		for ( int i = 0; i < skeleton.BindPose.Length; i++ )
			skeleton.BindPose[i] = skeleton.BindPose[i].WithPosition( skeleton.BindPose[i].Position * scale );
		return scale;
	}

	// Generated clips and event metadata must be rebuilt together after a code reload.
	void IHotloadManaged.Created( IReadOnlyDictionary<string, object> state ) => compiledAnimations = null;

	internal RabbitGuardAnimations GetGuardAnimations()
	{
		if ( compiledAnimations is null && guard )
		{
			// The mounted model can survive code hotload even when its animation data has been invalidated.
			var appearance = ReadAppearance();
			var (skeleton, rig, _, _) = ReadSkeleton( appearance );
			ReadBoneMasses( skeleton, rig );
			compiledAnimations = RabbitGuardAnimations.Create( Host.FileSystem, Path, filePath, skeleton, ScaleSkeleton( appearance, skeleton ) );
		}
		return compiledAnimations;
	}

	protected override object Load()
	{
		var appearance = ReadAppearance();
		var objectPath = appearance.Attribute( "obj_path" );
		var objXmlPath = Host.Resolve( objectPath );
		var (skeleton, rig, reference, skeletonModelPath) = ReadSkeleton( appearance );
		var modelObjPath = Host.Resolve( XmlAsset.Read( Host.FileSystem, objXmlPath, "Object" ).Element( "Model" )?.Value );
		var objData = string.Equals( modelObjPath, skeletonModelPath, StringComparison.OrdinalIgnoreCase )
			? reference : ObjLoader.Load( Host.FileSystem, modelObjPath );

		ReadBoneMasses( skeleton, rig );

		var weights = skeleton.BoneWeights ?? Array.Empty<Vector4>();
		var boneIds = skeleton.BoneIds ?? Array.Empty<Vector4>();
		if ( weights.Length != objData.FaceVertexCount || !objData.FaceVertPosIndices.SequenceEqual( reference.FaceVertPosIndices ) )
			throw new InvalidDataException( $"Character mesh and skeleton have different topology: {modelObjPath}" );
		FixZeroWeights( skeleton, weights, boneIds, reference );

		float scale = ScaleSkeleton( appearance, skeleton );
		for ( int i = 0; i < objData.Positions.Count; i++ )
			objData.Positions[i] *= scale;
		objData.Bounds = BBox.FromPoints( objData.Positions );

		var material = Material.Load( Host.MountedPath( objectPath ) + ".vmat" );
		var mesh = ObjLoader.CreateSkinnedMesh( objData, Path, material, weights, boneIds );
		CharacterMorph.AddTargets( Host.FileSystem, filePath, modelObjPath, objData, mesh, scale );
		var builder = Model.Builder.WithName( Path ).AddMesh( mesh );
		for ( int i = 0; i < skeleton.Bones.Count; i++ )
		{
			var parentIndex = skeleton.HierParents[i];
			var parent = parentIndex >= 0 ? $"bone_{parentIndex}" : null;
			var bindPose = skeleton.BindPose[i];
			builder.AddBone( $"bone_{i}", bindPose.Position, bindPose.Rotation, parent );
		}

		if ( guard ) compiledAnimations = RabbitGuardAnimations.Create( Host.FileSystem, Path, filePath, skeleton, scale );

		var shapeCounts = RagdollBuilder.Add( Host.FileSystem, builder, skeleton, System.IO.Path.ChangeExtension( modelObjPath, ".fzx" ), reference.Center, scale, appearance.Number( "default_scale" ) ?? 1f, out var bodyCenters );
		if ( guard )
		{
			var hullPath = modelObjPath[..^4] + "_hulls.obj";
			if ( Host.FileSystem.FileExists( hullPath ) )
				HitVolumes = CharacterHulls.Load( Host.FileSystem, hullPath, objData.Center, scale, skeleton, bodyCenters );
		}
		var model = builder.Create();
		if ( model.Physics is null || model.Physics.Parts.Count != shapeCounts.Length )
			throw new InvalidDataException( $"Unable to create Overgrowth ragdoll bodies: {filePath}" );
		for ( int i = 0; i < shapeCounts.Length; i++ )
			if ( model.Physics.Parts[i].Parts.Count != shapeCounts[i] )
				throw new InvalidDataException( $"Unable to create collision shapes for {model.Physics.Parts[i].BoneName}: {filePath}" );
		return model;
	}

	static void FixZeroWeights( SkeletonData skeleton, Vector4[] weights, Vector4[] boneIds, ObjLoader.ObjData reference )
	{
		var center = new Vector3( reference.Center.y, reference.Center.z, reference.Center.x ) / Scale;
		for ( int i = 0; i < weights.Length; i++ )
		{
			var weight = weights[i];
			if ( weight.x + weight.y + weight.z + weight.w >= 0.99f )
				continue;

			var position = reference.RawPositions[reference.FaceVertPosIndices[i]] - center;
			int closest = PhxbnLoader.GetClosestBone( skeleton, position );
			if ( closest < 0 )
				throw new InvalidDataException( "Overgrowth skeleton has no bone for an unweighted vertex." );
			weights[i].x = 1f;
			boneIds[i].x = closest;
		}
	}
}
