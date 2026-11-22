using GoldSrc.HalfLife;

namespace GoldSrc.Maps;

public sealed class MonsterMaker : MapEntity
{
	[Property] public string ChildClass { get; set; }
	[Property] public string ChildName { get; set; }
	[Property] public string PrefabPath { get; set; }
	[Property] public int Count { get; set; }
	[Property] public int MaxLive { get; set; }
	[Property] public float Interval { get; set; }
	[Property] public bool StartActive { get; set; }
	[Property] public bool HitMonsterClip { get; set; }
	[Property] public bool Cyclic { get; set; }

	readonly List<GameObject> _children = [];
	bool _active, _exhausted, _fadeChildren;
	float? _ground;
	TimeUntil _next;

	internal sealed record SavedChild( Transform Transform, float Health );
	internal sealed record SavedMaker( int Count, bool Active, bool Exhausted, float NextSpawn, SavedChild[] Children );

	internal SavedMaker CaptureMaker() => new( Count, _active, _exhausted, _next,
		_children.Where( go => go.IsValid() && !go.IsDestroyed && go.GetComponent<Npc>() is { Health: > 0 } )
			.Select( go => new SavedChild( go.WorldTransform, go.GetComponent<Npc>().Health ) ).ToArray() );

	internal void RestoreMaker( SavedMaker state )
	{
		foreach ( var child in _children )
			if ( child.IsValid() ) child.Destroy();
		_children.Clear();
		Count = state.Count;
		_active = state.Active;
		_exhausted = state.Exhausted;
		_next = state.NextSpawn;
		foreach ( var child in state.Children ) CreateChild( child.Transform, child.Health );
	}

	protected override void OnStart()
	{
		_active = StartActive;
		_fadeChildren = Count != 1;
		_next = Interval;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !_active || _exhausted || _next > 0 ) return;
		_next = Interval;
		SpawnChild();
	}

	public override void Trigger( GameObject activator = null ) => UseMaker( !_active );
	public override void TurnOn( GameObject activator = null ) => UseMaker( true );
	public override void TurnOff( GameObject activator = null ) => UseMaker( false );

	private void UseMaker( bool active )
	{
		if ( !CanTrigger || !Networking.IsHost || _exhausted ) return;
		if ( Cyclic ) SpawnChild();
		else if ( _active != active )
		{
			_active = active;
			_next = 0;
		}
	}

	private void SpawnChild()
	{
		_children.RemoveAll( go => !go.IsValid() || go.IsDestroyed || go.GetComponent<Npc>() is { Health: <= 0 } );
		if ( MaxLive > 0 && _children.Count >= MaxLive ) return;
		var pos = WorldPosition;
		_ground ??= Scene.Trace.Ray( pos, pos + Vector3.Down * 2048 ).WithoutTags( "npc", "player", "trigger" ).Run().EndPosition.z;
		var bounds = new BBox( new Vector3( pos.x - 34, pos.y - 34, _ground.Value ), pos + new Vector3( 34, 34, 0 ) );
		if ( Scene.FindInPhysics( bounds ).Any( go => go.GetComponentInParent<PlayerController>().IsValid() || go.GetComponentInParent<Npc>().IsValid() ) ) return;

		if ( !CreateChild( WorldTransform ) ) return;
		_exhausted = --Count == 0;
		Fire( OnTrigger, GameObject );
	}

	private bool CreateChild( Transform transform, float? health = null )
	{
		var prefab = GameObject.GetPrefab( PrefabPath );
		if ( !prefab.IsValid() )
		{
			Log.Warning( $"{GameObject.Name}: missing monster prefab '{PrefabPath}'." );
			_exhausted = true;
			return false;
		}
		var child = prefab.Clone( transform, startEnabled: false );
		child.SetParent( Map.GameObject, true );
		var npc = child.GetComponent<Npc>( true );
		child.Name = string.IsNullOrEmpty( ChildName ) ? ChildClass : ChildName;
		var actor = child.AddComponent<Monster>();
		actor.TargetName = ChildName;
		actor.Npc = npc;
		if ( HitMonsterClip ) MonsterClip.Restrict( child );
		actor.SpawnedByMaker = true;
		if ( npc.IsValid() )
		{
			npc.FadeCorpse = _fadeChildren;
			if ( health.HasValue ) npc.Health = health.Value;
		}
		actor.Renderer = npc?.Renderer;
		actor.Map = Map;
		if ( !string.IsNullOrEmpty( ChildName ) ) Map.Entities.Add( actor );
		_children.Add( child );
		child.Enabled = true;
		child.NetworkSpawn( true, null );
		return true;
	}
}
