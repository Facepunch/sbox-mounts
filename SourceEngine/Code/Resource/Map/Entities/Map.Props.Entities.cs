namespace Source1;

partial class MapLoader
{
	private class PropEntityLoader( bool animated = false ) : EntityLoader
	{
		public override void Load( EntityBuilder builder, MapEntity entity )
		{
			var instance = builder.BuildProp( Read( entity ), entity, animated );
			if ( !instance.IsValid() ) return;
			var damage = entity.Get( "OnBreak" ) is not null && instance.GetComponent<Prop>( true ).IsValid()
				? instance.GetOrAddComponent<PropDamage>() : null;
			var target = Configure( entity, instance ) ?? damage;
			if ( target.IsValid() ) builder.RegisterEntityIO( entity, target );
		}

		protected virtual Component Configure( MapEntity entity, GameObject instance ) => null;
		protected virtual MapProp Read( MapEntity entity ) => ReadProp( entity );
	}

	private class DynamicPropEntityLoader() : PropEntityLoader( true )
	{
		protected override Component Configure( MapEntity entity, GameObject instance )
		{
			var model = instance.GetComponent<Prop>( true )?.Model ?? instance.GetComponent<ModelRenderer>( true )?.Model;
			if ( ModelClips.Of( model ).Count == 0 ) return null;
			var initial = InitialSequence( entity, model );
			if ( string.IsNullOrEmpty( initial ) && string.IsNullOrEmpty( entity.Get( "targetname" ) ) ) return null;
			if ( instance.NetworkMode != NetworkMode.Object ) MakeNetworkRoot( instance );
			var animation = instance.AddComponent<PropAnimation>();
			animation.Sequence = initial;
			return animation;
		}

		protected virtual string InitialSequence( MapEntity entity, Model model ) => entity.Get( "DefaultAnim" );

		protected override MapProp Read( MapEntity entity )
		{
			var prop = base.Read( entity );
			return prop with { Visible = prop.Visible && entity.Integer( "StartDisabled" ) == 0, Gameplay = true };
		}
	}

	private sealed class FurnitureEntityLoader : DynamicPropEntityLoader
	{
		protected override string InitialSequence( MapEntity entity, Model model ) => ModelSequences.Of( model ).All.FirstOrDefault()?.Name;

		// Scripted furniture has no solid root. Maps can supply separate blocking brushes.
		protected override MapProp Read( MapEntity entity ) => base.Read( entity ) with { Solid = 0 };
	}

	private sealed class PhysicsPropEntityLoader : PropEntityLoader
	{
		protected override MapProp Read( MapEntity entity )
		{
			var flags = entity.Integer( "spawnflags" );
			return base.Read( entity ) with
			{
				Physics = PropPhysics.Rigid,
				Gameplay = true,
				StartAsleep = (flags & 1) != 0,
				EnableOnPhyscannon = (flags & 64) != 0,
				MotionEnabled = (flags & 8) == 0 && entity.Integer( "damagetoenablemotion" ) <= 0 && entity.Number( "forcetoenablemotion" ) <= 0,
				MassScale = entity.Number( "massscale", 1 )
			};
		}
	}

	private sealed class RagdollEntityLoader : PropEntityLoader
	{
		protected override MapProp Read( MapEntity entity ) => base.Read( entity ) with
		{
			Physics = PropPhysics.Ragdoll,
			StartAsleep = (entity.Integer( "spawnflags" ) & 0x10000) != 0,
			RagdollPose = entity.Get( "angleOverride" )
		};
	}

	private static MapProp ReadProp( MapEntity entity )
	{
		var className = entity.Get( "classname" );
		var color = entity.Vector( "rendercolor", new Vector3( 255 ) ) / 255.0f;
		var renderMode = entity.Integer( "rendermode" );
		var alpha = renderMode == 0 ? 1 : entity.Number( "renderamt", 255 ) / 255.0f;
		return new MapProp( entity.Get( "model" ), entity.Get( "targetname" ) ?? className.ToLowerInvariant(), entity.Vector( "origin" ), ReadEntityAngles( entity ), entity.Number( "modelscale", 1 ), entity.Integer( "skin" ), entity.Integer( "solid", 6 ), new Color( color.x, color.y, color.z, alpha ), Visible: renderMode != 10 );
	}
}
