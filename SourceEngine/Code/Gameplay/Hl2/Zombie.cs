using System;
using Sandbox.Npcs;

namespace Source1.Hl2;

/// <summary>
/// A zombie's body and headcrab rules: bullets hurt by where they hit, the killing blow decides whether the crab
/// leaves alive, is knocked off dead or stays on the corpse, blasts and sharp or heavy props tear the body apart,
/// and the poison zombie throws the crabs off its back and sheds the rest when it dies.
/// </summary>
[Title( "HL2 Zombie" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2Zombie", "Hl2Zombie" )]
public sealed class Zombie : CreatureBehaviour, INpcPart
{
	/// <summary>Share of max health a hit needs: a strong headshot knocks the crab off, a blast splits, a sharp prop slices, a hit provokes a crab throw.</summary>
	const float StrongHeadshot = 0.25f;
	const float SplitShare = 0.5f;
	const float SliceShare = 0.5f;
	const float ThrowProvoke = 0.2f;

	/// <summary>The game doubles every NPC headshot after the zombie's own rules; the zombie's head multipliers replace that bonus rather than stack on it.</summary>
	const float GameHeadBonus = 2;

	/// <summary>A crab needs a clear box this size standing on its spawn point.</summary>
	const float CrabFitSize = 26.4f;

	/// <summary>Damage carries no force, so a push is estimated as this impulse per point of damage along the hit, capped in speed.</summary>
	const float ForcePerDamage = 100;
	const float MaxPushSpeed = 1500;

	/// <summary>Gibs last as long as the game's NPC corpses, and stay when its keep-corpses setting is on.</summary>
	const float CorpseLifetime = 30;
	const string KeepCorpses = "sandbox.keepcorpses.npcs";

	const int MaxCrabs = 3;
	const string PickUpEvent = "AE_ZOMBIE_POISON_PICKUP_CRAB";
	const string ThrowEvent = "AE_ZOMBIE_POISON_THROW_CRAB";

	/// <summary>Generated prefab of the crab riding this zombie, such as headcrab.</summary>
	[Property] public string Headcrab { get; set; }

	/// <summary>Generated prefab this zombie's upper half becomes when it survives a heavy blast; only that type is flattened by heavy props.</summary>
	[Property] public string Torso { get; set; }

	/// <summary>Legs and upper body left by a split or a gib death; a zombie without them is never sliced.</summary>
	[Property] public string LegsModel { get; set; }
	[Property] public string TorsoModel { get; set; }

	/// <summary>Already a torso: it never splits or gibs, and a sharp prop slices its crab off instead.</summary>
	[Property] public bool IsTorso { get; set; }

	/// <summary>A killing blast tears it in two one time in three; one that can split needs half its max health for that.</summary>
	[Property] public bool GibsFromBlasts { get; set; }

	/// <summary>Carries crabs on its back and sends them at its enemy.</summary>
	[Property] public bool ThrowsCrabs { get; set; }

	[Sync( SyncFlags.FromHost )] bool Headless { get; set; }
	[Sync( SyncFlags.FromHost )] int BackCrabs { get; set; }
	[Sync( SyncFlags.FromHost )] bool CrabInHand { get; set; }
	[Sync( SyncFlags.FromHost )] string CrabClip { get; set; }
	(bool, int, bool, Model)? _shown;

	/// <summary>The last hit after the hit-location rules, which the death decides by; a shotgun blast's pellets add up into one.</summary>
	float _lastDamage, _provoke, _blastAt = -1;
	bool _lastHead;
	GameObject _blastBy;

	string _leapOffClip, _throwClip, _threatClip;
	List<(float Time, string Event)> _cues = [];
	TimeSince _clipStarted;
	float _clipLength;
	int _chosenSlot = -1;
	GameObject _enemy;
	Vector3 _throwPoint;
	TimeUntil _nextThrow;

	string MountName => MountContent.Of( GameObject );

	public override string Animation => CrabClip;
	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(_leapOffClip, false), (_throwClip, false), (_threatClip, false)];

	protected override void OnConfigure( Model model, ModelSequences sequences )
	{
		if ( !ThrowsCrabs )
			return;

		_leapOffClip = sequences.FindActivity( "ACT_RANGE_ATTACK1" );
		_throwClip = sequences.FindActivity( "ACT_RANGE_ATTACK2" );
		_threatClip = sequences.FindActivity( "ACT_ZOMBIE_POISON_THREAT" );
	}

	protected override void OnStart()
	{
		if ( Networking.IsHost && ThrowsCrabs )
			BackCrabs = (1 << MaxCrabs) - 1;
	}

	protected override void OnUpdate()
	{
		if ( Npc?.Renderer is { } renderer && renderer.IsValid() && renderer.Model.IsValid() )
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
			renderer.SetBodyGroup( $"headcrab{i + 2}", HasNestCrab( i ) ? 1 : 0 );
		renderer.SetBodyGroup( "headcrab5", CrabInHand ? 1 : 0 );
	}

	bool HasNestCrab( int slot ) => (BackCrabs & (1 << slot)) != 0;

	void LoseHeadCrab()
	{
		Headless = true;
		ApplyBodyGroups( Npc.Renderer );
	}

	/// <summary>Host think; true while a crab attack owns the creature.</summary>
	public override bool Think( GameObject target )
	{
		// The first crab goes 1 or 2 seconds after a new enemy is seen.
		if ( target.IsValid() && target != _enemy )
		{
			_enemy = target;
			_nextThrow = Game.Random.Int( 1, 2 );
		}

		if ( !ThrowsCrabs )
			return false;

		if ( CrabClip is not null )
		{
			UpdateCrabAttack();
			return CrabClip is not null;
		}

		if ( !target.IsValid() || BackCrabs == 0 || _nextThrow > 0 )
			return false;

		// Up close it leaps a crab off its back; further out it shows the crab, then throws it.
		var distance = WorldPosition.Distance( target.WorldPosition );
		var clip = distance is >= 12 and <= 256 ? _leapOffClip : distance is >= 250 and <= 800 ? _threatClip ?? _throwClip : null;
		if ( clip is null )
			return false;

		Npc.Movement?.Stop();
		_chosenSlot = Game.Random.FromArray( Enumerable.Range( 0, MaxCrabs ).Where( HasNestCrab ).ToArray() );
		StartCrabClip( clip );
		return true;
	}

	void StartCrabClip( string clip )
	{
		if ( _enemy.IsValid() )
			WorldRotation = Rotation.LookAt( (_enemy.WorldPosition - WorldPosition).WithZ( 0 ) );

		CrabClip = clip;
		_clipStarted = 0;
		_clipLength = Duration( clip, 1 );
		var events = ModelSequences.Of( Npc.Renderer.Model ).Get( clip )?.Events ?? [];
		_cues = events.Where( e => e.Name.StartsWith( "AE_ZOMBIE_POISON_", StringComparison.Ordinal ) ).Select( e => (e.Cycle * _clipLength, e.Name) ).ToList();

		// An attack clip without its crab events still picks the crab up and lets it go.
		if ( clip != _threatClip && !_cues.Any( x => x.Event == ThrowEvent ) )
			_cues.AddRange( [(0.3f * _clipLength, PickUpEvent), (0.5f * _clipLength, ThrowEvent)] );
		_cues.Sort();
	}

	void UpdateCrabAttack()
	{
		// A target that dies mid-throw still gets the crab flung at where it was.
		if ( _enemy.IsValid() )
			_throwPoint = Npcs.CapturePoint( _enemy );

		while ( _cues.Count > 0 && _clipStarted >= _cues[0].Time )
		{
			CrabEvent( _cues[0].Event );
			_cues.RemoveAt( 0 );
		}

		if ( _clipStarted < _clipLength )
			return;

		// The threat display leads into the throw.
		var next = CrabClip == _threatClip ? _throwClip : null;
		CrabClip = null;
		if ( next is not null )
			StartCrabClip( next );
	}

	void CrabEvent( string name )
	{
		if ( name == PickUpEvent && _chosenSlot >= 0 && HasNestCrab( _chosenSlot ) )
		{
			BackCrabs &= ~(1 << _chosenSlot);
			_chosenSlot = -1;
			CrabInHand = true;
		}
		else if ( name == ThrowEvent && CrabInHand )
		{
			// The crab in hand flies at the enemy on the high thrown arc.
			CrabInHand = false;
			_nextThrow = Game.Random.Int( 4, 10 );
			var hand = Npc.Renderer.GetAttachment( "headcrab5" )?.Position ?? Npc.EyePosition;
			SpawnCrab( hand, Rotation.FromYaw( WorldRotation.Yaw() ), _enemy )?.GetComponent<Leap>()?.ThrowAt( _throwPoint );
		}
		else if ( name.EndsWith( "_SOUND", StringComparison.Ordinal ) )
		{
			PlaySound( name.Contains( "WARN" ) ? "NPC_PoisonZombie.ThrowWarn" : "NPC_PoisonZombie.Throw" );
		}
	}

	/// <summary>
	/// Bullets hurt by where they hit: pellets are scaled down, heads take double (triple from pellets), and body shots do half unless buckshot or a sniper round.
	/// A blast's pellets land one by one in a frame but count as one hit: any head pellet makes it a headshot, and their damage adds up.
	/// </summary>
	void INpcPart.OnNpcDamaging( Component.IDamageable.DamageEvent damage )
	{
		var info = damage.DamageInfo;
		var tags = info.Tags;
		var head = tags.Contains( DamageTags.Headshot );
		var buckshot = tags.Contains( "buckshot" );
		var sameBlast = buckshot && _blastAt == Time.Now && _blastBy == info.Attacker;
		(_blastAt, _blastBy) = buckshot ? (Time.Now, info.Attacker) : (-1, null);
		_lastHead = head || sameBlast && _lastHead;
		if ( !sameBlast )
			_lastDamage = _provoke = 0;
		if ( !tags.Contains( DamageTags.Bullet ) )
			return;

		var amount = damage.AcceptedDamage * (buckshot ? 0.625f : 1) * (!head ? 1 : buckshot ? 3 : 2);
		_provoke += amount;
		if ( ThrowsCrabs && _provoke >= Npc.MaxHealth * ThrowProvoke )
			_nextThrow = 0;

		damage.AcceptedDamage = head ? amount / GameHeadBonus : buckshot || tags.Contains( "sniper" ) ? amount : amount * 0.5f;
	}

	void INpcPart.OnNpcHurt( Component.IDamageable.DamageEvent damage )
	{
		_lastDamage += damage.AcceptedDamage;
		var tags = damage.DamageInfo.Tags;
		// Bullets were checked before halving.
		if ( ThrowsCrabs && !tags.Contains( DamageTags.Bullet ) && _lastDamage >= Npc.MaxHealth * ThrowProvoke )
			_nextThrow = 0;

		if ( Npc.Health >= 1 && !IsTorso && !string.IsNullOrEmpty( Torso ) && IsBlast( tags ) && _lastDamage >= Npc.MaxHealth * SplitShare )
			Split( damage.DamageInfo, damage.Instigator );
	}

	static bool IsBlast( TagSet tags ) => tags.Contains( DamageTags.Explosion ) || tags.Contains( "blast" );

	/// <summary>The force a hit carries: along a bullet's path, or out from a blast, in proportion to the damage.</summary>
	Vector3 ForceOf( DamageInfo info )
	{
		var along = info.Tags.Contains( DamageTags.Bullet ) ? info.Position - info.Origin : GameObject.GetBounds().Center - info.Origin;
		return info.Origin == Vector3.Zero || along.Length < 1 ? Vector3.Zero : along.Normal * _lastDamage * ForcePerDamage;
	}

	/// <summary>The zombie's own death, before its ragdoll: the killing blow decides what becomes of the crab and the body.</summary>
	void INpcPart.OnNpcDeath( Component.IDamageable.DeathEvent death )
	{
		var info = death.DamageInfo;
		var tags = info.Tags;
		var killer = death.Instigator.IsValid() ? death.Instigator : info.Attacker;
		var share = Npc.MaxHealth > 0 ? MathF.Min( 1, _lastDamage / Npc.MaxHealth ) : 1;
		var force = ForceOf( info );
		var crusher = info.Weapon.IsValid() ? info.Weapon : info.Attacker;
		var crush = !tags.Contains( "vehicle" ) && (tags.Contains( DamageTags.Crush ) || tags.Contains( "impact" ));
		// Physics impacts aren't tagged sharp, so a sawblade is recognised by its model.
		var sharp = crush && share > SliceShare && (tags.Contains( "slash" )
			|| crusher?.GetComponentInChildren<ModelRenderer>()?.Model?.Name.Contains( "sawblade", StringComparison.OrdinalIgnoreCase ) == true);

		// A sharp prop tears a whole zombie in two, or slices a torso's crab off and tosses it up.
		if ( sharp && (IsTorso ? !Headless : !string.IsNullOrEmpty( TorsoModel )) )
		{
			Gore( WorldPosition, 0, 0, "E3_Phystown.Slicer", MountName );
			if ( IsTorso )
				KnockOffCrab( force * 0.1f + Vector3.Up * 2000 );
			else
				GibDeath( force, squashed: false, killer );
		}
		else if ( crush && !IsTorso && !string.IsNullOrEmpty( Torso ) && IsSquashedBy( crusher ) )
		{
			GibDeath( force, squashed: true, killer );
		}
		else
		{
			ReleaseCrab( info, share, force, killer );
			// A blast that kills sometimes tears the body in two; a zombie that can split needs a big one.
			if ( IsBlast( tags ) && GibsFromBlasts && !IsTorso && share >= (string.IsNullOrEmpty( Torso ) ? 0 : SplitShare) && Game.Random.Int( 0, 2 ) == 0 )
				GibDeath( force, squashed: false, killer );
		}

		if ( ThrowsCrabs )
			EmptyNest( info, killer );
	}

	/// <summary>A body-shot kill frees the crab alive; a sniper round, blast or strong headshot knocks it off dead; anything else leaves it on the corpse.</summary>
	void ReleaseCrab( DamageInfo info, float share, Vector3 force, GameObject killer )
	{
		if ( Headless )
			return;

		var tags = info.Tags;
		var bullet = tags.Contains( DamageTags.Bullet );

		if ( tags.Contains( "sniper" ) || IsBlast( tags ) || bullet && _lastHead && share > StrongHeadshot )
		{
			KnockOffCrab( force * 0.25f );
			return;
		}

		if ( !bullet || _lastHead )
			return;

		// The body falls along the shot, so the crab starts to one side of the head, clear of the ragdoll, and hops off.
		var at = CrabTransform();
		var shot = (info.Position - info.Origin).WithZ( 0 );
		var away = shot.Length > 1 ? Rotation.FromYaw( Game.Random.Float( 60, 120 ) * (Game.Random.Int( 0, 1 ) * 2 - 1) ) * shot.Normal : at.Rotation.Forward;
		var beside = Scene.Trace.Ray( at.Position, at.Position + away * 20 ).Radius( 8 ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		LoseHeadCrab();
		FreeCrab( beside.EndPosition, at.Rotation, killer, away );
	}

	/// <summary>Where a crab leaving the head goes: on the crab attachment facing its yaw, else just below the eyes.</summary>
	Transform CrabTransform() => Npc.Renderer.GetAttachment( "headcrab" ) is { } attachment
		? new Transform( attachment.Position, Rotation.FromYaw( attachment.Rotation.Yaw() ) )
		: new Transform( Npc.EyePosition - Vector3.Up * (IsTorso ? 0 : 16), Rotation.FromYaw( WorldRotation.Yaw() ) );

	/// <summary>The crab comes off dead as a light ragdoll in a spurt of blood, and fades after 15 seconds.</summary>
	void KnockOffCrab( Vector3 push )
	{
		LoseHeadCrab();
		var at = CrabTransform();
		var model = CrabPrefab()?.GetComponentInChildren<SkinnedModelRenderer>( true )?.Model;
		if ( model is null || !Fits( at.Position ) )
			return;

		SpawnRagdoll( model, at, push, lifetime: 15 );
		Gore( at.Position, 4, 8, null, MountName );
	}

	/// <summary>A lethal crush from something of 300 kg or more whose centre is above the eyes.</summary>
	bool IsSquashedBy( GameObject crusher )
	{
		var mass = crusher?.GetComponent<ModelPhysics>()?.Mass ?? crusher?.GetComponentInParent<Rigidbody>()?.Mass ?? 0;
		return mass >= 300 && crusher.GetBounds().Center.z > Npc.EyePosition.z;
	}

	/// <summary>
	/// The body is torn in two and leaves no corpse: legs scatter at the feet and the torso is flung up, or both are
	/// driven down when squashed. Unless squashed, half the time the crab gets away alive first.
	/// </summary>
	void GibDeath( Vector3 force, bool squashed, GameObject killer )
	{
		if ( !Headless && !squashed && Game.Random.Int( 0, 1 ) == 0 )
		{
			var at = CrabTransform();
			LoseHeadCrab();
			FreeCrab( at.Position, at.Rotation, killer, force * 0.005f );
		}

		var yaw = WorldRotation.Yaw();
		var legsPush = new Vector3( Game.Random.Float( -400, 400 ), Game.Random.Float( -400, 400 ), Game.Random.Float( 0, 250 ) * (squashed ? -10 : 1) );
		if ( LoadModel( LegsModel ) is { } legs )
			SpawnRagdoll( legs, new Transform( WorldPosition, Rotation.FromYaw( yaw ) ), legsPush );

		var torsoPush = force.WithZ( 0 ) * Game.Random.Float( 0.04f, 0.06f ) + Vector3.Up * (6000 * Game.Random.Float( 0.8f, 1.2f ) * (squashed ? -1 : 1));
		if ( LoadModel( TorsoModel ) is { } torso )
			SpawnRagdoll( torso, new Transform( WorldPosition + Vector3.Up * 64, Rotation.From( -90, yaw, 0 ) ), torsoPush, withCrab: !Headless );

		Gore( GameObject.GetBounds().Center, 8, 12, null, MountName );
		Npc.Renderer.Model = null;
	}

	/// <summary>Survives a heavy blast as a torso on half its max health, dropping from where it was on the body; the legs fall where it stood.</summary>
	void Split( DamageInfo info, GameObject attacker )
	{
		var prefab = GameObject.GetPrefab( MountContent.Prefab( MountName, Torso ).ResourcePath );
		var torso = prefab.Clone( new CloneConfig { Transform = new Transform( WorldPosition + Vector3.Up * 40, WorldRotation ), StartEnabled = false } );
		// The torso's own skill health would put it back to full.
		torso.GetComponent<NpcDefaults>( true ).HealthSetting = null;
		var npc = torso.GetComponent<CreatureNpc>( true );
		npc.MaxHealth = npc.Health = Npc.MaxHealth * 0.5f;
		if ( attacker.IsValid() )
			npc.SetRelationship( attacker, Disposition.Hostile, priority: 10 );
		torso.Enabled = true;
		torso.NetworkSpawn();

		if ( LoadModel( LegsModel ) is { } legs )
			SpawnRagdoll( legs, WorldTransform, ForceOf( info ) * 0.5f );

		// The torso carries on: the whole body leaves no corpse and no death.
		GameObject.Destroy();
	}

	/// <summary>Every crab still on the poison zombie's back, and one in its hand, scatters alive and goes for the killer, flung harder by a big blast.</summary>
	void EmptyNest( DamageInfo info, GameObject killer )
	{
		var spots = Enumerable.Range( 0, MaxCrabs ).Where( HasNestCrab ).Select( i => $"headcrab{i + 2}" ).ToList();
		if ( CrabInHand )
			spots.Add( "headcrab5" );

		(BackCrabs, CrabInHand) = (0, false);
		ApplyBodyGroups( Npc.Renderer );
		var scale = IsBlast( info.Tags ) && info.Damage > 10 ? 0.1f * info.Damage : 2;
		foreach ( var spot in spots )
		{
			var direction = new Vector3( Game.Random.Float( -1, 1 ), Game.Random.Float( -1, 1 ), Game.Random.Float( 0.5f, 1 ) );
			var at = Npc.Renderer.GetAttachment( spot )?.Position ?? Npc.EyePosition;
			FreeCrab( at, Rotation.FromYaw( Game.Random.Float( 0, 360 ) ), killer, direction * scale * Game.Random.Int( 20, 50 ) );
		}
	}

	/// <summary>A live crab leaves the body if it has room: flung at exactly the given velocity, or hopping off when it has no lift.</summary>
	void FreeCrab( Vector3 at, Rotation facing, GameObject enemy, Vector3 velocity )
	{
		if ( !Fits( at ) || SpawnCrab( at, facing, enemy ) is not { } crab )
			return;

		// A poison crab shaken off panics once it lands
		crab.GetComponent<PoisonCrabPanic>()?.PanicOnLanding( Game.Random.Float( 2, 8 ) );

		if ( velocity.z > 0 )
			crab.GetComponent<CreatureNpc>()?.Launch( velocity );
		else
			crab.GetComponent<Leap>()?.Release( velocity.WithZ( 0 ).Length > 1 ? velocity : facing.Forward );
	}

	/// <summary>Anything solid standing in a crab-sized box on the spot, other than this body and other creatures, means no crab.</summary>
	bool Fits( Vector3 bottom )
	{
		var half = CrabFitSize * 0.5f;
		var box = new BBox( new Vector3( -half, -half, 0 ), new Vector3( half, half, CrabFitSize ) );
		var tr = Scene.Trace.Box( box, bottom, bottom + Vector3.Down ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ragdoll", "npc" ).Run();
		return !tr.Hit && !tr.StartedSolid;
	}

	GameObject SpawnCrab( Vector3 origin, Rotation facing, GameObject enemy )
	{
		var crab = CrabPrefab()?.Clone( new Transform( origin, facing ) );
		if ( crab is null )
			return null;

		if ( enemy.IsValid() )
			crab.GetComponent<CreatureNpc>()?.SetRelationship( enemy, Disposition.Hostile, priority: 10 );
		crab.NetworkSpawn();
		return crab;
	}

	GameObject CrabPrefab() => string.IsNullOrEmpty( Headcrab ) || MountContent.Prefab( MountName, Headcrab ) is not { } prefab ? null : GameObject.GetPrefab( prefab.ResourcePath );

	Model LoadModel( string path )
	{
		var full = $"mount://{MountName}/{path}.vmdl";
		return string.IsNullOrEmpty( path ) || Sandbox.Mounting.Directory.Get( MountName )?.GetByPath( full ) is null ? null : Model.Load( full );
	}

	/// <summary>
	/// A ragdoll piece of this zombie, pushed by an impulse spread over its mass; a torso gib shows or hides its crab.
	/// It goes away like a corpse does, unless the server keeps corpses.
	/// </summary>
	void SpawnRagdoll( Model model, Transform transform, Vector3 push, bool? withCrab = null, float lifetime = CorpseLifetime )
	{
		var go = new GameObject( false, "Zombie piece" );
		go.Tags.Add( "ragdoll", "removable" );
		go.WorldTransform = transform;
		var prop = go.AddComponent<Prop>();
		prop.Model = model;
		prop.Tint = Npc.Renderer.Tint;

		// The prop builds its renderer and physics when enabled, which the batch defers to its end.
		using ( Scene.BatchGroup() )
			go.Enabled = true;

		if ( withCrab.HasValue && go.GetComponent<SkinnedModelRenderer>() is { } renderer )
		{
			renderer.SetBodyGroup( "headcrab1", withCrab.Value ? 1 : 0 );
			prop.BodyGroups = renderer.BodyGroups;
		}

		go.NetworkSpawn( true, null );
		if ( go.GetComponent<ModelPhysics>() is { Mass: > 0 } physics )
		{
			var velocity = (push / physics.Mass).ClampLength( MaxPushSpeed );
			foreach ( var body in physics.Bodies.Where( x => x.Component.IsValid() ) )
				body.Component.Velocity += velocity;
		}

		if ( ConsoleSystem.GetValue( KeepCorpses )?.ToBool() != true )
			prop.Invoke( lifetime, prop.DestroyGameObject );
	}

	/// <summary>Yellow blood spurting around a point, and a sound there; static, as the zombie may be gone by the time it arrives.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Gore( Vector3 center, int blood, float spread, string sound, string mount )
	{
		for ( var i = 0; i < blood; i++ )
			MountedEffects.Spawn( "blood_impact_yellow_01", new Transform( center + Vector3.Random * spread, Rotation.LookAt( Vector3.Random.WithZ( 0.5f ) ) * Rotation.FromPitch( 90 ) ), mount );

		if ( sound is not null && Sounds.Sound( sound, mount ) is { } played )
			Sound.Play( played, center );
	}
}
