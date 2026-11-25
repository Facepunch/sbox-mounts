using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;
using System.Runtime.CompilerServices;

namespace NS2;

[Title( "Exo" ), Category( "Natural Selection 2" )]
public sealed class Exo : BaseNpc, Component.IDamageable
{
	const float Scale = MapLoader.Scale;
	[Property] public bool Railguns { get; set; }
	float ArmorCapacity => Railguns ? 260 : 320;
	float IDamageable.MaxArmour => ArmorCapacity;
	float IDamageable.Armour
	{
		get => Health / MaxHealth * ArmorCapacity;
		set => Health = Math.Clamp( value / ArmorCapacity, 0, 1 ) * MaxHealth;
	}

	sealed class Arm
	{
		internal float Heat;
		internal bool Shooting;
		internal TimeSince Charge, Released, Flash;
		internal SoundHandle Loop;
	}

	sealed class Resonance
	{
		internal Guid Source;
		internal bool LeftArm;
		internal TimeUntil Expires;

		internal float Hit( Guid source, bool leftArm )
		{
			if ( Expires > 0 && (Source != source || LeftArm != leftArm) )
			{
				Expires = 0;
				return 1.5f;
			}

			Source = source;
			LeftArm = leftArm;
			Expires = 2;
			return 1;
		}
	}

	static readonly ConditionalWeakTable<GameObject, Resonance> marks = new();
	readonly Arm left = new(), right = new();
	readonly AlienTargeting targeting = new();
	readonly AlienAim.Acquisition acquisition = new( 95 );
	[Sync( SyncFlags.FromHost )] bool leftPressed { get; set; }
	[Sync( SyncFlags.FromHost )] bool rightPressed { get; set; }
	SparkAnimationGraph.Playback graph, weapon;
	Action<string> onTag;
	TimeSince age;
	TimeUntil repath;
	Rotation aim;
	bool attack, closePlayer;

	internal static void RegisterSounds( MountContext context )
	{
		foreach ( var cue in new[] { "spin", "spin_2", "railgun_fire" } )
		{
			var path = $"marine/heavy/{cue}";
			context.Add( ResourceType.GameResource, $"sounds/{path}.sound", new SparkSoundLoader( path ) );
		}
	}

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer, bool railguns )
	{
		if ( railguns ) renderer.LocalScale = Vector3.One * 0.95f;
		var capsule = root.AddComponent<CapsuleCollider>();
		capsule.Radius = 0.55f * Scale;
		capsule.Start = Vector3.Up * capsule.Radius;
		capsule.End = Vector3.Up * (2.4f * Scale - capsule.Radius);
		root.AddComponent<Rigidbody>().EnableImpactDamage = false;
		var agent = root.AddComponent<NavMeshAgent>();
		agent.Radius = capsule.Radius;
		agent.Height = 2.4f * Scale;
		agent.Enabled = false;
		root.AddComponent<NavigationLayer>().RunSpeed = (railguns ? 4.879f : 4.522f) * Scale;
		root.AddComponent<SensesLayer>().SightRange = 45 * Scale;
		root.AddComponent<AnimationLayer>().EnableFootstepSounds = false;
		var npc = root.AddComponent<Exo>();
		npc.Railguns = railguns;
		npc.DisplayName = railguns ? "Railgun Exo" : "Minigun Exo";
		npc.EyeHeight = (railguns ? 2.185f : 2.3f) * Scale;
		npc.MaxHealth = npc.Health = 100;
		npc.Faction = Factions.Ally;
		npc.Renderer = renderer;
	}

	protected override void OnStart()
	{
		base.OnStart();
		age = 0;
		aim = WorldRotation;
		left.Released = right.Released = 1;
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		var path = Renderer.Model.ResourcePath;
		if ( mount?.GetByPath( path ) is ModelLoader body )
			graph = new( body.PropGraph, body );
		if ( Networking.IsHost && mount?.GetByPath( path.Replace( ".model.vmdl", "_view.model.vmdl" ) ) is ModelLoader view )
		{
			weapon = new( view.PropGraph, view );
			onTag = OnWeaponTag;
		}
	}

	protected override void Think()
	{
		var enemy = targeting.FindEnemy( this, out var memory );
		bool visible = enemy.IsValid() && !memory.HasValue;
		Animation.SetLookTarget( visible ? enemy : null );
		float distance = float.MaxValue;
		if ( visible )
		{
			var size = enemy.GetComponentInChildren<Collider>()?.LocalBounds.Size ?? Vector3.Zero;
			distance = (EyePosition.Distance( enemy.WorldPosition ) - (size * enemy.WorldScale).WithZ( 0 ).Length * 0.5f) / Scale;
		}
		closePlayer = distance < 10 && enemy.GetComponentInParent<AlienStructure>() is null;
		attack = visible && distance < (Railguns ? 32 : 45) && acquisition.Ready( enemy, CalculateAimPoint( enemy ) - EyePosition, aim );
		if ( repath > 0 ) return;
		repath = 0.2f;
		if ( (memory ?? enemy?.WorldPosition) is { } goal && distance > 10 )
			Movement.MoveTo( goal, Scale, faceTarget: visible ? enemy : null );
		else Movement.Stop();
	}

	protected override void OnUpdate()
	{
		if ( GameObject.IsDestroyed ) return;
		base.OnUpdate();
		if ( Health <= 0 ) return;
		if ( Networking.IsHost )
		{
			if ( Animation.LookObject.IsValid() )
				aim = AlienAim.Turn( aim, CalculateAimPoint( Animation.LookObject ) - EyePosition, 5 );
			leftPressed = UpdateArm( left, leftPressed );
			rightPressed = UpdateArm( right, rightPressed );
		}
		var move = Animation.MoveVelocity.Length > 0.1f * Scale ? "run" : "idle";
		UpdateInputs( graph, move );
		graph?.Update( age, Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null );
		UpdateInputs( weapon, move );
		weapon?.Update( age, tag: onTag );
		if ( !leftPressed ) StopSound( left );
		if ( !rightPressed ) StopSound( right );
	}

	bool UpdateArm( Arm arm, bool pressed )
	{
		bool request = attack && NpcConVars.Enabled;
		if ( !Railguns )
		{
			arm.Heat = Math.Clamp( arm.Heat + Time.Delta * (arm.Shooting ? 0.3f : -0.4f), 0, 1 );
			request &= arm.Heat < 0.95f;
			if ( !request ) arm.Shooting = false;
			return request;
		}

		request &= pressed ? arm.Charge < (closePlayer ? 0.8f : 1) : arm.Released >= 1;
		if ( request && !pressed ) arm.Charge = 0;
		if ( !request && pressed ) arm.Released = 0;
		return request;
	}

	void UpdateInputs( SparkAnimationGraph.Playback playback, string move )
	{
		if ( playback is null ) return;
		playback.Set( "move", move );
		playback.Set( "weapon", "exo_weapon_holder" );
		playback.Set( "alive", 1 );
		playback.Set( "activity_left", leftPressed ? "primary" : "none" );
		playback.Set( "activity_right", rightPressed ? "primary" : "none" );
		playback.Set( "overheat_speed", 1.1666666269302368 / 2 );
	}

	void OnWeaponTag( string tag )
	{
		bool isLeft = tag == "l_shoot";
		if ( !isLeft && tag != "r_shoot" || !NpcConVars.Enabled ) return;
		var arm = isLeft ? left : right;
		if ( !Railguns && !(isLeft ? leftPressed : rightPressed) ) return;
		if ( !Railguns ) arm.Shooting = true;
		float charge = Math.Clamp( (float)arm.Charge, 0, 1 );
		Fire( Railguns ? 120 * charge : 6, isLeft, Railguns && charge >= 1 );
		if ( Railguns || arm.Flash >= 0.15f )
		{
			arm.Flash = 0;
			ShowShot( isLeft );
		}
	}

	void Fire( float damage, bool isLeft, bool fullCharge )
	{
		var direction = aim.Forward;
		if ( !Railguns )
		{
			float angle = Random.Shared.NextSingle() * MathF.Tau;
			float radius = Random.Shared.NextSingle() * MathF.Tan( 2.5f.DegreeToRadian() );
			direction = (direction + radius * (aim.Left * MathF.Cos( angle ) + aim.Up * MathF.Sin( angle ))).Normal;
		}
		var eye = EyePosition;
		var extents = new Vector3( direction.WithX( 0 ).Length, direction.WithY( 0 ).Length, direction.WithZ( 0 ).Length ) * ((Railguns ? 0.3f : 0.03f) * Scale);
		var ray = Scene.Trace.Ray( eye, eye + direction * ((Railguns ? 32 : 45) * Scale) ).WithoutTags( "trigger" ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes();
		for ( int i = 0; i < 20; i++ )
		{
			var hit = Railguns ? ray.Size( -extents, extents ).Run() : ray.Run();
			if ( !Railguns && (!hit.Hit || hit.GameObject?.Tags.Has( "world" ) == true) )
				hit = ray.FromTo( eye, hit.EndPosition ).Size( -extents, extents ).Run();
			var hp = hit.GameObject?.GetComponentInParent<IDamageable>();
			if ( hp is not Component victim ) break;
			var onos = victim as Onos;
			bool blocked = Railguns && onos is not null && onos.BlocksShotAt( hit.HitPosition );
			if ( hp.Health > 0 && GetDisposition( victim.GameObject ) == Disposition.Hostile )
			{
				float amount = damage;
				if ( fullCharge && victim.GameObject.Tags.Has( "ns2_lifeform" ) && !(onos?.Shielding ?? false) )
				{
					var mark = marks.GetValue( victim.GameObject, _ => new() );
					amount *= mark.Hit( GameObject.Id, isLeft );
				}
				var info = new DamageInfo { Damage = amount, Origin = eye, Position = hit.HitPosition, Attacker = GameObject, Weapon = GameObject };
				info.Tags.Add( Railguns ? "railgun" : "heavy" );
				info.Tags.Add( "bullet" );
				hp.OnDamage( info );
			}
			if ( blocked ) break;
			if ( !Railguns && !victim.GameObject.Tags.HasAny( "ns2_mac", "ns2_drifter", "ns2_hydra", "ns2_hallucination" ) ) break;
			if ( Railguns && victim.GameObject.Tags.Has( "ns2_lifeform" ) ) damage *= 0.5f;
			ray = ray.IgnoreGameObjectHierarchy( victim.GameObject );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowShot( bool isLeft )
	{
		string muzzle = Railguns ? (isLeft ? "fxnode_lrailgunmuzzle" : "fxnode_rrailgunmuzzle") : (isLeft ? "fxnode_l_minigun_muzzle" : "fxnode_r_minigun_muzzle");
		if ( Renderer.GetAttachment( muzzle ) is { } tx )
		{
			string effect = Railguns ? "railgun/muzzle_flash" : isLeft ? "minigun/muzzle_flash_left" : "minigun/muzzle_flash";
			CinematicLoader.Play( $"cinematics/marine/{effect}.cinematic", tx.Position, tx.Rotation );
		}
		var arm = isLeft ? left : right;
		if ( !Railguns && arm.Loop is not null ) return;
		var cue = Railguns ? "railgun_fire" : isLeft ? "spin_2" : "spin";
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/marine/heavy/{cue}.sound" ) is { } sound )
		{
			var handle = GameObject.PlaySound( sound );
			if ( !Railguns ) arm.Loop = handle;
		}
	}

	static void StopSound( Arm arm )
	{
		arm.Loop?.Stop();
		arm.Loop = null;
	}

	protected override void OnDisabled()
	{
		StopSound( left );
		StopSound( right );
		base.OnDisabled();
	}

	protected override void OnDamaging( IDamageable.DamageEvent hit )
	{
		float ratio = AlienArmor.HealthPerArmor( hit.DamageInfo );
		hit.AcceptedDamage = ratio > 0 && !hit.DamageInfo.Tags.Has( "biological" ) ? hit.AcceptedDamage * MaxHealth / (ratio * ArmorCapacity) : 0;
	}
}
