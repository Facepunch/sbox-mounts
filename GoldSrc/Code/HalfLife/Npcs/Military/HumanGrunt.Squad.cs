namespace GoldSrc.HalfLife;

public partial class HumanGrunt
{
	[Sync( SyncFlags.FromHost )] HumanGrunt SquadLeader { get; set; }
	GameObject _squadEnemy;
	TimeSince _lastSighting;
	bool _enemyEluded;

	internal override void OnModelLoaded( ModelLoader loader )
	{
		if ( !Networking.IsHost || SquadLeader.IsValid() ) return;
		foreach ( var grunt in Scene.GetAllComponents<HumanGrunt>() )
		{
			if ( grunt == this || grunt.Classification != Classification || grunt.Health <= 0 || WorldPosition.DistanceSquared( grunt.WorldPosition ) > 1024 * 1024 ) continue;
			if ( grunt.SquadLeader.IsValid() && (grunt.SquadLeader != grunt || grunt.Squadmates().Count() >= 4) ) continue;
			var sight = Scene.Trace.Ray( EyePosition, grunt.EyePosition ).IgnoreGameObjectHierarchy( GameObject )
				.WithoutTags( "npc", "player", "playercontroller", "trigger" ).Run();
			if ( sight.Hit ) continue;
			grunt.SquadLeader = grunt;
			SquadLeader = grunt;
			return;
		}
	}

	IEnumerable<HumanGrunt> Squadmates() => SquadLeader.IsValid() ? Scene.GetAllComponents<HumanGrunt>().Where( grunt =>
		grunt != this && grunt.Health > 0 && grunt.SquadLeader == SquadLeader ) : [];

	GameObject _encounterEnemy;
	bool _signaledPlayer;
	int _suppressionBursts;

	internal override void Engage( GameObject target )
	{
		if ( SquadLeader.IsValid() && _encounterEnemy != target )
		{
			_encounterEnemy = target;
			_suppressionBursts = 0;
			if ( SquadLeader != this )
			{
				if ( TakeCover( TargetEyePosition( target ), target ) ) return;
			}
			else
			{
				var enemyClass = ClassOf( target );
				if ( enemyClass == MonsterClass.Player ) Radio( "!HG_ALERT" );
				else if ( enemyClass is not (MonsterClass.PlayerAlly or MonsterClass.HumanPassive or MonsterClass.Machine) ) Radio( "!HG_MONST" );

				var aim = AimAt( target );
				if ( Ammo > 0 && !GunDropped && InAttackRange( target, aim ) && CanHit( target, aim ) && !FriendlyInLine( target ) )
				{
					_suppressionBursts = 5;
					if ( enemyClass == MonsterClass.Player && !_signaledPlayer && Find( "flank_signal" ) is { } signal )
					{
						_signaledPlayer = true;
						Movement.Stop();
						Animation.SetLookTarget( target );
						PlayAction( signal );
						return;
					}
				}
			}
		}
		base.Engage( target );
	}

	void UpdateSquadSight()
	{
		if ( !SquadLeader.IsValid() || FindVisibleEnemy() is not { } enemy ) return;
		var leader = SquadLeader;
		if ( leader._squadEnemy != enemy ) leader._enemyEluded = false;
		else if ( leader._lastSighting > 5 ) leader._enemyEluded = true;
		leader._squadEnemy = enemy;
		leader._lastSighting = 0;
	}

	bool SignalFoundEnemy( GameObject target )
	{
		if ( !SquadLeader.IsValid() || !SquadLeader._enemyEluded || SquadLeader._squadEnemy != target ) return false;
		var offset = WorldPosition - target.WorldPosition;
		var view = target.GetComponent<PlayerController>()?.EyeAngles.ToRotation() ?? target.WorldRotation;
		var facing = target.GetComponent<Npc>() is { } npc ? npc.InViewCone( offset ) : view.Forward.Dot( offset.WithZ( 0 ).Normal ) > 0.5f;
		if ( facing || FriendlyInLine( target ) || Find( "advance_signal" ) is not { } signal ) return false;
		SquadLeader._enemyEluded = false;
		PlayAction( signal );
		return true;
	}

	bool SquadBusyFiring( GameObject target ) => Squadmates().Where( grunt => grunt.IsAttacking( target ) ).Take( 2 ).Count() == 2;

	protected bool FriendlyInLine( GameObject target )
	{
		var aim = Rotation.LookAt( TargetHullCenter( target ) - WorldPosition );
		foreach ( var ally in Squadmates() )
		{
			var offset = ally.WorldPosition - WorldPosition;
			if ( aim.Forward.Dot( offset ) >= 0 && System.MathF.Abs( aim.Right.Dot( offset ) ) <= Definition.Radius * 3 * System.MathF.Abs( WorldTransform.UniformScale ) ) return true;
		}
		return false;
	}
}
