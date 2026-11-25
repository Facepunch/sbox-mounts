using Sandbox.Npcs;

namespace Diesel;

/// <summary>Local surveillance using installed camera placements and authored head transforms.</summary>
[Title( "PAYDAY Security Camera" ), Group( "PAYDAY" )]
public sealed class PaydaySecurityCamera : Component, Component.IDamageable
{
	[Property] public Transform Lens { get; set; } = global::Transform.Zero;
	[Property] public ModelRenderer Renderer { get; set; }
	[Property] public float SightRange { get; set; } = 1200;
	[Property] public float FieldOfView { get; set; } = 80;
	[Property] public float DetectionDelay { get; set; } = 1.5f;
	[Property] public float AlertRadius { get; set; } = 2000;
	[Sync( SyncFlags.FromHost )] public float Health { get; private set; } = 30;
	[Sync( SyncFlags.FromHost )] public bool Disabled { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool Activated { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool Detecting { get; private set; }
	GameObject suspect;
	TimeSince visibleFor;
	TimeUntil scan;
	readonly HashSet<GameObject> reported = [];

	protected override void OnUpdate()
	{
		if ( Renderer.IsValid() )
			Renderer.Tint = Disabled ? new Color( .2f, .2f, .2f ) : Activated ? new Color( 1, .3f, .3f )
				: Detecting ? new Color( 1, .75f, .3f ) : Color.White;
		if ( !Networking.IsHost || Disabled || scan > 0 ) return;
		scan = .1f;
		reported.RemoveWhere( p => !p.IsValid() );
		var player = Scene.GetAllComponents<PlayerController>()
			.Where( p => !reported.Contains( p.GameObject ) && PaydayPerception.Armed( p ) && CanSee( p ) )
			.OrderBy( p => p.GameObject == suspect ? 0 : 1 ).ThenBy( p => p.WorldPosition.Distance( WorldPosition ) ).FirstOrDefault();
		Detecting = player.IsValid();
		if ( !Detecting ) { suspect = null; return; }
		if ( suspect != player.GameObject ) { suspect = player.GameObject; visibleFor = 0; }
		if ( visibleFor < DetectionDelay ) return;
		Activated = true; Detecting = false;
		reported.Add( suspect );
		Scene.GetSystem<NpcSystem>().EmitStimulus( StimulusKinds.Gunshot, WorldTransform.ToWorld( Lens ).Position, suspect, AlertRadius, 8 );
		suspect = null;
	}

	public bool CanSee( PlayerController player )
	{
		if ( Disabled || !player.IsValid() || !player.Active || player.Scene != Scene ) return false;
		var lens = WorldTransform.ToWorld( Lens );
		var delta = player.EyePosition - lens.Position;
		if ( delta.Length > SightRange || Vector3.Dot( lens.Rotation.Forward, delta.Normal ) < MathF.Cos( FieldOfView * MathF.PI / 360 ) ) return false;
		var trace = Scene.Trace.Ray( lens.Position, player.EyePosition ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		return !trace.Hit || PaydayInteraction.BelongsTo( trace.GameObject, player.GameObject );
	}

	void IDamageable.OnDamage( in DamageInfo damage )
	{
		if ( !Networking.IsHost || Disabled || damage is null || !float.IsFinite( damage.Damage ) || damage.Damage <= 0 ) return;
		Health = Math.Max( 0, Health - damage.Damage );
		if ( Health > 0 ) return;
		Disabled = true; Detecting = false; suspect = null;
	}

	protected override void OnDisabled()
	{
		suspect = null;
		if ( Networking.IsHost ) Detecting = false;
		base.OnDisabled();
	}
}
