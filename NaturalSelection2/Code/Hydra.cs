using Sandbox.Npcs;
using System;

namespace NS2;

[Title( "Hydra" ), Category( "Natural Selection 2" )]
public sealed class Hydra : BaseNpc, Component.IDamageable
{
	const float Scale = MapLoader.Scale;
	const float BuildTime = 8 / (0.5f * 0.75f);
	const float Range = 17.78f * Scale;
	internal const string AttackSound = "alien/structures/hydra/attack";
	static readonly SparkAnimationGraph.PoseParameters BodyLayer = new( 0 ), FlinchLayer = new( 1 );
	[Sync( SyncFlags.FromHost )] TimeSince age { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose bodyPose { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose flinchPose { get; set; }
	[Sync( SyncFlags.FromHost )] float armor { get; set; } = 1.5f;
	[Sync( SyncFlags.FromHost )] float maxArmor { get; set; } = 5;
	float IDamageable.Armour { get => armor; set => armor = Math.Clamp( value, 0, maxArmor ); }
	float IDamageable.MaxArmour => maxArmor;
	SparkAnimationGraph.Playback graph;
	TimeUntil scan, nextFire, nextAlert, nextMaturityUpdate;
	readonly AlienWounds wounds = new();
	float previousGrowth;
	bool attacking, alerting;
	Vector3 Barrel => WorldPosition + WorldRotation.Up * Scale;

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer )
	{
		root.Tags.Add( "structure" );
		var npc = root.AddComponent<Hydra>();
		npc.Faction = Factions.Monster;
		npc.DisplayName = "Hydra";
		npc.Renderer = renderer;
		npc.MaxHealth = 145;
		npc.Health = 145 * 0.3f;
		npc.EyeHeight = Scale;
		var shape = root.AddComponent<BoxCollider>();
		shape.Scale = renderer.Model.Bounds.Size;
		shape.Center = renderer.Model.Bounds.Center;
	}

	protected override void OnStart()
	{
		base.OnStart();
		if ( !Networking.IsHost ) return;
		age = 0;
		scan = 0.5f;
		if ( Sandbox.Mounting.Directory.Get( "ns2" )?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model )
			graph = new( model.PropGraph, model );
	}

	protected override void OnUpdate()
	{
		if ( Health <= 0 ) return;
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		float growth = Math.Clamp( age / BuildTime, 0, 1 );
		output?.Set( "grow", growth );
		if ( !Networking.IsHost )
		{
			if ( output is null ) return;
			bodyPose.Write( BodyLayer, output, age );
			flinchPose.Write( FlinchLayer, output, age );
			return;
		}
		if ( previousGrowth < growth )
		{
			float before = 0.3f + previousGrowth * 0.7f;
			float after = 0.3f + growth * 0.7f;
			Health = Math.Clamp( after * MaxHealth - (before * MaxHealth - Health), 0, MaxHealth );
			armor = Math.Clamp( after * 5 - (before * 5 - armor), 0, 5 );
			previousGrowth = growth;
		}
		if ( growth >= 1 && MaxHealth < 175 && nextMaturityUpdate <= 0 )
		{
			nextMaturityUpdate = 1;
			float maturity = Math.Clamp( (age - BuildTime) / 140, 0, 1 );
			float healthLimit = 145 + MathF.Floor( 30 * maturity );
			float armorLimit = 5 + MathF.Floor( 20 * maturity );
			Health *= healthLimit / MaxHealth;
			armor *= armorLimit / maxArmor;
			MaxHealth = healthLimit;
			maxArmor = armorLimit;
		}
		bool burning = GameObject.Tags.Has( "burning" );
		if ( scan < 0 )
		{
			scan = 0.5f;
			attacking = false;
			if ( growth >= 1 && NpcConVars.Enabled && !burning && !GameObject.Tags.Has( "electrified" ) ) ScanTargets();
		}
		wounds.Update();
		if ( graph is null ) return;
		graph.Set( "alive", 1 );
		graph.Set( "built", growth >= 1 ? 1 : 0 );
		graph.Set( "attacking", attacking ? 1 : 0 );
		graph.Set( "alerting", alerting ? 1 : 0 );
		graph.Set( "onfire", burning ? 1 : 0 );
		graph.Set( "flinch", wounds.IsFlinching ? 1 : 0 );
		graph.Update( age, output );
		bodyPose = graph.GetPose( 0 );
		flinchPose = graph.GetPose( 1 );
	}

	void ScanTargets()
	{
		GameObject target = null;
		float closest = Range * Range;
		bool enemyNearby = false;
		foreach ( var obj in Scene.FindInPhysics( new Sphere( WorldPosition, Range + 2 * Scale ) ) )
		{
			if ( obj.GetComponentInParent<IDamageable>() is not Component { Active: true } victim ) continue;
			var enemy = victim.GameObject;
			if ( AlienTargeting.IsIgnoredPlayer( enemy ) || GetDisposition( enemy ) != Disposition.Hostile ) continue;
			if ( victim is IDamageable { MaxHealth: > 0, Health: <= 0 } ) continue;
			var aim = CalculateAimPoint( enemy );
			float distance = WorldPosition.DistanceSquared( aim );
			if ( distance < Range * Range && (enemy.Tags.HasAny( "ns2_marine", "ns2_exo", "ns2_lifeform" ) || enemy.GetComponent<PlayerController>().IsValid()) )
				enemyNearby = true;
			if ( distance >= closest || Scene.Trace.Ray( Barrel, aim ).IgnoreGameObjectHierarchy( GameObject )
				.IgnoreGameObjectHierarchy( enemy ).WithoutTags( "ns2_hydra", "trigger", "weapon" ).Run().Hit ) continue;
			target = enemy;
			closest = distance;
		}
		if ( target.IsValid() )
		{
			attacking = !Scene.Trace.Ray( Barrel, WorldPosition ).WithTag( "world" ).Run().Hit;
			if ( attacking && nextFire <= 0 ) Shoot( target );
		}
		else if ( nextAlert <= 0 )
		{
			alerting = enemyNearby;
			if ( alerting ) nextAlert = 2;
		}
	}

	internal static Vector3 SpikeDirection( Vector3 offset, Vector3 velocity, float angleRoll, float radiusRoll )
	{
		float distance = offset.Length / Scale;
		// Spark subtracts the target's travel during the notional flight time before the hitscan.
		var aim = Rotation.LookAt( offset - velocity * (distance / 50) );
		float spread = 1 + Math.Clamp( (distance - 2) / 8, 0, 1 );
		float radius = radiusRoll * MathF.Tan( spread.DegreeToRadian() );
		float angle = angleRoll * MathF.Tau;
		return (aim.Forward + (aim.Left * MathF.Cos( angle ) + aim.Up * MathF.Sin( angle )) * radius).Normal;
	}

	void Shoot( GameObject target )
	{
		nextFire = 0.8f;
		var origin = Barrel;
		var direction = SpikeDirection( CalculateAimPoint( target ) - origin, GetVelocity( target ), Random.Shared.NextSingle(), Random.Shared.NextSingle() );
		var hit = Scene.Trace.Ray( origin, origin + direction * Range ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "ns2_hydra", "trigger" ).UseHitboxes().Run();
		if ( hit.GameObject?.GetComponentInParent<IDamageable>() is Component victim && GetDisposition( victim.GameObject ) == Disposition.Hostile )
			((IDamageable)victim).OnDamage( new DamageInfo { Damage = 15, Origin = origin, Position = hit.HitPosition, Attacker = GameObject, Weapon = GameObject } );
		if ( hit.Hit ) Lerk.ShowSpike( origin, hit.EndPosition );
		AttackEffect( origin );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void AttackEffect( Vector3 origin )
	{
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{AttackSound}.sound" ) is { } sound ) Sound.Play( sound, origin );
	}

	protected override void OnDamaging( IDamageable.DamageEvent e ) => AlienArmor.Absorb( e, armor );

	protected override void OnDamaged( IDamageable.DamageEvent e )
	{
		if ( e.AcceptedDamage > 0 ) armor -= wounds.ApplyDamage( e, armor, MaxHealth, maxArmor );
	}
}
