namespace GoldSrc.HalfLife;

[Title( "Osprey" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeOsprey" )]
[NpcDefinition( "monster_osprey", "osprey", MonsterClass.Machine, 400, 62, 64, 170, Movement = NpcMovement.Flying, IdleSequence = "rotor" )]
public class Osprey : Aircraft
{
	internal override float ViewDot => 0;

	internal override float SpeedPerPitchStep => 75;
	internal override float RotorAttenuation => 0.15f;
	internal override string RotorSample => "apache/ap_rotor4.wav";
	internal override string DebrisModel => "osprey_bodygibs";

	internal virtual string UnitPrefab => HumanGrunt.DeploymentPrefab;
	internal virtual bool CanDeploy( HumanGrunt unit ) => unit.GetType() == typeof( HumanGrunt );

	readonly List<HumanGrunt> _units = [];
	TimeUntil _nextDeployment;
	bool _ready;
	float _leftEngine = 200, _rightEngine = 200;

	internal override void PlaceAtSpawn()
	{
		var floor = Scene.Trace.Ray( WorldPosition + Vector3.Up * 8, WorldPosition + Vector3.Down * 160 )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "npc", "player", "trigger" ).Run();
		if ( floor.Hit )
		{
			var ceiling = Scene.Trace.Ray( floor.EndPosition + Vector3.Up * 8, floor.EndPosition + Vector3.Up * 512 )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "npc", "player", "trigger" ).Run();
			WorldPosition = ceiling.EndPosition - Vector3.Up * 64;
		}
		_nextDeployment = 1;
		_ready = true;
	}

	protected override void Think()
	{
		if ( !_ready || _nextDeployment > 0 ) return;
		_nextDeployment = 1;
		if ( _units.Count == 0 )
		{
			_units.AddRange( Scene.GetAllComponents<HumanGrunt>().Where( grunt => grunt.Health > 0 && CanDeploy( grunt ) ).Take( 24 ) );
			// A standalone spawn starts with one deployment when no ground squad exists.
			if ( _units.Count == 0 ) _units.AddRange( new HumanGrunt[4] );
		}
		if ( _units.Any( unit => unit.IsValid() && unit.RopeAnchor.HasValue ) ) return;
		var slot = 0;
		for ( int exit = 0; exit < 4; ++exit )
		{
			while ( slot < _units.Count && _units[slot].IsValid() && _units[slot].Health > 0 ) slot++;
			if ( slot == _units.Count ) break;
			var forward = exit % 2 == 0 ? 32 : -64;
			var right = exit < 2 ? 100 : -100;
			var start = WorldTransform.PointToWorld( new Vector3( forward, -right, -96 ) );
			var ground = Scene.Trace.Ray( start, start + Vector3.Down * 4096 )
				.IgnoreGameObjectHierarchy( GameObject ).Run();
			if ( !ground.Hit || ground.GameObject?.GetComponentInParent<IDamageable>() is not null ) continue;
			_units[slot] = HumanGrunt.Deploy( this, start, WorldRotation, UnitPrefab );
		}
	}

	internal override void ModifyDamage( Component.IDamageable.DamageEvent damage )
	{
		if ( !HitStudioModel( damage.DamageInfo ) ) return;
		var groups = HitGroups( damage.DamageInfo );
		if ( groups.Has( "hitgroup_2" ) )
		{
			if ( _leftEngine < 0 ) damage.Cancelled = true;
			else _leftEngine -= damage.AcceptedDamage;
		}
		else if ( groups.Has( "hitgroup_3" ) )
		{
			if ( _rightEngine < 0 ) damage.Cancelled = true;
			else _rightEngine -= damage.AcceptedDamage;
		}
		else if ( !groups.Has( "hitgroup_1" ) && damage.AcceptedDamage <= 50 ) damage.Cancelled = true;
	}

}
