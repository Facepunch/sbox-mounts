// Ported from OpenMagicka (MIT, https://github.com/vogster/OpenMagicka),
// src/OpenMagicka.Content: Xnb/Readers, XnAnimation, PolygonHead and Magicka readers.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Magicka;

/// <summary>Map from <see cref="XnbTypeReader.Key"/> to the code that reads that type.</summary>
internal static partial class XnbReaders
{
	static readonly Dictionary<string, Func<XnbContentReader, object>> Readers = new()
	{
		["StringReader"] = r => r.ReadString(),
		["SingleReader"] = r => r.ReadSingle(),
		["BooleanReader"] = r => r.ReadBoolean(),
		["Vector3Reader"] = r => r.ReadVector3(),
		["MatrixReader"] = r => r.ReadMatrix(),
		["ExternalReferenceReader"] = r => r.ReadExternalReference(),
		["ListReader<Vector3>"] = ReadVector3List,
		["ListReader<String>"] = ReadStringList,
		["DictionaryReader<String,Object>"] = ReadStringObjectDictionary,
		["Texture2DReader"] = ReadTexture2D,
		["Texture3DReader"] = ReadTexture3D,
		["TextureCubeReader"] = ReadTextureCube,
		["VertexDeclarationReader"] = ReadVertexDeclaration,
		["VertexBufferReader"] = r => new VertexBuffer( r.ReadSizedBytes() ),
		["IndexBufferReader"] = ReadIndexBuffer,
		["EffectReader"] = r => new Effect( r.ReadSizedBytes() ),
		["BasicEffectReader"] = ReadBasicEffect,
		["EffectMaterialReader"] = ReadEffectMaterial,
		["ModelReader"] = ReadModel,
		["SkinnedModelReader"] = ReadSkinnedModel,
		["SkinnedModelBoneReader"] = ReadSkinnedModelBone,
		["AnimationClipReader"] = ReadAnimationClip,
		["SkinnedModelBasicEffectReader"] = ReadSkinnedModelBasicEffect,
		["RenderDeferredEffectReader"] = ReadRenderDeferredEffect,
		["RenderDeferredLiquidEffectReader"] = ReadLiquidEffect,
		["LavaEffectReader"] = ReadLavaEffect,
		["AdditiveEffectReader"] = ReadAdditiveEffect,
		["SkinnedModelDeferredNormalMappedEffectReader"] = ReadSkinnedNormalledEffect,
		["BiTreeModelReader"] = ReadBiTree,
		["LevelModelReader"] = ReadLevelModel,
		["CharacterTemplateReader"] = ReadCharacterTemplate,
		["ItemReader"] = ReadItemTemplate,
	};

	public static bool TryGet( string key, out Func<XnbContentReader, object> reader ) => Readers.TryGetValue( key, out reader );

	// ---- lists -------------------------------------------------------------------------------

	static object ReadVector3List( XnbContentReader r )
	{
		var items = new Vector3[r.ReadCount( 12 )];
		for ( var i = 0; i < items.Length; i++ )
			items[i] = r.ReadVector3();

		return items;
	}

	static object ReadStringList( XnbContentReader r )
	{
		var items = new string[r.ReadCount( 1 )];
		for ( var i = 0; i < items.Length; i++ )
			items[i] = r.ReadObject<string>();

		return items;
	}

	static object ReadStringObjectDictionary( XnbContentReader r )
	{
		var count = r.ReadCount( 3 );
		var items = new Dictionary<string, object>();
		for ( var i = 0; i < count; i++ )
		{
			var key = r.ReadObject<string>() ?? throw new InvalidDataException( "null dictionary key" );
			if ( !items.TryAdd( key, r.ReadObject() ) )
				throw new InvalidDataException( $"duplicate dictionary key '{key}'" );
		}

		return items;
	}

	/// <summary>A u32 count followed by that many items; minBytes guards against huge allocations.</summary>
	static T[] ReadList<T>( this XnbContentReader r, int minBytes, Func<XnbContentReader, T> read )
	{
		var items = new T[r.ReadCount( minBytes )];
		for ( var i = 0; i < items.Length; i++ )
			items[i] = read( r );

		return items;
	}

	static int ReadNonNegative( this XnbContentReader r, string what )
	{
		var value = r.ReadInt32();
		return value >= 0 ? value : throw new InvalidDataException( $"negative {what} {value}" );
	}

	// ---- textures ----------------------------------------------------------------------------

	const int MaxTextureSize = 16384;
	const int MaxMipLevels = 16;

	static object ReadTexture2D( XnbContentReader r )
	{
		var format = ReadSurfaceFormat( r );
		var width = ReadDimension( r );
		var height = ReadDimension( r );
		return new Texture2DContent( format, width, height, ReadMipLevels( r, ReadMipCount( r ) ) );
	}

	static object ReadTexture3D( XnbContentReader r )
	{
		var format = ReadSurfaceFormat( r );
		var width = ReadDimension( r );
		var height = ReadDimension( r );
		var depth = ReadDimension( r );
		return new Texture3DContent( format, width, height, depth, ReadMipLevels( r, ReadMipCount( r ) ) );
	}

	static object ReadTextureCube( XnbContentReader r )
	{
		var format = ReadSurfaceFormat( r );
		var size = ReadDimension( r );
		var mips = ReadMipCount( r );
		var faces = new byte[6][][];
		for ( var face = 0; face < faces.Length; face++ )
			faces[face] = ReadMipLevels( r, mips );

		return new TextureCubeContent( format, size, faces );
	}

	static SurfaceFormat ReadSurfaceFormat( XnbContentReader r )
	{
		var format = (SurfaceFormat)r.ReadInt32();
		return Enum.IsDefined( format ) ? format : throw new InvalidDataException( $"unknown surface format {(int)format}" );
	}

	static int ReadDimension( XnbContentReader r )
	{
		var value = r.ReadUInt32();
		return value is >= 1 and <= MaxTextureSize ? (int)value : throw new InvalidDataException( $"texture dimension {value}" );
	}

	static int ReadMipCount( XnbContentReader r )
	{
		var value = r.ReadUInt32();
		return value is >= 1 and <= MaxMipLevels ? (int)value : throw new InvalidDataException( $"texture mip count {value}" );
	}

	static byte[][] ReadMipLevels( XnbContentReader r, int count )
	{
		var levels = new byte[count][];
		for ( var i = 0; i < count; i++ )
			levels[i] = r.ReadSizedBytes();

		return levels;
	}

	// ---- geometry ----------------------------------------------------------------------------

	static object ReadVertexDeclaration( XnbContentReader r )
	{
		var count = r.ReadCount( 8 );
		var elements = new VertexElement[count];
		for ( var i = 0; i < count; i++ )
		{
			var stream = r.ReadUInt16();
			var offset = r.ReadUInt16();
			var format = (VertexElementFormat)r.ReadByte();
			var method = r.ReadByte();
			var usage = (VertexElementUsage)r.ReadByte();
			var usageIndex = r.ReadByte();

			if ( !Enum.IsDefined( format ) || !Enum.IsDefined( usage ) )
				throw new InvalidDataException( $"unknown vertex element format {(int)format} or usage {(int)usage}" );

			elements[i] = new VertexElement( stream, offset, format, method, usage, usageIndex );
		}

		return new VertexDeclaration( elements );
	}

	static object ReadIndexBuffer( XnbContentReader r )
	{
		var is16Bit = r.ReadBoolean();
		return new IndexBuffer( is16Bit, r.ReadSizedBytes() );
	}

	// ---- effects -----------------------------------------------------------------------------

	static object ReadBasicEffect( XnbContentReader r ) => new BasicEffect(
		Texture: r.ReadExternalReference(),
		DiffuseColor: r.ReadVector3(),
		EmissiveColor: r.ReadVector3(),
		SpecularColor: r.ReadVector3(),
		SpecularPower: r.ReadSingle(),
		Alpha: r.ReadSingle(),
		VertexColorEnabled: r.ReadBoolean() );

	static object ReadEffectMaterial( XnbContentReader r )
	{
		var effect = r.ReadExternalReference();
		var parameters = r.ReadObject() switch
		{
			null => new Dictionary<string, object>(),
			Dictionary<string, object> found => found,
			var other => throw new InvalidDataException( $"effect material parameters of type {other.GetType().Name}" ),
		};

		return new EffectMaterial( effect, parameters );
	}

	// ---- model -------------------------------------------------------------------------------

	const int ByteBoneRefLimit = 255;

	static object ReadModel( XnbContentReader r )
	{
		var boneCount = r.ReadCount( 1 + 64 );
		var names = new string[boneCount];
		var transforms = new Matrix4x4[boneCount];
		for ( var i = 0; i < boneCount; i++ )
		{
			names[i] = r.ReadObject<string>();
			transforms[i] = r.ReadMatrix();
		}

		var bones = new ModelBone[boneCount];
		for ( var i = 0; i < boneCount; i++ )
		{
			var parent = ReadBoneRef( r, boneCount );
			var children = new int[r.ReadCount( boneCount < ByteBoneRefLimit ? 1 : 4 )];
			for ( var c = 0; c < children.Length; c++ )
				children[c] = ReadBoneRef( r, boneCount ) ?? throw new InvalidDataException( "null child bone" );

			bones[i] = new ModelBone( names[i], transforms[i], parent, children );
		}

		var declarations = new VertexDeclaration[r.ReadCount( 1 )];
		for ( var i = 0; i < declarations.Length; i++ )
			declarations[i] = r.ReadObject<VertexDeclaration>() ?? throw new InvalidDataException( "null vertex declaration" );

		// name, bone ref, bounds, buffers, tag and part count: at least 25 bytes per mesh
		var meshes = new ModelMesh[r.ReadCount( 25 )];
		for ( var i = 0; i < meshes.Length; i++ )
			meshes[i] = ReadMesh( r, boneCount, declarations.Length );

		var root = ReadBoneRef( r, boneCount );
		r.ReadObject(); // tag
		return new ModelContent( bones, declarations, meshes, root );
	}

	static ModelMesh ReadMesh( XnbContentReader r, int boneCount, int declarationCount )
	{
		var name = r.ReadObject<string>();
		var parentBone = ReadBoneRef( r, boneCount );
		var bounds = r.ReadBoundingSphere();
		var vertexBuffer = r.ReadObject<VertexBuffer>();
		var indexBuffer = r.ReadObject<IndexBuffer>();
		r.ReadObject(); // tag

		var parts = new ModelMeshPart[r.ReadCount( 6 * 4 + 2 )];
		for ( var i = 0; i < parts.Length; i++ )
		{
			var streamOffset = ReadSize( r );
			var baseVertex = ReadSize( r );
			var numVertices = ReadSize( r );
			var startIndex = ReadSize( r );
			var primitiveCount = ReadSize( r );
			var declaration = ReadSize( r );

			if ( declaration >= declarationCount )
				throw new InvalidDataException( $"mesh part uses declaration {declaration} of {declarationCount}" );

			r.ReadObject(); // tag
			var effect = r.ReadSharedResourceRef();
			parts[i] = new ModelMeshPart( streamOffset, baseVertex, numVertices, startIndex, primitiveCount, declaration, effect );
		}

		return new ModelMesh( name, parentBone, bounds, vertexBuffer, indexBuffer, parts );
	}

	/// <summary>0 = no bone, otherwise the bone index plus one.</summary>
	static int? ReadBoneRef( XnbContentReader r, int boneCount )
	{
		var value = boneCount < ByteBoneRefLimit ? r.ReadByte() : r.ReadUInt32();
		if ( value == 0 )
			return null;

		if ( value > boneCount )
			throw new InvalidDataException( $"bone reference {value} in a model with {boneCount} bones" );

		return (int)value - 1;
	}

	static int ReadSize( XnbContentReader r )
	{
		var value = r.ReadUInt32();
		return value <= int.MaxValue ? (int)value : throw new InvalidDataException( $"model value {value} out of range" );
	}

	// ---- XNAnimation -------------------------------------------------------------------------

	// time + translation + orientation + scale
	const int KeyframeSize = 4 + 12 + 16 + 12;

	static object ReadSkinnedModel( XnbContentReader r )
	{
		var model = r.ReadObject<ModelContent>();
		var bones = ReadSharedRefs( r );
		var clips = ReadSharedRefs( r );
		return new SkinnedModelContent( model, bones, clips );
	}

	static object ReadSkinnedModelBone( XnbContentReader r )
	{
		var index = r.ReadUInt16();
		var name = r.ReadString();
		var pose = ReadPose( r );
		var inverseBindPose = r.ReadMatrix();
		var parent = r.ReadSharedResourceRef();
		var children = ReadSharedRefs( r );
		return new SkinnedModelBone( index, name, pose, inverseBindPose, parent, children );
	}

	static object ReadAnimationClip( XnbContentReader r )
	{
		var name = r.ReadString();
		var duration = r.ReadSingle();
		var channels = new AnimationChannel[r.ReadCount( 1 + 4 )];
		for ( var i = 0; i < channels.Length; i++ )
			channels[i] = new AnimationChannel( r.ReadString(), ReadKeyframes( r ) );

		return new AnimationClip( name, duration, channels );
	}

	/// <summary>Shared with Magicka's animated level parts, which store channels the same way.</summary>
	static AnimationKeyframe[] ReadKeyframes( XnbContentReader r )
	{
		var keyframes = new AnimationKeyframe[r.ReadCount( KeyframeSize )];
		for ( var i = 0; i < keyframes.Length; i++ )
			keyframes[i] = new AnimationKeyframe( r.ReadSingle(), ReadPose( r ) );

		return keyframes;
	}

	static BonePose ReadPose( XnbContentReader r ) => new( r.ReadVector3(), r.ReadQuaternion(), r.ReadVector3() );

	static SharedResourceRef?[] ReadSharedRefs( XnbContentReader r )
	{
		var refs = new SharedResourceRef?[r.ReadCount( 1 )];
		for ( var i = 0; i < refs.Length; i++ )
			refs[i] = r.ReadSharedResourceRef();

		return refs;
	}

	static object ReadSkinnedModelBasicEffect( XnbContentReader r ) => new SkinnedModelBasicEffect(
		Technique: r.ReadByte(),
		EmissiveAmount: r.ReadSingle(),
		DiffuseColor: r.ReadVector3(),
		SpecularAmount: r.ReadSingle(),
		SpecularPower: r.ReadSingle(),
		Diffuse1Alpha: r.ReadSingle(),
		UseSoftLightBlend: r.ReadBoolean(),
		DiffuseMap0Enabled: r.ReadBoolean(),
		DiffuseMap1Enabled: r.ReadBoolean(),
		SpecularMapEnabled: r.ReadBoolean(),
		DamageMap0Enabled: r.ReadBoolean(),
		DamageMap1Enabled: r.ReadBoolean(),
		NormalMapEnabled: r.ReadBoolean(),
		DiffuseMap0: r.ReadContentRef<Texture2DContent>(),
		DiffuseMap1: r.ReadContentRef<Texture2DContent>(),
		SpecularMap: r.ReadContentRef<Texture2DContent>(),
		DamageMap0: r.ReadContentRef<Texture2DContent>(),
		DamageMap1: r.ReadContentRef<Texture2DContent>(),
		NormalMap: r.ReadContentRef<Texture2DContent>() );

	// ---- PolygonHead -------------------------------------------------------------------------

	const int MaxTreeDepth = 32;
	// i32 primitive count, i32 start index, bounding box, two child flags
	const int TreeNodeSize = 4 + 4 + 24 + 2;

	static object ReadRenderDeferredEffect( XnbContentReader r ) => new RenderDeferredEffect(
		Alpha: r.ReadSingle(),
		Sharpness: r.ReadSingle(),
		VertexColorEnabled: r.ReadBoolean(),
		UseMaterialTextureForReflectiveness: r.ReadBoolean(),
		ReflectionMap: r.ReadExternalReference(),
		Layer0: ReadRenderDeferredLayer( r ),
		Layer1: r.ReadBoolean() ? ReadRenderDeferredLayer( r ) : null );

	static RenderDeferredLayer ReadRenderDeferredLayer( XnbContentReader r ) => new(
		DiffuseTextureAlphaDisabled: r.ReadBoolean(),
		AlphaMaskEnabled: r.ReadBoolean(),
		DiffuseColor: r.ReadVector3(),
		SpecAmount: r.ReadSingle(),
		SpecPower: r.ReadSingle(),
		EmissiveAmount: r.ReadSingle(),
		NormalPower: r.ReadSingle(),
		Reflectiveness: r.ReadSingle(),
		DiffuseTexture: r.ReadExternalReference(),
		MaterialTexture: r.ReadExternalReference(),
		NormalTexture: r.ReadExternalReference() );

	static object ReadLiquidEffect( XnbContentReader r ) => new RenderDeferredLiquidEffect(
		ReflectionMap: r.ReadExternalReference(),
		WaveHeight: r.ReadSingle(),
		WaveSpeed0: r.ReadVector2(),
		WaveSpeed1: r.ReadVector2(),
		WaterReflectiveness: r.ReadSingle(),
		BottomColor: r.ReadVector3(),
		DeepBottomColor: r.ReadVector3(),
		WaterEmissiveAmount: r.ReadSingle(),
		WaterSpecAmount: r.ReadSingle(),
		WaterSpecPower: r.ReadSingle(),
		BottomTexture: r.ReadExternalReference(),
		WaterNormalMap: r.ReadExternalReference(),
		IceReflectiveness: r.ReadSingle(),
		IceColor: r.ReadVector3(),
		IceEmissiveAmount: r.ReadSingle(),
		IceSpecAmount: r.ReadSingle(),
		IceSpecPower: r.ReadSingle(),
		IceDiffuseMap: r.ReadExternalReference(),
		IceNormalMap: r.ReadExternalReference() );

	static object ReadLavaEffect( XnbContentReader r ) => new LavaEffect(
		MaskDistortion: r.ReadSingle(),
		Speed0: r.ReadVector2(),
		Speed1: r.ReadVector2(),
		LavaHotEmissiveAmount: r.ReadSingle(),
		LavaColdEmissiveAmount: r.ReadSingle(),
		LavaSpecAmount: r.ReadSingle(),
		LavaSpecPower: r.ReadSingle(),
		TempFrequency: r.ReadSingle(),
		ToneMap: r.ReadExternalReference(),
		TempMap: r.ReadExternalReference(),
		MaskMap: r.ReadExternalReference(),
		RockColor: r.ReadVector3(),
		RockEmissiveAmount: r.ReadSingle(),
		RockSpecAmount: r.ReadSingle(),
		RockSpecPower: r.ReadSingle(),
		RockNormalPower: r.ReadSingle(),
		RockTexture: r.ReadExternalReference(),
		RockNormalMap: r.ReadExternalReference() );

	static object ReadAdditiveEffect( XnbContentReader r ) => new AdditiveEffect(
		ColorTint: r.ReadVector3(),
		VertexColorEnabled: r.ReadBoolean(),
		TextureEnabled: r.ReadBoolean(),
		Texture: r.ReadExternalReference() );

	static object ReadSkinnedNormalledEffect( XnbContentReader r ) => new SkinnedModelDeferredNormalMappedEffect(
		DiffuseColor: r.ReadVector3(),
		SpecularAmount: r.ReadSingle(),
		SpecularPower: r.ReadSingle(),
		EmissiveAmount: r.ReadSingle(),
		NormalPower: r.ReadSingle(),
		DiffuseMap: r.ReadExternalReference(),
		MaterialMap: r.ReadExternalReference(),
		DamageMap: r.ReadExternalReference(),
		NormalMap: r.ReadExternalReference(),
		NormalDamageMap: r.ReadExternalReference() );

	static object ReadBiTree( XnbContentReader r )
	{
		var roots = new BiTreeRoot[r.ReadCount( 2 + 12 + 8 + 4 + TreeNodeSize )];
		for ( var i = 0; i < roots.Length; i++ )
			roots[i] = ReadBiTreeRoot( r );

		return new BiTreeModel( roots );
	}

	static BiTreeRoot ReadBiTreeRoot( XnbContentReader r )
	{
		var visible = r.ReadBoolean();
		var castShadows = r.ReadBoolean();
		var sway = r.ReadSingle();
		var entityInfluence = r.ReadSingle();
		var groundLevel = r.ReadSingle();
		var numVertices = r.ReadNonNegative( "vertex count" );
		var stride = r.ReadNonNegative( "vertex stride" );
		var declaration = r.ReadObject<VertexDeclaration>();
		var vertexBuffer = r.ReadObject<VertexBuffer>();
		var indexBuffer = r.ReadObject<IndexBuffer>();
		var effect = r.ReadObject();
		var tree = ReadTreeNode( r, 0 );

		return new BiTreeRoot( visible, castShadows, sway, entityInfluence, groundLevel, numVertices, stride,
			declaration, vertexBuffer, indexBuffer, effect, tree );
	}

	/// <summary>Depth-first: child A completely, then child B.</summary>
	static BiTreeNode ReadTreeNode( XnbContentReader r, int depth )
	{
		if ( depth >= MaxTreeDepth )
			throw new InvalidDataException( $"BiTree deeper than {MaxTreeDepth}" );

		var primitives = r.ReadNonNegative( "primitive count" );
		var startIndex = r.ReadNonNegative( "start index" );
		var bounds = r.ReadBoundingBox();
		var childA = r.ReadBoolean() ? ReadTreeNode( r, depth + 1 ) : null;
		var childB = r.ReadBoolean() ? ReadTreeNode( r, depth + 1 ) : null;
		return new BiTreeNode( primitives, startIndex, bounds, childA, childB );
	}

	// ---- LevelModel --------------------------------------------------------------------------

	const int CollisionSlots = 10;
	const int MaxPartDepth = 16;
	// name, flag, model id, eight u32 counts or floats, two flags, child count
	const int MinPartSize = 1 + 1 + 1 + 4 * 8 + 2;
	// three u16 vertices, three u16 neighbours, three f32 costs, one u8 movement flag
	const int NavTriangleSize = 6 + 6 + 12 + 1;
	const ushort NoNeighbour = 0xFFFF;

	static object ReadLevelModel( XnbContentReader r )
	{
		var geometry = r.ReadObject<BiTreeModel>();
		var parts = r.ReadList( MinPartSize, x => ReadAnimatedPart( x, 0 ) );
		var lights = r.ReadList( 1 + 24 + 8 + 13 + 24 + 12 + 5, ReadLight );
		var effects = r.ReadList( 2 + 28, ReadEffectPlacement );
		var physics = r.ReadList( 64 + 1, x => new PhysicsEntityPlacement( x.ReadMatrix(), x.ReadString() ) );
		var liquids = r.ReadList( 4 + 12 + 3, ReadLiquid );
		var forceFields = r.ReadList( 41 + 3 + 12, ReadForceField );

		var collision = new CollisionMesh[CollisionSlots];
		for ( var slot = 0; slot < CollisionSlots; slot++ )
			collision[slot] = r.ReadBoolean() ? ReadCollisionMesh( r ) : null;

		var camera = r.ReadBoolean() ? ReadCollisionMesh( r ) : null;

		var triggers = r.ReadList( 1 + 40, x => new TriggerArea( x.ReadString(), x.ReadVector3(), x.ReadVector3(), x.ReadQuaternion() ) );
		var locators = r.ReadList( 1 + 68, x => new Locator( x.ReadString(), x.ReadMatrix(), x.ReadSingle() ) );
		var navMesh = ReadNavMesh( r );

		return new LevelModel( geometry, parts, lights, effects, physics, liquids, forceFields, collision,
			camera, triggers, locators, navMesh );
	}

	static LevelLight ReadLight( XnbContentReader r )
	{
		var name = r.ReadString();
		var position = r.ReadVector3();
		var direction = r.ReadVector3();
		var type = (LevelLightType)r.ReadInt32();
		var variation = (LightVariationType)r.ReadInt32();

		if ( !Enum.IsDefined( type ) )
			throw new InvalidDataException( $"light '{name}' has unsupported type {(int)type}" );

		return new LevelLight( name, position, direction, type, variation,
			Range: r.ReadSingle(),
			UseAttenuation: r.ReadBoolean(),
			CutoffAngle: r.ReadSingle(),
			Sharpness: r.ReadSingle(),
			DiffuseColor: r.ReadVector3(),
			AmbientColor: r.ReadVector3(),
			SpecularAmount: r.ReadSingle(),
			VariationSpeed: r.ReadSingle(),
			VariationAmount: r.ReadSingle(),
			ShadowMapSize: r.ReadInt32(),
			CastShadows: r.ReadBoolean() );
	}

	static VisualEffectPlacement ReadEffectPlacement( XnbContentReader r ) =>
		new( r.ReadString(), r.ReadVector3(), r.ReadVector3(), r.ReadSingle(), r.ReadString() );

	static Liquid ReadLiquid( XnbContentReader r )
	{
		var effect = r.ReadObject();
		if ( effect is not (RenderDeferredLiquidEffect or LavaEffect) )
			throw new InvalidDataException( $"liquid with effect {effect?.GetType().Name ?? "null"}" );

		return new Liquid( effect,
			VertexBuffer: r.ReadObject<VertexBuffer>(),
			IndexBuffer: r.ReadObject<IndexBuffer>(),
			Declaration: r.ReadObject<VertexDeclaration>(),
			VertexStride: r.ReadNonNegative( "vertex stride" ),
			NumVertices: r.ReadNonNegative( "vertex count" ),
			PrimitiveCount: r.ReadNonNegative( "primitive count" ),
			HasCollision: r.ReadBoolean(),
			Freezable: r.ReadBoolean(),
			AutoFreeze: r.ReadBoolean() );
	}

	static ForceField ReadForceField( XnbContentReader r ) => new(
		Color: r.ReadVector3(),
		Width: r.ReadSingle(),
		AlphaPower: r.ReadSingle(),
		AlphaFalloffPower: r.ReadSingle(),
		MaxRadius: r.ReadSingle(),
		RippleDistortion: r.ReadSingle(),
		MapDistortion: r.ReadSingle(),
		VertexColorEnabled: r.ReadBoolean(),
		DisplacementMap: r.ReadExternalReference(),
		Ttl: r.ReadSingle(),
		VertexBuffer: r.ReadObject<VertexBuffer>(),
		IndexBuffer: r.ReadObject<IndexBuffer>(),
		Declaration: r.ReadObject<VertexDeclaration>(),
		VertexStride: r.ReadNonNegative( "vertex stride" ),
		NumVertices: r.ReadNonNegative( "vertex count" ),
		PrimitiveCount: r.ReadNonNegative( "primitive count" ) );

	static AnimatedLevelPart ReadAnimatedPart( XnbContentReader r, int depth )
	{
		if ( depth >= MaxPartDepth )
			throw new InvalidDataException( $"animated level parts nested deeper than {MaxPartDepth}" );

		var name = r.ReadString();
		var affectShields = r.ReadBoolean();
		var model = r.ReadObject<ModelContent>();
		var meshSettings = r.ReadList( 1 + 2, x => new MeshSetting( x.ReadString(), x.ReadBoolean(), x.ReadBoolean() ) );
		var liquids = r.ReadList( 4 + 12 + 3, ReadLiquid );
		var locators = r.ReadList( 1 + 68, x => new Locator( x.ReadString(), x.ReadMatrix(), x.ReadSingle() ) );
		var duration = r.ReadSingle();
		var animation = ReadKeyframes( r );
		var effects = r.ReadList( 2 + 28, ReadEffectPlacement );
		var lights = r.ReadList( 1 + 64, x => new PartLightAttachment( x.ReadString(), x.ReadMatrix() ) );
		var collision = r.ReadBoolean() ? ReadPartCollision( r ) : null;
		var navMesh = r.ReadBoolean() ? ReadNavMesh( r ) : null;
		var children = r.ReadList( MinPartSize, x => ReadAnimatedPart( x, depth + 1 ) );

		return new AnimatedLevelPart( name, affectShields, model, meshSettings, liquids, locators, duration,
			animation, effects, lights, collision, navMesh, children );
	}

	static PartCollision ReadPartCollision( XnbContentReader r )
	{
		var material = (CollisionMaterial)r.ReadByte();
		if ( !Enum.IsDefined( material ) )
			throw new InvalidDataException( $"unknown collision material {(int)material}" );

		return new PartCollision( material, ReadCollisionMesh( r ) );
	}

	/// <summary>A List&lt;Vector3&gt; object of vertices, then an i32 count of index triples.</summary>
	static CollisionMesh ReadCollisionMesh( XnbContentReader r )
	{
		var vertices = r.ReadObject<Vector3[]>() ?? throw new InvalidDataException( "collision mesh without a vertex list" );
		var triangles = r.ReadList( 12, x => new CollisionTriangle( x.ReadInt32(), x.ReadInt32(), x.ReadInt32() ) );

		foreach ( var t in triangles )
		{
			if ( !InRange( t.I0, vertices.Length ) || !InRange( t.I1, vertices.Length ) || !InRange( t.I2, vertices.Length ) )
				throw new InvalidDataException( $"collision triangle {t} outside {vertices.Length} vertices" );
		}

		return new CollisionMesh( vertices, triangles );
	}

	static NavMesh ReadNavMesh( XnbContentReader r )
	{
		var vertices = new Vector3[CheckedU16( r, 12 )];
		for ( var i = 0; i < vertices.Length; i++ )
			vertices[i] = r.ReadVector3();

		var triangles = new NavTriangle[CheckedU16( r, NavTriangleSize )];
		for ( var i = 0; i < triangles.Length; i++ )
		{
			var a = r.ReadUInt16();
			var b = r.ReadUInt16();
			var c = r.ReadUInt16();
			if ( a >= vertices.Length || b >= vertices.Length || c >= vertices.Length )
				throw new InvalidDataException( $"nav triangle ({a}, {b}, {c}) outside {vertices.Length} vertices" );

			triangles[i] = new NavTriangle( a, b, c,
				Neighbour( r.ReadUInt16() ), Neighbour( r.ReadUInt16() ), Neighbour( r.ReadUInt16() ),
				r.ReadSingle(), r.ReadSingle(), r.ReadSingle(),
				(MovementProperties)r.ReadByte() );
		}

		return new NavMesh( vertices, triangles );
	}

	static ushort? Neighbour( ushort value ) => value == NoNeighbour ? null : value;

	static int CheckedU16( XnbContentReader r, int itemSize )
	{
		var count = r.ReadUInt16();
		if ( (long)count * itemSize > r.Remaining )
			throw new InvalidDataException( $"nav mesh count {count} exceeds the remaining {r.Remaining} bytes" );

		return count;
	}

	static bool InRange( int index, int count ) => index >= 0 && index < count;
}
