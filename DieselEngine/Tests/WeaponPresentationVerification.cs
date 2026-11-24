using Diesel;
using System.Numerics;

static class WeaponPresentationVerification
{
	public static void Run( BundleDatabase db )
	{
		var clips = new Dictionary<string, AnimationFile>();
		foreach ( var name in new[] { "idle", "equip", "recoil", "reload", "reload_not_empty" } )
			clips.Add( name, new( db.Find( "animation", $"anims/fps/m4/m4_{name}" ) ) );
		var badClip = db.Find( "animation", "anims/fps/m4/m4_idle" );
		badClip[^8] ^= 1;
		bool badClipRejected = false;
		try { _ = new AnimationFile( badClip ); } catch ( InvalidDataException ) { badClipRejected = true; }
		if ( !badClipRejected ) throw new Exception( "Animation checksum corruption accepted." );
		if ( clips["reload"].Cues.Select( c => c.Name ).SequenceEqual( new[] { "start", "mag_out", "mag_grab", "mag_in", "cock" } ) != true
			|| clips["reload_not_empty"].Cues.Any( c => c.Name == "cock" ) ) throw new Exception( "Original reload cue mapping changed." );
		var hands = new ModelFile( db.Find( "model", "units/characters/fps/suit/fps_hand" ) );
		var geometry = hands.ReadGeometry( [IdString.Hash( "g_fps_hand_dallas" )], 0, raw: true ).Single();
		if ( geometry.Skin is null || geometry.Skin.Bones.Length != 44 ) throw new Exception( "Dallas hand skin missing." );
		var positions = new global::Vector3[geometry.Positions.Length];
		var normals = new global::Vector3[positions.Length];
		global::Vector3[] idle = null;
		foreach ( var (name, clip) in clips )
		{
			for ( int frame = 0; frame <= 10; frame++ )
			{
				var pose = hands.Pose( clip, clip.Duration * frame / 10 );
				Matrix4x4.Invert( hands.NodePose( pose, "rp_fps_hands" ), out var root );
				hands.Deform( geometry, pose, root, positions, normals );
				if ( positions.Any( p => !float.IsFinite( p.LengthSquared ) || p.Length > 100 ) ||
					normals.Any( n => Math.Abs( n.Length - 1 ) > 0.01 ) ) throw new Exception( $"Invalid deformed {name} pose." );
				if ( name == "idle" && frame == 0 ) idle = positions.ToArray();
				if ( name == "reload" && frame == 5 && positions.Zip( idle ).Max( pair => (pair.First - pair.Second).Length ) < 5 )
					throw new Exception( "Reload did not move skinned hand vertices." );
			}
		}
		var bank = new WwiseBank( db.Find( "bnk", "soundbanks/weapon_amcar4" ) );
		if ( WwiseBank.Hash( "m4_fire" ) != 0x3610268d || bank.Resolve( "m4_fire" ).Length != 4 )
			throw new Exception( "AMCAR event graph mapping failed." );
		int samples = 0;
		foreach ( var name in new[] { "m4_fire", "m4_dryfire", "m4_equip", "m4_reload_mag_out", "m4_reload_mag_grab", "m4_reload_mag_in", "m4_reload_cock", "m4_stop" } )
			foreach ( uint id in bank.Resolve( name ) )
			{
				var wav = bank.Decode( id );
				int size = BitConverter.ToInt32( wav, 40 );
				if ( BitConverter.ToInt16( wav, 20 ) != 1 || size != wav.Length - 44 || size < 1000 ) throw new Exception( "Invalid decoded WAV." );
				long energy = 0;
				for ( int i = 44; i < wav.Length; i += 2 ) { int sample = BitConverter.ToInt16( wav, i ); energy += (long)sample * sample; }
				if ( energy == 0 ) throw new Exception( "Decoded sound is silent." );
				samples++;
			}
		// Independent nibble expansion vector: predictor 100, step 0; +7,+7,-7.
		var riff = new byte[80];
		using ( var s = new MemoryStream( riff ) )
		using ( var w = new BinaryWriter( s ) )
		{
			w.Write( 0x46464952 ); w.Write( 72 ); w.Write( 0x45564157 );
			w.Write( 0x20746d66 ); w.Write( 16 ); w.Write( (ushort)2 ); w.Write( (ushort)1 );
			w.Write( 44100 ); w.Write( 24806 ); w.Write( (ushort)36 ); w.Write( (ushort)4 );
			w.Write( 0x61746164 ); w.Write( 36 ); w.Write( (short)100 ); w.Write( (ushort)0 ); w.Write( (byte)0x77 ); w.Write( (byte)0x0f );
		}
		var decoded = WwiseAdpcm.Decode( riff );
		if ( !Enumerable.Range( 0, 4 ).Select( i => BitConverter.ToInt16( decoded, 44 + i * 2 ) ).SequenceEqual( new short[] { 100, 113, 143, 80 } ) )
			throw new Exception( "Wwise IMA arithmetic/header sample regression." );
		bool rejected = false;
		try { WwiseAdpcm.Decode( riff[..60] ); } catch ( InvalidDataException ) { rejected = true; }
		if ( !rejected ) throw new Exception( "Truncated ADPCM accepted." );
		Console.WriteLine( $"PASS: five original AMCAR clips, 44 hand skin bones, 55 sampled poses, reload cues and {samples} original audio recordings." );
	}
}
