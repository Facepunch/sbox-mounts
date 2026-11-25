namespace NS2;

internal sealed class CinematicActor( CinematicNode node, float duration )
{
	// Cinematic channels are in Spark axes (Z forward, X left, Y up).
	readonly Channels origin = new( node.Get( "Origin" ) ), angles = new( node.Get( "Angles" ) ), scale = new( node.Get( "Scale" ) );
	readonly Model model = Model.Load( $"mount://ns2/ns2/{node.Get( "Model" ).AsString()}.vmdl" );

	readonly struct Channels( CinematicValue value )
	{
		readonly CinematicChannel x = new( value, 2 ), y = new( value ), z = new( value, 1 );
		internal Vector3 At( float time ) => new( x.At( time ), y.At( time ), z.At( time ) );
	}

	internal void Play( Vector3 position, Rotation facing, GameObject parent )
	{
		if ( model is null || model.IsError ) return;
		var actor = new GameObject( false, node.Get( "Name" ).AsString() ) { NetworkMode = NetworkMode.Never, Parent = parent };
		var basis = new Transform( position, facing );
		if ( parent.IsValid() ) basis = parent.WorldTransform.ToLocal( basis );
		var render = actor.AddComponent<ModelRenderer>();
		render.Model = model;
		var playback = actor.AddComponent<CinematicPlayback>();
		playback.Sample = time =>
		{
			if ( time >= duration ) { actor.Destroy(); return; }
			render.Enabled = node.Get( "Visible" ).IsVisibleAt( time );
			var a = angles.At( time );
			var r = Rotation.From( a.z.RadianToDegree(), a.x.RadianToDegree(), a.y.RadianToDegree() );
			actor.LocalTransform = basis.ToWorld( new Transform( origin.At( time ) * MapLoader.Scale, r, scale.At( time ) ) );
		};
		playback.Sample( 0 );
		actor.Enabled = true;
	}
}
