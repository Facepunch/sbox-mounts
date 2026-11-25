using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NS2;

[Title( "MAC" ), Category( "Natural Selection 2" )]
public sealed class Mac : BaseNpc, Component.IDamageable
{
	const float Scale = MapLoader.Scale;
	[Sync( SyncFlags.FromHost )] float armor { get; set; } = 50;
	[Sync( SyncFlags.FromHost )] TimeUntil weldEnds { get; set; }
	float IDamageable.Armour { get => armor; set => armor = Math.Clamp( value, 0, 50 ); }
	float IDamageable.MaxArmour => 50;
	Vector3 anchor;
	Component target;
	float targetRadius;
	TimeUntil search, nextWeld, inCombat;
	TimeSince age;
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
		anchor = WorldPosition;
		age = 0;
		if ( Sandbox.Mounting.Directory.Get( "ns2" )?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model )
			graph = new( model.PropGraph, model );
	}

	protected override void Think()
	{
		if ( search <= 0 )
		{
			search = 0.35f;
			target = FindWeldTarget();
			if ( !target.IsValid() )
			{
				Animation.SetLookTarget( null );
				if ( WorldPosition.Distance( anchor ) > Scale ) Movement.MoveTo( anchor, Scale );
				else Movement.Stop();
				return;
			}
			var obj = target.GameObject;
			targetRadius = target.GetComponentInChildren<Collider>()?.LocalBounds.Size.WithZ( 0 ).Length * 0.5f ?? 0.1f * Scale;
			float reach = 2 * Scale + targetRadius;
			if ( WorldPosition.Distance( target.WorldPosition ) > reach )
				Movement.MoveTo( target.WorldPosition, reach, (inCombat > 0 ? 5.5f : 7) * Scale, obj );
			else Movement.Stop();
			Animation.SetLookTarget( obj );
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
			if ( hp.Armour >= hp.MaxArmour || OtherMacIsWelding( target ) ) return;
			hp.Armour = Math.Min( hp.MaxArmour, hp.Armour + 5 );
		}
		nextWeld = weldEnds = 0.5f;
		WeldEffect();
	}

	bool OtherMacIsWelding( Component candidate ) =>
		Scene.GetAllComponents<Mac>().Any( mac => mac != this && mac.target == candidate && mac.weldEnds > 0 );

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
		if ( !Application.IsDedicatedServer )
		{
			if ( Animation.MoveVelocity.LengthSquared > 1 )
			{
				if ( thrusters is null && GameResource.Load<SoundEvent>( "mount://ns2/sounds/marine/structures/mac/thrusters.sound" ) is { } sound )
					thrusters = GameObject.PlaySound( sound );
			}
			else StopThrusters();
		}
		if ( graph is null || !Renderer.Animation.IsValid ) return;
		// Autonomous welding runs as a side task. Spark only requests "run" for explicit orders.
		graph.Set( "move", "idle" );
		graph.Set( "activity", weldEnds > 0 ? "build" : "none" );
		graph.Update( age, Renderer.Animation.Parameters );
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

	protected override void OnDamaging( IDamageable.DamageEvent hit ) => AlienArmor.Absorb( hit, armor );

	protected override void OnDamaged( IDamageable.DamageEvent hit )
	{
		if ( hit.AcceptedDamage <= 0 ) return;
		inCombat = 3;
		float ratio = AlienArmor.HealthPerArmor( hit.DamageInfo );
		if ( ratio > 0 ) armor = Math.Max( 0, armor - hit.AcceptedDamage * (0.7f / 0.3f) / ratio );
	}
}
