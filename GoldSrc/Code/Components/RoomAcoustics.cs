using Sandbox.Audio;

namespace GoldSrc.Maps;

public sealed class SoundRoom : MapEntity
{
	[Property] public int RoomType { get; set; }
	[Property] public float Radius { get; set; }
}

public sealed class RoomAcoustics : Component
{
	private TimeUntil _next;
	private int _room = -1;
	private RoomDsp _processor;
	private Mixer _mixer;

	protected override void OnUpdate()
	{
		if ( _next > 0 || !Scene.Camera.IsValid() ) return;
		_next = 0.25f;
		var ear = Scene.Camera.WorldPosition;
		SoundRoom nearest = null;
		float best = float.MaxValue;
		foreach ( var room in GetComponent<MapState>().Entities.OfType<SoundRoom>() )
		{
			if ( !room.IsValid() ) continue;
			float distance = ear.DistanceSquared( room.WorldPosition );
			if ( distance > room.Radius * room.Radius || distance >= best ) continue;
			if ( Scene.Trace.Ray( ear, room.WorldPosition ).WithAnyTags( "world", "water" ).Run().Hit ) continue;
			nearest = room;
			best = distance;
		}

		// GoldSrc keeps the last room while the listener is outside every audible radius.
		if ( nearest is null || nearest.RoomType == _room ) return;
		RemoveProcessor();
		_room = nearest.RoomType;
		if ( _room <= 0 || _room >= RoomDsp.Presets.Length ) return;
		_mixer = Mixer.Default;
		_processor = new RoomDsp( RoomDsp.Presets[_room] );
		_mixer?.AddProcessor( _processor );
	}

	private void RemoveProcessor()
	{
		if ( _processor is not null ) _mixer?.RemoveProcessor( _processor );
		_processor = null;
		_room = -1;
	}

	protected override void OnDisabled() => RemoveProcessor();
}
