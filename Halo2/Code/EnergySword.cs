namespace Halo2;

public sealed class EnergySword : HandheldWeapon
{
	[Property] public bool Powered { get; set; }

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
		_ = ActivateBlade( renderer );
		if ( ReadySound is not null ) _ = PlayReadySound( renderer, firstPerson );
	}

	async System.Threading.Tasks.Task PlayReadySound( SkinnedModelRenderer renderer, bool firstPerson )
	{
		await Task.DelaySeconds( ReadySoundDelay );
		if ( !renderer.IsValid() || !IsActive ) return;
		bool viewingFirstPerson = ViewModel.IsValid() && Owner is { ThirdPerson: false };
		if ( firstPerson != viewingFirstPerson ) return;
		var marker = renderer.GetAttachmentObject( ReadySoundMarker );
		if ( marker.IsValid() ) ReadySound.Play( marker.WorldPosition, ref soundSeed, marker );
	}

	async System.Threading.Tasks.Task ActivateBlade( SkinnedModelRenderer renderer )
	{
		if ( readyAt > 0 ) await Task.DelaySeconds( readyAt );
		if ( !renderer.IsValid() || !IsActive ) return;
		int index = 0;
		var mount = Sandbox.Mounting.Directory.Get( Halo2Mount.MountIdent );
		foreach ( var material in renderer.Model.Materials )
		{
			var path = $"{material.Name}/turned_on.vmat";
			if ( mount?.GetByPath( path ) is not null )
				renderer.Materials.SetOverride( index, Material.Load( path ) );
			index++;
		}
	}

	public EnergySword()
	{
		UsesAmmo = UsesClips = false;
		PrimaryAutomatic = false;
	}

	public override bool CanPrimaryAttack() => NextPrimaryFire <= 0 && CanSecondaryAttack();
	public override void PrimaryAttack() => SecondaryAttack();
	public override bool CanReload() => false;
}
