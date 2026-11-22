using System;
using System.Threading.Tasks;

namespace GoldSrc.Maps;

public sealed partial class MapState : Global.ISaveEvents, Global.IPlayerEvents
{
	private string _checkpoint;
	private bool _loading;
	private int _savingTrigger = -1;
	internal static bool SoloCampaign => Networking.IsHost && Connection.All.Count <= 1 && !Application.IsDedicatedServer;

	private sealed class SavedEntity
	{
		public Transform Transform { get; set; }
		public bool ColliderEnabled { get; set; } = true;
		public float? Health { get; set; }
		public HalfLife.Npc.SavedScript Script { get; set; }
		public int? Count { get; set; }
		public bool Running { get; set; }
		public float Elapsed { get; set; }
		public int EventIndex { get; set; }
		public bool[] Inputs { get; set; }
		public BrushMover.SavedMove Move { get; set; }
		public MonsterMaker.SavedMaker Maker { get; set; }
		public TrackTrain.SavedTrain Train { get; set; }
	}

	private sealed record SavedActor( System.Text.Json.Nodes.JsonObject Object, float Health, HalfLife.Npc.SavedScript Script );

	private sealed class Snapshot
	{
		public string Map { get; set; }
		public Dictionary<int, SavedEntity> Entities { get; set; } = [];
		public List<SavedActor> IncomingActors { get; set; } = [];
		public Dictionary<string, int> Globals { get; set; } = [];
		public Vector3 Position { get; set; }
		public Angles Eyes { get; set; }
	}

	// Sandbox exposes the running save system's Save and Load methods in its type library.
	private GameObjectSystem SaveSystem()
	{
		GameObjectSystem system = null;
		var type = TypeLibrary.GetType( "Sandbox.SaveSystem" );
		Scene.RunEvent<ISceneLoadingEvents>( listener =>
		{
			if ( listener is GameObjectSystem candidate && TypeLibrary.GetType( candidate.GetType() ) == type ) system = candidate;
		} );
		return system;
	}

	internal bool SaveCheckpoint( AutoSave trigger )
	{
		if ( !SoloCampaign || SaveSystem() is not {} system ) return false;
		var name = GetComponentInParent<MapInstance>().MapName.Split( '/' ).Last();
		var path = $"goldsrc/checkpoints/{name}.sav";
		GetComponentInParent<MapInstance>().GameObject.GetOrAddComponent<CheckpointLoader>();
		_savingTrigger = trigger.EntityIndex;
		try
		{
			var result = TypeLibrary.GetType( system.GetType() ).GetMethod( "Save" ).InvokeWithReturn<bool>( system, [path] );
			if ( result ) _checkpoint = path;
			return result;
		}
		finally { _savingTrigger = -1; }
	}

	internal bool LoadCheckpoint()
	{
		if ( !SoloCampaign ) return false;
		if ( _loading ) return true;
		if ( _checkpoint is null || !FileSystem.Data.FileExists( _checkpoint ) || SaveSystem() is not {} system ) return false;
		_loading = true;
		_ = RestoreScene( system );
		return true;
	}

	private async Task RestoreScene( GameObjectSystem system )
	{
		try
		{
			if ( !await TypeLibrary.GetType( system.GetType() ).GetMethod( "Load" ).InvokeWithReturn<Task<bool>>( system, [_checkpoint] ) )
				Log.Warning( $"Could not load Half-Life checkpoint {_checkpoint}" );
		}
		finally { _loading = false; }
	}

	void Global.IPlayerEvents.OnPlayerDied( global::Player player, PlayerDiedParams args )
	{
		if ( Networking.IsHost && !player.IsProxy ) LoadCheckpoint();
	}

	void Global.ISaveEvents.BeforeSave( string filename )
	{
		if ( !Networking.IsHost ) return;
		var player = global::Player.FindLocalPlayer()?.Controller;
		if ( !player.IsValid() ) return;
		var data = new Snapshot { Map = GetComponentInParent<MapInstance>().MapName, Position = player.WorldPosition, Eyes = player.EyeAngles };
		foreach ( var pair in Globals ) data.Globals[pair.Key] = pair.Value;
		foreach ( var entity in Entities )
		{
			if ( !entity.IsValid() || entity.GameObject.IsDestroyed ) continue;
			if ( entity is Monster dead && dead.Npc.IsValid() && dead.Npc.Health <= 0 ) continue;
			if ( entity is Monster { FromPreviousMap: true } incoming && incoming.Npc.IsValid() )
				data.IncomingActors.Add( new( incoming.GameObject.Serialize(), incoming.Npc.Health, incoming.Npc.CaptureScript() ) );
			if ( entity.EntityIndex < 0 || entity.EntityIndex == _savingTrigger ) continue;
			var entry = new SavedEntity { Transform = entity.WorldTransform, ColliderEnabled = entity.GetComponent<Collider>( true )?.Enabled ?? true };
			data.Entities[entity.EntityIndex] = entry;
			switch ( entity )
			{
				case WallCharger charger: entry.Count = charger.Juice; break;
				case Monster actor when actor.Npc.IsValid():
					entry.Health = actor.Npc.Health;
					entry.Script = actor.Npc.CaptureScript();
					break;
				case MonsterMaker maker: entry.Maker = maker.CaptureMaker(); break;
				case BrushMover mover: entry.Move = mover.CaptureMove(); break;
				case TrackTrain train: entry.Train = train.CaptureTrain(); break;
				case MultiSource source: entry.Inputs = source.InputStates; break;
				case MultiManager manager:
					entry.Running = manager.Running;
					entry.Elapsed = Time.Now - manager.StartTime;
					entry.EventIndex = manager.EventIndex;
					break;
			}
		}
		Global.SetSaveData( "goldsrc.map", Json.Serialize( data ) );
	}

	internal void RestoreCheckpoint( string filename )
	{
		if ( !Networking.IsHost || Global.GetSaveData( "goldsrc.map" ) is not {} json ) return;
		var data = Json.Deserialize<Snapshot>( json );
		if ( data?.Map != GetComponentInParent<MapInstance>().MapName ) return;
		if ( filename.StartsWith( "goldsrc/checkpoints/", StringComparison.Ordinal ) ) _checkpoint = filename;
		foreach ( var pair in data.Globals ) SetGlobal( pair.Key, pair.Value );
		var player = global::Player.FindLocalPlayer()?.Controller;
		foreach ( var saved in data.IncomingActors )
		{
			var go = new GameObject( false ) { Parent = GameObject };
			go.Deserialize( saved.Object );
			var actor = go.GetComponent<Monster>( true );
			if ( !actor.IsValid() || !actor.Npc.IsValid() )
			{
				Log.Warning( $"Could not restore transferred actor '{go.Name}' from {filename}" );
				go.Destroy();
				continue;
			}
			actor.Map = this;
			Entities.Add( actor );
			actor.Npc.Health = saved.Health;
			actor.Npc.RestoreScript( saved.Script, this, player );
		}
		foreach ( var entity in Entities.ToArray() )
		{
			if ( !entity.IsValid() || entity.EntityIndex < 0 ) continue;
			if ( !data.Entities.TryGetValue( entity.EntityIndex, out var entry ) ) { entity.Kill(); continue; }
			if ( entity.GetComponent<Collider>( true ) is {} collider ) collider.Enabled = entry.ColliderEnabled;
			switch ( entity )
			{
				case WallCharger charger when entry.Count.HasValue: charger.Juice = entry.Count.Value; break;
				case Monster actor when actor.Npc.IsValid() && entry.Health.HasValue:
					actor.Npc.Health = entry.Health.Value;
					actor.WorldTransform = entry.Transform;
					if ( entry.Script is {} script ) actor.Npc.RestoreScript( script, this, player );
					break;
				case MonsterMaker maker when entry.Maker is not null: maker.RestoreMaker( entry.Maker ); break;
				case MonsterMaker maker when entry.Count.HasValue: maker.Count = entry.Count.Value; break;
				case BrushMover mover when entry.Move is not null: mover.RestoreMove( entry.Move ); break;
				case TrackTrain train when entry.Train is not null: train.RestoreTrain( entry.Transform, entry.Train ); break;
				case MultiSource source when entry.Inputs is not null: source.InputStates = entry.Inputs; source.Initialized = true; break;
				case MultiManager manager:
					manager.Running = entry.Running;
					manager.StartTime = Time.Now - entry.Elapsed;
					manager.EventIndex = entry.EventIndex;
					break;
			}
		}
		if ( !player.IsValid() ) return;
		player.WorldPosition = data.Position;
		player.EyeAngles = data.Eyes;
		player.Body.Velocity = Vector3.Zero;
		player.Transform.ClearInterpolation();
	}
}

[Hide]
public sealed class CheckpointLoader : Component, Global.ISaveEvents
{
	private string _pending;

	void Global.ISaveEvents.AfterLoad( string filename ) => _pending = filename;

	protected override void OnFixedUpdate()
	{
		if ( _pending is null || !GetComponent<MapInstance>().IsLoaded ) return;
		var state = GetComponentInChildren<MapState>();
		if ( !state.IsValid() ) return;
		var filename = _pending;
		_pending = null;
		state.RestoreCheckpoint( filename );
	}
}
