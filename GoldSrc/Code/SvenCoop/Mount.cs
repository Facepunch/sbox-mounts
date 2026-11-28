using GoldSrc.Bsp;
using GoldSrc.Maps;
using GoldSrc.SvenCoop;

namespace GoldSrc;

public class SvenCoopMount : GameMount
{
	public override string Ident => "svencoop";
	public override string Title => "Sven Co-op";
	public override long AppId => 225840;
	public override IReadOnlyList<string> GameDirs => ["svencoop"];

	internal override UseType ConfigureRelay( Relay relay, Entity entity )
	{
		if ( relay.FireOnSpawn ) return base.ConfigureRelay( relay, entity );
		relay.SelfActivator = (entity.SpawnFlags & 64) == 0;
		relay.KeepDelayedActivator = true;
		relay.Value = entity.GetFloat( "triggervalue" );
		relay.ResetDelay = entity.GetFloat( "m_flDelayBeforeReset" );
		return entity.GetInt( "triggerstate" ) switch
		{
			0 => UseType.Off,
			1 => UseType.On,
			3 => UseType.Set,
			4 => UseType.Kill,
			_ => UseType.Toggle
		};
	}

	internal override MapEntity CreatePointEntity( Entity entity )
	{
		if ( entity.ClassName is not ("trigger_random" or "trigger_random_time" or "trigger_random_unique") ) return null;
		var random = MapBuilder.CreatePoint( entity ).AddComponent<RandomTrigger>();
		var flags = entity.SpawnFlags;
		random.Timed = entity.ClassName == "trigger_random_time" || (flags & 8) != 0;
		random.Unique = entity.ClassName == "trigger_random_unique" || (flags & 16) != 0;
		random.Reusable = (flags & (entity.ClassName == "trigger_random_unique" ? 1 : 4)) != 0;
		random.StartOn = (flags & 1) != 0;
		random.Once = (flags & 2) != 0;
		random.MinDelay = entity.GetFloat( "min_delay" );
		random.MaxDelay = entity.GetFloat( "max_delay" );
		var count = System.Math.Clamp( entity.GetInt( "target_count", 4 ), 0, 16 );
		for ( int i = 1; i <= count; i++ ) random.Targets.Add( entity.GetString( $"target{i}" ) );
		return random;
	}
}
