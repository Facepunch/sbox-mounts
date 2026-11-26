using Sandbox.Npcs;
using System;

namespace NS2;

public abstract class AlienStructure : AlienNpc, Component.IDamageable
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
	Vector3 modelOrigin;
	protected Vector3 ModelOrigin => Renderer.WorldTransform.PointToWorld( modelOrigin );
	[Sync( SyncFlags.FromHost )] private protected TimeUntil flinchEnd { get; private set; }
	protected float FlinchIntensity => AlienWounds.Intensity( flinchEnd );
	SupportPain pain;
	TimeUntil nextMaturityUpdate;
	TimeSince maturityTick;
	float previousGrowth, maturity, publishedMaturity, maturityStart, lastSprayTime;
	protected float Growth => Math.Clamp( age * Stats.AutoRate / Stats.BuildSeconds + assistedGrowth + EnzymeGrowth, 0, 1 );
	float EnzymeGrowth => enzymeActive ? enzymeAge * 0.75f / Stats.BuildSeconds : 0;
	internal bool HasEnzyme => enzymeActive;

	internal float DistanceFrom( AlienLifeform alien, MapLocations locations ) => locations.IsValid()
		? locations.WalkDistance( alien.WorldPosition, WorldPosition ) : alien.WorldPosition.Distance( WorldPosition );

	protected override Vector3 GetEngagementPoint() => Renderer.WorldTransform.PointToWorld( Renderer.GetAttachment( "target", false )?.Position ?? modelOrigin );

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
		float Gain( float total ) => MathF.Floor( total * maturity ) - MathF.Floor( total * publishedMaturity );
		AdjustHealthLimit( Gain( Stats.MatureHealth - Stats.Health ) );
		float armorGain = Gain( Stats.MatureArmor - Stats.Armor );
		if ( armorGain != 0 )
		{
			float fraction = maxArmor > 0 ? armor / maxArmor : 1;
			maxArmor += armorGain;
			armor = maxArmor * fraction;
		}
		publishedMaturity = maturity;
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
		if ( Sandbox.Mounting.Directory.Get( "ns2" )?.GetByPath( renderer.Model.ResourcePath ) is ModelLoader model )
			model.AddMovementCollision( renderer, trigger: npc is Hydra );
	}

	protected override void OnStart()
	{
		base.OnStart();
		pain = GetComponent<SupportPain>();
		pain?.StartListening( flinchEnd );
		if ( !Networking.IsHost ) return;
		age = 0;
		maxArmor = Stats.Armor;
		armor = maxArmor * 0.3f;
		if ( Sandbox.Mounting.Directory.Get( "ns2" )?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model )
		{
			modelOrigin = model.BoundsCenter;
			graph = new( model.PropGraph, model );
		}
	}

	protected override void OnUpdate()
	{
		if ( Health <= 0 || GameObject.IsDestroyed ) return;
		pain?.UpdateSound( flinchEnd );
		if ( !Networking.IsHost ) return;
		UpdateBiomass();
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

	protected override void OnDamaging( IDamageable.DamageEvent e ) => SupportDamage.Absorb( e, this, armor );

	protected override void OnDamaged( IDamageable.DamageEvent e )
	{
		(armor, flinchEnd) = SupportDamage.Apply( e, this, armor, MaxHealth, flinchEnd );
	}
}
