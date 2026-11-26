using System;

namespace NS2;

[Title( "Hive" ), Category( "Natural Selection 2" )]
public sealed class Hive : AlienStructure
{
	internal const float HealRadius = 12.7f * Scale;
	internal const string PainSound = "alien/structures/hive_wound";
	protected override GrowthStats Stats => new( 180, 0.5f, 4000, 750, 260, 6000, 1400 );
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.BodyPose pose { get; set; }
	TimeUntil nextHeal;

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer ) => Configure<Hive>( root, renderer, "Hive", "hive_idle" );

	internal static Hive FindNearest( AlienLifeform alien, out float distance )
	{
		Hive best = null;
		distance = float.PositiveInfinity;
		foreach ( var hive in alien.Scene.GetAllComponents<Hive>() )
		{
			if ( !hive.IsBuilt || hive.Faction != alien.Faction || hive.Health <= 0 ) continue;
			float candidate = alien.WorldPosition.Distance( hive.WorldPosition );
			if ( candidate < distance ) (best, distance) = (hive, candidate);
		}
		return best;
	}

	protected override void OnUpdate()
	{
		if ( Health <= 0 || GameObject.IsDestroyed ) return;
		base.OnUpdate();
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		if ( !Networking.IsHost )
		{
			pose.Write( output, age );
			return;
		}
		if ( nextHeal < 0 )
		{
			nextHeal = 1;
			if ( IsBuilt && !GameObject.Tags.Has( "burning" ) )
			{
				float multiplier = GameObject.Tags.Has( "electrified" ) ? 0.75f : 1;
				foreach ( var alien in Scene.GetAllComponents<AlienLifeform>() )
				{
					if ( alien.Faction != Faction || alien.Health <= 0 || WorldPosition.DistanceSquared( alien.WorldPosition ) >= HealRadius * HealRadius ) continue;
					AlienHealing.Restore( alien, Math.Max( 10, alien.MaxHealth * 0.08f ) * multiplier, 1 );
				}
			}
		}
		if ( graph is null ) return;
		graph.Set( "built", IsBuilt ? 1 : 0 );
		graph.Set( "alive", 1 );
		graph.Set( "flinch", FlinchIntensity > 0 ? 1 : 0 );
		graph.Set( "onfire", GameObject.Tags.Has( "burning" ) ? 1 : 0 );
		graph.Update( age, output );
		pose = graph.GetBodyPose();
	}
}
