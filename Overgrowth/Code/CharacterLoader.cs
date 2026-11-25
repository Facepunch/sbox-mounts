using Sandbox.Mounting;
using System;
using System.IO;

namespace Overgrowth;

public class CharacterLoader( string filePath, bool guard = false ) : ResourceLoader<GameMount>, IHotloadManaged
{
	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		if ( guard && name.Equals( "guard.ag", StringComparison.OrdinalIgnoreCase ) )
		{
			type = ResourceType.AnimationGraph;
			return new GuardGraphLoader();
		}
		type = default;
		return null;
	}

	sealed class GuardGraphLoader : ResourceLoader<GameMount>
	{
		protected override object Load()
		{
			var character = (CharacterLoader)Parent;
			return Model.Load( character.Path ) is { IsError: false } ? character.GetGuardAnimations().Graph : null;
		}
	}

	readonly Dictionary<(string Equipment, string Stance, string Species), RabbitGuardAnimations> animationGraphs = new();
	CharacterAnimationLibrary animationLibrary;
	Vector3? modelCenter;
	SkeletonData animationRig;
	Dictionary<string, int> attachmentBones;
	int[] attachmentParents, attachmentSymmetry;
	internal Dictionary<int, Vector3> AttachmentCenters { get; private set; } = [];

	readonly record struct BodyPoint( BoneCollection.Bone Bone, Vector3 Center, int Body, Transform BodyOffset, float Mass );
	BodyPoint[] bodyPoints = [];
	readonly Dictionary<string, BodyPoint[]> chainCenters = new();

	// BulletObject::GetPosition returns the bone midpoint, including on fixed bones.
	// GetTransform exposes the underlying Bullet origin used by the vision hull instead.
	static Transform? BodyTransform( BodyPoint point, SkinnedModelRenderer renderer, ModelPhysics physics, RabbitGuard fighter )
	{
		if ( physics is { Active: true } )
		{
			return RagdollBone( point, physics, out _ );
		}
		return fighter?.SolvedBodyTransform( point.Bone.Index ) ??
			(renderer.TryGetBoneTransform( point.Bone, out var pose ) ? pose : null);
	}

	static Transform? RagdollBone( BodyPoint point, ModelPhysics physics, out Rigidbody body )
	{
		foreach ( var part in physics.Bodies )
		{
			if ( part.Bone != point.Body || !part.Component.IsValid() ) continue;
			body = part.Component;
			return body.WorldTransform.ToWorld( point.BodyOffset );
		}
		body = null;
		return null;
	}

	internal Vector3? RagdollGrip( ModelPhysics physics, string chain, out Rigidbody body )
	{
		body = null;
		return chainCenters.TryGetValue( chain, out var bones ) && bones.Length > 0 ? RagdollBone( bones[0], physics, out body )?.Position : null;
	}

	static CharacterLoader FromRenderer( SkinnedModelRenderer renderer ) => renderer.IsValid() ? FromModel( renderer.Model ) : null;

	bool IsGuard => guard;

	internal static CharacterLoader FromModel( Model model )
	{
		if ( model is null || Sandbox.Mounting.Directory.Get( "overgrowth" )?.GetByPath( model.ResourcePath ) is not CharacterLoader loader || !loader.IsGuard ) return null;
		if ( loader.Surface is null || loader.inertias is null || loader.forceLevers is null || loader.bodyPoints is not { Length: > 0 } ) loader.RestoreBodyRig( model );
		return loader;
	}

	internal static Vector3? RandomPhysicsBonePosition( SkinnedModelRenderer renderer, RabbitGuard fighter )
	{
		if ( FromRenderer( renderer )?.bodyPoints is not { Length: > 0 } points ) return null;
		return BodyTransform( points[Random.Shared.Next( points.Length )], renderer, renderer.GetComponent<ModelPhysics>(), fighter )?.Position;
	}

	internal Vector3? ChainOrigin( RabbitGuard fighter, string chain )
	{
		if ( !chainCenters.TryGetValue( chain, out var points ) || points.Length == 0 ) return null;
		var tip = points[0];
		return BodyTransform( tip, fighter.Renderer, null, fighter )?.PointToWorld( tip.Center );
	}

	internal static Vector3? AverageChainPosition( SkinnedModelRenderer renderer, string chain )
	{
		if ( FromRenderer( renderer ) is not { } character || !character.chainCenters.TryGetValue( chain, out var points ) ) return null;
		var physics = renderer.GetComponent<ModelPhysics>();
		var fighter = renderer.GetComponentInParent<RabbitGuard>();
		Vector3 sum = default;
		int count = 0;
		foreach ( var point in points )
		{
			if ( BodyTransform( point, renderer, physics, fighter ) is not { } pose ) continue;
			sum += pose.Position;
			count++;
		}
		return count > 0 ? sum / count : null;
	}
	internal readonly record struct ChainFrame( Vector3 Position, Vector3 Forward, Vector3 Left, Vector3 Up )
	{
		public Vector3 Direction( Vector3 local ) => Forward * local.x + Left * local.y + Up * local.z;
	}

	// GetAvgIKChainTransform averages the matrix columns directly. The result can
	// shorten or skew a direction when the constituent bones point different ways.
	internal static ChainFrame? AverageChainFrame( SkinnedModelRenderer renderer, string chain )
	{
		if ( FromRenderer( renderer ) is not { } character || !character.chainCenters.TryGetValue( chain, out var points ) ) return null;
		var physics = renderer.GetComponent<ModelPhysics>();
		var fighter = renderer.GetComponentInParent<RabbitGuard>();
		Vector3 position = default, forward = default, left = default, up = default;
		int count = 0;
		foreach ( var point in points )
		{
			if ( BodyTransform( point, renderer, physics, fighter ) is not { } pose ) continue;
			position += pose.PointToWorld( point.Center );
			forward += pose.Rotation.Forward;
			left += pose.Rotation.Left;
			up += pose.Rotation.Up;
			count++;
		}
		return count > 0 ? new ChainFrame( position / count, forward / count, left / count, up / count ) : null;
	}
	// Fixed FZX bones keep their bind-space offset from the nearest simulated ancestor.
	// Store their mean once so a ragdoll query only visits each live body once.
	readonly Dictionary<int, (Vector3 Center, int Count)> ragdollCenters = new();

	Vector3[] forceLevers, inertias;
	internal Vector3 RagdollInertiaPerMass( int bone ) => inertias[bone];

	internal Vector3 RagdollForceLever( int bone ) => forceLevers[bone];

	// RiggedObject::GetAvgVelocity weights the linked physics bodies by mass.
	internal static Vector3? RagdollVelocity( ModelPhysics physics )
	{
		Vector3 momentum = default;
		float mass = 0f;
		foreach ( var part in physics.Bodies )
		{
			var body = part.Component;
			if ( !body.IsValid() ) continue;
			float bodyMass = body.Mass;
			momentum += body.Velocity * bodyMass;
			mass += bodyMass;
		}
		return mass > 0f ? momentum / mass : null;
	}

	internal static Vector3? RagdollCenter( ModelPhysics physics )
	{
		if ( !physics.IsValid() || FromModel( physics.Model ) is not { } character ) return null;
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

	internal void CaptureRagdollBones( ModelPhysics physics, Transform[] worldPose )
	{
		foreach ( var part in physics.Bodies )
			if ( part.Component.IsValid() ) worldPose[part.Bone] = part.Component.WorldTransform;

		// Overgrowth's fixed physics bones follow their simulated ancestor at their bind offset.
		foreach ( var point in bodyPoints )
			if ( point.Bone.Index != point.Body )
				worldPose[point.Bone.Index] = worldPose[point.Body].ToWorld( point.BodyOffset );
	}

	internal static Vector3? UprightCenterOfMass( RabbitGuard fighter )
	{
		var renderer = fighter.Renderer;
		if ( FromRenderer( renderer ) is not { } character ) return null;
		Vector3 sum = default;
		float mass = 0f;
		foreach ( var point in character.bodyPoints )
		{
			if ( BodyTransform( point, renderer, null, fighter ) is not { } pose ) continue;
			sum += pose.Position * point.Mass;
			mass += point.Mass;
		}
		return mass > 0f ? sum / mass : null;
	}

	internal static Vector3? AverageBodyPosition( SkinnedModelRenderer renderer )
	{
		if ( FromRenderer( renderer ) is not { } character ) return null;
		if ( renderer.GetComponent<ModelPhysics>() is { Active: true } physics ) return RagdollCenter( physics );
		var fighter = renderer.GetComponentInParent<RabbitGuard>();
		Vector3 sum = default;
		int count = 0;
		foreach ( var point in character.bodyPoints )
		{
			if ( BodyTransform( point, renderer, null, fighter ) is not { } pose ) continue;
			sum += pose.Position;
			count++;
		}
		return count > 0 ? sum / count : null;
	}

	void SetBodyRig( Model model, SkeletonData skeleton, Dictionary<int, RagdollBuilder.BoneCenter> bodyCenters )
	{
		overlappingBodies = null;
		NonCollidingBones = bodyCenters.Where( x => x.Value.NonColliding ).Select( x => model.Bones.GetBone( $"bone_{x.Key}" ).Index ).ToHashSet();
		if ( !guard ) return;
		animationRig = skeleton;
		SetAttachmentRig( skeleton );
		AttachmentCenters = bodyCenters.ToDictionary( entry => entry.Key, entry => skeleton.BindPose[entry.Key].PointToLocal( entry.Value.Position ) );
		ragdollCenters.Clear();
		forceLevers = new Vector3[model.BoneCount];
		inertias = new Vector3[model.BoneCount];
		var bodyPoints = new Dictionary<int, BodyPoint>();
		foreach ( var (id, point) in bodyCenters )
		{
			int parent = id;
			while ( parent >= 0 && (!bodyCenters.TryGetValue( parent, out var body ) || body.Fixed) ) parent = skeleton.HierParents[parent];
			if ( parent < 0 ) continue;
			int index = model.Bones.GetBone( $"bone_{parent}" ).Index;
			if ( id == parent )
			{
				forceLevers[index] = -AttachmentCenters[id];
				inertias[index] = point.InertiaPerMass;
			}
			var group = ragdollCenters.GetValueOrDefault( index );
			var local = skeleton.BindPose[parent].PointToLocal( skeleton.BindPose[id].Position );
			bodyPoints[id] = new( model.Bones.GetBone( $"bone_{id}" ), AttachmentCenters[id], index, new Transform( local, skeleton.BindPose[parent].Rotation.Inverse * skeleton.BindPose[id].Rotation ), point.Mass );
			ragdollCenters[index] = (group.Center + (local - group.Center) / (group.Count + 1), group.Count + 1);
		}
		this.bodyPoints = bodyPoints.Values.ToArray();
		chainCenters.Clear();
		foreach ( var (name, chain) in skeleton.IKBones )
		{
			var points = new List<BodyPoint>( chain.ChainLength );
			int bone = chain.BoneId;
			for ( int i = 0; i < chain.ChainLength && bone >= 0; i++, bone = skeleton.HierParents[bone] )
				if ( bodyPoints.TryGetValue( bone, out var point ) ) points.Add( point );
			chainCenters[name] = points.ToArray();
		}
	}

	// A refreshed mount can receive a model still held by a live NPC. Recreate its CPU
	// rig without generating another render mesh, physics model or animation graph.
	void RestoreBodyRig( Model model )
	{
		var appearance = ReadAppearance();
		var (skeleton, rig, reference, skeletonModelPath) = ReadSkeleton( appearance );
		ReadBoneMasses( skeleton, rig );
		var mesh = ReadMesh( appearance, reference, skeletonModelPath, out var modelPath );
		modelCenter = mesh.Center;
		var scale = ScaleSkeleton( appearance, skeleton );
		FixZeroWeights( skeleton, skeleton.BoneWeights, skeleton.BoneIds, reference );
		Surface = new CharacterSurface( mesh, skeleton, scale );
		var centers = RagdollBuilder.ReadCenters( Host.FileSystem, skeleton, System.IO.Path.ChangeExtension( modelPath, ".fzx" ),
			reference.Center, scale, appearance.Number( "default_scale" ) ?? 1f );
		SetBodyRig( model, skeleton, centers );
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

	internal Vector3? InitialChainCenter( string chain, int link )
	{
		int bone = AttachmentBone( chain, link, false );
		return animationRig is not null && AttachmentCenters.TryGetValue( bone, out var center )
			? animationRig.BindPose[bone].PointToWorld( center ) : null;
	}

	internal CharacterSurface Surface { get; private set; }

	internal float AttachmentScale => (ReadAppearance().Number( "default_scale" ) ?? 1f) / (ReadAppearance().Number( "model_scale" ) ?? 1f);
	(int A, int B)[] overlappingBodies;

	internal void FilterRagdollContacts( ModelPhysics physics )
	{
		overlappingBodies ??= FindOverlaps();
		foreach ( var (a, b) in overlappingBodies )
		{
			// Bind the filter while both ragdoll bodies exist. Recovery can remove them
			// before a PhysicsFilter component gets its first update.
			Sandbox.Physics.PhysicsJoint.CreateFilter( physics.Bodies[a].Component.PhysicsBody, physics.Bodies[b].Component.PhysicsBody );
		}

		(int, int)[] FindOverlaps()
		{
			// Skeleton::AddNullConstraints checks unconnected collision shapes in the bind pose.
			// CheckOverlap can place B relative to A without moving either simulated body.
			var description = physics.Model.Physics;
			var connected = description.Joints.Select( joint => (Math.Min( joint.Body1, joint.Body2 ), Math.Max( joint.Body1, joint.Body2 )) ).ToHashSet();
			var result = new List<(int, int)>();
			for ( int a = 0; a < physics.Bodies.Count; a++ )
			{
				var first = physics.Bodies[a];
				if ( NonCollidingBones.Contains( first.Bone ) ) continue;
				var body = first.Component.PhysicsBody;
				var basis = body.Transform.WithScale( first.Component.WorldScale );
				for ( int b = a + 1; b < physics.Bodies.Count; b++ )
				{
					var second = physics.Bodies[b];
					if ( connected.Contains( (a, b) ) || NonCollidingBones.Contains( second.Bone ) ) continue;
					var relative = description.Parts[a].Transform.ToLocal( description.Parts[b].Transform );
					if ( body.CheckOverlap( second.Component.PhysicsBody, basis.ToWorld( relative ) ) ) result.Add( (a, b) );
				}
			}
			return result.ToArray();
		}
	}

	internal HashSet<int> NonCollidingBones { get; private set; } = [];
	const float Scale = 40f;
	(XmlAsset Groups, float Pitch)? voice;
	CharacterMorph[] handShapes;
	internal CharacterMorph[] HandShapes => handShapes ??= CharacterMorph.Read( Definition ).Where( m => m.Name is "fist_l" or "fist_r" ).ToArray();

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

	internal RabbitGuardAnimations GetGuardAnimations( Model heldModel = null, string stanceOverride = null, string species = null )
	{
		if ( !guard ) return null;
		var equipment = heldModel?.ResourcePath;
		var item = equipment is null ? null : (Host.GetByPath( equipment ) as ObjectLoader)?.ItemDefinition;
		if ( string.IsNullOrWhiteSpace( stanceOverride ) || item?.Element( "anim_override" )?.Attribute( "idle" ) is not null || !Host.FileSystem.FileExists( stanceOverride ) ) stanceOverride = null;
		species ??= Species;
		var key = (equipment, stanceOverride, species);
		if ( animationGraphs.TryGetValue( key, out var existing ) ) return existing;
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
			animationLibrary = Host.GetAnimationLibrary( Path, Definition, animationRig, AttachmentScale );
		}
		var graphPath = equipment is null ? Path : Path + "/equipped/" + equipment["mount://overgrowth/".Length..];
		if ( stanceOverride is not null ) graphPath += "/stance/" + Host.Resolve( stanceOverride );
		if ( species != Species ) graphPath += "/species/" + species;
		return animationGraphs[key] = RabbitGuardAnimations.Create( Host, graphPath, Definition, animationLibrary, modelCenter.Value, species, item, stanceOverride );
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

		var weights = skeleton.BoneWeights ?? Array.Empty<Vector4>();
		var boneIds = skeleton.BoneIds ?? Array.Empty<Vector4>();
		if ( weights.Length != objData.FaceVertexCount || !objData.FaceVertPosIndices.SequenceEqual( reference.FaceVertPosIndices ) )
			throw new InvalidDataException( $"Character mesh and skeleton have different topology: {modelObjPath}" );
		FixZeroWeights( skeleton, weights, boneIds, reference );

		float scale = ScaleSkeleton( appearance, skeleton );
		if ( guard ) Surface = new CharacterSurface( objData, skeleton, scale );
		for ( int i = 0; i < objData.Positions.Count; i++ )
			objData.Positions[i] *= scale;
		objData.Bounds = BBox.FromPoints( objData.Positions );

		// Permanent morphs are always at full weight. Bake them into the rest mesh while
		// keeping the original positions as the reference for expression and fist deltas.
		var restMesh = objData;
		if ( appearance.Attribute( "morph" ) is { } permanent && Host.FileSystem.FileExists( permanent ) )
		{
			restMesh.Positions = ObjLoader.LoadMorphPositions( Host.FileSystem, permanent, objData.Center, objData.FaceVertexCount );
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


		var shapeCounts = RagdollBuilder.Add( Host.FileSystem, builder, skeleton, System.IO.Path.ChangeExtension( modelObjPath, ".fzx" ), reference.Center, scale, appearance.Number( "default_scale" ) ?? 1f, out var bodyCenters );
		var hullPath = modelObjPath[..^4] + "_hulls.obj";
		if ( guard && Host.FileSystem.FileExists( hullPath ) )
			CharacterHulls.Add( builder.AddHitboxSet(), Host.FileSystem, hullPath, objData.Center, scale, skeleton, bodyCenters );

		var model = builder.Create();
		SetBodyRig( model, skeleton, bodyCenters );
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
