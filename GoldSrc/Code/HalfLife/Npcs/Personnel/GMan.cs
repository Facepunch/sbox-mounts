namespace GoldSrc.HalfLife;

[Title( "G-Man" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeGMan" )]
[NpcDefinition( "monster_gman", "gman", MonsterClass.None, 100, 16, 72, 0, IdleSequence = "idle01" )]
public sealed class GMan : TalkingNpc
{
	GameObject _player, _listener;
	TimeUntil _talkUntil;

	internal override void OnModelLoaded( ModelLoader loader )
	{
		base.OnModelLoaded( loader );
		Senses.HearingRange = 0;
		Animation.TurnsBody = false;
	}

	public void PlayScriptedSentence( string sentence, float duration, GameObject listener = null, float volume = 1, float attenuation = GoldSrcSoundEvents.Normal )
	{
		if ( !Networking.IsHost || string.IsNullOrWhiteSpace( sentence ) ) return;
		EmitSound( $"!{sentence.TrimStart( '!' )}", WorldPosition, volume, speaker: this, attenuation: attenuation );
		_listener = listener;
		_talkUntil = duration;
	}

	internal override void SpeakMapSentence( string sentence, float duration, GameObject listener, float volume, float attenuation, bool silenceFriends )
		=> PlayScriptedSentence( sentence, duration, listener, volume, attenuation );

	protected override void Think()
	{
		if ( AdvanceAction() || PhysicsCarrier.IsValid() ) { Animation.ClearLookTarget(); return; }
		if ( !_player.IsValid() ) _player = Scene.GetAllComponents<PlayerController>()
			.MinBy( player => WorldPosition.DistanceSquared( player.WorldPosition ) )?.GameObject;
		var target = _talkUntil > 0 && _listener.IsValid() ? _listener : _player;
		if ( target.IsValid() ) Animation.SetLookTarget( target );
		else Animation.ClearLookTarget();
	}

	internal override float GetHeadAngle( StudioController head )
	{
		if ( !Animation.IsLooking || !Animation.LookObject.IsValid() || PhysicsCarrier.IsValid() ) return 0;
		var direction = Animation.LookObject.WorldPosition - WorldPosition;
		return Angles.NormalizeAngle( direction.EulerAngles.yaw - WorldRotation.Yaw() );
	}

	internal override void ModifyDamage( Component.IDamageable.DamageEvent damage )
	{
		var hit = damage.DamageInfo;
		if ( HitStudioModel( hit ) )
			ArmorRicochet.Show( hit.Position, 1 );
		Health = MaxHealth * 0.5f;
		damage.Cancelled = true;
	}
}
