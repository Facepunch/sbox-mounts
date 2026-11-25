using System;

namespace Halo2;

public sealed class ShotgunContacts
{
	public CuePlayback[] Cues { get; set; }
	public Dictionary<string, int> Pellets { get; set; }
	public Dictionary<string, int> Casings { get; set; }
	public Dictionary<string, string> Effects { get; set; }

	uint seed = (uint)Random.Shared.Next();

	internal static ShotgunContacts For( Shotgun weapon )
	{
		if ( weapon.IsValid() && weapon.Contacts is { Effects: not null } contacts ) return contacts;
		// A pellet can outlive its weapon. Older scene instances can also lack the authored bank.
		return PrefabFile.Load( $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.PrefabPath}" )?.GetScene().GetComponent<Shotgun>()?.Contacts;
	}

	CuePlayback Cue( int index ) => index > 0 && index <= Cues.Length ? Cues[index - 1] : null;

	internal bool PlayPellet( Surface surface, Vector3 position )
	{
		if ( Pellets.Count == 0 ) return false;
		// Some authored materials have a visual effect and a silent impact.
		Cue( ContactMaterial.Lookup( Pellets, surface, false ) )?.Play( position, ref seed, null );
		return true;
	}

	internal SoundHandle PlayCasing( Surface surface, Vector3 position, float gain )
	{
		if ( gain <= 0 ) return null;
		var handle = Cue( ContactMaterial.Lookup( Casings, surface, true ) )?.Play( position, ref seed, null );
		if ( handle.IsValid() ) handle.Volume *= gain;
		return handle;
	}

}
