namespace GoldSrc.Maps;

public sealed class HevSuit : Component
{
	[ConVar( "hl1_suitvolume" )] public static float Volume { get; set; } = 0.25f;

	private readonly Queue<int> _reports = new();
	private readonly Dictionary<int, TimeUntil> _repeatAfter = [];
	private TimeUntil _next;

	internal void ReportCharge( float armour )
	{
		if ( Connection.All.Count > 1 ) return;
		int report = System.Math.Max( 0, (int)(armour + 0.5f) / 5 - 1 );
		if ( _repeatAfter.TryGetValue( report, out var wait ) && wait > 0 ) return;
		_repeatAfter[report] = 30;
		if ( _reports.Count == 0 && _next <= 0 ) _next = 0.1f;
		if ( _reports.Count == 4 ) _reports.Dequeue();
		_reports.Enqueue( report );
	}

	protected override void OnFixedUpdate()
	{
		if ( _reports.Count == 0 || _next > 0 ) return;
		Speak( $"!HEV_{_reports.Dequeue()}P" );
		_next = 3.5f;
	}

	internal void Speak( string sentence )
	{
		if ( Volume <= 0.05f || Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ) return;
		float pitch = Game.Random.Int( 0, 1 ) == 0 ? 1f : Game.Random.Int( 98, 104 ) / 100f;
		mount.NpcAudio.Emit( sentence, WorldPosition, Volume, pitch );
	}
}
