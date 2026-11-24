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
	readonly Dictionary<Component, float> avoided = [];

	internal bool CanVisit( Component target ) => !avoided.TryGetValue( target, out var until ) || Time.Now >= until;
	internal void Avoid( Component target )
	{
		foreach ( var old in avoided.Keys.Where( k => !k.IsValid() ).ToArray() ) avoided.Remove( old );
		if ( target.IsValid() ) avoided[target] = Time.Now + 20;
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost && Health > 0 )
		{
			if ( !Surrendered && Senses?.Disturbance is { } sound && sound.Kind is StimulusKinds.Gunshot or StimulusKinds.Death )
				Scare( sound.Source );
			Panicking = !Surrendered && fear > 0;
			if ( !Leader.IsValid() ) Leader = null;
		}
		base.OnUpdate();
	}

	public void Scare( GameObject source )
		=> Scare( source, true );

	void Scare( GameObject source, bool warnOthers )
	{
		if ( !Networking.IsHost || Health <= 0 || Surrendered || !source.IsValid() ) return;
		threat = source; fear = 10;
		if ( Panicking ) return;
		Panicking = true; EndCurrentSchedule();
		if ( !warnOthers ) return;
		foreach ( var other in Scene.GetAllComponents<PaydayCivilian>() )
		{
			if ( other == this || !other.Enabled || other.WorldPosition.Distance( WorldPosition ) > 300 ) continue;
			var trace = Scene.Trace.Ray( WorldPosition + Vector3.Up * 48, other.WorldPosition + Vector3.Up * 48 )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
			if ( !trace.Hit || PaydayInteraction.BelongsTo( trace.GameObject, other.GameObject ) )
				other.Scare( source, false );
		}
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
			var alarm = Scene.GetAllComponents<PaydayAlarm>().Where( a => a.CanReserve( GameObject ) && CanVisit( a ) )
				.OrderBy( a => a.WorldPosition.Distance( WorldPosition ) ).FirstOrDefault();
			if ( alarm.IsValid() && alarm.WorldPosition.Distance( WorldPosition ) < 800 )
			{
				var report = GetSchedule<PaydayReportAlarm>();
				report.Alarm = alarm; report.Threat = threat; return report;
			}
			var exit = Scene.GetAllComponents<PaydayEscapePoint>().Where( e => e.Enabled && CanVisit( e ) )
				.OrderBy( e => e.WorldPosition.Distance( WorldPosition ) ).FirstOrDefault();
			if ( exit.IsValid() )
			{
				var escape = GetSchedule<PaydayEscape>();
				escape.Exit = exit; return escape;
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
		return !tr.Hit || BelongsTo( tr.GameObject, target.GameObject );
	}

	internal static bool BelongsTo( GameObject hit, GameObject target )
	{
		for ( var current = hit; current.IsValid() && current is not Scene; current = current.Parent )
			if ( current == target ) return true;
		return false;
	}
}
