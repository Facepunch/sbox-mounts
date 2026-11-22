namespace GoldSrc.Maps;

public sealed class RestartLevel : MapEntity
{
	[Property] public ScreenFade Fade { get; set; }
	[Property] public TitleMessage Message { get; set; }
	[Property] public float MessageTime { get; set; }
	[Property] public float LoadTime { get; set; }

	private bool _pending, _shown;
	private TimeSince _elapsed;

	public override void Trigger( GameObject activator = null )
	{
		if ( !MapState.SoloCampaign || !CanTrigger ) return;
		Fade.Trigger( activator );
		_pending = true;
		_shown = false;
		_elapsed = 0;
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	protected override void OnFixedUpdate()
	{
		if ( !_pending ) return;
		if ( !MapState.SoloCampaign ) { _pending = false; return; }
		if ( !CanTrigger || _elapsed < MessageTime ) return;
		if ( !_shown )
		{
			_shown = true;
			if ( !string.IsNullOrEmpty( Message.Text ) ) Scene.GetSystem<MessageDisplay>()?.Show( Message );
		}

		if ( _elapsed < LoadTime ) return;
		_pending = false;
		if ( Map.LoadCheckpoint() ) return;
		// No checkpoint has been reached during this visit.
		var instance = Map.GetComponentInParent<MapInstance>();
		if ( !instance.IsValid() ) return;
		var scene = Scene;
		instance.OnMapLoaded += Respawn;
		instance.UnloadMap();

		void Respawn()
		{
			instance.OnMapLoaded -= Respawn;
			var start = instance.GetComponentsInChildren<SpawnPoint>().FirstOrDefault( x => x.GameObject.Name == "info_player_start" );
			if ( !start.IsValid() ) return;
			foreach ( var player in scene.GetAllComponents<PlayerController>() )
			{
				if ( player.IsProxy ) continue;
				player.WorldPosition = start.WorldPosition;
				player.EyeAngles = start.WorldRotation.Angles();
				player.Body.Velocity = Vector3.Zero;
				player.Transform.ClearInterpolation();
			}
		}
	}
}
