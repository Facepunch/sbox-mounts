using Sandbox.Mounting;
using System;
using System.IO;

namespace Overgrowth;

public class CharacterLoader( string filePath, bool guard = false ) : ResourceLoader<GameMount>, IHotloadManaged
{
	readonly Dictionary<string, RabbitGuardAnimations> animationGraphs = new();
	CharacterAnimationLibrary animationLibrary;
	Vector3? modelCenter;
	SkeletonData animationRig;
	Dictionary<string, int> attachmentBones;
	int[] attachmentParents, attachmentSymmetry;
	internal Dictionary<int, Vector3> AttachmentCenters { get; private set; } = [];

	(BoneCollection.Bone Bone, Vector3 Center)[] physicsCenters = [];

	// Fixed FZX bones keep their bind-space offset from the nearest simulated ancestor.
	// Store their mean once so a ragdoll query only visits each live body once.
	readonly Dictionary<int, (Vector3 Center, int Count)> ragdollCenters = new();

	internal static Vector3? RagdollCenter( ModelPhysics physics )
	{
		if ( !physics.IsValid() || physics.Model is null || Sandbox.Mounting.Directory.Get( "overgrowth" )?.GetByPath( physics.Model.ResourcePath ) is not CharacterLoader character ) return null;
		Vector3 sum = default;
		int count = 0;
		foreach ( var part in physics.Bodies )
		{
			if ( !part.Component.IsValid() || !character.ragdollCenters.TryGetValue( part.Bone, out var group ) ) continue;
			sum += part.Component.WorldTransform.PointToWorld( group.Center ) * group.Count;
			count += group.Count;
		}
		return count > 0 ? sum / count : null;
	}

	internal static Vector3? AverageBodyPosition( SkinnedModelRenderer renderer )
	{
		if ( !renderer.IsValid() || renderer.Model is null || Sandbox.Mounting.Directory.Get( "overgrowth" )?.GetByPath( renderer.Model.ResourcePath ) is not CharacterLoader character ) return null;
		Vector3 sum = default;
		int count = 0;
		foreach ( var (bone, center) in character.physicsCenters )
		{
			if ( !renderer.TryGetBoneTransform( bone, out var pose ) ) continue;
			sum += pose.PointToWorld( center );
			count++;
		}
		return count > 0 ? sum / count : null;
	}

	void SetAttachmentRig( SkeletonData rig )
	{
		attachmentBones = rig.IKBones.ToDictionary( entry => entry.Key, entry => entry.Value.BoneId );
		attachmentParents = rig.HierParents.ToArray();
		attachmentSymmetry = RabbitMirror.MatchBones( rig );
	}

	internal int AttachmentBone( string label, int depth, bool mirrored )
	{
		if ( attachmentBones is null ) SetAttachmentRig( ReadSkeleton( ReadAppearance() ).Skeleton );
		if ( !attachmentBones.TryGetValue( label, out var bone ) )
		{
			if ( label != "hip" || !attachmentBones.TryGetValue( "torso", out bone ) ) return -1;
			depth += 2;
		}
		while ( depth-- > 0 && bone >= 0 ) bone = attachmentParents[bone];
		return mirrored && bone >= 0 ? attachmentSymmetry[bone] : bone;
	}

	internal float AttachmentScale => (ReadAppearance().Number( "default_scale" ) ?? 1f) / (ReadAppearance().Number( "model_scale" ) ?? 1f);
	internal ModelHitboxes.ConvexHull[] HitVolumes { get; private set; } = [];
	internal HashSet<int> NonCollidingBones { get; private set; } = [];
	const float Scale = 40f;
	(XmlAsset Groups, float Pitch)? voice;
	XmlAsset characterDefinition;
	XmlAsset Definition => characterDefinition ??= XmlAsset.Read( Host.FileSystem, filePath, "character" );
	internal string DefaultTeams => Definition.Element( "team" )?.Value;
	internal string Species => Definition.Element( "tags" )?.Attribute( "species" );
	internal string Clothing => Definition.Element( "clothing" )?.Value;
	internal string SoundMod => Definition.Element( "soundmod" )?.Value;
	readonly Dictionary<string, VoiceTrack> voiceTracks = new();

	internal VoiceTrack GetVoiceTrack( SoundLoader sound )
	{
		var path = sound.LipSyncPath;
		if ( voiceTracks.TryGetValue( path, out var track ) ) return track;
		return voiceTracks[path] = Definition.Element( "visemes" ) is { } visemes && Host.FileSystem.FileExists( path )
			? VoiceTrack.Read( Host.FileSystem.ReadAllText( path ), Host.Phonemes, visemes ) : null;
	}

	internal (string Group, float Pitch) GetVoice( string cue )
	{
		if ( voice is null )
		{
			var definition = Definition.Element( "voice" );
			var groups = definition?.Attribute( "path" ) is { } path
				? XmlAsset.Read( Host.FileSystem, Host.Resolve( path ), "voice" )
				: null;
			voice = (groups, definition?.Number( "pitch" ) ?? 1f);
		}
		return (voice.Value.Groups?.Element( cue )?.Attribute( "path" ), voice.Value.Pitch);
	}

	XmlAsset ReadAppearance() => Definition.Element( "appearance" )
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
	void IHotloadManaged.Created( IReadOnlyDictionary<string, object> state )
	{
		animationGraphs.Clear();
		animationLibrary = null;
	}

	internal RabbitGuardAnimations GetGuardAnimations( Model heldModel = null )
	{
		if ( !guard ) return null;
		var key = heldModel?.ResourcePath ?? "";
		if ( animationGraphs.TryGetValue( key, out var existing ) ) return existing;
		var item = heldModel is null ? null : (Host.GetByPath( key ) as ObjectLoader)?.ItemDefinition;
		if ( animationLibrary is null )
		{
			var appearance = ReadAppearance();
			if ( animationRig is null )
			{
				var (skeleton, rig, reference, skeletonModelPath) = ReadSkeleton( appearance );
				ReadBoneMasses( skeleton, rig );
				modelCenter ??= ReadMesh( appearance, reference, skeletonModelPath, out _ ).Center;
				ScaleSkeleton( appearance, skeleton );
				animationRig = skeleton;
			}
			animationLibrary = new( Host, Path, Definition, animationRig, AttachmentScale );
		}
		var graphPath = heldModel is null ? Path : Path + "/equipped/" + key["mount://overgrowth/".Length..];
		return animationGraphs[key] = RabbitGuardAnimations.Create( Host, graphPath, Definition, animationLibrary, modelCenter.Value, item );
	}

	ObjLoader.ObjData ReadMesh( XmlAsset appearance, ObjLoader.ObjData reference, string skeletonModelPath, out string modelObjPath )
	{
		var objXmlPath = Host.Resolve( appearance.Attribute( "obj_path" ) );
		modelObjPath = Host.Resolve( XmlAsset.Read( Host.FileSystem, objXmlPath, "Object" ).Element( "Model" )?.Value );
		return string.Equals( modelObjPath, skeletonModelPath, StringComparison.OrdinalIgnoreCase )
			? reference : ObjLoader.Load( Host.FileSystem, modelObjPath );
	}

	protected override object Load()
	{
		var appearance = ReadAppearance();
		var objectPath = appearance.Attribute( "obj_path" );
		var (skeleton, rig, reference, skeletonModelPath) = ReadSkeleton( appearance );
		var objData = ReadMesh( appearance, reference, skeletonModelPath, out var modelObjPath );
		modelCenter = objData.Center;

		ReadBoneMasses( skeleton, rig );
		if ( guard ) SetAttachmentRig( skeleton );

		var weights = skeleton.BoneWeights ?? Array.Empty<Vector4>();
		var boneIds = skeleton.BoneIds ?? Array.Empty<Vector4>();
		if ( weights.Length != objData.FaceVertexCount || !objData.FaceVertPosIndices.SequenceEqual( reference.FaceVertPosIndices ) )
			throw new InvalidDataException( $"Character mesh and skeleton have different topology: {modelObjPath}" );
		FixZeroWeights( skeleton, weights, boneIds, reference );

		float scale = ScaleSkeleton( appearance, skeleton );
		for ( int i = 0; i < objData.Positions.Count; i++ )
			objData.Positions[i] *= scale;
		objData.Bounds = BBox.FromPoints( objData.Positions );

		// Permanent morphs are always at full weight. Bake them into the rest mesh while
		// keeping the original positions as the reference for expression and fist deltas.
		var restMesh = objData;
		if ( appearance.Attribute( "morph" ) is { } permanent && Host.FileSystem.FileExists( permanent ) )
		{
			var target = ObjLoader.Load( Host.FileSystem, permanent, objData.Center, objData.FaceVertexCount );
			if ( target.FaceVertexCount != objData.FaceVertexCount )
				throw new InvalidDataException( $"Permanent morph topology differs from {modelObjPath}: {permanent}" );
			restMesh.Positions = target.Positions;
			for ( int i = 0; i < restMesh.Positions.Count; i++ ) restMesh.Positions[i] *= scale;
			restMesh.Bounds = BBox.FromPoints( restMesh.Positions );
		}

		var material = Material.Load( Host.MountedPath( objectPath ) + ".vmat" );
		var mesh = ObjLoader.CreateSkinnedMesh( restMesh, Path, material, weights, boneIds );
		CharacterMorph.AddTargets( Host.FileSystem, filePath, modelObjPath, objData, mesh, scale );
		var builder = Model.Builder.WithName( Path ).AddMesh( mesh );
		for ( int i = 0; i < skeleton.Bones.Count; i++ )
		{
			var parentIndex = skeleton.AnimationParents[i];
			var parent = parentIndex >= 0 ? $"bone_{parentIndex}" : null;
			var bindPose = skeleton.BindPose[i];
			builder.AddBone( $"bone_{i}", bindPose.Position, bindPose.Rotation, parent );
		}


		var shapeCounts = RagdollBuilder.Add( Host.FileSystem, builder, skeleton, System.IO.Path.ChangeExtension( modelObjPath, ".fzx" ), reference.Center, scale, appearance.Number( "default_scale" ) ?? 1f, out var bodyCenters, out var nonCollidingBones );
		if ( guard )
		{
			animationRig = skeleton;
			AttachmentCenters = bodyCenters.ToDictionary( entry => entry.Key, entry => skeleton.BindPose[entry.Key].PointToLocal( entry.Value.Position ) );
			var hullPath = modelObjPath[..^4] + "_hulls.obj";
			if ( Host.FileSystem.FileExists( hullPath ) )
				HitVolumes = CharacterHulls.Load( Host.FileSystem, hullPath, objData.Center, scale, skeleton, bodyCenters );
		}
		var model = builder.Create();
		if ( guard )
		{
			physicsCenters = AttachmentCenters.Select( pair => (model.Bones.GetBone( $"bone_{pair.Key}" ), pair.Value) ).ToArray();
			ragdollCenters.Clear();
			foreach ( var (id, point) in bodyCenters )
			{
				int parent = id;
				while ( parent >= 0 && (!bodyCenters.TryGetValue( parent, out var body ) || body.Fixed) ) parent = skeleton.HierParents[parent];
				if ( parent < 0 ) continue;
				int index = model.Bones.GetBone( $"bone_{parent}" ).Index;
				var group = ragdollCenters.GetValueOrDefault( index );
				var local = skeleton.BindPose[parent].PointToLocal( point.Position );
				ragdollCenters[index] = (group.Center + (local - group.Center) / (group.Count + 1), group.Count + 1);
			}
		}
		NonCollidingBones = nonCollidingBones.Select( id => model.Bones.GetBone( $"bone_{id}" ).Index ).ToHashSet();
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
