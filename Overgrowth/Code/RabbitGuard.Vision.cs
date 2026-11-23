using System;
using Sandbox.Npcs;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	// Horizontal and vertical half angles in radians, followed by distance in metres.
	[Property] public Vector3 FocusVision { get; set; } = new( 3.1417f * 0.2f, 3.1417f * 0.2f, 100f );
	[Property] public Vector3 PeripheralVision { get; set; } = new( 3.1417f * 0.45f, 3.1417f * 0.45f, 4f );
	VisionHull focusHull, peripheralHull;
	readonly record struct KnownCharacter( Vector3 Position, bool Awake, bool Hostile );
	readonly Dictionary<GameObject, KnownCharacter> knownCharacters = [];

	void NoticeCharacter( GameObject actor )
	{
		var npc = actor.GetComponent<BaseNpc>();
		if ( npc is RabbitGuard { StaticCharacter: true } || actor.GetComponent<CharacterRemains>() is { StaticCharacter: true } ) return;
		bool awake = IsLiveTarget( actor ) && npc is not ({ Health: < 1 } or RabbitGuard { Unconscious: true }) &&
			!actor.GetComponent<CharacterRemains>().IsValid();
		knownCharacters[actor] = new( CharacterCenter( actor ), awake, GetDisposition( actor ) == Disposition.Hostile );
	}

	// A native character retains its identity after death. Keep the last observation
	// intact while s&box replaces the living NPC with a separate corpse object.
	void FollowCorpse( GameObject actor, GameObject corpse )
	{
		if ( knownCharacters.Remove( actor, out var memory ) ) knownCharacters[corpse] = memory;
		if ( seenHostiles.Remove( actor, out var seen ) ) seenHostiles[corpse] = seen;
		if ( enemy == actor ) enemy = corpse;
		if ( observedTarget == actor ) observedTarget = corpse;
		if ( fleeFrom == actor ) fleeFrom = corpse;
		if ( noticeSource == actor ) noticeSource = corpse;
		if ( knownCasualties.Remove( actor ) ) knownCasualties.Add( corpse );
		if ( chokeVictim == actor ) chokeVictim = corpse;
		if ( casualty == actor ) casualty = corpse;
		if ( escortTarget == actor ) escortTarget = corpse;
	}

	bool IsKnownThreat( GameObject actor ) => actor.IsValid() && !IsIgnoredPlayer( actor ) &&
		(knownCharacters.TryGetValue( actor, out var memory ) ? memory is { Awake: true, Hostile: true } : CanAttack( actor ));

	GameObject ClosestKnownThreat()
	{
		GameObject closest = null;
		float nearest = float.MaxValue;
		var center = CharacterCenter( GameObject );
		foreach ( var (other, memory) in knownCharacters )
		{
			if ( !other.IsValid() || IsIgnoredPlayer( other ) || !memory.Hostile || !memory.Awake ) continue;
			float distance = center.DistanceSquared( memory.Position );
			if ( distance >= nearest ) continue;
			nearest = distance;
			closest = other;
		}
		return closest;
	}

	static bool InView( ref VisionHull hull, Vector3 settings, Vector3 center )
	{
		const float characterRadius = 0.5f * 40f;
		float range = settings.z * 40f;
		if ( range <= 0f ) return center.LengthSquared <= characterRadius * characterRadius;
		if ( center.LengthSquared > (range + characterRadius) * (range + characterRadius) ) return false;
		var angles = new Vector2( settings.x, settings.y );
		if ( hull?.Angles != angles ) hull = new VisionHull( angles );
		return hull.IntersectsSphere( center / range, characterRadius / range );
	}

	(Vector3 Position, Vector3 Forward, Vector3 Left, Vector3 Up) GetViewBasis()
	{
		if ( CharacterLoader.AverageChainFrame( Renderer, "head" ) is not { } head )
			return (EyePosition, WorldRotation.Forward, WorldRotation.Left, WorldRotation.Up);
		// aschar passes -70 directly to mat4::SetRotationX, which takes radians.
		var offset = Rotation.FromAxis( Vector3.Left, (-70f).RadianToDegree() );
		return (head.Position, head.Direction( offset.Forward ), head.Direction( offset.Left ), head.Direction( offset.Up ));
	}

	void TrackEnemy()
	{
		if ( enemy.IsValid() && IsIgnoredPlayer( enemy ) )
		{
			seenHostiles.Remove( enemy );
			enemy = null;
			visible = false;
			enemySeen = 0;
			Movement.Stop();
		}
		if ( IsKnownThreat( enemy ) && !Sleeping )
		{
			visible = CanSeeCharacter( enemy, enemy.GetComponent<RabbitGuard>() );
			if ( visible )
			{
				ObserveTarget( enemy, true );
				lastSeen = TargetPosition( enemy );
				NoticeCharacter( enemy );
				sinceEnemySeen = 0;
				seenHostiles[enemy] = Time.Now;
			}
		}
	}

	void ScanForCharacters()
	{
		if ( nextVisionCheck <= 0 && !Sleeping )
		{
			nextVisionCheck = Random.Shared.Float( 0.2f, 0.3f );
			UpdateAwareness();
		}
	}

	void UpdateAwareness()
	{
		foreach ( var other in knownCharacters.Keys )
			if ( !other.IsValid() ) knownCharacters.Remove( other );
		foreach ( var (other, seen) in seenHostiles )
			if ( !other.IsValid() || Time.Now - seen >= 1f ) seenHostiles.Remove( other );
		GameObject closest = null;
		float nearest = float.MaxValue;
		float attention = 0f;
		bool detectedEnemy = false;
		knownCasualties.RemoveWhere( ally => !ally.IsValid() || ally.GetComponent<RabbitGuard>() is { Unconscious: false } );
		var candidates = Scene.GetAllComponents<BaseNpc>().Select( npc => npc.GameObject )
			.Concat( Scene.GetAllComponents<PlayerController>().Select( controller => controller.GameObject ) )
			.Concat( Scene.GetAllComponents<CharacterRemains>().Select( remains => remains.GameObject ) );
		var view = GetViewBasis();
		var observer = CharacterCenter( GameObject );
		foreach ( var candidate in candidates )
		{
			var ally = candidate.GetComponent<RabbitGuard>();
			if ( candidate == GameObject || IsIgnoredPlayer( candidate ) ) continue;
			var remains = candidate.GetComponent<CharacterRemains>();
			if ( ally is { StaticCharacter: true } || remains is { StaticCharacter: true } ) continue;
			bool friendly = GetDisposition( candidate ) == Disposition.Friendly;
			bool threat = !friendly && CanAttack( candidate );
			bool known = knownCharacters.ContainsKey( candidate );
			if ( !friendly && !threat && !known ) continue;
			var center = CharacterCenter( candidate );
			float separation = observer.DistanceSquared( center );
			bool concealed = !friendly && ally is { InvisibleWhenStationary: true };
			bool hidden = concealed && ally.duckAmount >= 0.5f
				&& ally.Animation.MoveVelocity.LengthSquared <= 0.1f * 40f * 40f && separation >= 4f * 40f * 40f;
			var local = center - view.Position;
			local = new( local.Dot( view.Forward ), local.Dot( view.Left ), local.Dot( view.Up ) );
			if ( !InView( ref focusHull, FocusVision, local ) && !InView( ref peripheralHull, PeripheralVision, local ) ) continue;
			if ( candidate == enemy ? !visible : !CanSeeCharacter( candidate, ally, remains ) ) continue;
			if ( friendly || known ) NoticeCharacter( candidate );
			if ( friendly )
			{
				if ( ally is { Unconscious: true } || remains.IsValid() ) NoticeCasualty( candidate );
				continue;
			}
			if ( !threat ) continue;
			if ( separation < nearest )
			{
				closest = candidate;
				nearest = separation;
			}
			if ( hidden ) continue;
			detectedEnemy = true;
			seenHostiles[candidate] = Time.Now;
			if ( !enemy.IsValid() && !escortTarget.IsValid() )
			{
				LookAtNotice( center + GetVelocity( candidate ) * (float)nextVisionCheck, source: candidate );
			}
			bool patrolling = !enemy.IsValid() && !escortTarget.IsValid() && !Investigating;
			attention += (FocusVision.z == 0f ? 0.1f : 0.5f - 0.4f * MathF.Sqrt( separation ) / (FocusVision.z * 40f)) * (patrolling ? 1f : 3f) * (concealed ? 0.3f : 1f);

		}
		enemySeen = detectedEnemy ? Math.Min( 1f, enemySeen + attention ) : Math.Max( 0f, enemySeen - 0.05f );
		if ( closest.IsValid() )
		{
			if ( enemySeen >= 1f )
			{
				if ( !enemy.IsValid() ) Remember( closest );
				else NoticeCharacter( closest );
			}
			else if ( !enemy.IsValid() && !escortTarget.IsValid() && !Investigating && enemySeen > 0.5f )
				investigatePoint = Movement.ProjectPoint( observer + (CharacterCenter( closest ) - observer).Normal * 120f );
		}
		AlertStage = enemy.IsValid() ? 2 : escortTarget.IsValid() || Investigating || enemySeen > 0 ? 1 : 0;
	}

	bool HasSight( GameObject other ) => !Scene.Trace.Ray( EyePosition, TargetPosition( other, eyes: true ) )
		.IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( other ).WithoutTags( "trigger" ).Run().Hit;

	bool CanSeeCharacter( GameObject target, RabbitGuard fighter, CharacterRemains remains = null )
	{
		remains ??= target.GetComponent<CharacterRemains>();
		var renderer = fighter?.Renderer ?? (remains.IsValid() ? target.GetComponent<SkinnedModelRenderer>() : null);
		var end = CharacterLoader.RandomSightPosition( renderer, fighter ) ?? TargetPosition( target, eyes: true );
		var start = TargetPosition( GameObject, eyes: true );
		if ( Scene.Trace.Ray( start, end )
			.IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( target ).WithoutTags( "trigger", "npc", "player" ).Run().Hit ) return false;
		var plant = Scene.Trace.Ray( start, end ).WithTag( PlantHull.Tag ).HitTriggers().Run();
		return !plant.Hit || plant.EndPosition.DistanceSquared( end ) <= 32f * 32f;
	}

}
