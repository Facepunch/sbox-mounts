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
		(armor, flinchEnd) = SupportDamage.Apply( hit, this, armor, MaxHealth, flinchEnd );

	protected override Vector3 GetEngagementPoint() =>
		Renderer.WorldTransform.PointToWorld( Renderer.GetAttachment( "target", false )?.Position ?? Vector3.Zero );

	readonly Dictionary<Marine, TimeUntil> resupplied = [];
	TimeUntil nextScan;
	const float UseRange = 2 * MapLoader.Scale;
	static readonly (Vector3 Offset, string Parameter)[] Sides =
	[
		(Vector3.Left * UseRange, "log_w"), (Vector3.Backward * UseRange, "log_n"),
		(Vector3.Forward * UseRange, "log_s"), (Vector3.Right * UseRange, "log_e")
	];
	[Sync( SyncFlags.FromHost )] int openSides { get; set; }
	[Sync( SyncFlags.FromHost )] bool deployed { get; set; }
	SparkAnimationGraph.Playback graph;
	Action<string> onTag;
	[Sync( SyncFlags.FromHost )] TimeSince age { get; set; }
	[Sync( SyncFlags.FromHost )] TimeUntil flinchEnd { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.BodyPose pose { get; set; }

	protected override void OnStart()
	{
		base.OnStart();
		if ( Networking.IsHost )
		{
			age = 0;
			graph = CreatePlayback( Renderer.Model.ResourcePath );
			onTag = tag => deployed |= tag == "deploy_end";
		}
		nextScan = 0.3f;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Health <= 0 || GameObject.IsDestroyed ) return;
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		float intensity = AlienWounds.Intensity( flinchEnd );
		output?.Set( "intensity", intensity );
		if ( deployed && IsPowered && IsBuilt && output is not null )
		{
			for ( int i = 0; i < Sides.Length; i++ )
			{
				string parameter = Sides[i].Parameter;
				float target = (openSides & (1 << i)) != 0 ? 1 : 0;
				if ( output.TryGetFloat( parameter, out var amount ) && amount != target )
					output.Set( parameter, amount.Approach( target, Time.Delta * 2 ) );
			}
		}
		if ( !Networking.IsHost )
		{
			pose.Write( output, age );
			return;
		}
		if ( graph is not null )
		{
			graph.Set( "built", IsBuilt ? 1 : 0 );
			graph.Set( "active", IsBuilt ? 1 : 0 );
			graph.Set( "powered", IsPowered ? 1 : 0 );
			graph.Set( "alive", 1 );
			graph.Set( "flinch", intensity > 0 ? 1 : 0 );
			graph.Update( age, output, onTag );
			pose = graph.GetBodyPose();
		}
		if ( nextScan > 0 ) return;
		nextScan = 0.3f;
		foreach ( var (marine, cooldown) in resupplied )
		{
			if ( cooldown <= 0 || !marine.IsValid() ) resupplied.Remove( marine );
		}
		openSides = 0;
		if ( !IsBuilt || !IsPowered ) return;
		foreach ( var marine in Scene.GetAllComponents<Marine>() )
		{
			CheckSides( marine, UseRange );
			TryResupply( marine );
		}
		foreach ( var exo in Scene.GetAllComponents<Exo>() )
			CheckSides( exo, UseRange + 1.5f * MapLoader.Scale );
	}

	void CheckSides( Ns2Npc visitor, float range )
	{
		if ( visitor.Faction != Faction || visitor.Health <= 0 ) return;
		var local = WorldTransform.PointToLocal( visitor.WorldPosition );
		if ( local.LengthSquared >= (UseRange + range) * (UseRange + range) ) return;
		for ( int i = 0; i < Sides.Length; i++ )
		{
			if ( local.DistanceSquared( Sides[i].Offset ) < range * range ) openSides |= 1 << i;
		}
	}

	protected override GameObject CreateRagdoll() => graph is null ? NpcCorpse.Attach( base.CreateRagdoll(), 1.5f ) :
		NpcCorpse.Animate( Renderer, graph, age );

	void TryResupply( Marine marine )
	{
		if ( marine.Faction != Faction || marine.Health <= 0 || resupplied.ContainsKey( marine ) ) return;
		var offset = WorldPosition - marine.WorldPosition;

		if ( offset.LengthSquared >= UseRange * UseRange || marine.AimDirection.Dot( offset.Normal ) <= 0.75f ) return;

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

	internal static Armory FindNearest( Ns2Npc visitor, MapLocations locations, ref Armory previous, out float best )
	{
		Armory nearest = null;
		best = float.MaxValue;
		foreach ( var armory in visitor.Scene.GetAllComponents<Armory>() )
		{
			if ( !armory.IsBuilt || !armory.IsPowered || armory.Health <= 0 || armory.Faction != visitor.Faction ) continue;
			float distance = locations.IsValid() ? locations.WalkDistance( visitor.WorldPosition, armory.WorldPosition ) : visitor.WorldPosition.Distance( armory.WorldPosition );
			if ( armory == previous ) distance *= 0.9f;
			if ( distance < best ) (nearest, best) = (armory, distance);
		}
		if ( nearest.IsValid() ) previous = nearest;
		return nearest;
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
