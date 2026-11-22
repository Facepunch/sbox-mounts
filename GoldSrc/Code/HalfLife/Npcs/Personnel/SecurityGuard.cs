namespace GoldSrc.HalfLife;

[Title( "Security Guard" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeSecurityGuard" )]
[NpcDefinition( "monster_barney", "barney", MonsterClass.PlayerAlly, 35, 16, 72, 145, ArmorGroup = 10, Attack = NpcAttack.Bullet, Damage = 5, Range = 1024, StrikeEvents = new[] { 3 }, Gibs = GibSet.Human, Blood = BloodColor.Red )]
public partial class SecurityGuard : Personnel, Component.IDamageable.IEvents
{
	protected override int PainVariants => 3;
	internal override void DeathSound() => EmitSound( $"{Sounds.Death}{Game.Random.Int( 1, 3 )}.wav", WorldPosition, VoiceVolume, SpeechPitch, this );
	internal override string SentencePrefix => "BA";

	internal override NpcSounds Sounds { get; } = new( "!BA_IDLE", "!BA_ATTACK", "barney/ba_pain", "barney/ba_die",
				"barney/ba_attack2.wav", Follow: "!BA_OK", Wait: "!BA_WAIT" );

	internal override IEnumerable<StudioSequence> ExtraSequences => [.. base.ExtraSequences, .. Activities( Ranged ), Find( "draw" )];
	internal override Vector3 AimOrigin => WorldPosition + Vector3.Up * 55;
	internal override Vector3 AimAt( GameObject target ) => TargetBodyPosition( target );
	GameObject _checkedTarget;
	TimeUntil _nextSightCheck;
	bool _clearShot;
	bool _enemyDied;

	void Component.IDamageable.IEvents.OnDied( Component.IDamageable.DeathEvent death )
	{
		if ( Networking.IsHost && death.Victim == LastHostile ) _enemyDied = true;
	}

	protected override void Think()
	{
		if ( _enemyDied && Action is null )
		{
			_enemyDied = false;
			if ( Health > 0 && !IsBarnacleVictim && CanStartConversation && !FindVisibleEnemy().IsValid() )
				Say( "KILL", duration: 4, attenuation: GoldSrcSoundEvents.Normal );
		}
		base.Think();
	}

	internal override void OnModelLoaded( ModelLoader loader )
	{
		base.OnModelLoaded( loader );
		EyeHeight = 50;
	}

	internal override bool CanHit( GameObject target, Vector3 position )
	{
		if ( _checkedTarget != target || _nextSightCheck <= 0 )
		{
			_checkedTarget = target;
			_nextSightCheck = 1.5f;
			_clearShot = base.CanHit( target, position );
		}
		return _clearShot;
	}

	internal override void BeginAttack( GameObject target )
	{
		if ( !EquipmentDrawn && Find( "draw" ) is { } draw )
		{
			PlayAction( draw );
			return;
		}
		if ( WorldRotation.Forward.Dot( (target.WorldPosition - WorldPosition).Normal ) < 0.5f ) return;
		AttackWith( target, WeightedActivity( Ranged ), target.GetComponent<PlayerController>().IsValid() ? 1.5f : 1 );
	}

	internal override void OnEnemySpotted( GameObject target )
	{
		if ( CanStartConversation ) Say( "ATTACK", target, Game.Random.Float( 2.8f, 3.2f ) );
	}

	internal override void HandleActionEvent( int id )
	{
		if ( id == 2 ) EquipmentDrawn = true;
		if ( id == 4 ) EquipmentDrawn = false;
	}
	internal override void FireBullet( GameObject target, Vector3 origin, Vector3 end )
	{
		var shift = Game.Random.Int( 0, 20 );
		PlayGunshot( Sounds.Attack, origin, pitch: shift > 10 ? 1 : (95 + shift) / 100f );
		TraceBullets( origin, end, 1, Definition.Damage, 0.01745f, 1024 );
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		if ( Networking.IsHost && Renderer.IsValid() && Renderer.Model.IsValid() && Renderer.GetBodyGroup( "gun" ) < 2 )
		{
			var transform = Renderer.GetAttachment( "attachment1" ) ?? WorldTransform.WithPosition( WorldPosition + Vector3.Up * 55 );
			if ( WeaponPickupLoader.Drop<Pistol>( transform ) )
				Renderer.SetBodyGroup( "gun", 2 );
		}
		base.OnDeath( death );
	}

	internal override void ApplyEquipment() => Renderer.SetBodyGroup( "gun", EquipmentDrawn ? 1 : 0 );
}
