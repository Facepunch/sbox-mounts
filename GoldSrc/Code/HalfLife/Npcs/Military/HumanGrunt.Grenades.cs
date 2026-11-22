namespace GoldSrc.HalfLife;

public partial class HumanGrunt
{
	[Property] public bool HandGrenades { get; set; } = true;
	TimeUntil _nextGrenade;
	Vector3 _toss;
	internal virtual void TossGrenade( Vector3 origin, Vector3 velocity ) => TimedGrenade.Throw( this, origin, velocity, 3.5f );
	GameObject _grenadeTarget;
	Vector3 ThrowOrigin => WorldPosition + Vector3.Up * 60;

	internal override bool TryOccludedAttack( GameObject target ) => TryThrowGrenade( target );

	bool TryThrowGrenade( GameObject target )
	{
		if ( _nextGrenade > 0 || (HasLauncher ? GunDropped : !HandGrenades) ) return false;
		_nextGrenade = 1;
		if ( Squadmates().Count( other => other._grenadeTarget == target ) >= 2 ) return false;
		var end = HasLauncher ? TargetBodyPosition( target ) : target.WorldPosition + Vector3.Up * 4;
		if ( HasLauncher )
		{
			var velocity = target.GetComponent<Rigidbody>()?.Velocity ?? Vector3.Zero;
			end += velocity * (ThrowOrigin.Distance( end ) / 600);
		}
		if ( (WorldPosition - end).WithZ( 0 ).LengthSquared <= 256 * 256 || Find( HasLauncher ? "launchgrenade" : "throwgrenade" ) is not { } clip ) return false;
		if ( Squadmates().Any( grunt => (grunt.WorldPosition - end).WithZ( 0 ).LengthSquared <= 256 * 256 ) )
			return false;
		if ( !TimedGrenade.TryAim( this, ThrowOrigin, target, end, out _toss, HasLauncher ? ThrowOrigin.Distance( end ) / 600 : 1, !HasLauncher ) ) return false;
		if ( !HasLauncher ) Radio( "!HG_THROW" );
		Movement?.Stop();
		_grenadeTarget = target;
		PlayAction( clip );
		return true;
	}

	internal override void FinishAction( string name )
	{
		if ( name is "throwgrenade" or "launchgrenade" ) _grenadeTarget = null;
	}
}
