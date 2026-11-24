using Sandbox.Npcs;
using Sandbox.Npcs.Schedules;

namespace Diesel;

[Title( "PAYDAY Civilian" ), Group( "PAYDAY" )]
public sealed class PaydayCivilian : BaseNpc, Component.IPressable
{
	[Property] public bool Wander { get; set; } = true;
	[Sync( SyncFlags.FromHost )] public bool Surrendered { get; private set; }
	[Sync( SyncFlags.FromHost )] public GameObject Leader { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool Panicking { get; private set; }
	GameObject threat;
	TimeUntil fear;

	protected override void OnUpdate()
	{
		if ( Networking.IsHost && Health > 0 )
		{
			if ( !Surrendered && Senses?.Disturbance is { } sound && sound.Kind is StimulusKinds.Gunshot or StimulusKinds.Death )
				Scare( sound.Source );
			Panicking = !Surrendered && fear > 0;
		}
		base.OnUpdate();
	}

	public void Scare( GameObject source )
	{
		if ( !Networking.IsHost || Health <= 0 || Surrendered || !source.IsValid() ) return;
		threat = source; fear = 10;
		if ( !Panicking ) { Panicking = true; EndCurrentSchedule(); }
	}

	protected override void OnDamaged( IDamageable.DamageEvent e )
	{
		base.OnDamaged( e );
		Scare( e.Instigator );
	}

	protected override BaseSchedule SelectSchedule()
	{
		if ( Surrendered )
		{
			if ( Leader.IsValid() ) { var follow = GetSchedule<FollowSchedule>(); follow.Target = Leader; return follow; }
			return GetSchedule<IdleSchedule>();
		}
		if ( Panicking && threat.IsValid() )
		{
			var alarm = Scene.GetAllComponents<PaydayAlarm>().Where( a => !a.Activated && !a.Disabled )
				.OrderBy( a => a.WorldPosition.Distance( WorldPosition ) ).FirstOrDefault();
			if ( alarm.IsValid() && alarm.WorldPosition.Distance( WorldPosition ) < 800 )
			{
				var report = GetSchedule<PaydayReportAlarm>();
				report.Alarm = alarm; report.Threat = threat; return report;
			}
			var flee = GetSchedule<FleeSchedule>(); flee.Source = threat; flee.PanicLevel = .7f; return flee;
		}
		return Wander ? GetSchedule<WanderSchedule>() : GetSchedule<IdleSchedule>();
	}

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e ) =>
		new IPressable.Tooltip( !Surrendered ? "Get down!" : Leader.IsValid() ? "Stay here" : "Follow me", "pan_tool", null );
	bool IPressable.CanPress( IPressable.Event e ) => Health > 0;
	bool IPressable.Press( IPressable.Event e ) { Command( e.Source.GameObject ); return true; }

	[Rpc.Host]
	public void Command( GameObject source )
	{
		if ( !PaydayInteraction.CanUse( this, source ) || Health <= 0 ) return;
		if ( !Surrendered ) { Surrendered = true; Panicking = false; }
		else Leader = Leader.IsValid() ? null : source.Root;
		EndCurrentSchedule();
		Movement?.Stop();
	}
}

static class PaydayInteraction
{
	public static bool CanUse( Component target, GameObject source )
	{
		if ( !source.IsValid() || source.Scene != target.Scene ) return false;
		var player = source.Root.GetComponent<PlayerController>();
		if ( !player.IsValid() || (Rpc.Caller is not null && player.Network.Owner != Rpc.Caller) ) return false;
		if ( player.WorldPosition.Distance( target.WorldPosition ) > 150 ) return false;
		var tr = target.Scene.Trace.Ray( player.EyePosition, target.WorldPosition + Vector3.Up * 24 )
			.IgnoreGameObjectHierarchy( player.GameObject ).WithoutTags( "trigger" ).Run();
		return !tr.Hit || tr.GameObject.Root == target.GameObject.Root;
	}
}
