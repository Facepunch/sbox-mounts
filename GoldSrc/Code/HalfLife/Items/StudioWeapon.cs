namespace GoldSrc.HalfLife;

[System.AttributeUsage( System.AttributeTargets.Class, Inherited = false )]
internal sealed class WeaponPickupAttribute( string model, string ammo, int limit ) : System.Attribute
{
	internal string Model => model;
	internal string Ammo => ammo;
	internal int Limit => limit;
	public int Bucket { get; set; }
	public int Weight { get; set; } = -1;
	public bool SkipAutoSelect { get; set; }
	public string AlternateModel { get; set; }
	internal IEnumerable<string> ViewModelPaths
	{
		get
		{
			yield return $"models/v_{Model}.mdl";
			if ( AlternateModel is not null ) yield return $"models/v_{AlternateModel}.mdl";
		}
	}
	public string ClassName { get; set; }
	public string PickupModel { get; set; }
	public string PreviewModel { get; set; }
	public int PreviewPitch { get; set; }
	internal string WorldPickupPath => $"models/{PickupModel ?? $"w_{model}"}.mdl";
	internal string PrefabPath => $"entities/items/weapon_{ClassName ?? model}.prefab";
}

[Hide]
public abstract partial class StudioWeapon : BaseCombatWeapon
{
	WeaponPickupAttribute _definition;
	internal WeaponPickupAttribute Definition => _definition ??= Game.TypeLibrary.GetType( GetType() ).GetAttribute<WeaponPickupAttribute>();
	internal virtual string DeploySequence => "draw";
	internal virtual bool CanIdle => true;
	internal virtual (string Name, float? Delay) IdleAnimation => ("idle1", null);
	internal virtual void PlayIdle( StudioWeaponView view )
	{
		var (name, delay) = IdleAnimation;
		view.Play( name, idleAfter: delay );
	}
	internal virtual string ShotSequence => "shoot";
	internal virtual float? IdleAfterShot => null;
	internal virtual string ReloadSequence => "reload";
	internal virtual float IdleAfterReloadStart => 3;

	// Some fresh SDK weapons carry more rounds than fit in their magazine.
	internal virtual int FreshPickupRounds => ClipMaxSize;

	protected override void OnAdded( BaseInventoryComponent inventory )
	{
		// Clipless pickups contribute their payload even when another weapon shares the pool.
		if ( UsesAmmo && !UsesClips && StartingAmmo > 0 && inventory.GetAmmo( PrimaryAmmoType ) != int.MaxValue )
		{
			inventory.GiveAmmo( PrimaryAmmoType, StartingAmmo );
			StartingAmmo = 0;
		}

		var extra = Clip1 < 0 && UsesPrimaryClip ? FreshPickupRounds - ClipMaxSize : 0;
		base.OnAdded( inventory );
		GetComponent<Maps.Relay>()?.Trigger( inventory.GameObject );
		if ( extra > 0 ) inventory.GiveAmmo( PrimaryAmmoType, extra );
	}

	protected override bool OnAdding( BaseInventoryComponent inventory )
	{
		if ( UsesAmmo && !UsesClips && StartingAmmo == 0 && inventory.Items.Any( item => item.GetType() == GetType() ) )
			return false;

		if ( Clip1 >= 0 || !UsesPrimaryClip || FreshPickupRounds <= ClipMaxSize ||
			!inventory.Items.Any( item => item != this && item.GetType() == GetType() ) )
			return base.OnAdding( inventory );

		if ( inventory.GiveAmmo( PrimaryAmmoType, FreshPickupRounds ) > 0 ) GameObject.Destroy();
		return false;
	}

	internal abstract void Configure();
	protected override bool WantsReload() => base.WantsReload() ||
		(AutoReload && UsesPrimaryClip && Clip1 == 0 && NextPrimaryFire <= 0 &&
		!Input.Down( "attack1" ) && !Input.Down( "attack2" ));
	internal virtual void PosePickup( SkinnedModelRenderer renderer ) { }

	// The p_ models have no attachments. These are barrel-tip positions in their right-hand bone space.
	internal virtual Vector3 WorldMuzzleOffset( bool hd ) => Vector3.Zero;
	internal virtual string ShotSound => null;

	internal virtual void PlayShotSound()
	{
		if ( ShotSound is { } sample ) PlaySample( sample );
	}

	protected override void OnShootEffects( ShotEffect shot )
	{
		if ( !Application.IsDedicatedServer && (shot.Hit || shot.HitPosition != default) )
		{
			var start = GetMuzzleTransform().Position;
			shot = shot with { Origin = start };
			DrawShotTrail( start, shot.HitPosition );
		}
		base.OnShootEffects( shot );
		if ( !shot.NoEvents ) PlayShotSound();
	}

	internal virtual void DrawShotTrail( Vector3 start, Vector3 end ) => BulletEffects.ShowTracer( start, end );

	protected override void OnShootImpact( in ShotEffect shot )
	{
		if ( !shot.Hit || Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ) return;
		var flesh = HitFlesh( shot );
		var sound = mount.GetBulletImpactSound( shot.Surface, flesh );
		BulletEffects.ShowImpact( shot.HitPosition, shot.Normal, sound, flesh ? 0 : Game.Random.Int( 1, 5 ) );
	}

	protected static bool HitFlesh( in ShotEffect shot ) => shot.HitObject.IsValid() &&
		GoldSrc.HalfLife.Npc.ClassOf( shot.HitObject ) is not (MonsterClass.None or MonsterClass.Machine);

	protected override SceneTrace BulletTrace( Ray ray, float distance, float radius ) =>
		TraceLineOfFire( ray, distance, radius );

	SceneTrace TraceLineOfFire( Ray ray, float distance, float radius ) =>
		base.BulletTrace( ray, distance, radius ).WithCollisionRules( "bullet" ).WithoutTags( "playercontroller" );

	Transform _worldMuzzle;
	SkinnedModelRenderer _viewRenderer;

	public override Transform GetMuzzleTransform()
	{
		if ( Owner is { ThirdPerson: false } && !IsProxy && _viewRenderer.IsValid() &&
			_viewRenderer.GetAttachment( "attachment1" ) is { } muzzle )
			return muzzle;

		if ( !WorldModel.IsValid() ) return base.GetMuzzleTransform();
		UpdateHeldGrip();
		return WorldModel.WorldTransform.ToWorld( _worldMuzzle );
	}

	public override Ray AimRay
	{
		get
		{
			if ( !IsHeld ) return base.AimRay;
			var sight = IsProxy && Owner.IsValid() ? Owner.EyeTransform.ForwardRay : base.AimRay;
			var aim = TraceLineOfFire( sight, Ballistics.Range, 0 ).Run().EndPosition;
			var muzzle = GetMuzzleTransform().Position;

			// If the barrel sticks through cover, keep the shot on the shooter's side.
			var eye = Owner.IsValid() ? Owner.EyePosition : sight.Position;
			var cover = TraceLineOfFire( new Ray( eye, (muzzle - eye).Normal ), eye.Distance( muzzle ), 0 ).Run();
			if ( cover.Hit || cover.StartedSolid )
				return new Ray( cover.StartedSolid ? eye : cover.EndPosition - (muzzle - eye).Normal * 0.1f, (muzzle - eye).Normal );

			var direction = aim - muzzle;
			return new Ray( muzzle, direction.Dot( sight.Forward ) > 0 ? direction.Normal : sight.Forward );
		}
	}

	protected override bool OnDrop()
	{
		var player = Owner;
		if ( !player.IsValid() ) return base.OnDrop();

		var eye = player.EyeTransform;
		var velocity = player.Velocity + eye.Forward * 200f + Vector3.Up * 100f;
		Network.DropOwnership();
		GameObject.SetParent( null, true );
		GameObject.Enabled = true;
		WorldPosition = eye.Position + eye.Forward * 48f;
		Slot = -1;

		if ( GetComponent<Rigidbody>( true ) is { } body )
		{
			body.Velocity = velocity;
			body.AngularVelocity = Vector3.Random * 8f;
		}
		return true;
	}

	internal void SetupPickup()
	{
		DisplayName = Game.TypeLibrary.GetType( GetType() ).Title;
		UsesAmmo = UsesClips = true;
		BindAmmoTypes();
		Configure();
		CanCancelReload = IncrementalReloading;
	}

	internal virtual void BindAmmoTypes()
	{
		if ( Definition.Ammo is { } ammo ) PrimaryAmmoType ??= GameResource.Load<BaseAmmoResource>( $"mount://hl1/ammo/{ammo}.ammo" );
	}

	protected override async System.Threading.Tasks.Task OnLoad()
	{
		await base.OnLoad();
		if ( Networking.IsHost ) return;
		// Load mounted resources for the complete replicated inventory, including holstered weapons.
		await Sandbox.Mounting.Directory.Mount( "hl1" );
		if ( !IsValid ) return;
		BindAmmoTypes();
		var mount = Sandbox.Mounting.Directory.Get( "hl1" );
		if ( mount?.GetByPath( $"mount://hl1/{Definition.PrefabPath}" ) is WeaponPickupLoader pickup )
			pickup.BindPresentation( this, GetComponent<SkinnedModelRenderer>( true ) );
	}

	protected void EjectShells( int count, bool shotgun )
	{
		if ( Application.IsDedicatedServer || !Owner.IsValid() ) return;
		var pose = Owner.EyeTransform;
		var origin = pose.PointToWorld( shotgun ? new Vector3( 32, -6, -12 ) : new Vector3( 20, -4, -12 ) );
		for ( int i = 0; i < count; i++ )
		{
			var velocity = Owner.Velocity + pose.Rotation.Right * Game.Random.Float( 50, 70 )
				+ pose.Rotation.Up * Game.Random.Float( 100, 150 ) + pose.Rotation.Forward * 25;
			ShellCasing.Eject( origin, pose.Rotation, shotgun, velocity );
		}
	}

	protected SoundHandle PlaySample( string sample, float volume = 1, float pitch = 1 )
	{
		if ( !Application.IsDedicatedServer && Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
			return NpcAudio.Play( mount.NpcAudio.Find( sample ), WorldPosition, volume, pitch );
		return null;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( GetComponent<ModelRenderer>( true ) is { } loose ) loose.Enabled = !IsHeld;
	}

	protected bool Zoomed { get; private set; }
	internal virtual float ZoomFieldOfView => 0;

	public override void SecondaryAttack()
	{
		if ( ZoomFieldOfView > 0 ) Zoomed = !Zoomed;
		else base.SecondaryAttack();
	}

	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		base.ModifyCamera( camera, ref view );
		if ( Zoomed ) view.FieldOfView = ZoomFieldOfView;
	}

	protected override void OnReloadStarted()
	{
		Zoomed = false;
		base.OnReloadStarted();
	}

	protected override void OnHolstered()
	{
		Zoomed = false;
		base.OnHolstered();
	}

	protected override void CreateViewModel()
	{
		if ( ViewModel.IsValid() || IsProxy || Holder is not PlayerController ) return;
		ViewModel = MakeModel( $"models/v_{HeldModelName}.mdl", GameObject, true );
	}

	GameObject MakeModel( string relativePath, GameObject parent, bool firstPerson )
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ) return null;
		foreach ( var dir in mount.GameDirs )
		{
			var path = $"mount://hl1/{dir}/{relativePath}.vmdl";
			if ( mount.GetByPath( path ) is not ModelLoader loader ) continue;
			var model = Model.Load( path );
			if ( !model.IsValid() || model.IsError ) continue;
			var go = new GameObject( parent, false, DisplayName );
			go.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
			var renderer = go.AddComponent<SkinnedModelRenderer>();
			renderer.Model = model;
			if ( firstPerson )
			{
				_viewRenderer = renderer;
				go.Flags |= GameObjectFlags.Absolute;
				go.Tags.Add( "firstperson", "viewmodel" );
				renderer.RenderOptions.Game = false;
				renderer.RenderOptions.Overlay = true;
				var view = go.AddComponent<StudioWeaponView>();
				view.Renderer = renderer;
				view.TracerEffect = null;
				view.Initialize( this, loader, mount.NpcAudio );
			}
			else AlignHeldModel( renderer, dir.EndsWith( "_hd" ) );
			go.Enabled = true;
			return go;
		}
		return null;
	}
}
