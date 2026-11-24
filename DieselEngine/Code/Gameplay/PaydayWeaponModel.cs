namespace Diesel;

/// <summary>Original AMCAR hand clips and soundbank recordings, driven by engine weapon presentation hooks.</summary>
[Title( "PAYDAY Weapon Presentation" ), Group( "PAYDAY" )]
public sealed class PaydayWeaponModel : BaseWeaponModel
{
	[Property] public bool AnimatedView { get; set; }
	[Property] public ModelRenderer Geometry { get; set; }
	[Property] public List<SoundEvent> Sounds { get; set; } = [];
	PaydayViewAssets assets;
	PaydayViewMesh mesh;
	readonly Dictionary<ulong, GameObject> rigid = [];
	string clip = "idle";
	float duration;
	float elapsed;
	int nextCue;
	PaydayWeapon weapon;
	float shotAge;
	bool firing;
	readonly Queue<SoundHandle> shotSounds = [];
	public string CurrentClip => clip;
	public float ClipTime => elapsed;

	void Initialize()
	{
		weapon ??= GetComponentInParent<PaydayWeapon>();
		weapon ??= Scene.GetAllComponents<PaydayWeapon>().FirstOrDefault( w => w.WorldModel == GameObject.Parent );
		if ( assets is not null ) return;
		if ( Sandbox.Mounting.Directory.Get( "payday" ) is not PaydayMount mount ) return;
		assets = mount.ViewAssets;
		if ( AnimatedView && Geometry.IsValid() )
		{
			mesh = new( assets );
			Geometry.Model = mesh.Model;
			foreach ( var (name, model) in assets.RigidModels )
			{
				var child = new GameObject( true, $"Weapon part {name:x}" ) { Parent = GameObject };
				var renderer = child.AddComponent<ModelRenderer>();
				renderer.Model = model;
				renderer.RenderOptions.Game = false;
				renderer.RenderOptions.Overlay = true;
				rigid.Add( name, child );
			}
		}
	}
	SoundHandle PlaySound( int index )
	{
		return index < Sounds.Count && Sounds[index] is { } sound ? GameObject.PlaySound( sound ) : null;
	}
	void PlayClip( string name, float seconds )
	{
		Initialize();
		clip = name; duration = seconds; elapsed = 0; nextCue = 0;
		SamplePose( 0 );
	}
	public override void OnDeploy()
	{
		base.OnDeploy();
		PlayClip( "equip", 0.5f );
		PlaySound( 2 );
	}
	public override void OnAttack( Vector3? hitPoint = null, Vector3? origin = null )
	{
		PlayClip( "recoil", 1f / 3 );
		base.OnAttack( hitPoint, origin );
		// The authored shot container limits overlapping voices to six.
		while ( shotSounds.Count >= 6 ) shotSounds.Dequeue()?.Stop( 0.01f );
		shotSounds.Enqueue( PlaySound( 0 ) );
		shotAge = 0;
		firing = true;
	}
	public override void OnDryFire()
	{
		base.OnDryFire();
		PlaySound( 1 );
	}
	public override void OnReloadStart()
	{
		Initialize();
		bool empty = weapon?.Clip1 == 0;
		PlayClip( empty ? "reload" : "reload_not_empty", weapon?.ReloadTime ?? (empty ? 1.9f : 1.4f) );
	}
	public override void OnReloadFinish()
	{
		base.OnReloadFinish();
		PlayClip( "idle", 0 );
	}
	/// <summary>Samples original clip time without advancing audio; also used for isolated pose verification.</summary>
	public void SamplePose( float seconds )
	{
		Initialize();
		if ( assets is null || mesh is null ) return;
		var animation = assets.Clips[clip];
		float time = duration > 0 ? Math.Clamp( seconds / duration, 0, 1 ) * animation.Duration : seconds % animation.Duration;
		mesh.Update( clip, time );
		foreach ( var (name, child) in rigid ) child.LocalTransform = mesh.RigidTransforms[name];
		if ( MuzzleGameObject.IsValid() ) MuzzleGameObject.LocalTransform = mesh.Muzzle;
	}
	protected override void OnUpdate()
	{
		base.OnUpdate();
		Initialize();
		if ( assets is null ) return;
		// The shipped fire container is a 100ms trigger-rate sequence of four recordings.
		// Engine shot callbacks provide that clock; the stop event supplies its separate tail.
		if ( firing && (shotAge += Time.Delta) > 0.1f + Math.Max( 0.03f, Time.Delta * 2 ) )
		{
			firing = false;
			PlaySound( 7 );
		}
		elapsed += Time.Delta;
		var animation = assets.Clips[clip];
		float time = duration > 0 ? Math.Clamp( elapsed / duration, 0, 1 ) * animation.Duration : elapsed % animation.Duration;
		if ( clip.StartsWith( "reload", StringComparison.Ordinal ) )
			while ( nextCue < animation.Cues.Count && animation.Cues[nextCue].Time <= time )
			{
				string cue = animation.Cues[nextCue++].Name;
				int index = cue switch { "mag_out" => 3, "mag_grab" => 4, "mag_in" => 5, "cock" => 6, _ => -1 };
				if ( index >= 0 ) PlaySound( index );
			}
		SamplePose( elapsed );
		if ( duration > 0 && elapsed >= duration ) PlayClip( "idle", 0 );
	}
	protected override void OnDisabled()
	{
		clip = "idle"; elapsed = 0; duration = 0; nextCue = 0;
		firing = false;
		while ( shotSounds.TryDequeue( out var sound ) ) sound?.Stop( 0.02f );
		base.OnDisabled();
	}
}
