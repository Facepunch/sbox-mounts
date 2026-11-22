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
		if ( !Downed ) base.OnUpdate();
		if ( Downed || Health < 1 ) ResetBodyLean();
		if ( Downed || plantedLegs && (Stance != Threat || !Animation.IsGrounded) ) ReleaseLegs();
		if ( Health < 1 || !Renderer.IsValid() || Renderer.Model is null ) return;
		if ( boundAnimations is null || Renderer.Animation.Graph is null )
		{
			var mount = Sandbox.Mounting.Directory.Get( "overgrowth" );
			if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is not CharacterLoader loader || loader.GetGuardAnimations() is not { } ready ) return;
			boundAnimations = ready;
			leanBones = null;
			Renderer.Animation.Graph = ready.Graph;
			playing = null;
		}
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
		var pose = AirPose();
		if ( pose == Idle ) Renderer.Animation.Parameters.Set( "tall_coord", 1f - Math.Clamp( CrouchAmount, 0f, 1f ) );
		if ( pose == Locomotion ) UpdateGait();
		else UpdateBodyLean( 0f );
		if ( pose == Threat ) UpdateStanceFeet( playing != Threat );
		if ( track ) UpdateChestLook( GazeTarget.Value );
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
		if ( UpdateDefense() ) return;
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

	void ReportCombatSound( GameObject source, Vector3 position )
	{
		// AISound sends the impact from the struck character. Rabbits hear it at twice the loud radius.
		const float range = 10f * 2f * 40f;
		foreach ( var guard in Scene.GetAllComponents<RabbitGuard>() )
		{
			if ( !guard.Active || guard.GameObject == source || guard.Health < 1 || guard.Downed || guard.hearingReady > 0 ||
				guard.WorldPosition.DistanceSquared( position ) >= range * range ) continue;
			if ( guard.enemy.IsValid() )
			{
				if ( guard.enemy != source ) continue;
				guard.lastSeen = guard.TargetPosition( source );
				guard.sinceEnemySeen = 0;
				continue;
			}
			if ( !guard.CanAttack( source ) ) continue;
			if ( !guard.investigatePoint.HasValue )
			{
				guard.Speak( "suspicious" );
				guard.startled = 1f;
			}
			guard.investigatePoint = guard.Movement.ProjectPoint( position );
			guard.urgentInvestigation = true;
			guard.AlertStage = 1;
		}
	}

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
		if ( engage && !enemy.IsValid() )
		{
			startled = 1f;
			Speak( "engage" );
		}
		enemy = target;
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
		AlertStage = enemy.IsValid() ? 2 : investigatePoint.HasValue || enemySeen > 0 ? 1 : 0;
	}

	bool HasSight( GameObject other ) => !Scene.Trace.Ray( EyePosition, TargetPosition( other, eyes: true ) )
		.IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( other ).WithoutTags( "trigger" ).Run().Hit;

}
