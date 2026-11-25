using Sandbox.Npcs;
using System;

namespace NS2;

public abstract class AlienStructure : BaseNpc, Component.IDamageable
{
	protected readonly record struct GrowthStats( float BuildSeconds, float AutoRate, float Health, float Armor, float MatureSeconds, float MatureHealth, float MatureArmor );
	protected abstract GrowthStats Stats { get; }
	protected const float Scale = MapLoader.Scale;
	[Sync( SyncFlags.FromHost )] protected TimeSince age { get; private set; }
	[Sync( SyncFlags.FromHost )] float assistedGrowth { get; set; }
	[Sync( SyncFlags.FromHost )] float armor { get; set; }
	[Sync( SyncFlags.FromHost )] float maxArmor { get; set; }
	[Sync( SyncFlags.FromHost )] bool enzymeActive { get; set; }
	[Sync( SyncFlags.FromHost )] TimeSince enzymeAge { get; set; }
	TimeUntil enzymeExpires;
	float IDamageable.Armour { get => armor; set => armor = Math.Clamp( value, 0, maxArmor ); }
	float IDamageable.MaxArmour => maxArmor;
	private protected SparkAnimationGraph.Playback graph;
	[Sync( SyncFlags.FromHost )] TimeUntil flinchEnd { get; set; }
	protected float FlinchIntensity => AlienWounds.Intensity( flinchEnd );
	TimeUntil nextMaturityUpdate;
	TimeSince maturityTick;
	float previousGrowth, maturity, maturityStart, lastSprayTime;
	protected float Growth => Math.Clamp( age * Stats.AutoRate / Stats.BuildSeconds + assistedGrowth + EnzymeGrowth, 0, 1 );
	float EnzymeGrowth => enzymeActive ? enzymeAge * 0.75f / Stats.BuildSeconds : 0;
	internal bool HasEnzyme => enzymeActive;

	internal void RefreshEnzyme()
	{
		if ( !enzymeActive )
		{
			enzymeActive = true;
			enzymeAge = 0;
		}
		enzymeExpires = 0.3f;
	}

	internal bool IsBuilt => Growth >= 1;

	internal float SprayHealingLimit => IsBuilt ? 60 :
		Math.Clamp( (MaxHealth + maxArmor) * (0.3f + 0.7f * previousGrowth) - Health - armor, 0, 60 );

	internal void OnHealSpray()
	{
		if ( !IsBuilt )
		{
			assistedGrowth += Math.Clamp( 0.9f + Time.Now - lastSprayTime, 0.9f, 1.8f ) / Stats.BuildSeconds;
			lastSprayTime = Time.Now;
			ApplyGrowth( Growth );
		}
		GameObject.Tags.Remove( "burning" );
		if ( IsBuilt ) AddMaturity( 1, true );
	}

	void ApplyGrowth( float growth )
	{
		if ( growth <= previousGrowth ) return;
		float before = 0.3f + previousGrowth * 0.7f;
		float after = 0.3f + growth * 0.7f;
		Health = Math.Clamp( after * MaxHealth - (before * MaxHealth - Health), 0, MaxHealth );
		armor = Math.Clamp( after * maxArmor - (before * maxArmor - armor), 0, maxArmor );
		previousGrowth = growth;
		if ( growth == 1 ) maturityTick = 0;
	}

	void AddMaturity( float seconds, bool publish )
	{
		if ( maturity >= 1 ) return;
		float amount = seconds / Stats.MatureSeconds;
		// The native soft cap is 1.5 times the combined built and nutrient mist rates.
		float uncapped = Math.Clamp( (Time.Now - maturityStart) * (4.5f / Stats.MatureSeconds) / (maturity + amount), 0, 1 );
		maturity = Math.Min( 1, maturity + amount * (0.25f + 0.75f * uncapped) );
		if ( !publish && maturity < 1 && nextMaturityUpdate > 0 ) return;
		nextMaturityUpdate = 1;
		float healthLimit = Stats.Health + MathF.Floor( (Stats.MatureHealth - Stats.Health) * maturity );
		float armorLimit = Stats.Armor + MathF.Floor( (Stats.MatureArmor - Stats.Armor) * maturity );
		Health *= healthLimit / MaxHealth;
		armor *= armorLimit / maxArmor;
		MaxHealth = healthLimit;
		maxArmor = armorLimit;
	}

	protected static void Configure<T>( GameObject root, SkinnedModelRenderer renderer, string name ) where T : AlienStructure, new()
	{
		root.Tags.Add( "structure" );
		var npc = root.AddComponent<T>();
		npc.Faction = Factions.Monster;
		npc.DisplayName = name;
		npc.Renderer = renderer;
		npc.MaxHealth = npc.Stats.Health;
		npc.Health = npc.MaxHealth * 0.3f;
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
		maxArmor = Stats.Armor;
		armor = maxArmor * 0.3f;
		if ( Sandbox.Mounting.Directory.Get( "ns2" )?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model )
			graph = new( model.PropGraph, model );
	}

	protected override void OnUpdate()
	{
		if ( Health <= 0 || GameObject.IsDestroyed ) return;
		if ( !Networking.IsHost ) return;
		if ( enzymeActive && (enzymeExpires <= 0 || IsBuilt) )
		{
			assistedGrowth += EnzymeGrowth;
			enzymeActive = false;
		}
		ApplyGrowth( Growth );
		if ( IsBuilt && maturity < 1 && maturityTick >= 0.1f )
		{
			if ( maturityStart == 0 ) maturityStart = Time.Now;
			AddMaturity( maturityTick, false );
			maturityTick = 0;
		}
	}

	protected override void OnDamaging( IDamageable.DamageEvent e ) => AlienArmor.Absorb( e, armor );

	protected override void OnDamaged( IDamageable.DamageEvent e )
	{
		if ( e.AcceptedDamage > 0 ) (armor, flinchEnd) = AlienWounds.ApplyFlinch( e, armor, MaxHealth, flinchEnd );
	}
}
