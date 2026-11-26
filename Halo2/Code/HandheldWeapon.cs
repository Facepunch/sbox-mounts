namespace Halo2;

[Sandbox.ModelEditor.GameData( "marker_groups" )]
public sealed class MarkerGroups
{
	public Dictionary<string, List<string>> Additional { get; set; }
}

public sealed record BulletImpact( string[] Effects, string[] Sounds );
public sealed record BulletContact( BulletImpact Primary, float AlternateChance, BulletImpact Alternate )
{
	public ShardContact Shard { get; init; }
}

public enum ShardResponse { Detonate, Fizzle, Overpenetrate, Attach, Bounce }

public sealed record ShardContact( ShardResponse Response, ShardResponse Alternate, int Flags, Vector2 Angles, float AngleNoise, Vector2 Friction,
	float EntryFriction, float PenetrationDistance )
{
	internal bool SelectAlternate( Scene scene, float angle, bool unit, float chance )
	{
		ref uint random = ref BlamRandom.GameplayState( scene );
		if ( AngleNoise > 0 )
			angle += (float)((2.0 * BlamRandom.Next( ref random ) / 65535 - 1) * AngleNoise);
		return chance > 0 && (Angles.y == 0 || (angle >= Angles.x && angle <= Angles.y))
			&& ((Flags & 2) == 0 || !unit) && ((Flags & 1) == 0 || unit)
			&& (chance >= 1 || BlamRandom.Next( ref random ) / 65535.0 < chance);
	}
}

public sealed record WeaponSound( SoundEvent Sound, string Marker );

public sealed record WeaponSoundTrack( string Marker, SoundEvent Start, SoundEvent Loop, SoundEvent End, float Gain, float FadeOut );

internal sealed class TrackVoice
{
	public SoundHandle Handle;
	public bool Started;
	public void Stop( WeaponSoundTrack track )
	{
		Handle?.Stop( track?.FadeOut ?? 0 );
		Handle = null;
		Started = false;
	}

	public void Update( WeaponSoundTrack track, bool active, GameObject emitter )
	{
		if ( track is null || (!active && !Started) ) return;
		if ( active && Handle.IsValid() )
		{
			if ( emitter.IsValid() && Handle.Parent != emitter )
			{
				Handle.Parent = emitter;
				Handle.FollowParent = true;
			}
			return;
		}
		var cue = active ? (Started ? track.Loop : track.Start ?? track.Loop) : track.End;
		if ( !active ) Stop( track );
		if ( !emitter.IsValid() || !cue.IsValid() ) return;
		var handle = emitter.PlaySound( cue );
		if ( !handle.IsValid() ) return;
		handle.Volume *= track.Gain;
		if ( active )
		{
			Handle = handle;
			Started = true;
		}
	}
}

public abstract class HandheldWeapon : Weapon
{
	[Property] public string RigDirectory { get; set; }
	[Property] public string SpartanModel { get; set; }
	[Property] public string EliteModel { get; set; }

	[Property] public WeaponSound[] AdditionalFiringSounds { get; set; }
	[Property] public MuzzleFlashDefinition[] PrimaryFlash { get; set; }
	[Property] public bool Instantaneous { get; set; } = true;
	double? lastAutomaticRound;
	double? automaticRoundAt;
	bool roundQueued;

	private protected void UpdateTrack( TrackVoice voice, WeaponSoundTrack track, bool active, GameObject source = null )
	{
		if ( track is null || (!active && !voice.Started) ) return;
		if ( source is null && active && voice.Handle.IsValid() ) return;
		var emitter = source ?? (string.IsNullOrEmpty( track.Marker ) ? WeaponModel?.GameObject ?? GameObject : EffectMarker( track.Marker ));
		voice.Update( track, active, emitter );
	}

	protected virtual bool CanCycleBarrel => PrimaryAutomatic &&
		CanUseHeldWeapon && !MeleeActive && !IsReloading && HasPrimaryAmmo();
	protected virtual double EarliestRateTick => 0;

	protected override bool WantsPrimaryAttack() => roundQueued || base.WantsPrimaryAttack();

	protected override void OnControl()
	{
		double now = Time.NowDouble;
		bool held = Input.Down( TriggerButton );
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
				double due = System.Math.Max( EarliestRateTick, previous + System.Math.Round( PrimaryDelay * 30 ) / 30.0 );
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

	protected override void PlayFiringSound()
	{
		base.PlayFiringSound();
		if ( AdditionalFiringSounds is null ) return;
		foreach ( var part in AdditionalFiringSounds )
			PlayFiringSound( part.Sound, part.Marker );
	}

	[Property] public Dictionary<string, string> ProjectileContactEffects { get; set; }
	[Property] public Dictionary<string, string[]> ProjectileContactSounds { get; set; }

	[Property] public Dictionary<string, BulletContact> BulletContacts { get; set; }

	protected virtual Dictionary<string, BulletContact> ContactsForProjectile( bool charged ) => BulletContacts;

	protected override void OnProjectileHit( in ShotEffect shot, in PelletResult hit )
	{
		if ( ContactsForProjectile( hit.Charged ) is not {} contacts )
		{
			base.OnProjectileHit( shot, hit );
			return;
		}
		PlayBulletContact( contacts, shot, hit.Direction, hit.Bone, hit.Alternate );
		OnShootEffects( shot with { Hit = false } );
	}

	void PlayBulletContact( Dictionary<string, BulletContact> contacts, in ShotEffect shot, Vector3 incoming, string bone = null, bool? alternate = null )
	{
		if ( ContactMaterial.Lookup( contacts, shot.Surface, true ) is not {} contact ) return;
		bool useAlternate = alternate ?? (contact.AlternateChance > 0 && Game.Random.Float() < contact.AlternateChance);
		var impact = useAlternate ? contact.Alternate : contact.Primary;
		if ( impact?.Effects is {} effects )
			foreach ( var path in effects ) PlaceProjectileEffect( path, shot, incoming, bone );
		PlayContactSounds( impact?.Sounds, shot.HitPosition );
	}

	protected override void OnShootImpact( in ShotEffect shot )
	{
		if ( BulletContacts is not null )
		{
			if ( shot.Hit ) PlayBulletContact( BulletContacts, shot, GetShotDirection( shot ) );
			return;
		}
		if ( ProjectileContactEffects is null )
		{
			base.OnShootImpact( shot );
			return;
		}
		if ( !shot.Hit ) return;
		if ( ProjectileContactSounds is not null )
			PlayContactSounds( ContactMaterial.Lookup( ProjectileContactSounds, shot.Surface, true ), shot.HitPosition );
		if ( ContactMaterial.Lookup( ProjectileContactEffects, shot.Surface, true ) is {} effect )
			PlaceProjectileEffect( effect, shot, GetShotDirection( shot ) );
	}

	void PlaceProjectileEffect( string path, in ShotEffect shot, Vector3 incoming, string bone = null ) =>
		PrefabFile.Load( path )?.GetScene().GetComponent<PelletImpactEffect>()?.Place( null, shot.HitObject.IsValid() ? shot.HitObject : Scene,
			shot.HitPosition, shot.Normal, incoming, bone );

	static void PlayContactSounds( string[] cues, Vector3 position )
	{
		if ( cues is null ) return;
		foreach ( var cue in cues )
			if ( GameResource.Load<SoundEvent>( cue ) is {} sound ) Sound.Play( sound, position );
	}

	protected override void OnShootEffects( ShotEffect shot )
	{
		base.OnShootEffects( shot );
		if ( !shot.NoEvents && !Application.IsDedicatedServer ) EmitMuzzleFlash( PrimaryFlash );
	}

	protected void EmitMuzzleFlash( MuzzleFlashDefinition[] groups, bool continuous = false, SkinnedModelRenderer viewRenderer = null )
	{
		if ( groups is null ) return;
		float eventRandom = System.Random.Shared.NextSingle();
		foreach ( var flash in groups )
		{
			var muzzle = viewRenderer.IsValid() ? viewRenderer.GetAttachmentObject( flash.Marker ) : EffectMarker( flash.Marker );
			if ( !muzzle.IsValid() ) continue;
			bool firstPerson = viewRenderer.IsValid() || WeaponModel?.GameObject == ViewModel;
			uint systemSeed = (uint)System.Random.Shared.Next();
			muzzle.GetOrAddComponent<MuzzleFlash>().Emit( flash, firstPerson, Holder?.GameObject, systemSeed, eventRandom, continuous );
			var rig = muzzle.GetComponentInParent<SkinnedModelRenderer>();
			if ( rig?.Model?.GetData<MarkerGroups>()?.Additional?.TryGetValue( flash.Marker, out var extra ) != true ) continue;
			foreach ( var name in extra )
				rig.GetAttachmentObject( name )?.GetOrAddComponent<MuzzleFlash>().Emit( flash, firstPerson, Holder?.GameObject, systemSeed, eventRandom, continuous );
		}
	}

	public override void PrimaryAttack()
	{
		if ( IsHeld && !TakePrimaryAmmo() ) return;
		var ray = AimRay;
		if ( Holder is PlayerController player ) ray = PlayerFiringRay( player, ray.Forward );
		var config = Ballistics;
		var volley = new ShotEffect[config.Pellets];
		ref uint seed = ref BlamRandom.GameplayState( Scene );
		float minimum = MinimumPelletAngle.DegreeToRadian(), maximum = CurrentSpread.x.DegreeToRadian();
		for ( int i = 0; i < volley.Length; i++ )
		{
			var direction = Spread.Deviate( ray.Forward, minimum, maximum, ref seed );
			var hit = BulletTrace( new Ray( ray.Position, direction ), config.Range, config.Radius ).Run();
			var surface = hit.Surface;
			if ( hit.Hit )
			{
				float scale = BodyMaterial.ResolveHit( hit, PelletBodyScales, PelletExplosive, out surface );
				ShootBullet( hit, config.Damage * scale, config.Force, AttackDamageTags );
			}
			volley[i] = new( hit.EndPosition, hit.Hit, hit.Normal, hit.GameObject, surface, ray.Position, NoEvents: i > 0 );
		}
		TimeSinceShoot = 0;
		ShootEffects( volley );
	}

	protected void LaunchBolt( Vector3 forward, EnergyDischarge discharge = null )
	{
		var ray = Holder is PlayerController player ? PlayerFiringRay( player, forward ) : new Ray( AimRay.Position, forward );
		LaunchProjectiles( ray, discharge?.Ballistics.SpreadBase.x ?? CurrentSpread.x, discharge );
	}

	protected virtual void ConfigureHeldRenderer( SkinnedModelRenderer renderer, bool firstPerson ) { }

	HandheldWeapon renderedOffhand;
	GameObject offhandModel;

	protected override void OnUpdate()
	{
		base.OnUpdate();
		var next = Offhand as HandheldWeapon;
		if ( next == renderedOffhand && next.IsValid() == offhandModel.IsValid() ) return;
		ClearOffhandModel();
		if ( !next.IsValid() || !WorldModel.IsValid() || HolderRenderer is not {} body ||
			body.Model?.GetData<WeaponAttachment>() is not { SecondaryHand: { Length: > 0 } marker } attachment ) return;
		body.CreateAttachments = true;
		var hand = body.GetAttachmentObject( marker );
		var world = next.GetComponent<SkinnedModelRenderer>( true )?.Model;
		if ( !hand.IsValid() || world is not { IsError: false } ) return;
		var grip = string.IsNullOrEmpty( attachment.PreferredGrip ) ? null : world.Attachments.GetTransform( attachment.PreferredGrip );
		if ( grip is null && !string.IsNullOrEmpty( next.GripMarker ) ) grip = world.Attachments.GetTransform( next.GripMarker );
		renderedOffhand = next;
		offhandModel = next.BuildHeldModel( hand, world, grip?.ToLocal( global::Transform.Zero ) ?? global::Transform.Zero ).GameObject;
	}

	void ClearOffhandModel()
	{
		if ( offhandModel.IsValid() )
		{
			if ( renderedOffhand.IsValid() && renderedOffhand.WorldModel == offhandModel ) renderedOffhand.WorldModel = null;
			offhandModel.Destroy();
		}
		offhandModel = null;
		renderedOffhand = null;
	}

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
		var renderer = BuildHeldModel( hand ?? GameObject, world, global::Transform.Zero );
		if ( BindHolderAnimator() is {} animator ) animator.Weapon = renderer;
		loose.Enabled = false;
	}

	SkinnedModelRenderer BuildHeldModel( GameObject hand, Model world, Transform local )
	{
		WorldModel = new GameObject( hand, true, DisplayName );
		WorldModel.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		WorldModel.LocalTransform = local;
		var renderer = WorldModel.AddComponent<SkinnedModelRenderer>();
		renderer.Model = world;
		ConfigureHeldRenderer( renderer, false );
		renderer.CreateAttachments = true;
		var model = WorldModel.AddComponent<BaseWeaponModel>();
		model.Renderer = renderer;
		model.MuzzleGameObject = renderer.GetAttachmentObject( "primary_trigger" );
		model.ShellEjectGameObject = renderer.GetAttachmentObject( "primary_ejection" );
		model.TracerEffect = model.EjectBrass = model.MuzzleEffect = null;
		return renderer;
	}

	protected override void DestroyWorldModel()
	{
		ClearOffhandModel();
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
	bool dualGraph;
	bool WantsDualGraph => Weapon.Offhand.IsValid() && AnimationData?.DualActions is not null;

	void SelectGraph( bool dual )
	{
		if ( dualGraph == dual ) return;
		var category = dual ? "dual" : "any";
		Renderer.Animation.Graph = Sandbox.Animation.Graph.Load( $"{Renderer.Model.Name}/any/{category}/any.ag" );
		dualGraph = dual;
	}

	internal void Play( string action )
	{
		AnimationData ??= Renderer.Model.GetData<FirstPersonAnimation>();
		SelectGraph( WantsDualGraph );
		var actions = dualGraph ? AnimationData.DualActions : AnimationData?.Actions;
		if ( !Renderer.Animation.IsValid || actions?.TryGetValue( action, out var selected ) != true ) return;
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
		if ( Weapon.IsValid() && dualGraph != WantsDualGraph )
			Play( Weapon.RestAction );
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
