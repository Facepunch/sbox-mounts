namespace GoldSrc.Maps;

public sealed class LongJumpPickup : MapEntity, Component.ITriggerListener
{
	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( !Networking.IsHost || !CanTrigger ) return;
		var player = other.GetComponentInParent<PlayerController>();
		if ( !player.IsValid() || player.GetComponent<LongJumpModule>().IsValid() ) return;
		player.GameObject.AddComponent<LongJumpModule>();
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
			mount.NpcAudio.Emit( "!HEV_A1", player.WorldPosition );
		Fire( OnTrigger, player.GameObject, detached: true );
		Kill();
	}
}

public sealed class LongJumpModule : Component, PlayerController.IEvents
{
	private TimeUntil _duckWindow;

	void PlayerController.IEvents.PreInput()
	{
		var controller = GetComponent<PlayerController>();
		if ( !IsProxy && Input.Pressed( controller.DuckButton ) ) _duckWindow = 1;
	}

	void PlayerController.IEvents.OnJumped()
	{
		var controller = GetComponent<PlayerController>();
		if ( IsProxy || _duckWindow <= 0 || !Input.Down( controller.DuckButton ) ) return;
		if ( controller.Body.Velocity.WithZ( 0 ).Length <= 50 ) return;
		// PM_Jump uses the horizontal components of view-forward, including pitch.
		controller.Body.Velocity = (controller.EyeAngles.Forward * 560).WithZ( System.MathF.Sqrt( 2 * 800 * 56 ) );
	}
}
