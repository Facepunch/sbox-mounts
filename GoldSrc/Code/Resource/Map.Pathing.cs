using System;
using GoldSrc.Bsp;

namespace GoldSrc;

partial class MapLoader
{
	private readonly Dictionary<string, PathNode> _pathNodes = new( StringComparer.Ordinal );

	private void AddPathNode( GameObject go, Entity entity )
	{
		const int SF_PATH_DISABLED = 1;
		const int SF_PATH_FIREONCE = 2;
		const int SF_PATH_DISABLE_CONTROLS = 8;

		var flags = entity.SpawnFlags;

		var node = go.AddComponent<PathNode>();
		node.IsTrack = entity.ClassName == "path_track";
		node.TargetName = entity.TargetName;
		node.Speed = entity.GetFloat( "speed" );
		node.Wait = entity.GetFloat( "wait" );
		node.Disabled = node.IsTrack && (flags & SF_PATH_DISABLED) != 0;
		node.FireOnce = (flags & (node.IsTrack ? SF_PATH_FIREONCE : 4)) != 0;
		node.DisablesControls = node.IsTrack && (flags & SF_PATH_DISABLE_CONTROLS) != 0;
		node.AlternateReverse = node.IsTrack && (flags & 4) != 0;
		node.UseAlternate = node.IsTrack && (flags & 0x8000) != 0;
		node.WaitForTrigger = !node.IsTrack && (flags & 1) != 0;
		node.Teleport = !node.IsTrack && (flags & 2) != 0;

		if ( !string.IsNullOrEmpty( entity.TargetName ) )
			_pathNodes.TryAdd( entity.TargetName, node );

		if ( node.IsTrack ) RegisterTarget( entity, node );
		else RegisterTarget( entity, node, null, null, null, nameof( MapEntity.Kill ) );

		_wiring.Add( () =>
		{
			// A path node's "target" is the next node in the chain, not something it fires; the
			// thing it fires as a train goes past is its "message".
			node.Next = FindPathNode( entity.Target );
			node.Alternate = node.IsTrack ? FindPathNode( entity.GetString( "altpath" ) ) : null;

			node.OnPass = BuildFire( entity.GetString( "message" ) );
			if ( node.IsTrack ) node.OnDeadEnd = BuildFire( entity.GetString( "netname" ) );
		} );
	}

	private PathNode FindPathNode( string name )
		=> !string.IsNullOrEmpty( name ) && _pathNodes.TryGetValue( name, out var node ) ? node : null;

	private void LinkPathNodes()
	{
		foreach ( var node in _pathNodes.Values )
			if ( node.Next.IsValid() && node.Next.Alternate != node )
				node.Next.Previous = node;
	}

	private void AddTrain( GameObject go, Entity entity, bool tracked )
	{
		const int SF_TRACKTRAIN_NOPITCH = 1;

		var train = go.AddComponent<TrackTrain>();
		train.TargetName = entity.TargetName;
		train.MaxSpeed = entity.GetFloat( "speed", 100f );
		if ( train.MaxSpeed == 0f ) train.MaxSpeed = 100f;
		train.NoRotation = !tracked;
		train.NoPitch = (entity.SpawnFlags & SF_TRACKTRAIN_NOPITCH) != 0;
		train.NoControl = tracked && (entity.SpawnFlags & 2) != 0;
		train.StartSpeed = tracked ? entity.GetFloat( "startspeed" ) : string.IsNullOrEmpty( entity.TargetName ) ? train.MaxSpeed : 0f;
		train.WaitForTrigger = !tracked && (!string.IsNullOrEmpty( entity.TargetName ) || (entity.SpawnFlags & 1) != 0);
		train.Bank = tracked ? entity.GetFloat( "bank" ) : 0f;

		if ( tracked )
		{
			train.WheelBase = entity.GetFloat( "wheels" );
			train.Height = entity.GetFloat( "height" );
			train.MoveSound = GetIndexedSound( entity, "sounds", TrainSounds );
		}

		RegisterTarget( entity, train );

		// The train's "target" is the first node of its path. GoldSrc teleports the train onto that
		// node when the map starts; we do it at load time so the map never renders it parked
		// wherever the mapper left the brush.
		_wiring.Add( () =>
		{
			train.Path = FindPathNode( entity.Target );
			if ( train.Path.IsValid() && train.Path.IsTrack == tracked )
				go.WorldPosition = train.Path.WorldPosition + Vector3.Up * train.Height;
			else
			{
				train.Path = null;
				Log.Warning( $"{FilePath}: {EntityName( entity )} has no valid {(tracked ? "path_track" : "path_corner")} target '{entity.Target}'." );
			}
		} );
	}
}
