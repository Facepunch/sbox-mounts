using System;

namespace Source1;

/// <summary>Mounted sound scripts and the sounds a weapon script names by role.</summary>
static class Sounds
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
		|| e.Id is AnimEvents.ScriptSound or AnimEvents.ScriptSoundVoice or AnimEvents.ClientSound;

	public static SoundEvent WeaponSound( BaseCombatWeapon weapon, string role ) => WeaponSound( WeaponSetup.WeaponData( weapon ), role );

	public static void PlayWeaponSound( BaseCombatWeapon weapon, string role )
	{
		if ( !Application.IsDedicatedServer && WeaponSound( weapon, role ) is { } sound )
			PlayWeaponSound( weapon, sound );
	}

	public static SoundHandle PlayWeaponSound( BaseCombatWeapon weapon, SoundEvent sound ) =>
		(weapon.Holder?.GameObject ?? weapon.GameObject).PlaySound( sound );

	/// <summary>Play the same mounted wave, volume and pitch for every listener of a networked weapon event.</summary>
	public static void PlayWeaponSound( BaseCombatWeapon weapon, string role, int seed )
	{
		if ( Application.IsDedicatedServer || WeaponSound( weapon, role ) is not { Sounds.Count: > 0 } sound ) return;
		var random = new Random( seed );
		var wave = sound.Sounds[random.Next( sound.Sounds.Count )];
		var handle = Sandbox.Sound.PlayFile( wave, random.Float( sound.Volume.Min, sound.Volume.Max ), random.Float( sound.Pitch.Min, sound.Pitch.Max ) );
		if ( !handle.IsValid() ) return;

		handle.Parent = weapon.Holder?.GameObject ?? weapon.GameObject;
		handle.FollowParent = true;
		handle.LocalTransform = Transform.Zero;
		handle.TargetMixer = sound.DefaultMixer.Get();
		handle.Distance = sound.Distance;
		handle.Falloff = sound.Falloff;
		handle.FalloffMode = sound.FalloffMode;
		handle.MinDistance = sound.MinDistance;
		handle.DistanceAttenuation = sound.DistanceAttenuation;
		handle.AirAbsorption = sound.AirAbsorption;
		handle.OcclusionEnabled = sound.OcclusionEnabled;
		handle.ReverbEnabled = sound.ReverbEnabled;
	}

	/// <summary>Counter-Strike scripts name the silenced shot special1.</summary>
	public static string FireSoundRole( BaseCombatWeapon weapon ) => weapon is BulletWeapon { Silenced: true } ? "special1" : "single_shot";

	public static SoundEvent WeaponSound( SourceModelWeaponData data, string role ) => data?.Sounds.TryGetValue( role, out var path ) == true
		&& Sandbox.Mounting.Directory.Get( MountContent.Of( path ) )?.GetByPath( path ) is not null ? GameResource.Load<SoundEvent>( path ) : null;
}
