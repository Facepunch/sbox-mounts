using System;
using Sandbox.Navigation;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	sealed class SearchPoint( Vector3 position )
	{
		public Vector3 Position { get; } = position;
		public float SeenTime;
	}

	readonly List<SearchPoint> searchPoints = [];
	TimeUntil searchUntil;
	Vector3? lastKnownSearchPoint;
	SearchPoint searchTarget;
	readonly HashSet<GameObject> knownCasualties = [];
	GameObject casualty;
	TimeUntil? inspectUntil;
	Vector3? inspectPosition;
	bool Investigating => investigatePoint.HasValue || casualty.IsValid() || inspectPosition.HasValue || searchUntil > 0f;

	void NoticeCasualty( GameObject ally )
	{
		if ( !knownCasualties.Add( ally ) || enemy.IsValid() || Investigating || escortTarget.IsValid() ) return;
		casualty = ally;
		inspectPosition = null;
		inspectUntil = null;
		startled = 1f;
		LookAtNotice( TargetPosition( ally ), 1f );
		Speak( "suspicious" );
		ReportBodySound( HeardSound.Voice, 10f );
	}

	bool InvestigateCasualty()
	{
		if ( !casualty.IsValid() && !inspectPosition.HasValue ) return false;
		var position = casualty.IsValid() ? TargetPosition( casualty ) : inspectPosition.Value;
		if ( startled > 0f || inspectUntil.HasValue )
		{
			ReleaseMovementInput();
			Stance = Ready;
			if ( inspectUntil is { } until && until <= 0f )
			{
				casualty = null;
				inspectUntil = null;
				inspectPosition = null;
			}
			return true;
		}
		if ( ApproachSite( position, true, 0.7f ) is { } reached ) InspectSite( reached );
		return true;
	}

	Vector3? ApproachSite( Vector3 target, bool urgent, float speed )
	{
		var point = Movement.ProjectPoint( target ) ?? target;
		var corner = NextPathCorner( point, out var end ) ?? end ?? point;
		// The AI checks the reachable end of the route, including for a partial path.
		if ( end is { } reached && (Movement.ProjectPoint( WorldPosition ) ?? WorldPosition).DistanceSquared( reached ) < (urgent ? 2f : 1f) * 40f * 40f )
			return reached;
		if ( moveDelay > 0f ) point = corner = WorldPosition;
		var toward = (point - WorldPosition).WithZ( 0f ).Normal;
		var intent = PathSteering( corner, toward, urgent ? 40f : 0f, ref point );
		Stance = MoveWithIntent( point, intent, speed ) ? Locomotion : Ready;
		return null;
	}

	void InspectSite( Vector3 point )
	{
		ReleaseMovementInput();
		var direction = (point - WorldPosition).WithZ( 0f );
		if ( !direction.IsNearlyZero() ) WorldRotation = Rotation.LookAt( direction );
		inspectPosition = point;
		BeginBodyInspection( Random.Shared.Float( 2f, 4f ) * (1f - 0.5f * GameDifficulty) );
		Stance = Ready;
	}

	void FinishCombat( Vector3 defeatedPosition )
	{
		suspicion = enemySeen = 0f;
		investigatePoint = null;
		urgentInvestigation = false;
		var armed = PrimaryItem.IsValid();
		int choice;
		do choice = Random.Shared.Next( 10 );
		while ( choice == 2 && (!armed || boundAnimations.WeaponLabel != "sword") || armed && choice is 3 or 8 );
		switch ( choice )
		{
			case 4:
				patrolWait = Random.Shared.Float( 0f, 5f );
				waypointAnimation = null;
				goto case 5;
			case 5:
				BeginAreaSearch();
				break;
			case 6:
				inspectPosition = defeatedPosition;
				BeginBodyInspection( Random.Shared.Float( 2f, 4f ) );
				break;
			case 7:
				inspectPosition = defeatedPosition;
				inspectUntil = null;
				break;
			default:
				(waypointAnimation, float duration) = choice switch
				{
					0 => ("Data/Animations/r_actionidle_footshake.anm", 2.5f),
					1 => ("Data/Animations/r_actionidle_yawn.anm", 5f),
					2 => ("Data/Animations/r_swordclean.anm", Random.Shared.Float( 5f, 10f )),
					3 => ("Data/Animations/r_crossarms_idle.anm", Random.Shared.Float( 5f, 10f )),
					8 => ("Data/Animations/r_dialogue_handhips.anm", Random.Shared.Float( 2f, 5f )),
					_ => ("Data/Animations/r_dialogue_kneelfist.anm", Random.Shared.Float( 2f, 5f ))
				};
				patrolWait = duration;
				break;
		}
	}

	void InvestigatePatrolDamage()
	{
		if ( CombatAllowed && !Unconscious && !StaticCharacter && !enemy.IsValid() && !Investigating &&
			!escortTarget.IsValid() && !soughtItem.IsValid() && !Held && !Holding )
			BeginAreaSearch();
	}

	void BeginAreaSearch( Vector3? lastKnown = null )
	{
		searchPoints.Clear();
		searchTarget = null;
		lastKnownSearchPoint = lastKnown;
		searchUntil = 10f + (GameDifficulty >= 0.25f ? Random.Shared.Float( 0.35f, 1.25f ) : 0f);
	}

	bool SearchArea()
	{
		if ( searchUntil <= 0f ) return false;
		if ( lastKnownSearchPoint is { } pointToCheck )
		{
			if ( ApproachSite( pointToCheck, false, 1f ) is null ) return true;
			lastKnownSearchPoint = null;
		}
		if ( FindWeapon() ) return true;

		var agent = GetComponent<NavMeshAgent>();
		if ( searchPoints.Count == 0 )
		{
			const float spacing = 8f * 40f;
			for ( int i = -3; i < 3; ++i )
			for ( int j = -3; j < 3; ++j )
			{
				var offset = new Vector3( j + Random.Shared.Float( -0.5f, 0.5f ), i + Random.Shared.Float( -0.5f, 0.5f ), Random.Shared.Float( -1f, 1f ) );
				var sample = WorldPosition + offset * spacing;
				var pointOnMesh = Scene.NavMesh.GetClosestPoint( BBox.FromPositionAndSize( sample, new Vector3( 4f, 4f, 8f ) * 40f ) )
					?? Scene.NavMesh.GetClosestPoint( BBox.FromPositionAndSize( sample, new Vector3( 8f, 8f, 32f ) * 40f ) );
				if ( pointOnMesh is not { } point ) continue;
				// Investigate only where the leg sphere actually touches level geometry.
				if ( !Scene.Trace.Sphere( 0.45f * 40f, point, point ).IgnoreDynamic()
					.WithoutTags( "trigger", "npc", "player" ).Run().Hit ) continue;
				var path = Scene.NavMesh.CalculatePath( new() { Start = WorldPosition, Target = point, Agent = agent } );
				if ( path.Status == NavMeshPathStatus.Complete ) searchPoints.Add( new( point + Vector3.Up * 18f ) );
			}
		}
		var frame = CharacterLoader.AverageChainFrame( Renderer, "head" );
		var head = frame?.Position ?? EyePosition;
		var facing = frame?.Direction( new Vector3( 0.2f, 0f, 0.8f ) ).Normal ?? WorldRotation.Forward;
		for ( int i = searchPoints.Count - 1; i >= 0; --i )
		{
			var point = searchPoints[i];
			if ( (WorldPosition + Vector3.Up * 18f).DistanceSquared( point.Position ) >= 40f * 40f &&
				((point.Position - head).Normal.Dot( facing ) <= 0.5f || Scene.Trace.Ray( head, point.Position )
					.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "npc", "player" ).Run().Hit) ) continue;
			point.SeenTime += Time.Delta * 3f;
			if ( point.SeenTime < 1f ) continue;
			searchPoints.RemoveAt( i );
			moveDelay = Random.Shared.Float( 0.2f, 0.6f );
			if ( point == searchTarget ) searchTarget = null;
		}

		if ( moveDelay <= 0.1f && (repath <= 0f || searchTarget is null) )
		{
			float shortest = float.PositiveInfinity;
			searchTarget = null;
			foreach ( var point in searchPoints )
			{
				var path = Scene.NavMesh.CalculatePath( new() { Start = WorldPosition, Target = point.Position - Vector3.Up * 18f, Agent = agent } );
				if ( path.Status != NavMeshPathStatus.Complete ) continue;
				float length = 0f;
				for ( int i = 1; i < path.Points.Count; ++i ) length += path.Points[i-1].Position.Distance( path.Points[i].Position );
				if ( length >= shortest ) continue;
				shortest = length;
				searchTarget = point;
			}
			repath = 0.2f;
		}
		if ( searchTarget is not null && moveDelay <= 0.1f ) LookAtNotice( searchTarget.Position, 1f );
		if ( searchTarget is null )
		{
			ReleaseMovementInput();
			Stance = Ready;
		}
		else
		{
			var dest = searchTarget.Position - Vector3.Up * 18f;
			Stance = MoveTowardPoint( moveDelay > 0f ? WorldPosition : dest, 0f, 0.4f ) ? Locomotion : Ready;
		}
		return true;
	}
}
