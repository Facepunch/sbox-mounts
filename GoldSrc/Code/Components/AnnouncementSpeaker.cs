using GoldSrc.HalfLife;

namespace GoldSrc.Maps;

public sealed class AnnouncementSpeaker : MapEntity
{
	[Property] public string Sentence { get; set; }
	[Property] public float Volume { get; set; }
	[Property] public bool StartSilent { get; set; }

	private bool _active;
	private TimeUntil _next;

	protected override void OnStart()
	{
		_active = !StartSilent;
		_next = Game.Random.Float( 5, 15 );
	}

	public override void Trigger( GameObject activator = null )
	{
		if ( _active ) TurnOff();
		else TurnOn();
	}

	public override void TurnOn( GameObject activator = null )
	{
		if ( _active ) return;
		_active = true;
		_next = 0.1f;
	}

	public override void TurnOff( GameObject activator = null ) => _active = false;

	protected override void OnFixedUpdate()
	{
		if ( !_active || !Networking.IsHost || _next > 0 ) return;

		var conversation = Personnel.GetConversation( Scene );
		if ( conversation.NextLine > 0 )
		{
			_next = conversation.NextLine.Relative + Game.Random.Float( 5, 10 );
			return;
		}

		bool once = Sentence.StartsWith( '!' );
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
			mount.NpcAudio.Emit( once ? Sentence : $"!{Sentence}", WorldPosition, Volume, attenuation: 0.3f );
		_active = !once;
		if ( once ) return;
		_next = Game.Random.Float( 15, 135 );
		conversation.NextLine = 5;
	}
}
