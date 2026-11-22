namespace GoldSrc.HalfLife;

internal sealed class StudioSequenceEvents
{
	StudioSequence _sequence;
	float _previous;

	internal void Reset( StudioSequence sequence, float time, bool includeFirstFrame )
	{
		_sequence = sequence;
		_previous = includeFirstFrame ? -1 : time;
	}

	internal void Update( NpcAudio audio, SkinnedModelRenderer renderer, float time, bool looping, Npc npc = null,
		bool reverse = false, bool grounded = false )
	{
		if ( _sequence is null || audio is null ) return;
		if ( looping && time < _previous )
		{
			Dispatch( _previous, _sequence.Duration );
			_previous = -1;
		}
		Dispatch( _previous, time );
		_previous = time;

		void Dispatch( float start, float end )
		{
			var events = _sequence.Events;
			for ( int i = 0; i < events.Length; i++ )
			{
				var e = events[reverse ? events.Length - i - 1 : i];
				var at = e.Frame / _sequence.Description.Fps;
				if ( reverse ) at = _sequence.Duration - at;
				if ( at > end ) break;
				if ( at > start ) Play( e, audio, renderer, npc, grounded );
			}
		}
	}

	static void Play( Mdl.Event e, NpcAudio audio, SkinnedModelRenderer renderer, Npc npc, bool grounded )
	{
		var position = renderer.WorldPosition;
		switch ( e.Index )
		{
			case 1003:
				if ( Networking.IsHost && npc.IsValid() )
					npc.GetComponent<Maps.Monster>()?.FireAnimationTarget( e.Options );
				break;
			case 1004:
				NpcAudio.Play( audio.Find( e.Options ), position, attenuation: GoldSrcSoundEvents.Idle );
				break;
			case 1008:
				if ( npc is TalkingNpc speaker )
				{
					if ( Networking.IsHost ) audio.Emit( e.Options, position, speaker: speaker, attenuation: GoldSrcSoundEvents.Idle );
				}
				else NpcAudio.Play( audio.Find( e.Options ), position, attenuation: GoldSrcSoundEvents.Idle );
				break;
			case 1005:
			case 1009:
				if ( Networking.IsHost && (e.Index == 1005 || Game.Random.Int( 0, 2 ) != 0) )
					audio.Emit( $"!{e.Options.TrimStart( '!' )}", position, speaker: npc as TalkingNpc, attenuation: GoldSrcSoundEvents.Idle );
				break;
			case 2001:
			case 2002:
				if ( grounded && Networking.IsHost )
					audio.Emit( $"common/bodydrop{Game.Random.Int( 3, 4 )}.wav", position, pitch: e.Index == 2002 ? 0.9f : 1 );
				break;
			case 2010:
				NpcAudio.Play( audio.Find( "zombie/claw_miss2.wav" ), position );
				break;
			case 5001:
			case 5011:
			case 5021:
			case 5031:
				if ( renderer.GetAttachment( $"attachment{1 + (e.Index - 5001) / 10}" ) is { } muzzle )
				{
					int.TryParse( e.Options, out var type );
					ShowMuzzleFlash( renderer, muzzle, type );
				}
				break;
			case 5002:
				if ( int.TryParse( e.Options, out var count ) && renderer.GetAttachment( "attachment1" ) is { } attachment )
					StudioSparks.Emit( attachment.Position, count );
				break;
			case 5004:
				NpcAudio.Play( audio.Find( e.Options ), renderer.GetAttachment( "attachment1" )?.Position ?? position );
				break;
			default:
				npc?.PlaySequenceSound( audio, e.Index );
				break;
		}
	}

	internal static void ShowMuzzleFlash( SkinnedModelRenderer model, Transform muzzle, int type )
	{
		var index = System.Math.Max( 0, type % 10 ) % 3;
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ||
			mount.GetIndexedSprite( $"muzzleflash{index + 1}" ) is not { } sprite ) return;
		var texture = sprite.Animations[0].Frames[0].Texture;
		var scale = type / 10 > 0 ? (type / 10) * 0.1f : 0.5f;
		var go = new GameObject( false, "Muzzle flash" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = muzzle.Position;
		go.WorldRotation = Rotation.FromRoll( Game.Random.Int( 0, index == 0 ? 20 : 359 ) );
		var flash = go.AddComponent<SpriteRenderer>();
		flash.Sprite = sprite;
		flash.CurrentFrameIndex = Game.Random.Int( 0, sprite.Animations[0].Frames.Count - 1 );
		flash.Additive = true;
		flash.Size = new Vector2( texture.Width, texture.Height ) * scale;
		flash.RenderOptions.Game = model.RenderOptions.Game;
		flash.RenderOptions.Overlay = model.RenderOptions.Overlay;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0.02f;
		go.Enabled = true;
	}
}
