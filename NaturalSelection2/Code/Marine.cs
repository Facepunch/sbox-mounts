using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NS2;

[Title( "Marine" ), Category( "Natural Selection 2" )]
public sealed class Marine : BaseNpc, Component.IDamageable
{
	const float Scale = MapLoader.Scale;
	internal const string RifleSound = "marine/rifle/fire_loop_1_upgrade_0";
	[Sync( SyncFlags.FromHost )] float armor { get; set; } = 30;
	float IDamageable.Armour { get => armor; set => armor = Math.Clamp( value, 0, 30 ); }
	float IDamageable.MaxArmour => 30;
	[Sync( SyncFlags.FromHost )] bool firing { get; set; }
	[Sync( SyncFlags.FromHost )] bool reloading { get; set; }
	int clip = 50, reserve = 200;
	bool deployed, reloaded;
	TimeSince age, lastShot, lastHit, lastRequest, lastTracked;
	TimeUntil acquireTarget;
	GameObject trackedTarget;
	TimeUntil repath;
	Rotation aim;
	readonly AlienTargeting targeting = new();
	SparkAnimationGraph.Playback graph, weapon;
	Action<string> weaponTag;
	SoundHandle rifleLoop;
	ModelRenderer rifle;

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer )
	{
		renderer.CreateAttachments = true;
		var capsule = root.AddComponent<CapsuleCollider>();
		capsule.Start = Vector3.Up * (0.35f * Scale);
		capsule.End = Vector3.Up * (1.55f * Scale);
		capsule.Radius = 0.35f * Scale;
		root.AddComponent<Rigidbody>().EnableImpactDamage = false;
		var agent = root.AddComponent<NavMeshAgent>();
		agent.Radius = capsule.Radius;
		agent.Height = 1.9f * Scale;
		agent.Enabled = false;
		root.AddComponent<NavigationLayer>().RunSpeed = 5 * Scale;
		root.AddComponent<SensesLayer>().SightRange = 41 * Scale;
		root.AddComponent<AnimationLayer>().EnableFootstepSounds = false;
		var npc = root.AddComponent<Marine>();
		npc.DisplayName = "Marine";
		npc.Faction = Factions.Ally;
		npc.MaxHealth = npc.Health = 100;
		npc.EyeHeight = 1.7f * Scale;
		npc.Renderer = renderer;
	}

	protected override void OnStart()
	{
		base.OnStart();
		age = 0;
		lastRequest = lastShot = lastHit = 1;
		aim = WorldRotation;
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader body )
			graph = new( body.PropGraph, body );
		if ( Networking.IsHost && mount?.GetByPath( "mount://ns2/ns2/models/marine/rifle/rifle_view.model.vmdl" ) is ModelLoader view )
		{
			weapon = new( view.PropGraph, view );
			weaponTag = OnRifleTag;
		}
		if ( Renderer.GetAttachmentObject( "RHand_Weapon" ) is { } hand )
		{
			var obj = new GameObject( hand, true, "Rifle" ) { NetworkMode = NetworkMode.Never };
			rifle = obj.AddComponent<ModelRenderer>();
			rifle.CreateAttachments = true;
			rifle.Model = Model.Load( "mount://ns2/ns2/models/marine/rifle/rifle.model.vmdl" );
		}
	}

	protected override void Think()
	{
		var enemy = targeting.FindEnemy( this, out var memory );
		var goal = memory ?? enemy?.WorldPosition;
		bool visible = enemy.IsValid() && !memory.HasValue;
		Animation.SetLookTarget( visible ? enemy : null );
		firing = visible && CanAimAt( enemy ) && !reloading && clip > 0 && WorldPosition.Distance( enemy.WorldPosition ) <= 41 * Scale;
		if ( repath > 0 ) return;
		repath = 0.2f;
		if ( goal is { } p && (!visible || WorldPosition.Distance( p ) > 22 * Scale) )
			Movement.MoveTo( p, Scale, faceTarget: visible ? enemy : null );
		else Movement.Stop();
	}

	bool CanAimAt( GameObject enemy )
	{
		var offset = CalculateAimPoint( enemy ) - EyePosition;
		// BotAim passes the Marine FOV (90) directly to the cone cosine.
		if ( offset.LengthSquared > 4 * Scale * Scale && Vector3.Dot( offset.Normal, aim.Forward ) <= MathF.Cos( 90 ) )
			return false;

		if ( trackedTarget != enemy || lastTracked > 0.5f )
		{
			// Tier one acquisition time for a standalone encounter.
			acquireTarget = trackedTarget == enemy && lastTracked < 1.5f ? 0.305f * 0.3f : 0.305f;
			trackedTarget = enemy;
		}
		lastTracked = 0;
		return acquireTarget <= 0;
	}

	protected override void OnUpdate()
	{
		if ( GameObject.IsDestroyed ) return;
		base.OnUpdate();
		if ( Health <= 0 ) return;
		bool trigger = firing && NpcConVars.Enabled;
		if ( Networking.IsHost && Animation.LookObject.IsValid() )
			aim = AlienAim.Turn( aim, CalculateAimPoint( Animation.LookObject ) - EyePosition, 5 );
		var activity = reloading ? "reload" : trigger ? "primary" : "none";
		var move = Animation.MoveVelocity.LengthSquared > 1 ? "run" : "idle";
		UpdateInputs( graph, move, activity );
		graph?.Update( age, Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null );
		if ( weapon is not null )
		{
			if ( trigger ) lastRequest = 0;
			UpdateInputs( weapon, move, activity );
			weapon.Update( age, tag: weaponTag );
		}
		if ( trigger && !reloading && !Application.IsDedicatedServer )
		{
			if ( rifleLoop is null && GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{RifleSound}.sound" ) is { } sound )
				rifleLoop = GameObject.PlaySound( sound );
		}
		else StopRifleSound();
	}

	static void UpdateInputs( SparkAnimationGraph.Playback playback, string move, string activity )
	{
		if ( playback is null ) return;
		playback.Set( "move", move );
		playback.Set( "activity", activity );
		playback.Set( "alive", 1 );
		playback.Set( "weapon", "rifle" );
		playback.Set( "catalyst_speed", 1 );
		playback.Set( "reload_speed", 1 );
		playback.Set( "skip_draw", 1 );
		playback.Set( "idleName", "idle" );
	}

	void OnRifleTag( string tag )
	{
		if ( tag == "shoot" && lastRequest < 0.1f && !reloading && clip > 0 && NpcConVars.Enabled )
		{
			FireRifle();
			if ( --clip == 0 && deployed && reserve > 0 ) reloading = true;
		}
		else if ( tag == "deploy_end" ) deployed = true;
		else if ( tag == "reload" )
		{
			int rounds = Math.Min( 50 - clip, reserve );
			clip += rounds;
			reserve -= rounds;
			reloaded = true;
		}
		else if ( tag == "reload_end" && reloaded )
		{
			reloaded = false;
			reloading = false;
		}
	}

	void FireRifle()
	{
		float theta = Random.Shared.NextSingle() * MathF.Tau;
		float radius = Random.Shared.NextSingle() * MathF.Tan( 1.4f.DegreeToRadian() );
		var d = (aim.Forward + (aim.Left * MathF.Cos( theta ) + aim.Up * MathF.Sin( theta )) * radius).Normal;
		var eye = EyePosition;
		var ray = Scene.Trace.Ray( eye, eye + d * (41 * Scale) ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).UseHitboxes();
		bool box = lastShot >= 0.2f || lastHit < 0.5f;
		lastShot = 0;
		for ( int i = 0; i < 20; i++ )
		{
			var hit = ray.Run();
			if ( box && (!hit.Hit || hit.GameObject?.Tags.Has( "world" ) == true) )
			{
				var extents = new Vector3( d.WithX( 0 ).Length, d.WithY( 0 ).Length, d.WithZ( 0 ).Length ) * (0.018f * Scale);
				hit = ray.FromTo( eye, hit.EndPosition ).Size( -extents, extents ).Run();
			}
			var hp = hit.GameObject?.GetComponentInParent<IDamageable>();
			var corpse = hit.GameObject?.GetComponentInParent<AlienCorpse>();
			var obj = (hp as Component)?.GameObject ?? corpse?.GameObject ?? hit.GameObject;
			bool pass = corpse.IsValid() || obj.IsValid() && (hp is { MaxHealth: > 0, Health: <= 0 } || obj.Tags.HasAny( "ragdoll", "ns2_mac", "ns2_drifter", "ns2_hydra", "ns2_hallucination" ));
			if ( hp is { Health: > 0 } && GetDisposition( obj ) == Disposition.Hostile )
			{
				var info = new DamageInfo { Damage = 10, Origin = eye, Position = hit.HitPosition, Attacker = GameObject, Weapon = GameObject };
				info.Tags.Add( "bullet" );
				hp.OnDamage( info );
				lastHit = 0;
			}
			if ( !pass ) break;
			ray = ray.IgnoreGameObjectHierarchy( obj );
		}
		ShowMuzzle();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowMuzzle()
	{
		if ( rifle?.GetAttachmentObject( "fxnode_riflemuzzle" ) is { } muzzle )
			CinematicLoader.Play( "cinematics/marine/rifle/muzzle_flash.cinematic", muzzle.WorldPosition, muzzle.WorldRotation );
	}

	void StopRifleSound()
	{
		rifleLoop?.Stop();
		rifleLoop = null;
	}

	protected override void OnDisabled()
	{
		StopRifleSound();
		base.OnDisabled();
	}

	protected override void OnDamaging( IDamageable.DamageEvent hit ) => AlienArmor.Absorb( hit, armor );

	protected override void OnDamaged( IDamageable.DamageEvent hit )
	{
		float ratio = AlienArmor.HealthPerArmor( hit.DamageInfo );
		if ( ratio > 0 && hit.AcceptedDamage > 0 ) armor = Math.Max( 0, armor - hit.AcceptedDamage * (0.7f / 0.3f) / ratio );
	}
}
