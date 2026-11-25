namespace Halo2;

public sealed class MagazineWeapon : Weapon
{
	[Property] public string RigDirectory { get; set; }
	[Property] public string SpartanModel { get; set; }
	[Property] public string EliteModel { get; set; }

	[Property] public int BurstLength { get; set; }
	[Property] public float RecoveryDelay { get; set; }
	int remainingShots;
	GameObject burstAttacker;

	public override void PrimaryAttack()
	{
		if ( BurstLength > 1 && remainingShots == 0 )
		{
			remainingShots = BurstLength;
			burstAttacker = Attacker;
		}
		base.PrimaryAttack();
		if ( remainingShots == 0 ) return;
		if ( --remainingShots == 0 || (IsHeld && !HasPrimaryAmmo()) )
		{
			remainingShots = 0;
			burstAttacker = null;
			SetNextPrimaryFire( RecoveryDelay );
		}
	}

	public override bool CanReload() => remainingShots == 0 && base.CanReload();

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( remainingShots > 0 && NextPrimaryFire <= 0 )
			FirePrimary( burstAttacker );
	}

	protected override void OnDisabled()
	{
		remainingShots = 0;
		burstAttacker = null;
		base.OnDisabled();
	}

	protected override void CreateWorldModel()
	{
		DestroyWorldModel();
		if ( HolderRenderer is not {} body || body.Model is null ) return;
		var loose = GetComponent<SkinnedModelRenderer>( true );
		if ( loose?.Model is not { IsError: false } world ) return;
		var hand = body.GetBoneObject( HoldBone );
		if ( hand is null && body.Model.GetData<WeaponAttachment>() is {} attachment )
		{
			body.CreateAttachments = true;
			hand = body.GetAttachmentObject( attachment.PrimaryHand );
		}
		WorldModel = new GameObject( hand ?? GameObject, true, DisplayName );
		WorldModel.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		WorldModel.LocalTransform = global::Transform.Zero;
		var renderer = WorldModel.AddComponent<SkinnedModelRenderer>();
		renderer.Model = world;
		renderer.CreateAttachments = true;
		var model = WorldModel.AddComponent<BaseWeaponModel>();
		model.Renderer = renderer;
		model.MuzzleGameObject = renderer.GetAttachmentObject( "primary_trigger" );
		model.ShellEjectGameObject = renderer.GetAttachmentObject( "primary_ejection" );
		model.TracerEffect = model.EjectBrass = model.MuzzleEffect = null;
		if ( BindHolderAnimator() is {} animator ) animator.Weapon = renderer;
		loose.Enabled = false;
	}

	protected override void DestroyWorldModel()
	{
		if ( HolderAnimator is {} animator && animator.Weapon.IsValid() && animator.Weapon.GameObject == WorldModel )
			animator.Weapon = null;
		base.DestroyWorldModel();
		if ( GetComponent<SkinnedModelRenderer>( true ) is {} loose ) loose.Enabled = true;
	}

	bool UseEliteRig => EliteModel is not null && HolderRenderer?.Model?.GetData<PlayerRepresentation>()?.Index is 1 or 3;
	protected override string FirstPersonRig => $"{RigDirectory}/{(UseEliteRig ? "first_person_elite" : "first_person")}.vmdl";
	protected override void PlayMeleeView( string action ) => ViewModel?.GetComponent<MagazineWeaponModel>()?.Play( action );
	public override bool CanSecondaryAttack() => remainingShots == 0 && base.CanSecondaryAttack();

	protected override void CreateViewModel()
	{
		if ( ViewModel.IsValid() || IsProxy || Holder is not PlayerController ) return;
		bool elite = UseEliteRig;
		var rig = FirstPersonRig;
		var view = new GameObject( GameObject, false, $"{DisplayName} view model" );
		view.Flags |= GameObjectFlags.Absolute | GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		view.Tags.Add( "firstperson", "viewmodel" );
		var arms = view.AddComponent<SkinnedModelRenderer>();
		arms.Model = Model.Load( rig );
		arms.UseAnimGraph = true;
		arms.Animation.Graph = Sandbox.Animation.Graph.Load( $"{rig}/any/any/any.ag" );
		arms.RenderOptions.Game = false;
		arms.RenderOptions.Overlay = true;
		var gun = new GameObject( view, true, DisplayName ).AddComponent<SkinnedModelRenderer>();
		gun.Model = Model.Load( elite ? EliteModel : SpartanModel );
		gun.CreateAttachments = true;
		gun.BoneMergeTarget = arms;
		gun.RenderOptions.Game = false;
		gun.RenderOptions.Overlay = true;
		var model = view.AddComponent<MagazineWeaponModel>();
		model.Renderer = arms;
		model.Weapon = this;
		model.TracerEffect = model.EjectBrass = model.MuzzleEffect = null;
		view.Enabled = true;
		model.MuzzleGameObject = gun.GetAttachmentObject( "primary_trigger" );
		model.ShellEjectGameObject = gun.GetAttachmentObject( "primary_ejection" );
		ViewModel = view;
		model.OnDeploy();
	}
}

public sealed class MagazineWeaponModel : BaseWeaponModel
{
	internal MagazineWeapon Weapon { get; set; }
	readonly AnimationSounds sounds = new();
	FirstPersonAnimation data;
	Halo2ClipInfo clip;
	string currentAction;
	float frame;

	internal void Play( string action )
	{
		if ( !Renderer.Animation.IsValid ) return;
		data ??= Renderer.Model.GetData<FirstPersonAnimation>();
		if ( data?.Actions.TryGetValue( action, out var selected ) != true ) return;
		clip = selected;
		currentAction = action;
		frame = 0;
		sounds.Restart();
		var parameters = Renderer.Animation.Parameters;
		if ( parameters.GetString( "action" ) == action ) parameters.Trigger( "action_restart" );
		parameters.Set( "action_once", action != "idle" );
		parameters.Set( "action", action );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( clip is null || !Weapon.IsValid() || currentAction == "idle" ) return;
		frame += Time.Delta * 30 * Renderer.PlaybackRate;
		sounds.Advance( clip, System.MathF.Min( frame, clip.Frames - 1 ), data, Weapon, GameObject );
		if ( frame >= clip.Frames - 1 ) Play( "idle" );
	}

	public override void OnReloadCancel()
	{
		if ( currentAction is "reload_full" or "reload_empty" ) Play( "idle" );
	}

	public override void OnDeploy() => Play( "ready" );
	public override void OnAttack( Vector3? hitPoint = null, Vector3? origin = null ) => Play( "fire_1" );
	public override void OnReloadStart() => Play( Weapon.Clip1 == 0 ? "reload_empty" : "reload_full" );
}
