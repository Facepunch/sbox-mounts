using Diesel;

static class ArsenalVerification
{
	public static void Run( BundleDatabase db )
	{
		var banks = new Dictionary<string, WwiseBank>();
		foreach ( var w in WeaponDefinition.All )
		{
			try { banks[w.Bank] = new WwiseBank( db.Find( "bnk", $"soundbanks/weapon_{w.Bank}" ), id => db.Find( "stream", $"soundbanks/streamed/weapon_{w.Bank}/{id}" ) ); }
			catch ( Exception e ) { throw new Exception( $"Bank {w.Bank}: {e.Message}", e ); }
		}
		int recordings = 0;
		foreach ( var w in WeaponDefinition.All )
		{
			try
			{
				foreach ( var unit in new[] { w.ViewUnit, w.WorldUnit } )
				{
					if ( db.Find( "unit", unit ) is null ) throw new Exception( $"Missing unit {unit}" );
					var model = new ModelFile( db.Find( "model", unit ) );
					var text = System.Text.Encoding.UTF8.GetString( db.Find( "object", unit ) ).TrimEnd( '\0', '\r', '\n', ' ' );
					if ( text.EndsWith( "</dynamic_object" ) ) text += ">";
					var obj = XmlNode.Parse( text );
					foreach ( var group in obj.Element( "animations" )?.Elements( "animation_group" ) ?? [] )
						_ = model.Controllers( group.Elements( "object" ).Where( n => n.Attribute( "enabled" ) == "true" ).Select( n => IdString.Hash( n.Attribute( "name" ) ) ).ToHashSet() );
					if ( model.ReadAttachment( IdString.Hash( "fire" ), IdString.Hash( obj.Element( "diesel" ).Attribute( "orientation_object" ) ) ) is null ) throw new Exception( "Missing muzzle." );
				}
				foreach ( var clip in w.Clips ) _ = new AnimationFile( db.Find( "animation", w.ClipPath( clip ) ) );
				foreach ( var clip in new[] { "sprint_enter", "sprint", "sprint_exit" } ) _ = new AnimationFile( db.Find( "animation", w.ClipPath( clip ) ) );
				var audio = new WeaponAudio( db, w, banks );
				if ( w.Id is "m308" or "bronco44" )
				{
					var bank = banks[w.Bank];
					float shortest = bank.Resolve( audio.Fire ).Select( id => bank.Decode( id ) )
						.Min( wav => (wav.Length - 44f) / BitConverter.ToInt32( wav, 28 ) );
					if ( bank.Render( audio.Fire ).Any( wav => (wav.Length - 44f) / BitConverter.ToInt32( wav, 28 ) <= shortest + .1f ) )
						throw new Exception( "Attack/tail sequence collapsed into a standalone fragment." );
				}
				foreach ( var (name, bank) in audio.Events )
				{
					try { foreach ( var wav in bank.Render( name ) ) if ( wav.Length < 48 ) throw new Exception( "Empty playback." ); }
					catch ( Exception e ) { throw new Exception( $"Playback {name}: {e.Message}", e ); }
					foreach ( var id in bank.Resolve( name ) )
					{
						try { _ = bank.Decode( id ); recordings++; }
						catch ( Exception e ) { throw new Exception( $"Audio {name}/{id}: {e.Message}", e ); }
					}
				}
				Console.WriteLine( $"PASS: {w.Title}: models, {w.Clips.Count()} clips, {audio.Events.Count} sound events." );
			}
			catch ( Exception e ) { throw new Exception( $"{w.Title}: {e.Message}", e ); }
		}
		Console.WriteLine( $"PASS: full 13-weapon arsenal, {recordings} decoded recordings." );
	}
}
