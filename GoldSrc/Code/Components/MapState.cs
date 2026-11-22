using System;

namespace GoldSrc.Maps;

[Hide]
public sealed partial class MapState : Component
{
	[Property] public List<MapEntity> Entities { get; set; } = [];
	[Property, Sync( SyncFlags.FromHost )] public NetDictionary<string, int> Globals { get; set; } = new();
	private readonly List<(MapEntity Entity, float Time)> _removals = [];
	private System.Threading.Tasks.Task<bool> _navigation;
	internal bool NavigationReady => _navigation?.IsCompletedSuccessfully == true;

	public void ScheduleRemoval( MapEntity entity, float delay ) => _removals.Add( (entity, Time.Now + delay) );

	protected override void OnFixedUpdate()
	{
		// A level transition replaces the collision world inside the same scene.
		// Its previous navmesh can still return valid positions on the old map.
		if ( Networking.IsHost && _navigation is null && !Scene.NavMesh.IsGenerating && GetComponentInParent<MapInstance>() is { IsLoaded: true } )
			_navigation = Scene.NavMesh.Generate( Scene.PhysicsWorld );
		for ( int i = _removals.Count - 1; i >= 0; i-- )
		{
			var entry = _removals[i];
			if ( Time.Now < entry.Time ) continue;
			_removals.RemoveAt( i );
			if ( entry.Entity.IsValid() ) entry.Entity.Kill();
		}
	}

	public int GetGlobal( string name ) => Globals.TryGetValue( name, out var state ) ? state : 0;

	public void SetGlobal( string name, int state )
	{
		ArgumentException.ThrowIfNullOrEmpty( name );
		if ( Networking.IsActive && !Networking.IsHost ) return;
		Globals[name] = state;
	}

	public MapEntity Find( string name ) =>
		FindAll( name ).FirstOrDefault();

	public IEnumerable<MapEntity> FindAll( string name ) =>
		string.IsNullOrEmpty( name ) ? [] :
		Entities.Where( x => x.IsValid() && !x.GameObject.IsDestroyed && x.TargetName == name );

	public Doo BuildOutput( string name, UseType use = UseType.Toggle, float delay = 0f, string killName = null )
	{
		var body = new List<Doo.Block>();
		if ( delay > 0f )
			body.Add( new Doo.DelayBlock { Seconds = new Doo.LiteralExpression { LiteralValue = delay } } );
		foreach ( var entity in FindAll( killName ) )
		{
			if ( entity.KillInput is not null )
				body.Add( InvokeBlock( entity, entity.KillInput, false ) );
		}
		if ( !string.IsNullOrEmpty( killName ) )
		{
			var removeClones = (Doo.InvokeBlock)InvokeBlock( this, nameof( KillManagerClones ), false );
			removeClones.Arguments = [new Doo.LiteralExpression { LiteralValue = killName }];
			body.Add( removeClones );
		}
		foreach ( var entity in FindAll( name ) )
		{
			var method = entity.UseMethods[(int)use];
			if ( method is not null )
				body.Add( InvokeBlock( entity, method ) );
		}
		return body.Count > 0 ? new Doo { Body = body } : null;
	}

	public void KillManagerClones( string name )
	{
		foreach ( var clone in FindAll( name ).Where( x => x is MultiManager { IsClone: true } or Monster { SpawnedByMaker: true } ).ToArray() )
			clone.Kill();
	}

	public static Doo.Block InvokeBlock( Component component, string method, bool activator = true )
	{
		bool input = activator && component is MapEntity
			&& method is nameof( MapEntity.Trigger ) or nameof( MapEntity.TurnOn ) or nameof( MapEntity.TurnOff ) or nameof( MapEntity.SetValue );
		var use = method switch
		{
			nameof( MapEntity.TurnOn ) => UseType.On,
			nameof( MapEntity.TurnOff ) => UseType.Off,
			nameof( MapEntity.SetValue ) => UseType.Set,
			_ => UseType.Toggle
		};
		return new Doo.InvokeBlock
		{
			InvokeType = Doo.InvokeType.Member,
			TargetComponent = new Doo.TargetComponent
			{
				Type = Doo.TargetComponent.TargetType.Direct,
				ComponentValue = component
			},
			Member = $"{component.GetType().FullName}.{(input ? nameof( MapEntity.Use ) : method)}",
			Arguments = input ?
			[
				new Doo.VariableExpression { VariableName = "activator" },
				new Doo.VariableExpression { VariableName = "caller" },
				new Doo.LiteralExpression { LiteralValue = (int)use },
				new Doo.VariableExpression { VariableName = "value" }
			] : activator ? [new Doo.VariableExpression { VariableName = "activator" }] : null
		};
	}

	public bool IsMasterTriggered( string name, GameObject activator )
	{
		if ( string.IsNullOrEmpty( name ) ) return true;
		var entity = Find( name );
		if ( entity is MultiSource source )
			return source.IsTriggered;
		Log.Warning( $"GoldSrc: master '{name}' is missing or is not a master entity." );
		return true;
	}
}
