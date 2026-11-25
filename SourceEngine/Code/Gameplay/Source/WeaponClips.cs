using System;

namespace Source1;

/// <summary>
/// The clips a mounted viewmodel plays, from its activity table with the prefab's clip names as the
/// fallback for untagged models. Shared by the viewmodel and the weapon's gameplay timing.
/// </summary>
sealed record WeaponClips
{
	public string Idle { get; init; }
	public string Draw { get; init; }
	public string EmptyIdle { get; init; }
	public string LoweredIdle { get; init; }
	public string Lower { get; init; }
	public string Raise { get; init; }
	public string EmptyDraw { get; init; }
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
	public float? WindupDuration { get; init; }
	public string LowWindup { get; init; }
	public float? LowWindupDuration { get; init; }
	public readonly record struct Release( string Clip, float Delay, float Duration );
	public Release PrimaryThrow { get; init; }
	public Release Lob { get; init; }
	public Release Roll { get; init; }

	public static float Duration( Model model, string clip ) => model.AnimationClips().TryGetValue( clip ?? "", out var animation ) ? animation.Duration : 0;

	/// <summary>A model with silencer clips resolves a second set preferring the *_SILENCED activities.</summary>
	public static WeaponClips Resolve( Model model, ViewModel names, bool silenced = false )
	{
		var lookup = ModelSequences.Of( model );
		var sequences = lookup.All;
		string Find( string activity, string fallback ) => (silenced ? lookup.FindActivity( activity + "_SILENCED" ) : null)
			?? lookup.FindActivity( activity, fallback );
		SourceModelSequenceData.Event[] Muzzles( string clip ) => sequences.FirstOrDefault( s => s.Name == clip )?.Events.Where( AnimEvents.IsMuzzleFlash ).ToArray() ?? [];
		string MuzzleOf( string clip ) => Muzzles( clip ).Select( e => $"{e.Id} {e.Options}" ).FirstOrDefault();
		var incremental = names.IsIncremental || Find( "ACT_SHOTGUN_RELOAD_START", null ) is not null;
		var fire = Find( "ACT_VM_PRIMARYATTACK", names.Fire );
		var draw = Find( "ACT_VM_DRAW", names.Draw );
		var release = sequences.FirstOrDefault( s => s.Activity == "ACT_VM_THROW" && model.AnimationClips().ContainsKey( s.Name ) );
		var launch = release?.Events.FirstOrDefault( e => e.Id is AnimEvents.WeaponThrow or AnimEvents.WeaponThrow2 or AnimEvents.WeaponThrow3
			|| e.Name == "AE_WPN_PRIMARYATTACK" );
		var throws = launch is not null;
		var windup = !throws ? null
			: sequences.FirstOrDefault( s => s.Activity == "ACT_VM_PULLBACK_HIGH" && model.AnimationClips().ContainsKey( s.Name ) )
			?? sequences.FirstOrDefault( s => s.Activity == "ACT_VM_HAULBACK" && s.Events.Any( e => e.Id == AnimEvents.WeaponSequenceFinished )
				&& model.AnimationClips().ContainsKey( s.Name ) );
		float? WindupTime( SourceModelSequenceData sequence )
		{
			if ( sequence is null ) return null;
			var duration = Duration( model, sequence.Name );
			if ( sequence.Events.FirstOrDefault( e => e.Id == AnimEvents.WeaponSequenceFinished ) is not { } ready ) return duration;
			// Source dispatches remaining events at the start of the fade-out.
			var end = sequence.Looping ? duration : MathF.Max( 0, duration - sequence.FadeOut );
			return Math.Clamp( duration * ready.Cycle, 0, end );
		}
		Release ReadRelease( SourceModelSequenceData sequence, SourceModelSequenceData.Event e )
		{
			if ( e is null ) return default;
			var duration = Duration( model, sequence.Name );
			return new( sequence.Name, Math.Clamp( e.Cycle, 0, 1 ) * duration, duration );
		}
		Release AlternateRelease( string activity, int id )
		{
			var sequence = lookup.Get( Find( activity, null ) );
			return ReadRelease( sequence, sequence?.Events.FirstOrDefault( e => e.Id == id ) );
		}
		var lowWindup = throws ? lookup.Get( Find( "ACT_VM_PULLBACK_LOW", null ) ) : null;
		var dryFire = Find( "ACT_VM_DRYFIRE", names.DryFire );
		var lastShot = Muzzles( dryFire ).Length > 0 ? dryFire : null;
		var secondaryFire = Find( "ACT_VM_SECONDARYATTACK", names.SecondaryFire );
		var burst = Muzzles( secondaryFire ).Length >= 3 ? secondaryFire : null;
		return new WeaponClips
		{
			Idle = Find( "ACT_VM_IDLE", names.Idle ),
			LoweredIdle = Find( "ACT_VM_IDLE_LOWERED", null ),
			Lower = Find( "ACT_VM_IDLE_TO_LOWERED", null ),
			Raise = Find( "ACT_VM_LOWERED_TO_IDLE", null ),
			Draw = draw,
			EmptyDraw = Find( "ACT_VM_DRAW_EMPTY", null ),
			EmptyIdle = Find( "ACT_VM_IDLE_EMPTY", null ) ?? (lookup.Get( "idle_empty" ) is { } empty ? empty.Name : null),
			Fire = throws ? release.Name : fire,
			DryFire = lastShot is null ? dryFire : null,
			LastShot = lastShot,
			SecondaryFire = secondaryFire,
			Burst = burst,
			Alternates = burst is null && MuzzleOf( fire ) is { } primary && MuzzleOf( secondaryFire ) is { } other && primary != other,
			AttachSilencer = Find( "ACT_VM_ATTACH_SILENCER", null ),
			DetachSilencer = Find( "ACT_VM_DETACH_SILENCER", null ),
			SecondaryWindup = Find( "ACT_VM_FIDGET", null ),
			Recoil = [.. (names.RecoilStages ?? []).Select( i => Find( $"ACT_VM_RECOIL{i}", fire ) )],
			Reload = throws ? draw : Find( incremental ? "ACT_SHOTGUN_RELOAD_START" : "ACT_VM_RELOAD", names.Reload ),
			ReloadInsert = incremental ? Find( "ACT_VM_RELOAD", names.ReloadInsert ) : names.ReloadInsert,
			ReloadEnd = Find( "ACT_SHOTGUN_RELOAD_FINISH", names.ReloadEnd ),
			Pump = Find( "ACT_SHOTGUN_PUMP", names.Pump ),
			ThrowWindup = windup?.Name,
			Incremental = incremental,
			Throws = throws,
			WindupDuration = WindupTime( windup ),
			LowWindup = lowWindup?.Name,
			LowWindupDuration = WindupTime( lowWindup ),
			PrimaryThrow = ReadRelease( release, launch ),
			Lob = lowWindup is null ? default : AlternateRelease( "ACT_VM_HAULBACK", AnimEvents.WeaponThrow3 ),
			Roll = lowWindup is null ? default : AlternateRelease( "ACT_VM_SECONDARYATTACK", AnimEvents.WeaponThrow2 )
		};
	}
}
