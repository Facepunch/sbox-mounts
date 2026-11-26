namespace Halo2;

public sealed record BladeGlowEvent( float SkipChance, float Duration, MuzzleFlashDefinition[] Particles );

public sealed partial class EnergySword : MeleeWeapon, Component.IDamageable.IEvents
{
	[Property] public bool AgesWithKills { get; set; }
	[Property] public string AgeEffectMarker { get; set; }
	[Property] public string AgedWeaponPrefab { get; set; }
	[Sync( SyncFlags.FromHost ), Change( nameof( OnChargeChanged ) )] public float Spent { get; private set; }

	void Component.IDamageable.IEvents.OnDied( Component.IDamageable.DeathEvent death )
	{
		if ( !Networking.IsHost || !AgesWithKills || Spent >= 1 || death.DamageInfo.Weapon != GameObject ) return;
		var npc = death.Victim.GetComponent<Sandbox.Npcs.BaseNpc>( true );
		var player = death.Victim.GetComponent<PlayerController>( true );
		if ( !npc.IsValid() && !player.IsValid() ) return;
		var model = (npc?.Renderer ?? player?.Renderer)?.Model;
		// Campaign uses the victim's authored default team even if its current allegiance changed.
		int? team = model?.GetData<UnitDefinition>()?.DefaultTeam;
		Spent = System.MathF.Min( 1, Spent + (team is 4 or 5 ? 0.025f : 0.1f) );
		if ( Spent >= 1 && AgeEffectMarker is not null ) PlayAgeEffect();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayAgeEffect()
	{
		if ( Application.IsDedicatedServer ) return;
		var renderer = ViewModel.IsValid() && Owner is { ThirdPerson: false } ? viewBlade : worldBlade;
		GetComponent<PelletImpactEffect>()?.PlayAtMarker( renderer?.GetAttachmentObject( AgeEffectMarker ) );
	}

	public bool Powered => AgedWeaponPrefab is not null && Spent < 1;

	void OnChargeChanged()
	{
		if ( Spent < 1 || AgedWeaponPrefab is null ) return;
		if ( GameObject.GetPrefab( AgedWeaponPrefab )?.GetComponent<EnergySword>() is not {} aged ) return;

		// Halo changes the definition on the existing object during the killing strike.
		DisplayName = aged.DisplayName;
		RigDirectory = aged.RigDirectory;
		TargetRange = aged.TargetRange;
		LungeDamage = aged.LungeDamage;
		MinimumMeleeDamage = aged.MinimumMeleeDamage;
		MeleeDamage = aged.MeleeDamage;
		MeleeForce = aged.MeleeForce;
		MeleeHalfExtents = aged.MeleeHalfExtents;
		MeleeRange = aged.MeleeRange;
		MeleeBodyScales = aged.MeleeBodyScales;
		MeleeExplosive = aged.MeleeExplosive;
		Assassination = aged.Assassination;
		ContactSounds = aged.ContactSounds;
		ReadySound = aged.ReadySound;
		ReadySoundDelay = aged.ReadySoundDelay;
		ReadySoundMarker = aged.ReadySoundMarker;

		if ( GetComponent<SkinnedModelRenderer>( true ) is {} loose ) SetBladeMaterials( loose, false );
		if ( worldBlade.IsValid() ) SetBladeMaterials( worldBlade, false );
		if ( viewBlade.IsValid() ) SetBladeMaterials( viewBlade, false );
	}

	SkinnedModelRenderer worldBlade, viewBlade;
	[Property] public BladeGlowEvent[] GlowEvents { get; set; }
	[Property] public int GlowLoopStart { get; set; }
	int glowEvent, remainingGlowTicks;
	double? nextGlowTick;
	uint glowSeed = (uint)System.Random.Shared.Next();

	[Property] public WeaponSoundTrack BladeSound { get; set; }
	readonly TrackVoice bladeVoice = new();

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Application.IsDedicatedServer ) return;
		UpdateGlow();
		if ( BladeSound is null ) return;
		GameObject marker = null;
		if ( Powered && (!IsHeld || IsActive) )
		{
			var renderer = IsHeld ? (ViewModel.IsValid() && Owner is { ThirdPerson: false } ? viewBlade : worldBlade)
				: GetComponent<SkinnedModelRenderer>();
			if ( renderer.IsValid() )
			{
				if ( !renderer.CreateAttachments ) renderer.CreateAttachments = true;
				marker = renderer.GetAttachmentObject( BladeSound.Marker );
			}
		}
		UpdateTrack( bladeVoice, BladeSound, marker.IsValid(), marker );
	}

	protected override void OnDisabled()
	{
		nextGlowTick = null;
		bladeVoice.Stop( BladeSound );
		base.OnDisabled();
	}

	void UpdateGlow()
	{
		if ( GlowEvents is not { Length: > 0 } || !Powered || !IsActive || readyAt > 0 ||
			Owner is not { ThirdPerson: false } || !ViewModel.IsValid() || !viewBlade.IsValid() )
		{
			nextGlowTick = null;
			return;
		}
		double now = Time.NowDouble;
		if ( nextGlowTick is null )
		{
			nextGlowTick = now;
			glowEvent = 0;
			remainingGlowTicks = 0;
		}
		while ( nextGlowTick <= now )
		{
			nextGlowTick += 1.0 / 30;
			while ( remainingGlowTicks == 0 )
			{
				if ( glowEvent >= GlowEvents.Length ) glowEvent = System.Math.Max( 0, GlowLoopStart );
				var ev = GlowEvents[glowEvent];
				if ( ev.SkipChance > 0 && BlamRandom.Next( ref glowSeed ) * (1.0 / 65535) < ev.SkipChance )
				{
					glowEvent++;
					continue;
				}
				remainingGlowTicks = System.Math.Max( 1, (int)System.MathF.Round( ev.Duration * 30 ) );
			}
			EmitMuzzleFlash( GlowEvents[glowEvent].Particles, true );
			if ( --remainingGlowTicks == 0 ) glowEvent++;
		}
	}

	[Property] public CuePlayback ReadySound { get; set; }
	[Property] public MuzzleFlashDefinition[] ReadyParticles { get; set; }
	[Property] public string ReadySoundMarker { get; set; }
	[Property] public float ReadySoundDelay { get; set; }

	TimeUntil readyAt;

	protected override void OnStart()
	{
		base.OnStart();
		if ( !Application.IsDedicatedServer && Powered && GetComponent<SkinnedModelRenderer>( true ) is {} loose )
			SetBladeMaterials( loose, true );
	}

	protected override void OnEquipped()
	{
		readyAt = CooldownSeconds( (int)System.MathF.Round( DeployTime * 30 ) );
		base.OnEquipped();
		if ( !IsValid || !IsActive ) return;
		if ( Holder is PlayerController ) SetNextPrimaryFire( 0 );
		if ( !Application.IsDedicatedServer && Powered && viewBlade.IsValid() )
			EmitMuzzleFlash( ReadyParticles, viewRenderer: viewBlade );
	}

	protected override void ConfigureHeldRenderer( SkinnedModelRenderer renderer, bool firstPerson )
	{
		if ( !Powered ) return;
		if ( firstPerson ) viewBlade = renderer;
		else worldBlade = renderer;
		_ = ActivateBlade( renderer );
		if ( ReadySound is not null ) _ = PlayReadySound( renderer, firstPerson );
	}

	async System.Threading.Tasks.Task PlayReadySound( SkinnedModelRenderer renderer, bool firstPerson )
	{
		await Task.DelaySeconds( ReadySoundDelay );
		if ( !renderer.IsValid() || !IsActive || !Powered ) return;
		bool viewingFirstPerson = ViewModel.IsValid() && Owner is { ThirdPerson: false };
		if ( firstPerson != viewingFirstPerson ) return;
		var marker = renderer.GetAttachmentObject( ReadySoundMarker );
		if ( marker.IsValid() ) ReadySound.Play( Scene, marker.WorldPosition, marker );
	}

	async System.Threading.Tasks.Task ActivateBlade( SkinnedModelRenderer renderer )
	{
		if ( readyAt > 0 ) await Task.DelaySeconds( readyAt );
		if ( !renderer.IsValid() || !IsActive || !Powered ) return;
		SetBladeMaterials( renderer, true );
	}

	static void SetBladeMaterials( SkinnedModelRenderer renderer, bool powered )
	{
		int index = 0;
		var mount = Sandbox.Mounting.Directory.Get( Halo2Mount.MountIdent );
		foreach ( var material in renderer.Model.Materials )
		{
			var path = $"{material.Name}/turned_on.vmat";
			if ( mount?.GetByPath( path ) is not null )
				renderer.Materials.SetOverride( index, powered ? Material.Load( path ) : null );
			index++;
		}
	}

	[Property] public MeleeDamageProfile LungeDamage { get; set; }
	protected override MeleeDamageProfile LungeProfile => LungeDamage;
}
