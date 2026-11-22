namespace GoldSrc.HalfLife;

public partial class HumanGrunt
{
	[Property, Sync( SyncFlags.FromHost )] public Vector3? RopeAnchor { get; private set; }
	float _descentSpeed;
	bool _descending;
	GameObject _rope;
	TimeUntil _nextBurst;

	internal const string DeploymentPrefab = "mount://hl1/entities/npcs/monster_human_grunt.prefab";

	internal static HumanGrunt Deploy( Component source, Vector3 start, Rotation rotation, string prefabPath = DeploymentPrefab )
	{
		var prefab = PrefabFile.Load( prefabPath );
		if ( prefab is null ) return null;
		var go = GameObject.Clone( prefab, new Transform( start, rotation ), startEnabled: false );
		var grunt = go.GetComponent<HumanGrunt>( true );
		grunt.RopeAnchor = start + Vector3.Up * 112;
		grunt._descentSpeed = Game.Random.Float( 128, 196 );
		if ( source is GruntRepel ) go.SetParent( source.GameObject, true );
		go.Enabled = true;
		go.NetworkSpawn( true, null );
		return grunt;
	}

	internal override void PlaceAtSpawn()
	{
		if ( !RopeAnchor.HasValue ) return;
		_descending = true;
		Launch( Vector3.Down * _descentSpeed );
	}

	internal override StudioSequence MovementSequence( float speed ) => RopeAnchor.HasValue
		? Find( "repel_repel" ) : base.MovementSequence( speed );

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !RopeAnchor.HasValue )
		{
			if ( _rope.IsValid() ) _rope.Destroy();
			return;
		}
		if ( !_rope.IsValid() ) ShowRope( RopeAnchor.Value );
		if ( !Networking.IsHost || !_descending ) return;
		if ( IsOnGround )
		{
			_descending = false;
			RopeAnchor = null;
			if ( Find( "repel_land" ) is { } landing ) PlayAction( landing );
			return;
		}
		var body = GetComponent<Rigidbody>();
		if ( body.IsValid() ) body.Velocity = Vector3.Down * _descentSpeed;
		if ( AdvanceAction() || _nextBurst > 0 ) return;
		var target = FindVisibleEnemy();
		if ( !IsLiveTarget( target ) || Find( "repel_shoot" ) is not { } shoot ) return;
		if ( GunDropped || Ammo <= 0 ) return;
		_nextBurst = shoot.Duration + 0.3f;
		AttackWith( target, shoot );
	}

	void ShowRope( Vector3 anchor )
	{
		_rope = new GameObject( GameObject, false, "Rappel rope" );
		_rope.NetworkMode = NetworkMode.Never;
		_rope.LocalPosition = Vector3.Up * 48;
		var beam = _rope.AddComponent<BeamEffect>();
		beam.TargetPosition = anchor;
		beam.Scale = 1;
		beam.BeamColor = Color.White;
		beam.BeamLifetime = 2;
		beam.Looped = true;
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount && mount.GetIndexedSprite( "rope" ) is { } sprite )
			beam.Texture = sprite.Animations[0].Frames[0].Texture;
		_rope.Enabled = true;
	}
}
