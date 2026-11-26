namespace NS2;

public sealed class UmbraCloud : LerkCloud
{
	const float Scale = MapLoader.Scale;
	bool settled;
	TimeUntil pulse, expires;

	protected override void OnStart()
	{
		pulse = 0.15f;
		expires = 2.5f;
		CinematicLoader.Play( "cinematics/alien/crag/umbra.cinematic", WorldPosition, WorldRotation, GameObject );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( !settled && TravelToDestination() )
		{
			settled = true;
			foreach ( var other in Scene.GetAllComponents<UmbraCloud>() )
				if ( other != this && other.Faction == Faction && WorldPosition.DistanceSquared( other.WorldPosition ) <= 25 * Scale * Scale )
					other.GameObject.Destroy();
		}
		if ( pulse > 0 ) return;
		pulse = 0.15f;
		foreach ( var alien in Scene.GetAllComponents<Ns2Npc>() )
		{
			if ( alien.Health <= 0 || alien.Faction != Faction || alien is not (AlienNpc or Drifter) ||
				alien.GameObject.Tags.Has( "burning" ) || WorldPosition.DistanceSquared( alien.WorldPosition ) >= 16 * Scale * Scale ) continue;
			alien.GameObject.GetOrAddComponent<UmbraProtection>().Refresh();
		}
		if ( expires <= 0 ) GameObject.Destroy();
	}
}

public sealed class UmbraProtection : Component
{
	TimeUntil expires;
	TimeSince lastEffect;
	Vector3? previousPosition;
	GameObject overlay;
	Ns2Npc npc;

	internal void Refresh() => expires = 2.5f;

	protected override void OnStart()
	{
		npc = GetComponent<Ns2Npc>();
		if ( !Graphics.IsAvailable || !npc.IsValid() || !npc.Renderer.IsValid() ) return;
		overlay = new GameObject( npc.Renderer.GameObject, false, "Umbra overlay" ) { NetworkMode = NetworkMode.Never };
		var renderer = overlay.AddComponent<SkinnedModelRenderer>();
		renderer.Model = npc.Renderer.Model;
		renderer.BoneMergeTarget = npc.Renderer;
		renderer.MaterialOverride = Material.Load( "mount://ns2/ns2/cinematics/vfx_materials/umbra.material.vmat" );
		overlay.Enabled = true;
	}

	protected override void OnDestroy() => overlay?.Destroy();

	protected override void OnUpdate()
	{
		if ( Networking.IsHost && (expires <= 0 || GameObject.Tags.Has( "burning" )) ) { Destroy(); return; }
		if ( !Graphics.IsAvailable || !npc.IsValid() ) return;
		float interval = previousPosition == WorldPosition ? 0.34f : 0.1f;
		previousPosition = WorldPosition;
		if ( lastEffect <= interval ) return;
		lastEffect = 0;
		var effect = npc is Onos or Hive ? "umbra_drag_big" : "umbra_drag";
		CinematicLoader.Play( $"cinematics/alien/{effect}.cinematic", npc.EngagementPoint, WorldRotation );
	}
}
