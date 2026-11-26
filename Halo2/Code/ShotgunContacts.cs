namespace Halo2;

public sealed class ShotgunContacts : CasingSounds
{
	public Dictionary<string, int> Pellets { get; set; }
	public Dictionary<string, string> Effects { get; set; }

	internal static ShotgunContacts For( Shotgun weapon )
	{
		if ( weapon.IsValid() && weapon.Contacts is { Effects: not null } contacts ) return contacts;
		// A pellet can outlive its weapon. Older scene instances can also lack the authored bank.
		return GameObject.GetPrefab( $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.PrefabPath}" )?.GetComponent<Shotgun>()?.Contacts;
	}

	internal bool PlayPellet( Scene scene, Surface surface, Vector3 position )
	{
		if ( Pellets.Count == 0 ) return false;
		// Some authored materials have a visual effect and a silent impact.
		Cue( ContactMaterial.Lookup( Pellets, surface, false ) )?.Play( scene, position );
		return true;
	}
}
