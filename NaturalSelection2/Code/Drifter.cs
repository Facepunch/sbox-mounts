using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NS2;

[Title( "Drifter" ), Category( "Natural Selection 2" )]
public sealed class Drifter : BaseNpc, Component.IDamageable
{
	const float Scale = MapLoader.Scale;
	internal const string ConstructSound = "alien/drifter/drift";
	[Sync( SyncFlags.FromHost )] float armor { get; set; } = 20;
	float IDamageable.Armour { get => armor; set => armor = Math.Clamp( value, 0, 20 ); }
	float IDamageable.MaxArmour => 20;
	[Property] public bool CragHive { get; set; }
	[Property] public bool ShiftHive { get; set; }
	[Sync( SyncFlags.FromHost )] TimeUntil nextRegeneration { get; set; }
	bool hasOrder;
	[Sync( SyncFlags.FromHost )] bool constructing { get; set; }
	Harvester target;
	Vector3 returnPosition;
	TimeUntil search;
	TimeSince age;
	[Sync( SyncFlags.FromHost )] TimeSince AnimationTime { get => age; set => age = value; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.BodyPose pose { get; set; }
	[Sync( SyncFlags.FromHost )] TimeUntil flinchEnd { get; set; }
	SupportPain pain;
	internal const string PainSound = "alien/drifter/wound";
	SparkAnimationGraph.Playback graph;
	SoundHandle constructLoop;
	Action<string> onTag;

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer )
	{
		renderer.LocalPosition = Vector3.Up * Scale;
		var capsule = root.AddComponent<CapsuleCollider>();
		capsule.Start = Vector3.Up * (0.5f * Scale);
		capsule.End = Vector3.Up * (0.55f * Scale);
		capsule.Radius = 0.5f * Scale;
		var body = root.AddComponent<Rigidbody>();
		body.EnableImpactDamage = false;
		var agent = root.AddComponent<NavMeshAgent>();
		agent.Height = 1.05f * Scale;
		agent.Radius = capsule.Radius;
		agent.Enabled = false;
		root.AddComponent<NavigationLayer>().RunSpeed = 11 * Scale;
		root.AddComponent<AnimationLayer>().EnableFootstepSounds = false;
		var npc = root.AddComponent<Drifter>();
		npc.DisplayName = "Drifter";
		npc.Faction = Factions.Monster;
		npc.MaxHealth = npc.Health = 300;
		npc.Renderer = renderer;
		npc.EyeHeight = Scale;
	}

	protected override void OnStart()
	{
		base.OnStart();
		pain = GetComponent<SupportPain>();
		pain?.StartListening( flinchEnd );
		if ( !Networking.IsHost ) return;
		AnimationTime = 0;
		onTag = OnTag;
		if ( Sandbox.Mounting.Directory.Get( "ns2" )?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model )
			graph = new( model.PropGraph, model );
	}

	protected override void Think()
	{
		if ( !hasOrder && search <= 0 && Time.Now > 3 )
		{
			search = 4;
			foreach ( var candidate in Scene.GetAllComponents<Harvester>() )
			{
				if ( candidate.Health <= 0 || candidate.IsBuilt || candidate.HasEnzyme || candidate.Faction != Faction ||
					WorldPosition.DistanceSquared( candidate.WorldPosition ) > 144 * Scale * Scale ) continue;
				if ( Scene.GetAllComponents<Drifter>().Any( other => other != this && other.target == candidate && other.Health > 0 ) ) continue;
				target = candidate;
				returnPosition = WorldPosition;
				hasOrder = true;
				Movement.MoveTo( target.WorldPosition, 2.9f * Scale );
				break;
			}
		}
		if ( !hasOrder ) return;
		if ( target.IsValid() && !target.IsBuilt && target.Health > 0 )
		{
			constructing = WorldPosition.Distance( target.WorldPosition ) <= 3 * Scale;
			if ( constructing )
			{
				Movement.Stop();
				Animation.SetLookTarget( target.GameObject );
				target.RefreshEnzyme();
			}
			else if ( !Movement.MoveTarget.HasValue ) Movement.MoveTo( target.WorldPosition, 2.9f * Scale );
			return;
		}
		constructing = false;
		Animation.SetLookTarget( null );
		target = null;
		if ( WorldPosition.Distance( returnPosition ) <= 0.5f * Scale )
		{
			Movement.Stop();
			hasOrder = false;
		}
		else if ( Movement.MoveTarget != returnPosition ) Movement.MoveTo( returnPosition, 0.5f * Scale );
	}

	void OnTag( string tag )
	{
		if ( constructing && tag == "attack_end" )
			ShowConstruct();
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	void ShowConstruct()
	{
		if ( !Application.IsHeadless )
			CinematicLoader.Play( "cinematics/alien/drifter/construct.cinematic", WorldPosition, WorldRotation );
	}

	protected override void OnUpdate()
	{
		if ( GameObject.IsDestroyed ) return;
		base.OnUpdate();
		if ( Health > 0 ) pain?.UpdateSound( flinchEnd );
		if ( !Application.IsDedicatedServer )
		{
			if ( constructing && NpcConVars.Enabled )
			{
				if ( constructLoop is null && GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{ConstructSound}.sound" ) is { } sound )
					constructLoop = GameObject.PlaySound( sound );
			}
			else StopLoop();
		}
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		float intensity = AlienWounds.Intensity( flinchEnd );
		output?.Set( "intensity", intensity );
		if ( !Networking.IsHost )
		{
			pose.Write( output, age );
			return;
		}
		if ( Health > 0 )
		{
			Movement.WalkSpeed = Movement.RunSpeed = (ShiftHive ? 13 : 11) * Scale;
			if ( CragHive && nextRegeneration <= 0 )
			{
				nextRegeneration = 2;
				float amount = 0.03f * MaxHealth;
				float health = Math.Clamp( MaxHealth - Health, 0, amount );
				Health += health;
				armor = Math.Min( 20, armor + amount - health );
			}
		}
		if ( graph is null ) return;
		graph.Set( "alive", Health > 0 ? 1 : 0 );
		graph.Set( "flinch", intensity > 0 ? 1 : 0 );
		graph.Set( "onfire", GameObject.Tags.Has( "burning" ) ? 1 : 0 );
		graph.Set( "move", hasOrder ? "run" : "idle" );
		graph.Set( "activity", constructing ? "parasite" : "none" );
		graph.Update( age, output, onTag );
		pose = graph.GetBodyPose();
	}

	void StopLoop()
	{
		constructLoop?.Stop();
		constructLoop = null;
	}

	protected override void OnDisabled()
	{
		StopLoop();
		base.OnDisabled();
	}

	protected override void OnDamaging( IDamageable.DamageEvent hit ) => AlienArmor.Absorb( hit, armor );

	protected override void OnDamaged( IDamageable.DamageEvent hit )
	{
		if ( hit.AcceptedDamage > 0 ) (armor, flinchEnd) = AlienWounds.ApplyFlinch( hit, armor, MaxHealth, flinchEnd );
	}
}
