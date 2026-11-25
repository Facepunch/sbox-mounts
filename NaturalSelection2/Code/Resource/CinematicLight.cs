using System;

namespace NS2;

internal sealed class CinematicLight( CinematicNode node, float duration )
{
	readonly CinematicChannel x = new( node.Get( "Origin" ), 2 ), y = new( node.Get( "Origin" ) ), z = new( node.Get( "Origin" ), 1 );
	readonly CinematicChannel red = new( node.Get( "Color" ) ), green = new( node.Get( "Color" ), 1 ), blue = new( node.Get( "Color" ), 2 );
	readonly CinematicChannel intensity = new( node.Get( "Intensity" ) ), radius = new( node.Get( "Radius" ) );

	internal void Play( Vector3 position, Rotation facing )
	{
		var go = new GameObject( false, node.Get( "Name" ).AsString() ) { NetworkMode = NetworkMode.Never };
		var light = go.AddComponent<PointLight>();
		var playback = go.AddComponent<CinematicLightPlayback>();
		playback.Sample = time =>
		{
			if ( time >= duration ) { go.Destroy(); return; }
			float range = radius.At( time ) * MapLoader.Scale;
			float power = intensity.At( time );
			light.Enabled = power > 0 && range > 0 && node.Get( "Visible" ).IsVisibleAt( time );
			if ( !light.Enabled ) return;
			light.WorldPosition = position + facing * (new Vector3( x.At( time ), y.At( time ), z.At( time ) ) * MapLoader.Scale);
			light.LightColor = (new Color( red.At( time ), green.At( time ), blue.At( time ) ) * (power * MapLoader.FalloffGain)).WithAlpha( 1 );
			light.Radius = range;
			light.Attenuation = MapLoader.AttenuationFor( range );
			light.Shadows = node.Get( "Casts Shadows" ).IsVisibleAt( time );
		};
		playback.Sample( 0 );
		go.Enabled = true;
	}
}

internal sealed class CinematicLightPlayback : Component
{
	internal Action<float> Sample;
	TimeSince started = 0;
	protected override void OnUpdate() => Sample( started );
}
