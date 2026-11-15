using System;

namespace Source1;

/// <summary>Hold-use gameplay for a mounted health or suit charger.</summary>
[Title( "HL2 Charger" )]
[Category( "Half-Life 2" )]
public sealed class Hl2Charger : Component, Component.IPressable
{
	static readonly (string Name, bool Loop)[] Choices = [("idle", true), ("empty", false)];

	[Property] public bool Armour { get; set; }
	[Property] public string CapacitySetting { get; set; }
	[Property] public string StartSound { get; set; }
	[Property] public string LoopSound { get; set; }
	[Property] public string DenySound { get; set; }
	[Property] public float UnitsPerSecond { get; set; } = 10;
	[Property] public float Reach { get; set; } = 120;
	[Sync( SyncFlags.FromHost )] public float Remaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool Charging { get; private set; }
	PlayerController _user;
	TimeSince _lastRequest;
	TimeSince _lastKeepAlive = 10;
	TimeSince _lastDeny = 10;
	TimeSince _chargingTime;
	float _credit;
	bool _wasCharging;
	bool _loopStarted;
	SoundHandle _loop;
	SourceAnimationPlayer _animation;
	string _mountName;

	protected override async void OnStart()
	{
		_mountName = SourceMount.Of( GameObject );
		try
		{
			var values = await SourceSkillData.Read( _mountName );
			if ( !IsValid ) return;
			if ( Networking.IsHost && values.TryGetValue( CapacitySetting ?? "", out var capacity ) ) Remaining = Math.Max( 0, capacity );
			if ( !Application.IsDedicatedServer )
				foreach ( var name in new[] { StartSound, LoopSound, DenySound } )
					if ( SourceSounds.Sound( name, _mountName ) is { } sound )
						foreach ( var wave in sound.Sounds ) await wave.LoadAsync();
		}
		catch ( Exception e ) { Log.Warning( $"Mounted charger: {e.Message}" ); }
	}

	static IDamageable HealthOf( PlayerController player ) => SourceNpcs.HealthOf( player.IsValid() ? player.GameObject : null );

	static IDamageable ArmourOf( PlayerController player ) => SourceNpcs.ArmourOf( player.IsValid() ? player.GameObject : null );

	bool NeedsCharge( PlayerController player ) => HealthOf( player ) is { Health: > 0 } health
		&& (Armour ? ArmourOf( player ) is { } armour && armour.Armour < armour.MaxArmour : health.Health < health.MaxHealth);

	bool InReach( PlayerController player )
	{
		if ( !player.IsValid() || player.EyeTransform.Position.Distance( WorldPosition ) > Reach ) return false;
		var trace = Scene.Trace.Ray( player.EyeTransform.Position, WorldPosition ).IgnoreGameObjectHierarchy( player.GameObject ).Run();
		return !trace.Hit || trace.GameObject?.Root == GameObject.Root;
	}

	bool IPressable.CanPress( IPressable.Event e ) => Enabled;
	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e ) => new IPressable.Tooltip(
		Armour ? "Suit Charger" : "Health Charger", Armour ? "shield" : "medical_services",
		Remaining > 0 ? $"Hold use · {MathF.Ceiling( Remaining )} remaining" : "Empty" );
	bool IPressable.Press( IPressable.Event e )
	{
		RequestCharge( e.Source.GameObject );
		_lastKeepAlive = 0;
		return true;
	}
	bool IPressable.Pressing( IPressable.Event e )
	{
		if ( _lastKeepAlive > 0.2f )
		{
			RequestCharge( e.Source.GameObject );
			_lastKeepAlive = 0;
		}
		return true;
	}
	void IPressable.Release( IPressable.Event e ) => StopCharge( e.Source.GameObject );

	[Rpc.Host]
	void RequestCharge( GameObject source )
	{
		var player = source?.Root.GetComponent<PlayerController>();
		if ( !player.IsValid() || (Rpc.Caller is not null && player.Network.Owner != Rpc.Caller) || !InReach( player ) ) return;
		if ( _user.IsValid() && _user != player ) return;
		if ( Remaining <= 0 || !NeedsCharge( player ) )
		{
			if ( _lastDeny > 0.6f )
			{
				_lastDeny = 0;
				Deny();
			}
			return;
		}
		_user = player;
		_lastRequest = 0;
		Charging = true;
	}

	[Rpc.Host]
	void StopCharge( GameObject source )
	{
		if ( !_user.IsValid() || source?.Root != _user.GameObject.Root
			|| (Rpc.Caller is not null && _user.Network.Owner != Rpc.Caller) ) return;
		Stop();
	}

	void Stop()
	{
		_user = null;
		Charging = false;
		_credit = 0;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Deny()
	{
		if ( !Application.IsDedicatedServer && SourceSounds.Sound( DenySound, _mountName ) is { } sound ) GameObject.PlaySound( sound );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !Charging ) return;
		if ( _lastRequest > 0.6f || !NeedsCharge( _user ) || !InReach( _user ) || Remaining <= 0 )
		{
			Stop();
			return;
		}
		_credit += Math.Max( 0, UnitsPerSecond ) * Time.Delta;
		var requested = MathF.Floor( _credit );
		if ( requested < 1 ) return;
		_credit -= requested;
		var target = Armour ? ArmourOf( _user ) : HealthOf( _user );
		var missing = Armour ? target.MaxArmour - target.Armour : target.MaxHealth - target.Health;
		var amount = Math.Min( requested, Math.Min( Remaining, missing ) );
		if ( Armour ) target.Armour += amount;
		else target.Health += amount;
		Remaining -= amount;
		if ( Remaining <= 0 || !NeedsCharge( _user ) ) Stop();
	}

	protected override void OnUpdate()
	{
		if ( Charging != _wasCharging )
		{
			_wasCharging = Charging;
			StopLoop();
			if ( Charging )
			{
				_chargingTime = 0;
				if ( SourceSounds.Sound( StartSound, _mountName ) is { } sound ) GameObject.PlaySound( sound );
			}
		}
		if ( Charging && !_loopStarted && _chargingTime > 0.5f )
		{
			_loopStarted = true;
			if ( SourceSounds.Sound( LoopSound, _mountName ) is { } sound ) _loop = GameObject.PlaySound( sound );
		}
		var renderer = GetComponentInChildren<SkinnedModelRenderer>();
		if ( !renderer.IsValid() || !renderer.Model.IsValid() ) return;
		_animation ??= new();
		if ( !_animation.Ensure( renderer, Choices ) ) return;
		_animation.Play( Remaining > 0 ? "idle" : "empty" );
		_animation.Tick();
	}

	protected override void OnDisabled()
	{
		if ( Networking.IsHost ) Stop();
		StopLoop();
		_wasCharging = false;
		_animation?.Dispose();
		_animation = null;
	}

	void StopLoop()
	{
		_loop?.Stop();
		_loop = null;
		_loopStarted = false;
	}
}
