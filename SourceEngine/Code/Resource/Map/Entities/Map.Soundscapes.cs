using System;

namespace Source1;

partial class MapLoader
{
	private sealed class SoundscapeEntityLoader : EntityLoader
	{
		public override bool SupportsSkybox => false;
		public override void Load( EntityBuilder builder, MapEntity entity ) => builder.BuildSoundscape( entity );
	}

	private sealed partial class EntityBuilder
	{
		private GameObject soundscapeParent;
		Dictionary<string, string> triggeredSoundscapes;

		public void BuildSoundscapeVolume( MapEntity entity, SoundscapeVolume volume, BspModel model )
		{
			if ( triggeredSoundscapes is null )
			{
				triggeredSoundscapes = new( StringComparer.OrdinalIgnoreCase );
				foreach ( var point in bsp.Entities )
					if ( point.Get( "classname" ) == "env_soundscape_triggerable" && point.Get( "targetname" ) is { Length: > 0 } target )
						triggeredSoundscapes.TryAdd( target, point.Get( "soundscape" ) );
			}
			if ( !triggeredSoundscapes.TryGetValue( entity.Get( "soundscape", "" ), out var name ) || string.IsNullOrWhiteSpace( name ) ) return;
			try
			{
				var soundscape = owner.Host.GetSoundscape( name, owner.ContentRoot );
				var center = new GameObject( volume.GameObject, true, name );
				center.LocalPosition = (model.Mins + model.Maxs) * 0.5f;
				var playback = center.AddComponent<SoundscapeTrigger>();
				playback.Soundscape = soundscape;
				playback.Type = SoundscapeTrigger.TriggerType.Box;
				playback.BoxSize = (model.Maxs - model.Mins) * 0.5f;
				playback.StayActiveOnExit = true;
				volume.Playback = playback;
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
			{
				Log.Trace( $"Soundscape '{name}' was not imported: {e.Message}" );
			}
		}

		public void BuildSoundscape( MapEntity entity )
		{
			try
			{
				var name = entity.Get( "soundscape" );
				if ( string.IsNullOrWhiteSpace( name ) )
					throw new InvalidDataException( "env_soundscape requires a soundscape name." );
				var soundscape = owner.Host.GetSoundscape( name, owner.ContentRoot );
				var radius = entity.Number( "radius" );
				if ( radius < 0 && radius != -1 )
					throw new InvalidDataException( $"Soundscape '{name}' has invalid radius {radius}." );
				soundscapeParent ??= new GameObject( true, "soundscapes" )
				{
					Parent = World
				};
				var instance = new GameObject( true, entity.Get( "targetname" ) ?? name )
				{
					Parent = soundscapeParent,
					LocalPosition = entity.Vector( "origin" )
				};
				var trigger = instance.AddComponent<SoundscapeTrigger>( false );
				trigger.Soundscape = soundscape;
				trigger.Type = radius == -1 ? SoundscapeTrigger.TriggerType.Point : SoundscapeTrigger.TriggerType.Sphere;
				trigger.Radius = radius;
				trigger.StayActiveOnExit = true;
				trigger.Enabled = entity.Integer( "StartDisabled" ) == 0;
				Log.Trace( "Soundscapes use native selection without Source brush/water visibility traces, native fades and random scheduling rather than Source sine fades and half-interval first stings. DSP, fixed sound positions and entity inputs are not reproduced." );
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
			{
				Log.Trace( $"Soundscape '{entity.Get( "soundscape" )}' was not imported: {e.Message}" );
			}
		}
	}
}
