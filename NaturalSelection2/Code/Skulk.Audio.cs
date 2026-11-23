using System;
using Sandbox.Npcs;

namespace NS2;

public sealed partial class Skulk
{
	Action<string> worldTag;
	float flinchIntensity, damageFraction;
	TimeUntil nextFlinch = 0.3f;

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

	internal void PlayMovementSound( string cue ) => PlayCombatSound( $"alien/skulk/{cue}", WorldPosition );

	void PlayVoice( string cue ) => PlayCombatSound( $"alien/skulk/{cue}", EyePosition );

	void OnWorldTag( string tag )
	{
		if ( tag is not ("step" or "step_run" or "step_sprint") ) return;
		var surface = mover.ContactSurface;
		if ( Networking.IsHost )
		{
			if ( !Animation.IsGrounded ) return;
		}
		else
		{
			var up = mover.ContactNormal;
			if ( up.IsNearlyZero() ) return;
			var center = WorldPosition + Vector3.Up * SkulkMovement.Radius;
			var ground = Scene.Trace.Ray( center + up * 2, center - up * 8 ).Radius( SkulkMovement.Radius - 1 )
				.IgnoreGameObjectHierarchy( GameObject ).Run();
			if ( !ground.Hit ) return;
			surface = ground.Surface;
		}
		var material = surface?.ResourceName switch
		{
			"ns2_rock" => "rock",
			"ns2_organic" or "ns2_infestation" => "organic",
			"ns2_thin_metal" or "ns2_light_metal" => "thin_metal",
			_ => "metal"
		};
		var listener = Scene.Camera?.GetComponentInParent<PlayerController>();
		var cue = !listener.IsValid() || GetDisposition( listener.GameObject ) == Disposition.Hostile ? "skulk_step_for_enemy" : "skulk_step";
		PlayLocal( $"materials/{material}/{cue}", WorldPosition );
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
		ConsumeArmor( e.AcceptedDamage );
		var fraction = e.AcceptedDamage / MaxHealth;
		damageFraction += fraction;
		flinchIntensity = Math.Clamp( flinchIntensity + fraction / 0.2f, 0.2f, 1 );
	}

	void UpdateFlinch()
	{
		flinchIntensity = Math.Max( 0, flinchIntensity - 0.4f * Time.Delta );
		if ( nextFlinch > 0 ) return;
		nextFlinch = 0.3f;
		if ( damageFraction <= 0 ) return;
		PlayVoice( damageFraction > 0.35f || flinchIntensity > 0.35f ? "wound_serious" : "wound" );
		damageFraction = 0;
	}

	protected override void OnDeath( IDamageable.DeathEvent e )
	{
		PlayVoice( "death" );
		base.OnDeath( e );
	}
}
