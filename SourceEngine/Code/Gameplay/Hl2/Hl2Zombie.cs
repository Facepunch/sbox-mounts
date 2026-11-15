using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>
/// npc_BaseZombie's headcrab and body rules: body bullets do half damage, a body-shot kill frees a live headcrab,
/// a heavy blast splits a classic zombie into a torso, and the poison zombie throws the crabs off its back.
/// </summary>
[Title( "HL2 Zombie" )]
[Category( "Half-Life 2" )]
public sealed class Hl2Zombie : SourceCreatureBehaviour, ISourceNpcPart
{
	/// <summary>MAX_CRABS, ZOMBIE_THROW_RANGE_MIN/MAX in npc_PoisonZombie.cpp.</summary>
	const int MaxCrabs = 3;
	const float ThrowRangeMin = 250;
	const float ThrowRangeMax = 800;

	/// <summary>ZOMBIE_THROW_FIRST_MIN/MAX_DELAY and ZOMBIE_THROW_MIN/MAX_DELAY.</summary>
	const int FirstThrowMinDelay = 1;
	const int FirstThrowMaxDelay = 2;
	const int ThrowMinDelay = 4;
	const int ThrowMaxDelay = 10;

	/// <summary>Generated prefab of the crab riding this zombie, such as headcrab.</summary>
	[Property] public string Headcrab { get; set; }

	/// <summary>Generated prefab this zombie's upper half becomes after a heavy blast; empty when it has none.</summary>
	[Property] public string Torso { get; set; }
	[Property] public string LegsModel { get; set; }

	/// <summary>Carries crabs on its back and throws them, like npc_poisonzombie.</summary>
	[Property] public bool ThrowsCrabs { get; set; }

	[Sync( SyncFlags.FromHost )] bool Headless { get; set; }
	[Sync( SyncFlags.FromHost )] int BackCrabs { get; set; }
	[Sync( SyncFlags.FromHost )] bool CrabInHand { get; set; }
	[Sync( SyncFlags.FromHost )] bool Throwing { get; set; }
	(bool, int, bool, Model)? _shown;
	GameObject _throwTarget;
	Vector3 _throwPoint;
	TimeSince _throwStarted;
	float _pickupTime;
	float _releaseTime;
	bool _pickedUp;
	TimeUntil _nextThrow;

	string MountName => SourceMount.Of( GameObject );

	/// <summary>The crab throw, ACT_RANGE_ATTACK2.</summary>
	string ThrowClip { get; set; }

	public override string Animation => Throwing ? ThrowClip : null;
	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(ThrowClip, false)];

	protected override void OnConfigure( Model model, SourceModelSequences sequences ) => ThrowClip = sequences.FindActivity( "ACT_RANGE_ATTACK2" );

	protected override void OnStart()
	{
		if ( Networking.IsHost && ThrowsCrabs )
			BackCrabs = (1 << MaxCrabs) - 1;
		_nextThrow = Game.Random.Int( FirstThrowMinDelay, FirstThrowMaxDelay );
	}

	protected override void OnUpdate()
	{
		var renderer = Npc?.Renderer;
		if ( !renderer.IsValid() || !renderer.Model.IsValid() )
			return;

		if ( Networking.IsHost )
			UpdateThrow();

		ApplyBodyGroups( renderer );
	}

	void ApplyBodyGroups( SkinnedModelRenderer renderer )
	{
		var state = (Headless, BackCrabs, CrabInHand, renderer.Model);
		if ( _shown == state )
			return;

		_shown = state;
		// Bodygroups: headcrab1 rides the head, headcrab2-4 are the back nest, headcrab5 is the crab in hand.
		renderer.SetBodyGroup( "headcrab1", Headless ? 0 : 1 );
		if ( !ThrowsCrabs )
			return;

		for ( var i = 0; i < MaxCrabs; i++ )
			renderer.SetBodyGroup( $"headcrab{i + 2}", (BackCrabs & (1 << i)) != 0 ? 1 : 0 );
		renderer.SetBodyGroup( "headcrab5", CrabInHand ? 1 : 0 );
	}

	/// <summary>Host think; true while the throw owns the creature.</summary>
	public override bool Think( GameObject target )
	{
		if ( Throwing )
		{
			Throwing = _throwStarted < Duration( ThrowClip, 1 );
			return Throwing;
		}

		if ( !target.IsValid() || !CanThrowCrab( WorldPosition.Distance( target.WorldPosition ) ) )
			return false;

		Npc.Movement?.Stop();
		WorldRotation = Rotation.LookAt( (target.WorldPosition - WorldPosition).WithZ( 0 ) );
		ThrowCrab( target );
		Throwing = true;
		return true;
	}

	bool CanThrowCrab( float distance ) => ThrowsCrabs && BackCrabs != 0 && ThrowClip is not null && _throwTarget is null
		&& _nextThrow <= 0 && distance is >= ThrowRangeMin and <= ThrowRangeMax;

	void ThrowCrab( GameObject target )
	{
		var events = SourceModelSequences.Of( Npc.Renderer.Model ).Get( ThrowClip )?.Events ?? [];
		var duration = Duration( ThrowClip, 1 );
		_pickupTime = (events.FirstOrDefault( e => e.Name == "AE_ZOMBIE_POISON_PICKUP_CRAB" )?.Cycle ?? 0.3f) * duration;
		_releaseTime = (events.FirstOrDefault( e => e.Name == "AE_ZOMBIE_POISON_THROW_CRAB" )?.Cycle ?? 0.5f) * duration;
		_throwTarget = target;
		_throwPoint = SourceNpcs.CapturePoint( target );
		_throwStarted = 0;
		_pickedUp = false;
		_nextThrow = Game.Random.Int( ThrowMinDelay, ThrowMaxDelay );
	}

	void UpdateThrow()
	{
		if ( _throwTarget is null )
			return;

		// A target that dies mid-throw still gets the crab flung at where it was.
		if ( _throwTarget.IsValid() )
			_throwPoint = SourceNpcs.CapturePoint( _throwTarget );

		if ( !_pickedUp && _throwStarted >= _pickupTime )
		{
			_pickedUp = true;
			var nest = Enumerable.Range( 0, MaxCrabs ).Where( i => (BackCrabs & (1 << i)) != 0 ).DefaultIfEmpty( -1 ).First();
			if ( nest >= 0 )
				BackCrabs &= ~(1 << nest);
			CrabInHand = true;
		}

		if ( _throwStarted < _releaseTime )
			return;

		_throwTarget = null;
		if ( !_pickedUp )
			return;

		CrabInHand = false;
		var origin = Npc.Renderer.GetAttachment( "headcrab5" )?.Position ?? WorldPosition + Vector3.Up * 60;
		var crab = SpawnCrab( origin );
		crab?.GetComponent<Hl2Leap>()?.ThrowAt( _throwPoint );
	}

	/// <summary>How far to the side of the head a freed crab starts, clear of the body.</summary>
	const float CrabReleaseOffset = 20;

	GameObject SpawnCrab( Vector3 origin )
	{
		var prefab = string.IsNullOrEmpty( Headcrab ) ? null : SourceMount.Prefab( MountName, Headcrab );
		if ( prefab is null )
			return null;

		var crab = GameObject.GetPrefab( prefab.ResourcePath )?.Clone( new Transform( origin, Rotation.FromYaw( WorldRotation.Yaw() ) ) );
		crab?.NetworkSpawn();
		return crab;
	}

	/// <summary>npc_BaseZombie halves bullet damage anywhere but the head.</summary>
	void ISourceNpcPart.OnNpcDamaging( Component.IDamageable.DamageEvent damage )
	{
		if ( damage.DamageInfo.Tags.Contains( SourceDamageTags.Bullet ) && !damage.DamageInfo.Tags.Contains( SourceDamageTags.Headshot ) )
			damage.AcceptedDamage *= 0.5f;
	}

	/// <summary>The zombie's own death, before its ragdoll: a heavy blast leaves a torso, a body-shot kill frees the crab.</summary>
	void ISourceNpcPart.OnNpcDeath( Component.IDamageable.DeathEvent death )
	{
		if ( Headless )
			return;

		var damage = death.DamageInfo;

		var threshold = Npc.MaxHealth > 0 ? MathF.Min( 1, damage.Damage / Npc.MaxHealth ) : 1;
		var blast = damage.Tags.Contains( SourceDamageTags.Explosion );
		if ( blast && threshold >= 0.5f && !string.IsNullOrEmpty( Torso ) )
		{
			BecomeTorso();
			return;
		}

		// ShouldReleaseHeadcrab: a body-shot kill frees a live crab; heads shots, blasts, fire and melee leave it on the corpse.
		if ( !damage.Tags.Contains( SourceDamageTags.Bullet ) || damage.Tags.Contains( SourceDamageTags.Headshot ) )
			return;

		var origin = Npc.Renderer.GetAttachment( "headcrab" )?.Position ?? WorldPosition + Vector3.Up * 60;
		Headless = true;
		ApplyBodyGroups( Npc.Renderer );

		// The body falls along the shot, so the crab comes off to one side of it: beside the head, not inside the
		// ragdoll that is about to be there, and hopping on from there.
		var shot = (damage.Position - damage.Origin).WithZ( 0 );
		var side = Game.Random.Float( 60, 120 ) * (Game.Random.Int( 0, 1 ) == 0 ? 1 : -1);
		var away = shot.Length > 1 ? Rotation.FromYaw( side ) * shot.Normal : Rotation.FromYaw( Game.Random.Float( 0, 360 ) ).Forward;
		var beside = Scene.Trace.Ray( origin, origin + away * CrabReleaseOffset ).Radius( 8 ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		SpawnCrab( beside.EndPosition )?.GetComponent<Hl2Leap>()?.Release( away );
	}

	void BecomeTorso()
	{
		var prefab = SourceMount.Prefab( MountName, Torso );
		var torso = prefab is null ? null : GameObject.GetPrefab( prefab.ResourcePath )?.Clone( WorldTransform );
		torso?.NetworkSpawn();

		Headless = true;
		ApplyBodyGroups( Npc.Renderer );
		if ( string.IsNullOrEmpty( LegsModel ) )
			return;

		var path = $"mount://{MountName}/{LegsModel}.vmdl";
		if ( Sandbox.Mounting.Directory.Get( MountName )?.GetByPath( path ) is null )
			return;

		var legs = new GameObject( false, "Zombie legs" );
		legs.Tags.Add( "ragdoll" );
		legs.Tags.Add( "removable" );
		legs.WorldTransform = WorldTransform;
		var prop = legs.AddComponent<Prop>();
		prop.Model = Model.Load( path );
		legs.Enabled = true;
		legs.NetworkSpawn();
		prop.Invoke( 30, prop.DestroyGameObject );
		// The upper body lives on as the torso NPC and the legs replace the corpse, so no full-body ragdoll.
		Npc.Renderer.Model = null;
	}
}
