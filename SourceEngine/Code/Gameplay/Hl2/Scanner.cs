namespace Source1.Hl2;

public sealed class Scanner : CreatureNpc
{
	SoundHandle _motor;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		if ( !Application.IsDedicatedServer )
			_motor = GameObject.PlaySound( Sounds.Sound( $"{Voice}.FlyLoop", MountContent.Of( GameObject ) ) );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !_motor.IsValid() ) return;
		var speed = Animation.MoveVelocity.Length / MoveSpeed;
		_motor.Volume = MathX.Lerp( _motor.Volume, 0.25f + 0.75f * speed, Time.Delta * 10 );
		_motor.Pitch = MathX.Lerp( _motor.Pitch, System.Math.Min( 2.55f, 0.8f + 0.2f * speed ), Time.Delta * 10 );
	}

	protected override void OnDisabled()
	{
		_motor?.Stop();
		base.OnDisabled();
	}
}
