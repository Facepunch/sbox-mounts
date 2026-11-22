using GoldSrc.Bsp;

namespace GoldSrc.Maps;

partial class MapBuilder
{
	private bool SpawnLevelTrigger( Entity entity, BspModel model )
	{
		if ( entity.ClassName is not ("trigger_changelevel" or "trigger_transition") ) return false;
		var go = new GameObject( true, EntityName( entity ) ) { IsStatic = true };
		go.WorldPosition = entity.Origin + (model.Mins + model.Maxs) * 0.5f;
		var size = model.Maxs - model.Mins;
		if ( entity.ClassName == "trigger_transition" )
		{
			var volume = go.AddComponent<TransitionVolume>();
			volume.TargetName = entity.TargetName;
			volume.Bounds = new BBox( -size / 2, size / 2 );
			RegisterTarget( entity, volume, null, null, null, nameof( MapEntity.Kill ) );
			return true;
		}

		var change = go.AddComponent<ChangeLevel>();
		change.TargetName = entity.TargetName;
		change.LandmarkName = entity.GetString( "landmark" );
		var path = Host.FindMountPath( $"maps/{entity.GetString( "map" )}.bsp" );
		if ( path is not null ) change.Destination = $"mount://{Host.Ident}/{path}.scene";
		change.ChangeTargetName = entity.GetString( "changetarget" );
		change.ChangeDelay = entity.GetFloat( "changedelay" );
		if ( (entity.SpawnFlags & 2) == 0 )
			AddCollider( go, GetTriggerPhysics( entity.BrushModelIndex, size ), true, trigger: true );
		RegisterTarget( entity, change );
		WireTrigger( entity, change );
		return true;
	}
}
