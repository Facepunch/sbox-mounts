namespace Halo2;

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
		ContactSounds = aged.ContactSounds;
		ReadySound = aged.ReadySound;
		ReadySoundDelay = aged.ReadySoundDelay;
		ReadySoundMarker = aged.ReadySoundMarker;

		if ( worldBlade.IsValid() ) SetBladeMaterials( worldBlade, false );
		if ( viewBlade.IsValid() ) SetBladeMaterials( viewBlade, false );
	}

	SkinnedModelRenderer worldBlade, viewBlade;

	[Property] public WeaponSoundTrack BladeSound { get; set; }
	readonly TrackVoice bladeVoice = new();

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Application.IsDedicatedServer || BladeSound is null ) return;
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
		bladeVoice.Stop( BladeSound );
		base.OnDisabled();
	}

	[Property] public CuePlayback ReadySound { get; set; }
	[Property] public string ReadySoundMarker { get; set; }
	[Property] public float ReadySoundDelay { get; set; }

	TimeUntil readyAt;
	uint soundSeed = (uint)System.Random.Shared.Next();

	protected override void OnEquipped()
	{
		readyAt = CooldownSeconds( (int)System.MathF.Round( DeployTime * 30 ) );
		base.OnEquipped();
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
		if ( marker.IsValid() ) ReadySound.Play( marker.WorldPosition, ref soundSeed, marker );
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
