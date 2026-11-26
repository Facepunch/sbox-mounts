using Sandbox.Npcs;
using System;
using System.Runtime.CompilerServices;

namespace NS2;

[Title( "Crag" ), Category( "Natural Selection 2" )]
public sealed class Crag : AlienStructure
{
	internal const float HealRadius = 14 * Scale;
	internal const string PainSound = "alien/structures/crag/wound";
	internal Vector3 EngagementPoint => Renderer.GetAttachment( "target" )?.Position ?? ModelOrigin;
	protected override GrowthStats Stats => new( 25, 0.5f, 300, 150, 120, 550, 275 );
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.BodyPose pose { get; set; }
	[Sync( SyncFlags.FromHost )] TimeUntil nextHeal { get; set; }
	TimeUntil nextEffect;

	sealed class Pulse { public TimeUntil Next; }
	static readonly ConditionalWeakTable<BaseNpc, Pulse> pulses = new();

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer ) => Configure<Crag>( root, renderer, "Crag" );

	bool TryHeal( BaseNpc target )
	{
		if ( target.Faction != Faction || target.Health <= 0 ||
			target.WorldPosition.DistanceSquared( WorldPosition ) > HealRadius * HealRadius ||
			target is not IDamageable hp || hp.Health >= hp.MaxHealth && hp.Armour >= hp.MaxArmour ) return false;

		var pulse = pulses.GetValue( target, static _ => new() );
		if ( pulse.Next > 0 ) return false;
		float amount = target switch
		{
			Skulk => 10, Gorge => 15, Lerk => 16, Fade => 25, Onos => 80,
			_ => Math.Clamp( hp.MaxHealth * 0.042f, 7, 42 )
		};
		pulse.Next = 2;
		return AlienHealing.Restore( target, amount, 0.5f );
	}

	protected override void OnUpdate()
	{
		if ( Health <= 0 || GameObject.IsDestroyed ) return;
		base.OnUpdate();
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		output?.Set( "grow", Growth );
		output?.Set( "intensity", FlinchIntensity );
		if ( Graphics.IsAvailable && nextHeal > 0 && nextEffect <= 0 )
		{
			nextEffect = 1;
			CinematicLoader.Play( "cinematics/alien/crag/heal.cinematic", WorldPosition, WorldRotation );
		}
		if ( !Networking.IsHost )
		{
			pose.Write( output, age );
			return;
		}
		if ( IsBuilt && nextHeal <= 0 && !GameObject.Tags.HasAny( "burning", "electrified" ) )
		{
			bool healed = false;
			foreach ( var obj in Scene.FindInPhysics( new Sphere( WorldPosition, HealRadius ) ) )
				if ( obj.GetComponentInParent<BaseNpc>() is { } target ) healed |= TryHeal( target );
			if ( healed ) nextHeal = 2;
		}
		if ( graph is null ) return;
		graph.Set( "built", IsBuilt ? 1 : 0 );
		graph.Set( "alive", 1 );
		graph.Set( "flinch", FlinchIntensity > 0 ? 1 : 0 );
		graph.Set( "heal", nextHeal > 0 ? 1 : 0 );
		graph.Set( "moving", 0 );
		graph.Set( "onfire", GameObject.Tags.Has( "burning" ) ? 1 : 0 );
		graph.Update( age, output );
		pose = graph.GetBodyPose();
	}

	protected override GameObject CreateRagdoll() => graph is null ? NpcCorpse.Attach( base.CreateRagdoll(), 1.5f ) : NpcCorpse.Animate( Renderer, graph, age );
}
