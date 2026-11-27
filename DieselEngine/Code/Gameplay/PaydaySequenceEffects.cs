namespace Diesel;

static class PaydaySequenceEffects
{
	internal static void Play( PaydayUnitState unit, string sequence )
	{
		if ( Sandbox.Mounting.Directory.Get( "payday" ) is not PaydayMount mount
			|| mount.GetSequenceManager( unit.Unit ) is not { } manager ) return;
		foreach ( var action in SequenceEffects.Read( manager.Root, sequence ) )
		{
			try
			{
				if ( mount.EffectAnchor( unit.Unit, action.Anchor ) is not { } anchor ) continue;
				var transform = unit.WorldTransform.ToWorld( anchor.ToWorld( new Transform( action.Offset ) ) );
				if ( action.Kind == "effect" )
				{
					if ( Application.IsDedicatedServer ) continue;
					var go = new GameObject( unit.Scene, false, action.Path.Split( '/' ).Last() ) { NetworkMode = NetworkMode.Never };
					go.WorldTransform = transform;
					var playback = go.AddComponent<PaydayEffect>();
					playback.EffectPath = action.Path;
					if ( mount.GetEffect( action.Path ).IsLooping )
					{
						playback.Source = unit.GameObject;
						playback.SourceTransform = unit.WorldTransform.ToLocal( transform );
					}
					go.Enabled = true;
				}
				else if ( action.Kind == "spawn_unit" )
					PaydayDebris.Spawn( mount, unit.Scene, action.Path, transform );
			}
			catch ( Exception e ) { Log.Warning( $"PAYDAY sequence effect {unit.Unit}/{sequence}: {e.Message}" ); }
		}
	}
}
