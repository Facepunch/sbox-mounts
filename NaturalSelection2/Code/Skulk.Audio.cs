using System;
using Sandbox.Npcs;

namespace NS2;

public sealed partial class Skulk
{
	Action<string> worldTag;
	readonly AlienWounds wounds = new();

	internal static void RegisterSounds( MountContext context )
	{
		static void Add( MountContext mount, string id ) => mount.Add( ResourceType.GameResource, $"sounds/{id}.sound", new SparkSoundLoader( id ) );
		Add( context, "alien/common/xenocide_start" );
		Add( context, "alien/common/xenocide_end" );
		foreach ( var cue in new[] { "spawn", "death", "bite", "jump", "jump_good", "jump_best", "land", "bite_alt", "parasite", "bite_kill", "wound", "wound_serious", "bite_hit_marine", "full_speed" } )
			Add( context, $"alien/skulk/{cue}" );
		foreach ( var material in new[] { "metal", "thin_metal", "organic", "rock" } )
		{
			Add( context, $"alien/skulk/bite_hit_{material}" );
			Add( context, $"materials/{material}/skulk_step" );
			Add( context, $"materials/{material}/skulk_step_for_enemy" );
		}
	}

	SoundHandle dash;
	SoundHandle xenocideCharge;
	TimeSince dashStarted;
	bool dashing;

	void UpdateDashSound()
	{
		if ( Application.IsDedicatedServer ) return;
		bool fast = Health > 0 && Animation.MoveVelocity.WithZ( 0 ).Length > 8.7f * MapLoader.Scale;
		if ( fast != dashing )
		{
			dashing = fast;
			dashStarted = 0;
			if ( !fast ) StopDash();
		}
		if ( !fast || dashStarted <= 1 ) return;
		if ( dash is null ) dash = PlayLocal( "alien/skulk/full_speed", WorldPosition );
		if ( dash is not null ) dash.Position = WorldPosition;
	}

	void StopDash()
	{
		dash?.Stop();
		dash = null;
	}

	protected override void OnDisabled()
	{
		StopDash();
		xenocideCharge?.Stop();
		dashing = false;
		base.OnDisabled();
	}

	static SoundHandle PlayLocal( string id, Vector3 position )
	{
		var sound = GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{id}.sound" );
		return sound is not null ? Sound.Play( sound, position ) : null;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayCombatSound( string id, Vector3 position ) => PlayLocal( id, position );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayXenocideCharge()
	{
		xenocideCharge = GameObject.PlaySound( GameResource.Load<SoundEvent>( "mount://ns2/sounds/alien/common/xenocide_start.sound" ) );
	}

	internal void PlayMovementSound( string cue ) => PlayCombatSound( $"alien/skulk/{cue}", WorldPosition );

	internal void OnLand( float impact, float planarSpeed )
	{
		if ( impact > 5 * MapLoader.Scale && (!sneaking || planarSpeed > 7 * MapLoader.Scale) )
			PlayMovementSound( "land" );
	}

	void PlayVoice( string cue ) => PlayCombatSound( $"alien/skulk/{cue}", EyePosition );

	void OnWorldTag( string tag )
	{
		if ( tag is not ("step" or "step_run" or "step_sprint") ) return;
		if ( !Animation.IsGrounded ) return;
		var material = mover.ContactSurface?.ResourceName switch
		{
			"ns2_rock" => "rock",
			"ns2_organic" or "ns2_infestation" => "organic",
			"ns2_thin_metal" or "ns2_light_metal" => "thin_metal",
			_ => "metal"
		};
		PlayFootstep( material, WorldPosition );
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	void PlayFootstep( string material, Vector3 position )
	{
		var listener = Scene.Camera?.GetComponentInParent<PlayerController>();
		var cue = !listener.IsValid() || GetDisposition( listener.GameObject ) == Disposition.Hostile ? "skulk_step_for_enemy" : "skulk_step";
		PlayLocal( $"materials/{material}/{cue}", position );
	}

	void PlayBiteImpact( SceneTraceResult hit )
	{
		var material = hit.Surface?.ResourceName switch
		{
			"ns2_organic" or "ns2_infestation" or "ns2_membrane" => "organic",
			"ns2_thin_metal" or "ns2_light_metal" => "thin_metal",
			"ns2_rock" => "rock",
			"ns2_armor" or "ns2_flesh" or "flesh" => "marine",
			_ when hit.GameObject?.GetComponentInParent<PlayerController>() is not null => "marine",
			_ => "metal"
		};
		PlayCombatSound( $"alien/skulk/bite_hit_{material}", hit.HitPosition );
	}

	protected override void OnDamaged( IDamageable.DamageEvent e )
	{
		ConsumeArmor( e.AcceptedDamage, e.DamageInfo );
		var fraction = e.AcceptedDamage / MaxHealth;
		wounds.Hit( fraction, fraction );
	}

	void UpdateFlinch()
	{
		if ( wounds.Update() is { } cue ) PlayVoice( cue );
	}

	protected override void OnDeath( IDamageable.DeathEvent e )
	{
		PlayVoice( "death" );
		base.OnDeath( e );
	}
}
