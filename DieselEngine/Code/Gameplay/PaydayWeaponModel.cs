namespace Diesel;

/// <summary>Original AMCAR hand clips and soundbank recordings, driven by engine weapon presentation hooks.</summary>
[Title( "PAYDAY Weapon Presentation" ), Group( "PAYDAY" )]
public sealed class PaydayWeaponModel : BaseWeaponModel
{
	[Property] public bool AnimatedView { get; set; }
	[Property] public ModelRenderer Geometry { get; set; }
	[Property] public List<SoundEvent> Sounds { get; set; } = [];
	[Property] public List<string> SoundNames { get; set; } = [];
	[Property] public string WeaponId { get; set; } = "amcar4";
	WeaponDefinition Definition => WeaponDefinition.Get( WeaponId );
	WeaponAudio audio;
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
	SoundHandle sustainedFire;
	public bool SustainedFirePlaying => sustainedFire?.IsPlaying == true;
	readonly Queue<SoundHandle> shotSounds = [];
	readonly Queue<SoundHandle> cueSounds = [];
	bool reloadCancelled;
	public string CurrentClip => clip;
	public float ClipTime => elapsed;

	void Initialize()
	{
		weapon ??= GetComponentInParent<PaydayWeapon>();
		weapon ??= Scene.GetAllComponents<PaydayWeapon>().FirstOrDefault( w => w.WorldModel == GameObject.Parent );
		if ( assets is not null ) return;
		if ( Sandbox.Mounting.Directory.Get( "payday" ) is not PaydayMount mount ) return;
		assets = mount.GetViewAssets( WeaponId );
		audio = mount.WeaponAudio[WeaponId];
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
	SoundHandle PlaySound( string name )
	{
		int index = SoundNames.IndexOf( name );
		return index >= 0 && index < Sounds.Count && Sounds[index] is { } sound ? GameObject.PlaySound( sound ) : null;
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
		PlaySound( audio?.Equip );
	}
	public override void OnAttack( Vector3? hitPoint = null, Vector3? origin = null )
	{
		Initialize();
		string recoil = weapon?.IsAiming == true && assets.Clips.ContainsKey( "recoil_zoom" ) ? "recoil_zoom" : "recoil";
		PlayClip( recoil, Definition.Shotgun ? Definition.Interval : assets.Clips[recoil].Duration );
		base.OnAttack( hitPoint, origin );
		if ( WeaponId == "stryk" )
		{
			if ( sustainedFire?.IsPlaying != true ) sustainedFire = PlaySound( audio?.Fire );
		}
		else
		{
			while ( shotSounds.Count >= 32 ) shotSounds.Dequeue()?.Stop( .05f );
			shotSounds.Enqueue( PlaySound( audio?.Fire ) );
		}
		shotAge = 0;
		firing = true;
	}
	public override void OnDryFire()
	{
		base.OnDryFire();
		Initialize();
		PlaySound( audio?.Dry );
	}
	public override void OnReloadStart()
	{
		StopFiring();
		reloadCancelled = false;
		Initialize();
		bool empty = weapon?.Clip1 == 0;
		PlayClip( Definition.Shotgun ? "reload_enter" : empty || Definition.Grenade ? "reload" : "reload_not_empty",
			Definition.Shotgun ? .4f : weapon?.ReloadTime ?? (empty ? Definition.Empty : Definition.Tactical) );
	}
	public override void OnReloadFinish()
	{
		base.OnReloadFinish();
		PlayClip( Definition.Shotgun ? "reload_exit" : "idle", Definition.Shotgun ? (reloadCancelled ? .3f : 1f) : 0 );
	}
	public override void OnReloadCancel()
	{
		base.OnReloadCancel();
		reloadCancelled = true;
		while ( cueSounds.TryDequeue( out var sound ) ) sound?.Stop( .02f );
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
		UpdateLocomotion( weapon?.IsSprinting == true );
		// The shipped fire container is a 100ms trigger-rate sequence of four recordings.
		// Engine shot callbacks provide that clock; the stop event supplies its separate tail.
		if ( firing && (shotAge += Time.Delta) > Definition.Interval + Math.Max( 0.03f, Time.Delta * 2 ) )
		{
			StopFiring();
		}
		elapsed += Time.Delta;
		var animation = assets.Clips[clip];
		float time = duration > 0 ? Math.Clamp( elapsed / duration, 0, 1 ) * animation.Duration : elapsed % animation.Duration;
		if ( audio is not null && audio.Cues.TryGetValue( clip, out var cueMap ) )
			while ( nextCue < animation.Cues.Count && animation.Cues[nextCue].Time <= time )
			{
				string cue = animation.Cues[nextCue++].Name;
				if ( cueMap.TryGetValue( cue, out var sound ) )
				{
					while ( cueSounds.Count >= 16 ) cueSounds.Dequeue()?.Stop();
					cueSounds.Enqueue( PlaySound( sound ) );
				}
			}
		SamplePose( elapsed );
		if ( duration > 0 && elapsed >= duration )
		{
			bool loop = Definition.Shotgun && weapon?.IsReloading == true && clip is "reload_enter" or "reload";
			string next = loop ? "reload" : clip == "sprint_enter" && weapon?.IsSprinting == true ? "sprint" : "idle";
			PlayClip( next, loop ? Definition.Tactical : 0 );
		}
	}
	protected override void OnDisabled()
	{
		clip = "idle"; elapsed = 0; duration = 0; nextCue = 0;
		firing = false;
		sustainedFire?.Stop( .05f ); sustainedFire = null;
		while ( shotSounds.TryDequeue( out var sound ) ) sound?.Stop( 0.02f );
		while ( cueSounds.TryDequeue( out var cue ) ) cue?.Stop( 0.02f );
		base.OnDisabled();
	}
	public void StopFiring()
	{
		if ( !firing ) return;
		firing = false;
		sustainedFire?.Stop( .05f ); sustainedFire = null;
		PlaySound( audio?.Stop );
	}
	public void UpdateLocomotion( bool sprinting )
	{
		Initialize();
		if ( assets is null ) return;
		if ( sprinting && clip == "idle" && assets.Clips.TryGetValue( "sprint_enter", out var enter ) )
			PlayClip( "sprint_enter", enter.Duration );
		else if ( !sprinting && clip is "sprint" or "sprint_enter" )
			PlayClip( "sprint_exit", assets.Clips["sprint_exit"].Duration );
	}
}
