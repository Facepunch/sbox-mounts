namespace Halo2;

public abstract class HandheldWeapon : Weapon
{
	[Property] public string RigDirectory { get; set; }
	[Property] public string SpartanModel { get; set; }
	[Property] public string EliteModel { get; set; }

	[Property] public bool Instantaneous { get; set; } = true;
	double? lastAutomaticRound;
	double? automaticRoundAt;
	bool roundQueued;

	protected virtual bool CanCycleBarrel => PrimaryAutomatic &&
		CanUseHeldWeapon && !MeleeActive && !IsReloading && HasPrimaryAmmo();

	protected override bool WantsPrimaryAttack() => roundQueued || base.WantsPrimaryAttack();

	protected override void OnControl()
	{
		double now = Time.NowDouble;
		bool held = Input.Down( "attack1" );
		bool canCycle = CanCycleBarrel;
		if ( !canCycle ) roundQueued = false;
		else if ( held && lastAutomaticRound is double last && now >= last + 1 / 30.0 )
			roundQueued = true;

		if ( canCycle && (held || roundQueued) && PrimaryDelay > 0 )
		{
			automaticRoundAt = now;
			if ( lastAutomaticRound is double previous )
			{
				// The barrel checks elapsed ticks against its current firing rate.
				double due = previous + System.Math.Round( PrimaryDelay * 30 ) / 30.0;
				SetNextPrimaryFire( (float)(due - now) );
				if ( now - due < PrimaryDelay ) automaticRoundAt = due;
			}
		}
		else lastAutomaticRound = null;
		base.OnControl();
		automaticRoundAt = null;
	}

	protected double RecordRound()
	{
		roundQueued = false;
		if ( automaticRoundAt is not double time ) return Time.NowDouble;
		lastAutomaticRound = time;
		return time;
	}

	protected override void PlayPutAway() => ViewModel?.GetComponent<HandheldWeaponModel>()?.Play( "put_away" );

	protected override void OnDisabled()
	{
		lastAutomaticRound = null;
		automaticRoundAt = null;
		roundQueued = false;
		base.OnDisabled();
	}

	protected void LaunchBolt( Vector3 forward, EnergyDischarge discharge = null )
	{
		var ray = Holder is PlayerController player ? PlayerFiringRay( player, forward ) : new Ray( AimRay.Position, forward );
		LaunchProjectiles( ray, discharge?.Ballistics.SpreadBase.x ?? CurrentSpread.x, discharge );
	}

	protected virtual void ConfigureHeldRenderer( SkinnedModelRenderer renderer, bool firstPerson ) { }

	internal virtual string RestAction => "idle";

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
		ConfigureHeldRenderer( renderer, false );
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

	bool UseEliteRig => EliteModel is not null && (SpartanModel is null || HolderRenderer?.Model?.GetData<PlayerRepresentation>()?.Index is 1 or 3);
	protected override string FirstPersonRig => $"{RigDirectory}/{(UseEliteRig ? "first_person_elite" : "first_person")}.vmdl";
	protected override void PlayMeleeView( string action ) => ViewModel?.GetComponent<HandheldWeaponModel>()?.Play( action );

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
		ConfigureHeldRenderer( gun, true );
		gun.CreateAttachments = true;
		gun.BoneMergeTarget = arms;
		gun.RenderOptions.Game = false;
		gun.RenderOptions.Overlay = true;
		var model = view.AddComponent<HandheldWeaponModel>();
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

public sealed class HandheldWeaponModel : BaseWeaponModel
{
	internal HandheldWeapon Weapon { get; set; }
	readonly AnimationSounds sounds = new();
	internal FirstPersonAnimation AnimationData { get; private set; }
	Halo2ClipInfo clip;
	string currentAction;
	float frame;
	int reloadStartRounds;
	int reloadKeyframe;

	internal void Play( string action )
	{
		if ( !Renderer.Animation.IsValid ) return;
		AnimationData ??= Renderer.Model.GetData<FirstPersonAnimation>();
		if ( AnimationData?.Actions.TryGetValue( action, out var selected ) != true ) return;
		clip = selected;
		currentAction = action;
		frame = 0;
		sounds.Restart();
		var parameters = Renderer.Animation.Parameters;
		if ( parameters.GetString( "action" ) == action ) parameters.Trigger( "action_restart" );
		parameters.Set( "action_once", action is not ("idle" or "overheated" or "overcharged") );
		parameters.Set( "action", action );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Weapon.IsValid() && AnimationData is { AmmunitionFrames: > 1 } data && Renderer.Animation.IsValid )
		{
			int loaded = Weapon.Clip1;
			if ( Weapon.Shard is not null && currentAction is ("reload_full" or "reload_empty") && reloadKeyframe > 0 )
			{
				float fraction = ((int)frame - reloadKeyframe) * 0.2f;
				loaded = reloadStartRounds + (int)((loaded - reloadStartRounds) * fraction.Clamp( 0, 1 ));
			}
			Renderer.Animation.Parameters.Set( "ammunition_time", (loaded / (data.AmmunitionFrames - 1f)).Clamp( 0, 1 ) );
		}
		if ( clip is null || !Weapon.IsValid() || currentAction is "idle" or "overheated" or "overcharged" ) return;
		frame += Time.Delta * 30 * Renderer.PlaybackRate;
		sounds.Advance( clip, System.MathF.Min( frame, clip.Frames - 1 ), AnimationData, Weapon, GameObject );
		if ( frame >= clip.Frames - 1 ) Play( Weapon.RestAction );
	}

	public override void OnReloadCancel()
	{
		if ( currentAction is "reload_full" or "reload_empty" ) Play( "idle" );
	}

	public override void OnDeploy() => Play( "ready" );
	public override void OnAttack( Vector3? hitPoint = null, Vector3? origin = null ) => Play( "fire_1" );
	public override void OnReloadStart()
	{
		reloadStartRounds = Weapon.Clip1;
		Play( reloadStartRounds == 0 ? "reload_empty" : "reload_full" );
		reloadKeyframe = clip?.Cues.FirstOrDefault( cue => cue.Kind == "primary_keyframe" )?.Frame ?? 0;
	}
}
