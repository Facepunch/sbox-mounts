using Sandbox.Npcs;

namespace Diesel;

/// <summary>PDTH tank: shotgun, melee immunity and a destructible visor over the head weak point.</summary>
[Title( "PAYDAY Bulldozer" ), Group( "PAYDAY" )]
public sealed class PaydayBulldozer : PaydayCombatNpc
{
	// Original tank sequence: glass endurance damage=20. Use the mount's 10x damage scale.
	[Property] public float VisorDurability { get; set; } = 200;
	[Sync( SyncFlags.FromHost )] public float VisorDamage { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool VisorBroken { get; private set; }
	[Property] public SoundEvent VisorBreakSound { get; set; }

	protected override void OnDamaging( IDamageable.DamageEvent e )
	{
		base.OnDamaging( e );
		if ( e.DamageInfo.Tags.Contains( "melee" ) ) { e.Cancelled = true; return; }
		if ( e.DamageInfo.Tags.Contains( "explosion" ) ) return;
		if ( !e.DamageInfo.Tags.Contains( "payday_visor" ) ) return;
		if ( VisorBroken ) { e.AcceptedDamage *= 2; return; }
		if ( !float.IsFinite( e.AcceptedDamage ) || e.AcceptedDamage <= 0 ) { e.Cancelled = true; return; }
		VisorDamage += e.AcceptedDamage;
		if ( VisorDamage >= VisorDurability )
		{
			VisorBroken = true;
			UpdateVisor();
			PlayVisorBreak();
		}
		// This hit retains ordinary body damage; the exposed-head bonus starts on the next hit.
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayVisorBreak()
	{
		if ( VisorBreakSound is not null ) Sound.Play( VisorBreakSound, EyePosition );
	}

	void UpdateVisor()
	{
		if ( Renderer.IsValid() ) Renderer.SetBodyGroup( "visor", VisorBroken ? 1 : 0 );
	}

	protected override void OnUpdate()
	{
		UpdateVisor();
		base.OnUpdate();
	}
}
