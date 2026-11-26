using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NS2;

[Title( "MAC" ), Category( "Natural Selection 2" )]
public sealed class Mac : BaseNpc, Component.IDamageable
{
	const float Scale = MapLoader.Scale;
	[Sync( SyncFlags.FromHost )] float armor { get; set; } = 50;
	float IDamageable.Armour { get => armor; set => armor = Math.Clamp( value, 0, 50 ); }
	float IDamageable.MaxArmour => 50;
	const float ArrivalDistance = 0.01f * Scale;
	Vector3 anchor;
	bool AtAnchor => (WorldPosition - anchor).WithZ( 0 ).LengthSquared <= ArrivalDistance * ArrivalDistance;
	Component target;
	float targetRadius;
	TimeUntil nextIdleSearch, nextPositionCheck, nextWeld, inCombat;
	TimeSince age;
	[Sync( SyncFlags.FromHost )] TimeSince AnimationTime { get => age; set => age = value; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.BodyPose pose { get; set; }
	[Sync( SyncFlags.FromHost )] TimeUntil flinchEnd { get; set; }
	SupportPain pain;
	internal const string PainSound = "marine/structures/mac/pain";
	SparkAnimationGraph.Playback graph;
	SoundHandle thrusters;

	internal static void RegisterSounds( MountContext context )
	{
		foreach ( var cue in new[] { "weld", "thrusters" } )
		{
			var path = $"marine/structures/mac/{cue}";
			context.Add( ResourceType.GameResource, $"sounds/{path}.sound", new SparkSoundLoader( path ) );
		}
	}

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer )
	{
		renderer.LocalScale = Vector3.One * 0.75f;
		renderer.LocalPosition = Vector3.Up * (0.4f * Scale);
		var collider = root.AddComponent<CapsuleCollider>();
		collider.Start = Vector3.Up * (0.5f * Scale);
		collider.End = Vector3.Up * (0.7f * Scale);
		collider.Radius = 0.5f * Scale;
		var body = root.AddComponent<Rigidbody>();
		body.EnableImpactDamage = false;
		var agent = root.AddComponent<NavMeshAgent>();
		agent.Acceleration = 7 * 3.5f * Scale;
		agent.Radius = collider.Radius;
		agent.Height = 1.2f * Scale;
		agent.Enabled = false;
		root.AddComponent<NavigationLayer>().RunSpeed = 7 * Scale;
		root.AddComponent<AnimationLayer>().EnableFootstepSounds = false;
		var npc = root.AddComponent<Mac>();
		npc.DisplayName = "MAC";
		npc.Faction = Factions.Ally;
		npc.MaxHealth = npc.Health = 300;
		npc.EyeHeight = 0.4f * Scale;
		npc.Renderer = renderer;
	}

	protected override void OnStart()
	{
		base.OnStart();
		pain = GetComponent<SupportPain>();
		pain?.StartListening( flinchEnd );
		if ( !Networking.IsHost ) return;
		anchor = WorldPosition;
		AnimationTime = 0;
		if ( Sandbox.Mounting.Directory.Get( "ns2" )?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model )
			graph = new( model.PropGraph, model );
	}

	protected override void Think()
	{
		if ( nextIdleSearch <= 0 )
		{
			var found = FindWeldTarget();
			if ( found != target ) nextPositionCheck = 0;
			target = found;
			if ( !target.IsValid() && AtAnchor ) nextIdleSearch = 0.35f;
		}
		if ( nextPositionCheck <= 0 )
		{
			nextPositionCheck = 0.4f;
			UpdateWeldPosition();
		}
		if ( !target.IsValid() || nextWeld > 0 || target is not IDamageable { Health: > 0 } hp ) return;
		if ( WorldPosition.Distance( target.WorldPosition ) >= 2.5f * Scale + targetRadius ) return;
		if ( target.GameObject.Tags.Has( "structure" ) )
		{
			if ( hp.Health >= hp.MaxHealth && hp.Armour >= hp.MaxArmour ) return;
			float health = Math.Clamp( hp.MaxHealth - hp.Health, 0, 15 );
			hp.Health += health;
			hp.Armour = Math.Min( hp.MaxArmour, hp.Armour + 15 - health );
		}
		else
		{
			if ( hp.Armour >= hp.MaxArmour ) return;
			hp.Armour = Math.Min( hp.MaxArmour, hp.Armour + 5 );
		}
		nextWeld = 0.5f;
		WeldEffect();
	}

	void UpdateWeldPosition()
	{
		float speed = (inCombat > 0 ? 5.5f : 7) * Scale;
		if ( !target.IsValid() )
		{
			Animation.SetLookTarget( null );
			if ( !AtAnchor ) Movement.MoveTo( anchor, ArrivalDistance, speed );
			else Movement.Stop();
			return;
		}

		var obj = target.GameObject;
		Animation.SetLookTarget( obj );
		targetRadius = target.GetComponentInChildren<Collider>()?.LocalBounds.Size.WithZ( 0 ).Length * 0.5f ?? 0.1f * Scale;
		float distance = WorldPosition.Distance( target.WorldPosition ) - targetRadius;
		if ( distance < 2.5f * Scale && !obj.Tags.Has( "structure" ) )
		{
			var forward = target.GetComponent<PlayerController>()?.EyeTransform.Forward ?? target.WorldRotation.Forward;
			if ( BackPosition( WorldPosition, target.WorldPosition, forward, targetRadius ) is { } position )
			{
				Movement.MoveTo( position, 0, speed, obj );
				return;
			}
		}
		if ( distance > 2 * Scale ) Movement.MoveTo( target.WorldPosition, targetRadius + 2 * Scale, speed, obj );
		else Movement.Stop();
	}

	static Vector3? BackPosition( Vector3 origin, Vector3 target, Vector3 forward, float radius )
	{
		forward = forward.WithZ( 0 ).Normal;
		var offset = (origin - target).WithZ( 0 );
		if ( offset.Length >= 1.5f * Scale && Vector3.Dot( forward, offset.Normal ) <= 0.866f ) return null;
		var back = target - forward * (radius + 1.6f * Scale);
		var side = Vector3.Cross( Vector3.Up, forward ) * (2 * Scale);
		return origin.DistanceSquared( back - side ) < origin.DistanceSquared( back + side ) ? back - side : back + side;
	}

	bool OtherMacIsWelding( Component candidate )
	{
		foreach ( var mac in Scene.GetAllComponents<Mac>() )
			if ( mac != this && mac.Faction == Faction && mac.target == candidate && mac.nextWeld > 0 ) return true;
		return false;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void WeldEffect()
	{
		if ( GameResource.Load<SoundEvent>( "mount://ns2/sounds/marine/structures/mac/weld.sound" ) is { } sound )
			GameObject.PlaySound( sound );
		if ( Renderer.GetAttachment( "fxnode_welder" ) is { } attachment )
			CinematicLoader.Play( "cinematics/marine/mac/weld.cinematic", attachment.Position, attachment.Rotation, Renderer.GameObject );
	}

	Component FindWeldTarget()
	{
		if ( (WorldPosition - anchor).WithZ( 0 ).Length > 14 * Scale ) return null;
		Component best = null;
		float closest = float.MaxValue;
		bool foundPlayer = false;
		foreach ( var hp in Scene.GetAllComponents<IDamageable>() )
		{
			if ( hp is not Component c || c is Mac || hp.Health <= 0 || hp.MaxHealth <= 0 ) continue;
			bool structure = c.GameObject.Tags.Has( "structure" );
			if ( structure ? foundPlayer || hp.Health >= hp.MaxHealth && hp.Armour >= hp.MaxArmour : hp.Armour >= hp.MaxArmour ) continue;
			if ( !structure && !c.GameObject.Tags.HasAny( "ns2_marine", "ns2_exo", "ns2_exosuit" ) && !c.GetComponent<PlayerController>().IsValid() ) continue;
			if ( GetDisposition( c.GameObject ) != Disposition.Friendly && !(c is BaseNpc npc && npc.Faction == Faction) ) continue;
			if ( (anchor - c.WorldPosition).WithZ( 0 ).LengthSquared > 144 * Scale * Scale ) continue;
			float distance = WorldPosition.DistanceSquared( c.WorldPosition );
			if ( best.IsValid() && (foundPlayer || structure) && distance >= closest ) continue;
			if ( !structure && OtherMacIsWelding( c ) ) continue;
			best = c;
			closest = distance;
			foundPlayer = !structure;
		}
		return best;
	}

	protected override void OnUpdate()
	{
		if ( GameObject.IsDestroyed ) return;
		base.OnUpdate();
		if ( Health > 0 ) pain?.UpdateSound( flinchEnd );
		if ( !Application.IsDedicatedServer )
		{
			if ( Animation.MoveVelocity.LengthSquared > 1 )
			{
				if ( thrusters is null && GameResource.Load<SoundEvent>( "mount://ns2/sounds/marine/structures/mac/thrusters.sound" ) is { } sound )
					thrusters = GameObject.PlaySound( sound );
			}
			else StopThrusters();
		}
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		float intensity = AlienWounds.Intensity( flinchEnd );
		output?.Set( "intensity", intensity );
		if ( !Networking.IsHost )
		{
			pose.Write( output, age );
			return;
		}
		if ( graph is null ) return;
		graph.Set( "alive", Health > 0 ? 1 : 0 );
		graph.Set( "flinch", intensity > 0 ? 1 : 0 );
		// Autonomous welding runs as a side task. Spark only requests "run" for explicit orders.
		graph.Set( "move", "idle" );
		graph.Set( "activity", nextWeld > 0 ? "build" : "none" );
		graph.Update( age, output );
		pose = graph.GetBodyPose();
	}

	void StopThrusters()
	{
		thrusters?.Stop();
		thrusters = null;
	}

	protected override void OnDisabled()
	{
		StopThrusters();
		base.OnDisabled();
	}

	protected override void OnDamaging( IDamageable.DamageEvent hit ) => SupportDamage.Absorb( hit, this, armor );

	protected override void OnDamaged( IDamageable.DamageEvent hit )
	{
		var previousArmor = armor;
		(armor, flinchEnd) = SupportDamage.Apply( hit, this, armor, MaxHealth, flinchEnd );
		if ( hit.AcceptedDamage > 0 || armor < previousArmor ) inCombat = 3;
	}

	protected override GameObject CreateRagdoll() => graph is null ? NpcCorpse.Attach( base.CreateRagdoll(), 1.5f ) : NpcCorpse.Animate( Renderer, graph, age );
}
