using Sandbox.Movement;
using static Source1.MovementTrigger;

namespace Source1;

[Hide]
public sealed class MovementRestrictions : Component
{
	readonly HashSet<MovementTrigger> _volumes = [];
	Restrictions _active;
	MoveModeWalk _walk;
	float _step;
	System.IDisposable _input;

	internal void SetInside( MovementTrigger volume, bool inside )
	{
		if ( inside ) _volumes.Add( volume );
		else _volumes.Remove( volume );

		_active = 0;
		foreach ( var other in _volumes ) _active |= other.Settings;

		if ( (_active & (Restrictions.Duck | Restrictions.Walk | Restrictions.NoJump)) != 0 )
			_input ??= Scene.AddHook( GameObjectSystem.Stage.StartFixedUpdate, 0, ApplyInput, nameof( MovementRestrictions ), "Apply movement trigger input" );
		else ReleaseInput();

		if ( (_active & Restrictions.NoStep) == 0 ) RestoreStep();
		else if ( !_walk.IsValid() && GetComponent<MoveModeWalk>() is { } walk )
		{
			_walk = walk;
			_step = walk.StepUpHeight;
			walk.StepUpHeight = 0;
		}
	}

	void ApplyInput()
	{
		if ( _active == 0 || GetComponent<PlayerController>() is not { UseInputControls: true, IsProxy: false } player ) return;
		if ( (_active & Restrictions.NoJump) != 0 ) Input.Clear( "jump" );
		if ( (_active & Restrictions.Duck) != 0 && !string.IsNullOrWhiteSpace( player.DuckButton ) ) Input.SetAction( player.DuckButton, true );
		if ( (_active & Restrictions.Walk) != 0 && !string.IsNullOrWhiteSpace( player.AltMoveButton ) ) Input.SetAction( player.AltMoveButton, player.RunByDefault );
	}

	void RestoreStep()
	{
		if ( _walk.IsValid() ) _walk.StepUpHeight = _step;
		_walk = null;
	}

	void ReleaseInput()
	{
		_input?.Dispose();
		_input = null;
	}

	protected override void OnDestroy()
	{
		ReleaseInput();
		RestoreStep();
	}
}
