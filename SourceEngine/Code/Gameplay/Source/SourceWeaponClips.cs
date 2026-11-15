using System;

namespace Source1;

/// <summary>
/// The clips a mounted viewmodel plays, from its activity table with the prefab's clip names as the
/// fallback for untagged models. Shared by the viewmodel and the weapon's gameplay timing.
/// </summary>
sealed record SourceWeaponClips
{
	public string Idle { get; init; }
	public string Draw { get; init; }
	public string Fire { get; init; }
	public string DryFire { get; init; }
	public string SecondaryFire { get; init; }
	public string SecondaryWindup { get; init; }
	public string[] Recoil { get; init; } = [];
	public string Reload { get; init; }
	public string ReloadInsert { get; init; }
	public string ReloadEnd { get; init; }
	public string Pump { get; init; }
	public string ThrowWindup { get; init; }
	/// <summary>An ACT_VM_DRYFIRE that fires, Counter-Strike's last round, rather than an empty click.</summary>
	public string LastShot { get; init; }
	/// <summary>A secondary clip authoring three or more shots, Counter-Strike's burst fire.</summary>
	public string Burst { get; init; }
	/// <summary>Primary and secondary clips flash different muzzles, like Counter-Strike's dual Elites.</summary>
	public bool Alternates { get; init; }
	public string AttachSilencer { get; init; }
	public string DetachSilencer { get; init; }
	/// <summary>One shell per insert, inferred from ACT_SHOTGUN_RELOAD_START.</summary>
	public bool Incremental { get; init; }
	/// <summary>An ACT_VM_THROW clip with a throw event; <see cref="Fire"/> is then the release.</summary>
	public bool Throws { get; init; }
	public float ReleaseDelay { get; init; }
	public float ThrowDuration { get; init; }

	public static float Duration( Model model, string clip ) => model.AnimationClips().TryGetValue( clip ?? "", out var animation ) ? animation.Duration : 0;

	/// <summary>A model with silencer clips resolves a second set preferring the *_SILENCED activities.</summary>
	public static SourceWeaponClips Resolve( Model model, SourceViewModel names, bool silenced = false )
	{
		var lookup = SourceModelSequences.Of( model );
		var sequences = lookup.All;
		string Find( string activity, string fallback ) => (silenced ? lookup.FindActivity( activity + "_SILENCED" ) : null)
			?? lookup.FindActivity( activity, fallback );
		SourceModelSequenceData.Event[] Muzzles( string clip ) => sequences.FirstOrDefault( s => s.Name == clip )?.Events.Where( SourceAnimEvents.IsMuzzleFlash ).ToArray() ?? [];
		string MuzzleOf( string clip ) => Muzzles( clip ).Select( e => $"{e.Id} {e.Options}" ).FirstOrDefault();
		var incremental = names.IsIncremental || Find( "ACT_SHOTGUN_RELOAD_START", null ) is not null;
		var fire = Find( "ACT_VM_PRIMARYATTACK", names.Fire );
		var draw = Find( "ACT_VM_DRAW", names.Draw );
		var release = sequences.FirstOrDefault( s => s.Activity == "ACT_VM_THROW" && model.AnimationClips().ContainsKey( s.Name ) );
		var launch = release?.Events.FirstOrDefault( e => e.Id is SourceAnimEvents.WeaponThrow or SourceAnimEvents.WeaponThrow2 or SourceAnimEvents.WeaponThrow3
			|| e.Name == "AE_WPN_PRIMARYATTACK" );
		var throws = launch is not null;
		var windup = !throws ? null
			: sequences.FirstOrDefault( s => s.Activity == "ACT_VM_PULLBACK_HIGH" && model.AnimationClips().ContainsKey( s.Name ) )
			?? sequences.FirstOrDefault( s => s.Activity == "ACT_VM_HAULBACK" && s.Events.Any( e => e.Id == SourceAnimEvents.WeaponSequenceFinished )
				&& model.AnimationClips().ContainsKey( s.Name ) );
		var preparation = Duration( model, windup?.Name );
		var throwDuration = throws ? Duration( model, release.Name ) : 0;
		var dryFire = Find( "ACT_VM_DRYFIRE", names.DryFire );
		var lastShot = Muzzles( dryFire ).Length > 0 ? dryFire : null;
		var secondaryFire = Find( "ACT_VM_SECONDARYATTACK", names.SecondaryFire );
		var burst = Muzzles( secondaryFire ).Length >= 3 ? secondaryFire : null;
		return new SourceWeaponClips
		{
			Idle = Find( "ACT_VM_IDLE", names.Idle ),
			Draw = draw,
			Fire = throws ? release.Name : fire,
			DryFire = lastShot is null ? dryFire : null,
			LastShot = lastShot,
			SecondaryFire = secondaryFire,
			Burst = burst,
			Alternates = burst is null && MuzzleOf( fire ) is { } primary && MuzzleOf( secondaryFire ) is { } other && primary != other,
			AttachSilencer = Find( "ACT_VM_ATTACH_SILENCER", null ),
			DetachSilencer = Find( "ACT_VM_DETACH_SILENCER", null ),
			SecondaryWindup = Find( "ACT_VM_FIDGET", null ),
			Recoil = [.. Enumerable.Range( 1, 3 ).Select( i => Find( $"ACT_VM_RECOIL{i}", fire ) )],
			Reload = throws ? draw : Find( incremental ? "ACT_SHOTGUN_RELOAD_START" : "ACT_VM_RELOAD", names.Reload ),
			ReloadInsert = incremental ? Find( "ACT_VM_RELOAD", names.ReloadInsert ) : names.ReloadInsert,
			ReloadEnd = Find( "ACT_SHOTGUN_RELOAD_FINISH", names.ReloadEnd ),
			Pump = Find( "ACT_SHOTGUN_PUMP", names.Pump ),
			ThrowWindup = windup?.Name,
			Incremental = incremental,
			Throws = throws,
			ReleaseDelay = throws ? preparation + Math.Clamp( launch.Cycle, 0, 1 ) * throwDuration : 0,
			ThrowDuration = preparation + throwDuration
		};
	}
}
