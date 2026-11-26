using Sandbox.Npcs;
using System;

namespace NS2;

[Title( "Armory" ), Category( "Natural Selection 2" )]
public sealed class Armory : Ns2Npc, Component.IDamageable
{
	[Property, Sync( SyncFlags.FromHost )] public bool IsBuilt { get; set; } = true;
	[Property, Sync( SyncFlags.FromHost )] public bool IsPowered { get; set; } = true;

	[Sync( SyncFlags.FromHost )] float armor { get; set; } = 200;
	float IDamageable.Armour { get => armor; set => armor = Math.Clamp( value, 0, 200 ); }
	float IDamageable.MaxArmour => 200;

	protected override void OnDamaging( IDamageable.DamageEvent hit ) => SupportDamage.Absorb( hit, this, armor );

	protected override void OnDamaged( IDamageable.DamageEvent hit ) =>
		(armor, _) = SupportDamage.Apply( hit, this, armor, MaxHealth, default );

	protected override Vector3 GetEngagementPoint() =>
		Renderer.WorldTransform.PointToWorld( Renderer.GetAttachment( "target", false )?.Position ?? Vector3.Zero );

	readonly Dictionary<Marine, TimeUntil> resupplied = [];
	TimeUntil nextScan;

	protected override void OnStart()
	{
		base.OnStart();
		nextScan = 0.3f;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Networking.IsHost || Health <= 0 || nextScan > 0 ) return;
		nextScan = 0.3f;
		foreach ( var (marine, cooldown) in resupplied )
		{
			if ( cooldown <= 0 || !marine.IsValid() ) resupplied.Remove( marine );
		}
		if ( !IsBuilt || !IsPowered ) return;
		foreach ( var marine in Scene.GetAllComponents<Marine>() )
			TryResupply( marine );
	}

	void TryResupply( Marine marine )
	{
		if ( marine.Faction != Faction || marine.Health <= 0 || resupplied.ContainsKey( marine ) ) return;
		var offset = WorldPosition - marine.WorldPosition;
		const float range = 2 * MapLoader.Scale;
		if ( offset.LengthSquared >= range * range || marine.AimDirection.Dot( offset.Normal ) <= 0.75f ) return;

		bool heal = marine.Health < marine.MaxHealth;
		if ( heal )
		{
			marine.Health = Math.Min( marine.Health + 25, marine.MaxHealth );
			marine.GetComponent<LerkPoison>()?.Destroy();
		}
		bool ammo = marine.ResupplyAmmo();
		if ( !heal && !ammo ) return;
		resupplied[marine] = 0.8f;
		ShowResupply( marine.WorldPosition, heal, ammo );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ShowResupply( Vector3 origin, bool heal, bool ammo )
	{
		if ( Application.IsHeadless ) return;
		if ( heal ) PlaySupplyEffect( origin, "health" );
		if ( ammo ) PlaySupplyEffect( origin, "pickup_ammo" );
	}

	static void PlaySupplyEffect( Vector3 origin, string cue )
	{
		CinematicLoader.Play( "cinematics/marine/spawn_item.cinematic", origin, Rotation.Identity );
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/marine/common/{cue}.sound" ) is { } sound )
			Sound.Play( sound, origin );
	}

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer )
	{
		root.Tags.Add( "structure" );
		var armory = root.AddComponent<Armory>();
		armory.DisplayName = "Armory";
		armory.Faction = Factions.Ally;
		armory.Renderer = renderer;
		armory.MaxHealth = armory.Health = 1500;
		if ( Sandbox.Mounting.Directory.Get( "ns2" )?.GetByPath( renderer.Model.ResourcePath ) is ModelLoader model )
			model.AddMovementCollision( renderer, trigger: false );
	}
}
