using System;
using Sandbox.Npcs;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	// Horizontal and vertical half angles in radians, followed by distance in metres.
	[Property] public Vector3 FocusVision { get; set; } = new( 3.1417f * 0.2f, 3.1417f * 0.2f, 100f );
	[Property] public Vector3 PeripheralVision { get; set; } = new( 3.1417f * 0.45f, 3.1417f * 0.45f, 4f );
	VisionHull focusHull, peripheralHull;
	readonly HashSet<GameObject> knownCharacters = [];

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
		var bone = Renderer.Model.Bones.GetBone( boundAnimations.HeadBone );
		// aschar passes -70 directly to mat4::SetRotationX, which takes radians.
		var offset = Rotation.FromAxis( Vector3.Left, (-70f).RadianToDegree() );
		Vector3 position = default, forward = default, left = default, up = default;
		int count = 0;
		while ( count < boundAnimations.Head.ChainLength && bone is not null && Renderer.TryGetBoneTransform( bone, out var pose ) )
		{
			var basis = pose.Rotation * offset;
			position += pose.Position;
			forward += basis.Forward;
			left += basis.Left;
			up += basis.Up;
			count++;
			bone = bone.Parent;
		}
		return count > 0 ? (position / count, forward / count, left / count, up / count)
			: (EyePosition, WorldRotation.Forward, WorldRotation.Left, WorldRotation.Up);
	}

	void UpdateAwareness()
	{
		if ( strike is not null ) return;
		knownCharacters.RemoveWhere( other => !other.IsValid() );
		foreach ( var (other, seen) in seenHostiles )
			if ( !other.IsValid() || Time.Now - seen >= 1f ) seenHostiles.Remove( other );
		GameObject closest = null;
		float nearest = float.MaxValue;
		float attention = 0f;
		knownCasualties.RemoveWhere( ally => !ally.IsValid() || !ally.Unconscious );
		var candidates = Scene.GetAllComponents<BaseNpc>().Select( npc => npc.GameObject )
			.Concat( Scene.GetAllComponents<PlayerController>().Select( controller => controller.GameObject ) );
		var view = GetViewBasis();
		foreach ( var candidate in candidates )
		{
			var ally = candidate.GetComponent<RabbitGuard>();
			if ( candidate == GameObject || IsIgnoredPlayer( candidate ) ) continue;
			bool friendly = GetDisposition( candidate ) == Disposition.Friendly;
			if ( !friendly && !CanAttack( candidate ) ) continue;
			float separation = WorldPosition.DistanceSquared( TargetPosition( candidate ) );
			bool concealed = !friendly && ally is { InvisibleWhenStationary: true };
			if ( concealed && ally.duckAmount >= 0.5f
				&& ally.Animation.MoveVelocity.LengthSquared <= 0.1f * 40f * 40f && separation >= 4f * 40f * 40f ) continue;
			var center = CharacterCenter( candidate );
			var local = center - view.Position;
			local = new( local.Dot( view.Forward ), local.Dot( view.Left ), local.Dot( view.Up ) );
			if ( !InView( ref focusHull, FocusVision, local ) && !InView( ref peripheralHull, PeripheralVision, local ) ) continue;
			if ( candidate == enemy ? !visible : !CanSeeCharacter( candidate, ally ) ) continue;
			if ( friendly )
			{
				knownCharacters.Add( candidate );
				if ( ally is { Unconscious: true } ) NoticeCasualty( ally );
				continue;
			}
			seenHostiles[candidate] = Time.Now;
			if ( !enemy.IsValid() && !escortTarget.IsValid() )
			{
				var velocity = candidate.GetComponent<BaseNpc>()?.Animation.MoveVelocity
					?? candidate.GetComponent<PlayerController>()?.Velocity ?? Vector3.Zero;
				LookAtNotice( center + velocity * (float)nextVisionCheck, source: candidate );
			}
			attention += (FocusVision.z == 0f ? 0.1f : 0.5f - 0.4f * MathF.Sqrt( separation ) / (FocusVision.z * 40f)) * (enemy.IsValid() || investigatePoint.HasValue ? 3f : 1f) * (concealed ? 0.3f : 1f);
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

	bool CanSeeCharacter( GameObject target, RabbitGuard fighter )
	{
		var end = TargetPosition( target, eyes: true );
		if ( fighter?.boundAnimations?.SightBones is { Length: > 0 } bones &&
			fighter.Renderer.TryGetBoneTransform( bones[Random.Shared.Next( bones.Length )], out var bone ) )
			end = bone.Position;

		return !Scene.Trace.Ray( TargetPosition( GameObject, eyes: true ), end )
			.IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( target ).WithoutTags( "trigger", "npc", "player" ).Run().Hit;
	}

}
