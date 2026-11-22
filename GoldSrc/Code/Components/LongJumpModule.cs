namespace GoldSrc.Maps;

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
