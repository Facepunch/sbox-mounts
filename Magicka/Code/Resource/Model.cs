using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Nv3 = System.Numerics.Vector3;
using NQuat = System.Numerics.Quaternion;
using NMat = System.Numerics.Matrix4x4;

namespace Magicka;

/// <summary>
/// A Model or SkinnedModel XNB file, registered as an s&amp;box model. When paired with a
/// separate animation skeleton XNB, its clips are merged onto the mesh model directly.
/// </summary>
internal sealed class ModelLoader( string file, string skeletonFile = null, string surface = null ) : ResourceLoader<MagickaMount>
{
	protected override object Load()
	{
		var content = XnbContent.Read( Host.ReadAllBytes( file ) );
		XnbContent skeletonContent = null;
		if ( skeletonFile is not null )
		{
			try
			{
				skeletonContent = XnbContent.Read( Host.ReadAllBytes( skeletonFile ) );
			}
			catch ( Exception e )
			{
				Log.Warning( $"{Host.Title}: could not read paired animations '{skeletonFile}' for '{file}': {e.Message}" );
			}
		}

		try
		{
			return new MagickaModelBuilder( Host, file, Path, content, skeletonContent, surface ).Build();
		}
		catch ( Exception e )
		{
			Log.Warning( $"{Host.Title}: {Path} could not be built: {e}" );
			return null;
		}
	}
}

/// <summary>
/// Builds XNA 3.1 static and skinned models as s&amp;box models. Static meshes are baked into
/// their parent bone's absolute transform; skinned meshes keep bind-pose vertices and attach
/// clips from their own file or a paired animation skeleton file.
/// </summary>
internal sealed partial class MagickaModelBuilder(
	MagickaMount host,
	string file,
	string path,
	XnbContent content,
	XnbContent skeletonContent,
	string surface = null )
{
	private const float AnimationFps = 30f;
	private const int MaxAnimationFrames = 1200;

	private readonly List<Bone> bones = [];
	private readonly List<string> channelNames = [];
	private int[] boneVertexCounts;
	private int inlineTextures;

	private sealed record Bone( string Name, string ParentName, Vector3 Position, Rotation Rotation, Transform LocalBind );

	public Model Build()
	{
		var skinned = content.Root as SkinnedModelContent;
		var model = skinned?.Model ?? content.Root as ModelContent;
		if ( model is null || model.Meshes.Count == 0 )
			return null;

		var builder = Model.Builder.WithName( path );
		if ( !string.IsNullOrEmpty( surface ) )
			builder.WithSurface( surface );

		if ( skinned is not null )
		{
			CollectSkinnedBones( skinned );
			boneVertexCounts = new int[bones.Count];
			foreach ( var bone in bones )
				builder.AddBone( bone.Name, bone.Position, bone.Rotation, bone.ParentName );

			AddAnimations( builder, skinned );
		}

		var staticPositions = skinned is null ? new List<Vector3>() : null;
		var totalBounds = AddMeshes( builder, model, skinned is not null && bones.Count > 0, staticPositions );

		// Supply a default hitbox and procedural skeleton ragdoll for skinned characters.
		if ( skinned is not null && bones.Count > 0 && totalBounds.Size.LengthSquared > 0.01f )
		{
			var set = builder.AddHitboxSet( "default" );
			var box = set.AddBox( "body", bones[0].Name, totalBounds );
			if ( !string.IsNullOrEmpty( surface ) )
				box.WithSurface( surface );

			AddRagdollPhysics( builder, totalBounds );
		}
		else if ( staticPositions is { Count: >= 4 } && totalBounds.Size.LengthSquared > 0.01f )
		{
			builder.AddCollisionHull( staticPositions.ToArray() );
		}
		return builder.Create();
	}

	internal Model BuildFromModel( ModelContent model, CollisionMesh collision = null, bool skipRootBoneTransform = false )
	{
		if ( model is null || model.Meshes.Count == 0 )
			return null;

		var builder = Model.Builder.WithName( path );
		if ( !string.IsNullOrEmpty( surface ) )
			builder.WithSurface( surface );

		var totalBounds = AddMeshes( builder, model, false, null, skipRootBoneTransform );
		if ( collision is { Vertices.Count: > 0, Triangles.Count: > 0 } )
		{
			var verts = new Vector3[collision.Vertices.Count];
			for ( var i = 0; i < verts.Length; i++ )
				verts[i] = MagickaSpace.Position( collision.Vertices[i] );

			var indices = new int[collision.Triangles.Count * 3];
			for ( var i = 0; i < collision.Triangles.Count; i++ )
			{
				var tri = collision.Triangles[i];
				indices[i * 3] = tri.I0;
				indices[i * 3 + 1] = tri.I1;
				indices[i * 3 + 2] = tri.I2;
			}

			builder.AddCollisionMesh( verts, indices );
		}

		return totalBounds.Size.LengthSquared > 0.01f ? builder.Create() : null;
	}

	private void CollectSkinnedBones( SkinnedModelContent skinned )
	{
		// Magicka skins characters against CharacterTemplate.Skeleton (not the mesh file's own
		// bone list, which can have mangled Maya names and mismatched bind poses).
		var externalSkinned = skeletonContent?.Root as SkinnedModelContent;
		var useExternal = externalSkinned is { Bones.Count: > 0 };
		var sourceContent = useExternal ? skeletonContent : content;
		var sourceBones = useExternal ? externalSkinned.Bones : skinned.Bones;

		var byIndex = new SkinnedModelBone[sourceBones.Count];
		foreach ( var reference in sourceBones )
		{
			if ( sourceContent.Resolve( reference ) is not SkinnedModelBone bone )
				continue;

			if ( bone.Index >= 0 && bone.Index < byIndex.Length )
				byIndex[bone.Index] = bone;
		}

		var used = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var uniqueNames = new string[byIndex.Length];

		for ( var i = 0; i < byIndex.Length; i++ )
		{
			var original = byIndex[i]?.Name;
			var name = string.IsNullOrWhiteSpace( original ) ? $"bone_{i}" : original;
			if ( !used.Add( name ) )
			{
				name = $"{name}_{i}";
				used.Add( name );
			}

			uniqueNames[i] = name;
			channelNames.Add( original ?? uniqueNames[i] );
		}

		for ( var i = 0; i < byIndex.Length; i++ )
		{
			var bone = byIndex[i];
			if ( bone is null )
				continue;

			int? parentIndex = null;
			if ( bone.Parent is { } reference && sourceContent.Resolve( reference ) is SkinnedModelBone parent )
				parentIndex = parent.Index;

			var parentName = parentIndex is { } p && p >= 0 && p < uniqueNames.Length ? uniqueNames[p] : null;
			if ( !NMat.Invert( bone.InverseBindPose, out var absolute ) )
				absolute = NMat.Identity;

			var localBind = new Transform(
				MagickaSpace.Position( bone.BindPose.Translation ),
				MagickaSpace.ToWorld( bone.BindPose.Orientation ),
				ToScale( bone.BindPose.Scale ) );

			bones.Add( new Bone(
				uniqueNames[i],
				parentName,
				MagickaSpace.Position( MagickaSpace.Translation( absolute ) ),
				MagickaSpace.ToWorld( absolute ),
				localBind ) );
		}
	}

	private void AddAnimations( ModelBuilder builder, SkinnedModelContent skinned )
	{
		var externalSkinned = skeletonContent?.Root as SkinnedModelContent;
		var hasExternalClips = externalSkinned is { Clips.Count: > 0 };

		var clipSource = hasExternalClips ? skeletonContent : content;
		var clipRefs = hasExternalClips ? externalSkinned.Clips : skinned.Clips;
		var addedNames = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var reference in clipRefs )
		{
			if ( clipSource.Resolve( reference ) is not AnimationClip clip )
				continue;

			var name = string.IsNullOrWhiteSpace( clip.Name ) ? "default" : clip.Name;
			// Drop the exporter stub clip when real clips are present.
			if ( hasExternalClips && name.Equals( "Take 001", StringComparison.OrdinalIgnoreCase ) )
				continue;

			if ( !addedNames.Add( name ) )
				continue;

			var frames = SampleClip( clip );
			if ( frames.Count == 0 )
				continue;

			var animation = builder.AddAnimation( name, AnimationFps );
			foreach ( var frame in frames )
				animation.AddFrame( frame );
		}
	}

	private List<Transform[]> SampleClip( AnimationClip clip )
	{
		var channels = new Dictionary<string, AnimationChannel>( StringComparer.OrdinalIgnoreCase );
		foreach ( var channel in clip.Channels )
		{
			if ( !string.IsNullOrEmpty( channel.BoneName ) )
				channels.TryAdd( channel.BoneName, channel );
		}

		var duration = clip.Duration > 0 ? clip.Duration : 0;
		var frameCount = Math.Clamp( (int)MathF.Ceiling( duration * AnimationFps ) + 1, 2, MaxAnimationFrames );
		var frames = new List<Transform[]>( frameCount );

		for ( var f = 0; f < frameCount; f++ )
		{
			var time = duration * f / (frameCount - 1);
			var transforms = new Transform[bones.Count];

			for ( var i = 0; i < bones.Count; i++ )
			{
				var bind = bones[i];
				if ( i < channelNames.Count
					&& channels.TryGetValue( channelNames[i], out var channel )
					&& TrySample( channel, time, out var pose ) )
				{
					transforms[i] = new Transform(
						MagickaSpace.Position( pose.Translation ),
						MagickaSpace.ToWorld( pose.Orientation ),
						ToScale( pose.Scale ) );
				}
				else
				{
					// Un-keyed bones stay in their parent-relative bind pose, as XNAnimation does.
					transforms[i] = bind.LocalBind;
				}
			}

			frames.Add( transforms );
		}

		return frames;
	}

	private static bool TrySample( AnimationChannel channel, float time, out BonePose pose )
	{
		pose = default;
		var keys = channel.Keyframes;
		if ( keys.Length == 0 )
			return false;

		if ( time <= keys[0].Time )
		{
			pose = keys[0].Pose;
			return true;
		}

		var last = keys[^1];
		if ( time >= last.Time )
		{
			pose = last.Pose;
			return true;
		}

		for ( var i = 1; i < keys.Length; i++ )
		{
			if ( keys[i].Time < time )
				continue;

			var a = keys[i - 1];
			var b = keys[i];
			var span = b.Time - a.Time;
			var t = span <= 0 ? 1 : (time - a.Time) / span;

			pose = new BonePose(
				Nv3.Lerp( a.Pose.Translation, b.Pose.Translation, t ),
				NQuat.Slerp( a.Pose.Orientation, b.Pose.Orientation, t ),
				Nv3.Lerp( a.Pose.Scale, b.Pose.Scale, t ) );
			return true;
		}

		return false;
	}

	private BBox AddMeshes( ModelBuilder builder, ModelContent model, bool skinned, List<Vector3> staticPositions, bool skipRootBoneTransform = false )
	{
		var materials = new Dictionary<object, Material>();
		var materialIndex = 0;
		var absoluteBones = skinned ? null : AbsoluteBoneTransforms( model, skipRootBoneTransform );
		var totalBounds = new BBox { Mins = float.MaxValue, Maxs = float.MinValue };
		var hasBounds = false;

		foreach ( var mesh in model.Meshes )
		{
			var world = skinned ? NMat.Identity : MeshWorld( mesh, absoluteBones );

			foreach ( var part in mesh.Parts )
			{
				if ( part.VertexDeclarationIndex < 0 || part.VertexDeclarationIndex >= model.VertexDeclarations.Count )
					continue;

				var declaration = model.VertexDeclarations[part.VertexDeclarationIndex];
				var effect = content.Resolve( part.Effect );

				if ( effect is RenderDeferredLiquidEffect or LavaEffect )
					continue;

				var material = MaterialFor( effect, materials, ref materialIndex );
				if ( material is null )
					continue;

				var built = BuildMesh( mesh, part, declaration, material, skinned, world, out var meshBounds, staticPositions );
				if ( built is null )
					continue;

				builder.AddMesh( built );
				totalBounds = hasBounds ? totalBounds.AddBBox( meshBounds ) : meshBounds;
				hasBounds = true;
			}
		}

		return hasBounds ? totalBounds : default;
	}

	private Mesh BuildMesh( ModelMesh mesh, ModelMeshPart part, VertexDeclaration declaration, Material material, bool skinned, NMat world, out BBox bounds, List<Vector3> staticPositions )
	{
		bounds = default;
		var stride = declaration.Stride;
		if ( stride <= 0 || mesh.VertexBuffer is null || mesh.IndexBuffer is null || part.NumVertices <= 0 )
			return null;

		var data = mesh.VertexBuffer.Data;
		var first = part.StreamOffset + (long)part.BaseVertex * stride;
		if ( first + (long)part.NumVertices * stride > data.Length )
			return null;

		var offsets = new VertexOffsets( declaration );
		bounds = new BBox { Mins = float.MaxValue, Maxs = float.MinValue };
		var positions = new Vector3[part.NumVertices];
		var normals = new Vector3[part.NumVertices];
		var tangents = new Vector4[part.NumVertices];
		var texcoords = new Vector2[part.NumVertices];
		var colors = new Color32[part.NumVertices];
		ReadSkin( data, offsets, part.NumVertices, first, stride, bones.Count, out var indices, out var weights );

		for ( var i = 0; i < part.NumVertices; i++ )
		{
			var offset = checked((int)(first + i * stride));
			var rawPos = ReadVector3( data, checked((int)(offset + offsets.Position)), offsets.Position );
			var rawNormal = ReadVector3( data, checked((int)(offset + offsets.Normal)), offsets.Normal );

			var position = skinned ? rawPos : Nv3.Transform( rawPos, world );
			var normal = skinned ? rawNormal : Nv3.TransformNormal( rawNormal, world );

			positions[i] = MagickaSpace.Position( position );
			normals[i] = MagickaSpace.ToWorld( normal ).Normal;
			tangents[i] = new Vector4( MagickaSpace.ToWorld( ReadVector3( data, checked((int)(offset + offsets.Tangent)), offsets.Tangent ) ), 1 );
			texcoords[i] = ReadTexCoord( data, offset, offsets.TexCoord );
			colors[i] = ReadColor( data, offset, offsets );
			bounds = bounds.AddPoint( positions[i] );
			staticPositions?.Add( positions[i] );
		}

		var triangles = ReadIndices( mesh, part );
		if ( triangles.Length == 0 )
			return null;

		var result = new Mesh( material ) { Bounds = bounds };

		if ( skinned )
		{
			var vertices = new SkinnedVertex[part.NumVertices];
			for ( var i = 0; i < vertices.Length; i++ )
			{
				vertices[i] = new SkinnedVertex
				{
					Position = positions[i],
					Normal = normals[i],
					Tangent = tangents[i],
					TexCoord = texcoords[i],
					Color = colors[i],
					BlendIndices = indices[i],
					BlendWeights = weights[i],
				};

				if ( boneVertexCounts is not null )
				{
					if ( weights[i].r >= 32 && indices[i].r < boneVertexCounts.Length ) boneVertexCounts[indices[i].r]++;
					if ( weights[i].g >= 32 && indices[i].g < boneVertexCounts.Length ) boneVertexCounts[indices[i].g]++;
					if ( weights[i].b >= 32 && indices[i].b < boneVertexCounts.Length ) boneVertexCounts[indices[i].b]++;
					if ( weights[i].a >= 32 && indices[i].a < boneVertexCounts.Length ) boneVertexCounts[indices[i].a]++;
				}
			}

			result.CreateVertexBuffer( vertices.Length, vertices );
		}
		else
		{
			var vertices = new StaticVertex[part.NumVertices];
			for ( var i = 0; i < vertices.Length; i++ )
			{
				vertices[i] = new StaticVertex
				{
					Position = positions[i],
					Normal = normals[i],
					Tangent = tangents[i],
					TexCoord = texcoords[i],
					Color = colors[i],
				};
			}

			result.CreateVertexBuffer( vertices.Length, vertices );
		}

		result.CreateIndexBuffer( triangles.Length, triangles );
		return result;
	}

	/// <summary>s&amp;box front faces are counter-clockwise; XNA's are clockwise, so swap two corners.</summary>
	private static int[] ReadIndices( ModelMesh mesh, ModelMeshPart part )
	{
		var buffer = mesh.IndexBuffer;
		var size = buffer.Is16Bit ? 2 : 4;
		var start = part.StartIndex * size;
		var count = part.PrimitiveCount * 3;
		var indices = new int[count];

		for ( var i = 0; i + 2 < count; i += 3 )
		{
			var offset = start + i * size;
			indices[i] = ReadIndex( buffer, offset );
			indices[i + 1] = ReadIndex( buffer, offset + size * 2 );
			indices[i + 2] = ReadIndex( buffer, offset + size );
		}

		return indices;
	}

	private static int ReadIndex( IndexBuffer buffer, int byteOffset )
	{
		if ( byteOffset < 0 || byteOffset >= buffer.Data.Length )
			return 0;

		return buffer.Is16Bit
			? BinaryPrimitives.ReadUInt16LittleEndian( buffer.Data.AsSpan( byteOffset ) )
			: (int)BinaryPrimitives.ReadUInt32LittleEndian( buffer.Data.AsSpan( byteOffset ) );
	}

	private static NMat[] AbsoluteBoneTransforms( ModelContent model, bool skipRootBone = false )
	{
		var absolute = new NMat[model.Bones.Count];
		var done = new bool[model.Bones.Count];

		if ( skipRootBone && model.Bones.Count > 0 )
		{
			absolute[0] = NMat.Identity;
			done[0] = true;
		}

		for ( var i = 0; i < model.Bones.Count; i++ )
			ComputeBone( model, i, absolute, done, 0 );

		return absolute;
	}

	private static void ComputeBone( ModelContent model, int index, NMat[] absolute, bool[] done, int depth )
	{
		if ( done[index] || depth > model.Bones.Count )
			return;

		done[index] = true;
		var bone = model.Bones[index];

		if ( bone.Parent is { } parent && parent >= 0 && parent < model.Bones.Count )
		{
			ComputeBone( model, parent, absolute, done, depth + 1 );
			absolute[index] = bone.Transform * absolute[parent];
		}
		else
		{
			absolute[index] = bone.Transform;
		}
	}

	private static NMat MeshWorld( ModelMesh mesh, NMat[] bones )
	{
		if ( mesh.ParentBone is not { } bone || bone < 0 || bone >= bones.Length )
			return NMat.Identity;

		return bones[bone];
	}

	private readonly struct VertexOffsets( VertexDeclaration declaration )
	{
		public readonly int Position = declaration.Find( VertexElementUsage.Position )?.Offset ?? -1;
		public readonly int Normal = declaration.Find( VertexElementUsage.Normal )?.Offset ?? -1;
		public readonly int Tangent = declaration.Find( VertexElementUsage.Tangent )?.Offset ?? -1;
		public readonly int TexCoord = declaration.Find( VertexElementUsage.TextureCoordinate )?.Offset ?? -1;
		public readonly int Color = declaration.Find( VertexElementUsage.Color )?.Offset ?? -1;
		public readonly int Indices = declaration.Find( VertexElementUsage.BlendIndices )?.Offset ?? -1;
		public readonly int Weights = declaration.Find( VertexElementUsage.BlendWeight )?.Offset ?? -1;
		public readonly VertexElementFormat ColorFormat = declaration.Find( VertexElementUsage.Color )?.Format ?? VertexElementFormat.Color;
	}

	private static Nv3 ReadVector3( byte[] data, int offset, int elementOffset ) =>
		elementOffset < 0 ? Nv3.Zero : new( ReadSingle( data, offset ), ReadSingle( data, offset + 4 ), ReadSingle( data, offset + 8 ) );

	private static Vector2 ReadTexCoord( byte[] data, int offset, int element ) =>
		new( element < 0 ? 0 : ReadSingle( data, offset + element ),
			element < 0 ? 0 : ReadSingle( data, offset + element + 4 ) );

	private static Color32 ReadColor( byte[] data, int offset, VertexOffsets offsets )
	{
		if ( offsets.Color < 0 )
			return Color32.White;

		var o = offset + offsets.Color;
		if ( o < 0 || o + 4 > data.Length )
			return Color32.White;

		// Packed colours are four bytes (XNA 3.1 Color stores B, G, R, A); Vector4 colours are floats.
		if ( offsets.ColorFormat is VertexElementFormat.Color or VertexElementFormat.Byte4 or VertexElementFormat.Rgba32 )
			return new Color32( data[o + 2], data[o + 1], data[o], data[o + 3] );

		if ( o + 16 > data.Length )
			return Color32.White;

		var r = ReadSingle( data, o );
		var g = ReadSingle( data, o + 4 );
		var b = ReadSingle( data, o + 8 );
		var a = ReadSingle( data, o + 12 );
		return new Color32( ToByte( r ), ToByte( g ), ToByte( b ), ToByte( a ) );
	}

	/// <summary>
	/// Bone influences per vertex: drop zero or out-of-range weights, then normalize with largest-remainder
	/// rounding so the four bytes sum to exactly 255.
	/// </summary>
	private static void ReadSkin( byte[] data, VertexOffsets offsets, int count, long first, int stride, int boneCount,
		out Color32[] indices, out Color32[] weights )
	{
		indices = new Color32[count];
		weights = new Color32[count];

		Span<int> keep = stackalloc int[4];
		Span<float> value = stackalloc float[4];
		Span<float> scaled = stackalloc float[4];
		Span<int> bytes = stackalloc int[4];

		for ( var v = 0; v < count; v++ )
		{
			var offset = checked((int)(first + v * stride));
			if ( offsets.Weights < 0 || offsets.Indices < 0 )
			{
				indices[v] = new Color32( 0, 0, 0, 0 );
				weights[v] = new Color32( 255, 0, 0, 0 );
				continue;
			}

			var w = offset + offsets.Weights;
			var i = offset + offsets.Indices;
			var used = 0;

			for ( var b = 0; b < 4; b++ )
			{
				var weight = ReadSingle( data, w + b * 4 );
				var bone = data[i + b];
				if ( weight <= 0f || float.IsNaN( weight ) || float.IsInfinity( weight ) || bone >= boneCount )
					continue;

				keep[used] = bone;
				value[used] = weight;
				used++;
			}

			if ( used == 0 )
			{
				indices[v] = new Color32( 0, 0, 0, 0 );
				weights[v] = new Color32( 255, 0, 0, 0 );
				continue;
			}

			var total = 0f;
			for ( var b = 0; b < used; b++ )
				total += value[b];

			if ( total <= 0.00001f )
			{
				indices[v] = new Color32( (byte)keep[0], 0, 0, 0 );
				weights[v] = new Color32( 255, 0, 0, 0 );
				continue;
			}

			var sum = 0;
			for ( var b = 0; b < used; b++ )
			{
				scaled[b] = value[b] / total * 255f;
				bytes[b] = (int)scaled[b];
				sum += bytes[b];
			}

			var remainder = 255 - sum;
			while ( remainder > 0 )
			{
				var best = 0;
				for ( var b = 1; b < used; b++ )
				{
					if ( scaled[b] - bytes[b] > scaled[best] - bytes[best] )
						best = b;
				}

				bytes[best]++;
				scaled[best] = bytes[best] - 1f;
				remainder--;
			}

			indices[v] = new Color32( (byte)keep[0], used > 1 ? (byte)keep[1] : (byte)0,
				used > 2 ? (byte)keep[2] : (byte)0, used > 3 ? (byte)keep[3] : (byte)0 );
			weights[v] = new Color32( (byte)bytes[0], used > 1 ? (byte)bytes[1] : (byte)0,
				used > 2 ? (byte)bytes[2] : (byte)0, used > 3 ? (byte)bytes[3] : (byte)0 );
		}
	}

	private static float ReadSingle( byte[] data, long offset )
	{
		if ( offset < 0 || offset + 4 > data.Length )
			return 0f;

		return BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( checked((int)offset) ) );
	}

	private static byte ToByte( float value ) => (byte)Math.Clamp( (int)(value * 255f + 0.5f), 0, 255 );

	private static Vector3 ToScale( Nv3 s ) => new( s.Z, s.X, s.Y );

	[StructLayout( LayoutKind.Sequential )]
	private struct StaticVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.TexCoord] public Vector2 TexCoord;
		[VertexLayout.Color] public Color32 Color;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct SkinnedVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.TexCoord] public Vector2 TexCoord;
		[VertexLayout.Color] public Color32 Color;
		[VertexLayout.BlendIndices] public Color32 BlendIndices;
		[VertexLayout.BlendWeight] public Color32 BlendWeights;
	}
}
