using System;

namespace Source1;

internal sealed class ModelLoader( string fullPath, bool animated = false ) : ResourceLoader<BaseMount>
{
	protected override object Load() => Host.ReadSourceModel( fullPath, animated: animated ).Build( Path );

	// A model's skeleton and clips are only known once its animations are read, so they aren't listed while mounting.
	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = default;
		if ( !animated )
			return null;

		if ( name.Equals( SourceAnimations.SkeletonName, StringComparison.OrdinalIgnoreCase ) )
		{
			type = ResourceType.AnimationSkeleton;
			return new SkeletonLoader();
		}

		if ( !name.EndsWith( ".anim", StringComparison.OrdinalIgnoreCase ) )
			return null;

		type = ResourceType.AnimationClip;
		return new ClipLoader( name[..^5] );
	}
}

internal sealed partial class SourceModelBuilder( BaseMount mount, string fullPath )
{
	private BaseMount Host { get; } = mount;
	private string FullPath { get; } = fullPath;
	private string ContentRoot { get; } = BaseMount.GetContentRoot( fullPath, "models" );
	internal Model BuildModel( MdlFile mdl, VtxFile vtx, PhyFile physics, string name = null, Transform[] referencePose = null,
		StaticPropLighting lighting = null, MdlFile.ImportedAnimations animations = null )
	{
		var builder = Model.Builder.WithName( name );
		var viewBounds = GetViewBounds( mdl );
		var flexNames = mdl.FlexNames.ToArray();
		var bodyGroupNames = AddBodyGroups( builder, mdl );
		var materials = new MaterialSlots( this, mdl, vtx, lighting );
		var boneTransforms = GetBoneTransforms( mdl, referencePose );
		VertexTransform[] skinTransforms = null;
		if ( referencePose is not null )
		{
			skinTransforms = new VertexTransform[boneTransforms.Length];
			for ( var i = 0; i < skinTransforms.Length; i++ )
			{
				var matrix = mdl.Bones[i].PoseToBone;
				var bone = boneTransforms[i];
				skinTransforms[i] = new VertexTransform(
					bone.Rotation * new Vector3( matrix.M11, matrix.M21, matrix.M31 ),
					bone.Rotation * new Vector3( matrix.M12, matrix.M22, matrix.M32 ),
					bone.Rotation * new Vector3( matrix.M13, matrix.M23, matrix.M33 ),
					bone.PointToWorld( new Vector3( matrix.M14, matrix.M24, matrix.M34 ) ) );
			}
		}
		var boneBounds = AddGeometry( builder, mdl, vtx, materials, viewBounds, bodyGroupNames, flexNames, skinTransforms, lighting,
			retainBoneSurfaces: animations is not null );
		materials.AddGroups( builder );

		var boneNames = AddSkeleton( builder, mdl, boneBounds, boneTransforms, parentLocal: vtx == null );
		PhysicsPlan physicsPlan = null;
		if ( physics != null )
		{
			try
			{
				physicsPlan = PreparePhysics( physics, mdl, boneTransforms, boneNames );
			}
			catch ( InvalidDataException e )
			{
				Log.Trace( $"{FullPath}: physics was not imported: {e.Message}" );
			}
			if ( physicsPlan != null )
				AddPhysics( builder, physics, physicsPlan );
		}

		AddMetadata( builder, mdl, boneNames );
		AddHitboxes( builder, mdl, boneNames );
		AddGameData( builder, mdl, physics );

		string[] animatedBones = null;
		if ( animations is not null )
		{
			builder.WithData( animations.StoredSequences() );
			builder.WithData( animations.Clips.Where( animation => animation.PoseLayer is not null ).Select( animation => animation.PoseLayer ).Distinct().ToArray() );
			builder.WithData( animations.PoseParameters );
			// IK target bones ride on the bone their rule is relative to; the clips animate them.
			animatedBones = [.. boneNames, .. animations.IkBones.Select( x => x.Name )];
			foreach ( var (target, parent) in animations.IkBones )
			{
				// Bones are given in model space when the model has meshes, as the skeleton's own are.
				var bind = vtx == null ? new Transform( Vector3.Zero ) : boneTransforms[parent];
				builder.AddBone( target, bind.Position, bind.Rotation, boneNames[parent] );
			}
			if ( name is not null )
			{
				builder.WithData( new ModelAnimationData
				{
					Skeleton = SourceAnimations.SkeletonPath( name ),
					ClipFolder = SourceAnimations.ClipFolder( name ),
					Clips = animations.Clips.Select( animation => animation.Name ).Distinct( StringComparer.OrdinalIgnoreCase ).ToArray()
				} );
			}
		}

		builder.WithViewBounds( viewBounds );
		var result = builder.Create();
		if ( animations is not null && name is not null )
		{
			// The skeleton and clips are mounted resources of their own, created when something first loads them.
			Host.AddAnimations( name, new SourceAnimations( animatedBones.Select( bone =>
			{
				var built = result.Bones.GetBone( bone );
				if ( built is null ) return (bone, (string)null, Transform.Zero);
				return (bone, built.Parent?.Name, built.Parent is null ? built.LocalTransform : built.Parent.LocalTransform.ToLocal( built.LocalTransform ));
			} ).ToArray(), animations.BoneMasks, animations.Clips ) );
		}
		if ( physicsPlan != null )
			ValidatePhysics( result, physics, physicsPlan );
		return result;
	}
}
