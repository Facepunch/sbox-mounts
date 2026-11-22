using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace Overgrowth;

[Library( "overgrowth_turner" )]
public sealed partial class RabbitGuard : BaseNpc, IHotloadManaged
{
	internal const string Idle = "r_idle", Watch = "r_dialogue_shade", Threat = "r_combatidlethreat", Locomotion = "movement";
	internal const string Sweep = "r_sweep", SoccerKick = "r_soccerkick";
	internal const string FreeAir = "jump";
	internal const string Kick = "r_frontkick", Knee = "r_kneestrike", Jab = "r_jab", Roundhouse = "r_roundhouse";

	[Sync] public int AlertStage { get; private set; }
	TimeUntil nextVisionCheck = 1f;
	float enemySeen;
	string playing;
	[Sync] string Stance { get; set; } = Idle;
	RabbitGuardAnimations boundAnimations;

	void IHotloadManaged.Created( IReadOnlyDictionary<string, object> state )
	{
		boundAnimations = null;
		strike = null;
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost && Health > 0 && boundAnimations is not null ) UpdateActiveBlock( Time.Delta );
		if ( !Downed ) base.OnUpdate();
		if ( Networking.IsHost && Health > 0 && !Downed && InThrow && boundAnimations is not null ) UpdateThrow();
		if ( Downed || Health < 1 )
		{
			if ( armsActive || earsActive || plantedLegs || chestActive ) ReleaseProceduralPose();
		}
		else if ( plantedLegs && (Stance is not (Threat or Idle or Watch or Locomotion) || !Animation.IsGrounded) ) ReleaseLegs();
		if ( Health < 1 || !Renderer.IsValid() || Renderer.Model is null ) return;
		if ( boundAnimations is null || Renderer.Animation.Graph is null )
		{
			var mount = Sandbox.Mounting.Directory.Get( "overgrowth" );
			if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is not CharacterLoader loader || loader.GetGuardAnimations() is not { } ready ) return;
			Renderer.ClearPhysicsBones();
			boundAnimations = ready;
			// Overgrowth reseeds secondary motion for four animation updates after a discontinuity.
			secondaryReset = 4f * (4f / 120f);
			leanBones = null;
			Renderer.Animation.Graph = ready.Graph;
			playing = null;
		}
		if ( Networking.IsHost ) UpdateBlockFlinch();
		Renderer.Animation.Parameters.Set( "mirrored_flinch", MirroredFlinch );
		Renderer.Animation.Parameters.Set( "block_flinch", FlinchWeight );
		UpdateMorphs();
		if ( Downed )
		{
			if ( Networking.IsHost ) TryGetUp();
			else
			{
				if ( getUpGraph is not null ) EndGetUpBlend();
				CapturePhysicsPose();
			}
			return;
		}
		if ( !Networking.IsHost && lastPhysicsPose is not null && getUpGraph is null )
		{
			if ( IsGetUp( Stance ) ) BeginGetUpBlend();
			else lastPhysicsPose = null;
		}
		if ( getUpGraph is not null && (Stance != getUpSlot || ReactionPhase * boundAnimations.Clips[getUpSlot].Duration >= getUpPose.Duration) )
			EndGetUpBlend();
		if ( Networking.IsHost ) GazeTarget = visible && enemy.IsValid() ? TargetPosition( enemy, eyes: true ) : null;
		bool track = Animation.IsGrounded && Stance is Threat or Locomotion && GazeTarget.HasValue;
		if ( chestActive && !track ) ReleaseLegs();
		headMotion = chestMotion = null;
		var pose = AirPose();
		hasGaitPose = false;
		if ( pose == Locomotion && playing == Locomotion ) ReadGaitPose();
		UpdateIdleBalance( pose is Threat or Idle or Watch );
		if ( pose == Idle ) Renderer.Animation.Parameters.Set( "tall_coord", 1f - Math.Clamp( CrouchAmount, 0f, 1f ) );
		if ( pose == Locomotion && Networking.IsHost ) MirroredStance = LeftHanded;
		Renderer.Animation.Parameters.Set( "mirrored_pose", pose is Threat or Idle or Locomotion ? MirroredStance : MirroredAction );
		if ( pose == Locomotion ) UpdateGait();
		else UpdateBodyLean( 0f );
		if ( pose == Threat ) UpdateStanceFeet( playing != Threat );
		if ( pose is Idle or Watch && Animation.IsGrounded ) UpdateIdleFeet();
		if ( track ) UpdateChestLook( GazeTarget.Value );
		UpdateArms();
		UpdateEars();
		PublishPose();
		hasGaitPose = false;
		if ( boundAnimations.AttackStates.Contains( Stance ) )
		{
			Renderer.Animation.Parameters.Set( "attack_height_coord", AttackHeight );
			Renderer.Animation.Parameters.Set( "attack_phase", AttackPhase );
		}
		if ( ReactionPhase < 1f ) Renderer.Animation.Parameters.Set( "reaction_phase", ReactionPhase );
		if ( playing == pose ) return;
		playing = pose;
		Renderer.Animation.Parameters.Set( "stance", playing );
	}

	protected override void Think()
	{
		if ( !Renderer.IsValid() || boundAnimations is null ) return;
		UpdateStanceAmounts();
		if ( InThrow || UpdateDefense() ) return;
		if ( nextVisionCheck <= 0 )
		{
			nextVisionCheck = Random.Shared.Float( 0.2f, 0.3f );
			UpdateAwareness();
		}
		if ( !IsGetUp( Stance ) ) hitsOnGround = 0;
		UpdateCombat();
	}

	GameObject enemy;
	Vector3 lastSeen;
	bool visible;
	Vector3? investigatePoint;
	bool urgentInvestigation;
	TimeUntil hearingReady = 1f;

	TimeSince sinceEnemySeen;

	protected override void SetupRelationships() => SetRelationship( Factions.Player, Disposition.Hostile );

	protected override Relationship GetFallbackRelationship( GameObject other ) =>
		new( other.GetComponent<BaseNpc>().IsValid() ? Disposition.Hostile : Disposition.Neutral );

	bool CanAttack( GameObject other ) => other.IsValid() && other != GameObject
		&& IsLiveTarget( other ) && other.GetComponent<BaseNpc>() is not { Health: < 1 }
		&& GetDisposition( other ) == Disposition.Hostile;

	// MovementObject follows the mean physics-bone position while ragdolled. Here the
	// navigation root stays parked until recovery, so observers read the bodies directly.
	Vector3 TargetPosition( GameObject target, bool eyes = false )
	{
		if ( target.GetComponent<RabbitGuard>() is { Downed: true, ragdoll: { } physics } )
		{
			Vector3 sum = default;
			int count = 0;
			foreach ( var body in physics.Bodies )
			{
				if ( !body.Component.IsValid() ) continue;
				sum += body.Component.WorldPosition;
				count++;
			}
			if ( count > 0 ) return sum / count;
		}
		return eyes ? CalculateEyePosition( target ) : target.WorldPosition;
	}

	void NoticeAttacker( GameObject attacker )
	{
		if ( !CanAttack( enemy ) && CanAttack( attacker ) )
			Remember( attacker, engage: false );
	}

	void Remember( GameObject target, bool engage = true )
	{
		if ( engage && !enemy.IsValid() && !escortTarget.IsValid() )
		{
			startled = 1f;
			Speak( "engage" );
		}
		enemy = target;
		escortTarget = null;
		investigatePoint = null;
		urgentInvestigation = false;
		lastSeen = TargetPosition( target );
		sinceEnemySeen = 0;
		visible = true;
		AlertStage = 2;
	}

	void UpdateAwareness()
	{
		if ( strike is not null ) return;
		visible = false;
		GameObject closest = null;
		float nearest = float.MaxValue;
		float attention = 0f;
		var candidates = Scene.GetAllComponents<BaseNpc>().Select( npc => npc.GameObject )
			.Concat( Scene.GetAllComponents<PlayerController>().Select( controller => controller.GameObject ) );
		foreach ( var candidate in candidates )
		{
			if ( !CanAttack( candidate ) ) continue;
			float separation = WorldPosition.DistanceSquared( TargetPosition( candidate ) );
			var delta = TargetPosition( candidate, eyes: true ) - EyePosition;
			float distance = delta.Length;
			var angles = (WorldRotation.Inverse * delta).EulerAngles.Normal;
			bool focus = distance <= 4000 && MathF.Abs( angles.yaw ) <= 36 && MathF.Abs( angles.pitch ) <= 36;
			bool peripheral = distance <= 160 && MathF.Abs( angles.yaw ) <= 81 && MathF.Abs( angles.pitch ) <= 81;
			if ( !focus && !peripheral ) continue;
			if ( !HasSight( candidate ) ) continue;
			attention += (0.5f - 0.4f * MathF.Sqrt( separation ) / 4000f) * (enemy.IsValid() || investigatePoint.HasValue ? 3f : 1f);
			if ( candidate == enemy ) visible = true;
			if ( separation < nearest )
			{
				closest = candidate;
				nearest = separation;
			}
		}
		if ( closest.IsValid() )
		{
			enemySeen = Math.Min( 1f, enemySeen + attention );
			if ( enemySeen >= 1f ) Remember( closest );
			else if ( !enemy.IsValid() && !investigatePoint.HasValue && enemySeen > 0.5f )
				investigatePoint = Movement.ProjectPoint( WorldPosition + (TargetPosition( closest ) - WorldPosition).Normal * 120f );
		}
		else enemySeen = Math.Max( 0, enemySeen - 0.05f );
		AlertStage = enemy.IsValid() ? 2 : escortTarget.IsValid() || investigatePoint.HasValue || enemySeen > 0 ? 1 : 0;
	}

	bool HasSight( GameObject other ) => !Scene.Trace.Ray( EyePosition, TargetPosition( other, eyes: true ) )
		.IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( other ).WithoutTags( "trigger" ).Run().Hit;

}
