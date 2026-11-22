using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace Overgrowth;

[Library( "overgrowth_turner" )]
public sealed partial class RabbitGuard : BaseNpc, IHotloadManaged
{
	internal const string Idle = "r_idle", Watch = "r_dialogue_shade", Threat = "r_combatidlethreat", Locomotion = "movement";
	internal const string Sweep = "r_sweep", SoccerKick = "r_soccerkick";
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
		if ( Health < 1 || !Renderer.IsValid() || Renderer.Model is null ) return;
		if ( boundAnimations is null || Renderer.Animation.Graph is null )
		{
			var mount = Sandbox.Mounting.Directory.Get( "overgrowth" );
			if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is not CharacterLoader loader || loader.GetGuardAnimations() is not { } ready ) return;
			boundAnimations = ready;
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
		if ( Stance == Locomotion ) UpdateGait();
		if ( boundAnimations.AttackStates.Contains( Stance ) )
		{
			Renderer.Animation.Parameters.Set( "attack_height_coord", AttackHeight );
			Renderer.Animation.Parameters.Set( "attack_phase", AttackPhase );
		}
		if ( ReactionPhase < 1f ) Renderer.Animation.Parameters.Set( "reaction_phase", ReactionPhase );
		if ( playing == Stance ) return;
		playing = Stance;
		Renderer.Animation.Parameters.Set( "stance", playing );
	}

	protected override void Think()
	{
		if ( !Renderer.IsValid() || boundAnimations is null ) return;
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
	GameObject watching;

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

	void Remember( GameObject target )
	{
		if ( !enemy.IsValid() )
		{
			startled = 1f;
			Speak( "engage" );
		}
		enemy = target;
		lastSeen = TargetPosition( target );
		sinceEnemySeen = 0;
		visible = true;
		AlertStage = 2;
	}

	void UpdateAwareness()
	{
		if ( strike is not null ) return;
		// A known opponent remains recognizable outside the initial acquisition cone.
		visible = CanAttack( enemy ) && HasSight( enemy );
		GameObject closest = visible ? enemy : null;
		float nearest = visible ? WorldPosition.DistanceSquared( TargetPosition( enemy ) ) : float.MaxValue;
		var candidates = Scene.GetAllComponents<BaseNpc>().Select( npc => npc.GameObject )
			.Concat( Scene.GetAllComponents<PlayerController>().Select( controller => controller.GameObject ) );
		foreach ( var candidate in candidates )
		{
			if ( candidate == enemy || !CanAttack( candidate ) ) continue;
			float separation = WorldPosition.DistanceSquared( TargetPosition( candidate ) );
			if ( separation >= nearest ) continue;
			var delta = TargetPosition( candidate, eyes: true ) - EyePosition;
			float distance = delta.Length;
			var angles = (WorldRotation.Inverse * delta).EulerAngles.Normal;
			bool focus = distance <= 4000 && MathF.Abs( angles.yaw ) <= 36 && MathF.Abs( angles.pitch ) <= 36;
			bool peripheral = distance <= 160 && MathF.Abs( angles.yaw ) <= 81 && MathF.Abs( angles.pitch ) <= 81;
			if ( !focus && !peripheral ) continue;
			if ( !HasSight( candidate ) ) continue;
			closest = candidate;
			nearest = separation;
		}
		if ( closest.IsValid() )
		{
			if ( closest != watching ) enemySeen = 0;
			watching = closest;
			enemySeen = Math.Min( 1, enemySeen + 0.5f - 0.4f * MathF.Sqrt( nearest ) / 4000f );
			if ( enemy.IsValid() || enemySeen >= 1 ) Remember( closest );
		}
		else enemySeen = Math.Max( 0, enemySeen - 0.05f );
		AlertStage = enemy.IsValid() ? 2 : enemySeen > 0 ? 1 : 0;
	}

	bool HasSight( GameObject other ) => !Scene.Trace.Ray( EyePosition, TargetPosition( other, eyes: true ) )
		.IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( other ).WithoutTags( "trigger" ).Run().Hit;

}
