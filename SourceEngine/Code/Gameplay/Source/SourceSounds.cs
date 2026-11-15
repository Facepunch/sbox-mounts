using System;

namespace Source1;

/// <summary>Mounted sound scripts and the sounds a weapon script names by role.</summary>
static class SourceSounds
{
	public static SoundEvent Sound( string name, string mountName )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return null;
		var path = $"mount://{mountName}/sounds/events/{Uri.EscapeDataString( name.ToLowerInvariant() )}.sound";
		return Sandbox.Mounting.Directory.Get( mountName )?.GetByPath( path ) is not null ? GameResource.Load<SoundEvent>( path ) : null;
	}

	static readonly Dictionary<string, HashSet<string>> Events = new();

	/// <summary>The names of the sound scripts a mount has, lower case; empty until it is mounted.</summary>
	public static IReadOnlyCollection<string> EventNames( string mountName )
	{
		if ( Events.TryGetValue( mountName ?? "", out var names ) )
			return names;

		var mount = Sandbox.Mounting.Directory.Get( mountName ?? "" );
		if ( mount?.IsMounted != true )
			return [];

		const string folder = "/sounds/events/";
		names = mount.Resources.Select( x => x.Path ).Where( x => x.Contains( folder ) && x.EndsWith( ".sound" ) )
			.Select( x => Uri.UnescapeDataString( x[(x.IndexOf( folder ) + folder.Length)..^".sound".Length] ) ).ToHashSet();
		Events[mountName] = names;
		return names;
	}

	public static bool IsSoundEvent( SourceModelSequenceData.Event e ) => e.Name is "AE_CL_PLAYSOUND" or "AE_SV_PLAYSOUND"
		or "AE_SCRIPT_EVENT_SOUND" or "AE_SCRIPT_EVENT_SOUND_VOICE"
		|| e.Id is SourceAnimEvents.ScriptSound or SourceAnimEvents.ScriptSoundVoice or SourceAnimEvents.ClientSound;

	public static SoundEvent WeaponSound( BaseCombatWeapon weapon, string role ) => WeaponSound( SourceWeaponSetup.WeaponData( weapon ), role );

	/// <summary>Counter-Strike scripts name the silenced shot special1.</summary>
	public static string FireSoundRole( BaseCombatWeapon weapon ) => weapon is SourceBulletWeapon { Silenced: true } ? "special1" : "single_shot";

	public static SoundEvent WeaponSound( SourceModelWeaponData data, string role ) => data?.Sounds.TryGetValue( role, out var path ) == true
		&& Sandbox.Mounting.Directory.Get( SourceMount.Of( path ) )?.GetByPath( path ) is not null ? GameResource.Load<SoundEvent>( path ) : null;

	/// <summary>
	/// The script's reload sound, which Source's DefaultReload plays for everyone. The owner's
	/// viewmodel skips it when the played clip authors its own reload sounds.
	/// </summary>
	public static void PlayReloadSound( BaseCombatWeapon weapon, GameObject source )
	{
		if ( !Application.IsDedicatedServer && source.IsValid() && WeaponSound( weapon, "reload" ) is { } sound ) source.PlaySound( sound );
	}
}
