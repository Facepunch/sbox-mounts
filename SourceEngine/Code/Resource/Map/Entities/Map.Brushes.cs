using System;
using System.Globalization;

namespace Source1;

partial class MapLoader
{
	private abstract class BrushEntityLoader : EntityLoader
	{
		public override void Load( EntityBuilder builder, MapEntity entity ) => builder.BuildBrush( entity, this );
		public virtual bool IsEnabled( MapEntity entity ) => true;
		public virtual bool IsSolid( MapEntity entity ) => true;
		public virtual bool CollidesWhenHidden( MapEntity entity ) => false;
		public virtual bool HasUnsupportedBehavior => false;
		public virtual bool AddInteraction( EntityBuilder builder, MapEntity entity, GameObject instance, BspModel model ) => false;
	}

	private sealed class ToggleBrushEntityLoader : BrushEntityLoader
	{
		public override bool IsEnabled( MapEntity entity ) => entity.Integer( "StartDisabled" ) == 0;
		public override bool IsSolid( MapEntity entity ) => entity.Integer( "Solidity" ) != 1;
		public override bool CollidesWhenHidden( MapEntity entity ) => entity.Integer( "Solidity" ) == 2;
	}

	private class WallEntityLoader : BrushEntityLoader
	{
		public override Angles ReadAngles( MapEntity entity ) => Angles.Zero;
	}

	private sealed class ToggleWallEntityLoader : WallEntityLoader
	{
		public override bool IsEnabled( MapEntity entity ) => (entity.Integer( "spawnflags" ) & 1) == 0;
	}

	private sealed class IllusionaryEntityLoader : WallEntityLoader
	{
		public override bool IsSolid( MapEntity entity ) => false;
	}

	private sealed class LodBrushEntityLoader : BrushEntityLoader
	{
		public override bool IsSolid( MapEntity entity ) => entity.Integer( "Solid" ) == 0;
		public override bool HasUnsupportedBehavior => true;
	}

	private sealed class ConveyorEntityLoader : WallEntityLoader
	{
		public override bool HasUnsupportedBehavior => true;
	}

	private class UnsimulatedBrushEntityLoader : BrushEntityLoader
	{
		public override bool HasUnsupportedBehavior => true;
	}

	private sealed class BreakableEntityLoader : UnsimulatedBrushEntityLoader
	{
		public override Angles ReadAngles( MapEntity entity ) =>
			string.IsNullOrWhiteSpace( entity.Get( "parentname" ) ) && entity.Vector( "gibdir" ) == Vector3.Zero ? Angles.Zero : entity.Angles;
	}

	private sealed partial class EntityBuilder
	{
		private readonly Dictionary<int, Model> brushModels = [];
		private readonly Dictionary<int, List<Model>> shadowBrushModels = [];
		private GameObject brushParent;
		// VBSP writes these four corners for the visible face, including oblique windows.
		private bool TryBuildGlassPane( MapEntity entity, GameObject instance, BspModel model )
		{
			var lower = entity.Vector( "lowerleft" );
			var across = entity.Vector( "lowerright" ) - lower;
			var up = entity.Vector( "upperleft" ) - lower;
			if ( entity.Integer( "error" ) != 0 || across.Length < 1 || up.Length < 1 )
				return false;

			var surface = new GameObject( false, "Glass pane" ) { Parent = instance };
			surface.LocalPosition = lower + across * 0.5f - entity.Vector( "origin" );
			surface.LocalRotation = Rotation.LookAt( Vector3.Cross( across, up ).Normal, up.Normal );
			var pane = surface.AddComponent<SourceGlassPane>( false );
			pane.Width = across.Length;
			pane.Height = up.Length;
			pane.Fragility = entity.Number( "fragility", 100 );
			pane.IntactBrush = instance.GetComponent<ModelRenderer>();
			pane.ContentMount = owner.Host.Ident;
			for ( int i = model.FirstFace; i < model.FirstFace + model.FaceCount; i++ )
			{
				var tex = bsp.TexInfos[bsp.Faces[i].TexInfo];
				if ( (tex.Flags & 0x80) != 0 ) continue;
				var data = bsp.TexDatas[tex.TexData];
				var path = VmtFile.NormalizePath( bsp.GetTexDataString( tex.TexData ), ".vmt" );
				var resolved = owner.Host.GetContentPath( path, owner.ContentRoot )
					?? owner.Host.GetContentPath( GetBaseMaterialPath( path[..^4] ) + ".vmt" );
				if ( resolved is null ) continue;
				var file = resolved[($"mount://{owner.Host.Ident}/").Length..];
				var vmt = owner.Host.GetSourceMaterial( file ).Source;
				if ( VmtFile.SelectParameters( vmt ).TryGetValue( "$crackmaterial", out var cracked )
					&& owner.Host.GetContentPath( VmtFile.NormalizePath( cracked, ".vmt" ), owner.ContentRoot ) is { } crackPath )
					pane.CrackMaterial = crackPath + ".vmat";
				Vector2 UV( Vector3 position ) => new(
					(Vector3.Dot( position, new Vector3( tex.TextureVecsS.x, tex.TextureVecsS.y, tex.TextureVecsS.z ) ) + tex.TextureVecsS.w) / Math.Max( 1, data.Width ),
					(Vector3.Dot( position, new Vector3( tex.TextureVecsT.x, tex.TextureVecsT.y, tex.TextureVecsT.z ) ) + tex.TextureVecsT.w) / Math.Max( 1, data.Height ) );
				pane.UvOrigin = UV( lower );
				pane.UvAcross = UV( lower + across ) - pane.UvOrigin;
				pane.UvUp = UV( lower + up ) - pane.UvOrigin;
				break;
			}
			RegisterEntityIO( entity, pane );
			pane.Enabled = true;
			surface.Enabled = true;
			return true;
		}

		public void BuildBrush( MapEntity entity, BrushEntityLoader loader )
		{
			var path = entity.Get( "model" );
			if ( path is null || !path.StartsWith( '*' ) || !int.TryParse( path.AsSpan( 1 ), NumberStyles.None, CultureInfo.InvariantCulture, out var index ) || index <= 0 || index >= bsp.Models.Length )
				throw new InvalidDataException( $"{owner.FullPath}: brush entity '{entity.Get( "classname" )}' has invalid model '{path}'." );
			if ( !brushModels.TryGetValue( index, out var model ) )
			{
				model = owner.BuildWorldModel( bsp, useAggregates: false, modelIndex: index, skybox: skybox ).Surfaces;
				brushModels.Add( index, model );
			}

			brushParent ??= new GameObject( true, "brushes" )
			{
				Parent = World
			};
			var className = entity.Get( "classname" ).ToLowerInvariant();
			var instance = new GameObject( true, entity.Get( "targetname" ) ?? className )
			{
				Parent = brushParent,
				IsStatic = true,
				LocalPosition = entity.Vector( "origin" ),
				LocalRotation = Rotation.From( loader.ReadAngles( entity ) )
			};
			instance.Tags.Add( "world" );
			instance.Tags.Add( "source1_entity" );
			var prop = ReadProp( entity );
			var visible = prop.Visible && (entity.Integer( "effects" ) & 32) == 0 && loader.IsEnabled( entity );
			if ( model is not null )
			{
				var renderer = instance.AddComponent<ModelRenderer>();
				renderer.Model = model;
				renderer.Tint = prop.Tint;
				renderer.Enabled = visible;
			}
			if ( !skybox && className == "func_breakable_surf" && entity.Integer( "surfacetype" ) == 0
				&& TryBuildGlassPane( entity, instance, bsp.Models[index] ) )
				return;

			var moving = !skybox && loader.AddInteraction( this, entity, instance, bsp.Models[index] );
			instance.IsStatic = !moving;
			if ( loader.IsSolid( entity ) )
			{
				owner.BuildWorldPhysics( bsp, instance, modelIndex: index, staticCollider: !moving );
				foreach ( var collider in instance.GetComponentsInChildren<PhysicsCollider>( true ) )
					collider.Enabled = visible || loader.CollidesWhenHidden( entity );
			}
			if ( !skybox && loader.HasUnsupportedBehavior )
				warnings.Add( $"Entity '{className}' has no equivalent native behavior; retaining its geometry and collision without Source simulation." );
			if ( !skybox && (entity.Integer( "effects" ) & 16) == 0 )
			{
				if ( !shadowBrushModels.TryGetValue( index, out var shadowModel ) )
				{
					shadowModel = owner.BuildShadowModel( bsp, index );
					shadowBrushModels.Add( index, shadowModel );
				}

				owner.AddShadowBlocker( shadowModel, instance, visible );
			}
		}
	}
}
