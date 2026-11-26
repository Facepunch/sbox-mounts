using System;
using Sandbox.Npcs;
using Sandbox.Physics;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	static readonly string[] DragParts = ["head", "leftarm", "rightarm", "left_leg", "right_leg"];
	TimeUntil dragWindow, dragSlideSound, recentChoke;
	GameObject chokeVictim;
	RabbitGuard bodyDragger;
	ModelPhysics draggedPhysics;
	Rigidbody draggedBody;
	// SetAnimUpdateFreqs evaluates both actors in a tether every two native frames.
	int SecondaryPoseFrames => IsTethered || DragStrength > 0f ? 2 : 4;
	bool IsTethered => Holding || Held || draggedBody.IsValid() || bodyDragger.IsValid();
	Vector3 localDragPoint, dragTarget;
	PhysicsBody dragAnchor;
	Sandbox.Physics.ControlJoint dragJoint;
	[Sync( SyncFlags.Interpolate )] Vector3 DragPoint { get; set; }
	[Sync( SyncFlags.Interpolate )] float DragStrength { get; set; }

	bool WantsToDragBody => Investigating && !soughtItem.IsValid() && dragWindow > 0f;

	void BeginBodyInspection( float seconds )
	{
		// The following area search keeps the deadline set on entering body inspection.
		BeginAreaSearch();
		inspectUntil = seconds;
		dragWindow = seconds + 2f;
	}

	void UpdateBodyDrag()
	{
		if ( !WantsToDragBody || !IsOnGround || Downed || StaticCharacter || Holding || Held || strike is not null || InThrow )
		{
			ReleaseBodyDrag();
			return;
		}
		if ( draggedPhysics is null )
		{
			if ( currentKnifeLayer is { Active: true } ) return;
			FindBodyToDrag();
			if ( draggedBody.IsValid() && flinching )
			{
				flinching = false;
				flinchFadeOut = 4f;
			}
		}
		if ( !draggedPhysics.IsValid() || !draggedPhysics.Active || !draggedBody.IsValid() || !draggedBody.PhysicsBody.IsValid() )
		{
			ReleaseBodyDrag();
			return;
		}

		var body = draggedBody.PhysicsBody;
		DragPoint = body.Transform.PointToWorld( localDragPoint );
		var facing = WorldRotation.Forward;
		var hand = CharacterCenter( GameObject ) + facing * 12f + facing.Cross( Vector3.Up ) * (MirroredStance ? -6f : 6f)
			+ Vector3.Up * (4f - duckAmount * 8f);
		var delta = (DragPoint - hand).WithZ( 0f );
		if ( delta.Length > 8f )
		{
			var pull = delta.Normal * ((delta.Length - 8f) * 5f * DragStrength);
			if ( pull.LengthSquared > 0.2f * 40f * 40f ) { ReleaseBodyDrag(); return; }
			// HandleTethering adds this velocity once per two 120 Hz frames.
			combatVelocity += pull * (Time.Delta * 60f);
		}
		float blend = 1f - MathF.Pow( 0.95f, Time.Delta * 120f );
		if ( DragStrength > 0.3f )
		{
			dragTarget = Vector3.Lerp( dragTarget, hand, blend );
			if ( dragJoint is null )
			{
				dragAnchor = new PhysicsBody( body.World ) { BodyType = PhysicsBodyType.Keyframed, AutoSleep = false };
				dragAnchor.Position = DragPoint;
				dragJoint = PhysicsJoint.CreateControl( new PhysicsPoint( dragAnchor ), new PhysicsPoint( body, localDragPoint ) );
				// Bullet's point constraint corrects 20% of position error each 120 Hz step, with a 3 kg m/s impulse limit.
				dragJoint.MaxVelocityForce = 3f * 40f * 120f;
				dragJoint.MaxVelocityTorque = 0f;
			}
			body.Sleeping = false;
			dragJoint.LinearVelocity = (dragTarget - DragPoint) * (0.2f * 120f);
		}
		else dragTarget = DragPoint;

		float previous = DragStrength;
		DragStrength += (1f - DragStrength) * blend;
		if ( previous < 0.7f && DragStrength >= 0.7f ) PlayCombatSound( "Data/Sounds/hit/grip.xml", CharacterCenter( GameObject ) );
		var carrier = draggedPhysics.GetComponentInParent<RabbitGuard>();
		var target = carrier.IsValid() ? carrier.GameObject : draggedPhysics.GameObject;
		var toward = (TargetPosition( target ) - WorldPosition).WithZ( 0f ).Normal;
		if ( !toward.IsNearlyZero() ) WorldRotation = InterpolateFacing( WorldRotation.Forward, toward, blend );
		var damping = draggedPhysics.GetComponent<RagdollDamping>();
		damping?.Refresh();
		float volume = (CharacterLoader.RagdollVelocity( draggedPhysics )?.Length ?? 0f) * (0.4f / 40f);
		if ( volume > 0.2f && dragSlideSound <= 0f )
		{
			PlayMaterialEvent( "slide", WorldPosition, GroundMaterialName, volume );
			dragSlideSound = Random.Shared.Float( 0.3f, 0.6f );
		}
	}

	void FindBodyToDrag()
	{
		var center = CharacterCenter( GameObject );
		ModelPhysics closest = null;
		float best = 98f * 98f; // two metres plus the leg sphere radius
		foreach ( var model in Scene.GetAllComponents<ModelPhysics>() )
		{
			if ( !model.Active ) continue;
			var fighter = model.GetComponentInParent<RabbitGuard>();
			if ( fighter.IsValid() ? !fighter.Unconscious || fighter.StaticCharacter : model.GetComponent<CharacterRemains>() is not { StaticCharacter: false } ) continue;
			var other = fighter.IsValid() ? fighter.GameObject : model.GameObject;
			float d = center.DistanceSquared( TargetPosition( other ) );
			if ( d >= best ) continue;
			best = d;
			closest = model;
		}
		if ( !closest.IsValid() ) return;
		bool ThreatNearby( GameObject other ) => CanAttack( other ) && center.DistanceSquared( CharacterCenter( other ) ) <= 418f * 418f;
		if ( recentChoke <= 0f && (Scene.GetAllComponents<BaseNpc>().Any( n => ThreatNearby( n.GameObject ) ) ||
			Scene.GetAllComponents<PlayerController>().Any( p => ThreatNearby( p.GameObject ) )) ) return;
		if ( CharacterLoader.FromModel( closest.Model ) is not { } character ) return;
		var owner = closest.GetComponentInParent<RabbitGuard>();
		bool useHead = chokeVictim.IsValid() && chokeVictim == (owner.IsValid() ? owner.GameObject : closest.GameObject);
		best = useHead ? float.PositiveInfinity : 0.2f * 1.5f * 40f * 40f;
		foreach ( var part in DragParts )
		{
			if ( character.RagdollGrip( closest, part, out var body ) is not { } point || !body.PhysicsBody.IsValid() ) continue;
			float d = point.DistanceSquared( center );
			if ( d > best ) continue;
			best = d;
			draggedBody = body;
			draggedPhysics = closest;
			localDragPoint = body.PhysicsBody.Transform.PointToLocal( point );
			if ( useHead ) { chokeVictim = null; break; }
		}
		if ( draggedPhysics.IsValid() && owner.IsValid() )
		{
			if ( owner.bodyDragger.IsValid() ) owner.bodyDragger.ReleaseBodyDrag();
			owner.bodyDragger = this;
		}
		else if ( draggedPhysics.IsValid() && draggedPhysics.GetComponent<CharacterRemains>() is { } remains )
		{
			if ( remains.Dragger.IsValid() ) remains.Dragger.ReleaseBodyDrag();
			remains.Dragger = this;
		}
	}

	// Overgrowth keeps the same ragdoll when a casualty dies. Keep the grip on
	// the corresponding body when Sandbox transfers it to a corpse object.
	void TransferBodyDrag( CharacterRemains remains, Rigidbody live, Rigidbody replacement )
	{
		if ( !bodyDragger.IsValid() || bodyDragger.draggedBody != live ) return;
		bodyDragger.RemoveDragJoint();
		bodyDragger.draggedBody = replacement;
		bodyDragger.draggedPhysics = remains.Physics;
		remains.Dragger = bodyDragger;
		bodyDragger = null;
	}

	void RemoveDragJoint()
	{
		dragJoint?.Remove();
		dragJoint = null;
		dragAnchor?.Remove();
		dragAnchor = null;
	}

	void ReleaseTetherExcept( RabbitGuard partner, bool enteringThrow = false )
	{
		if ( holdPartner.IsValid() && holdPartner != partner ) ReleaseHold( enteringThrow );
		if ( bodyDragger.IsValid() && bodyDragger != partner ) bodyDragger.ReleaseBodyDrag();
		if ( draggedPhysics.IsValid() && draggedPhysics.GetComponentInParent<RabbitGuard>() != partner )
			ReleaseBodyDrag();
	}

	void ReleaseBodyDrag()
	{
		RemoveDragJoint();
		if ( draggedPhysics.IsValid() && draggedPhysics.GetComponentInParent<RabbitGuard>() is { } owner && owner.bodyDragger == this )
			owner.bodyDragger = null;
		if ( draggedPhysics.IsValid() && draggedPhysics.GetComponent<CharacterRemains>() is { } remains && remains.Dragger == this )
			remains.Dragger = null;
		draggedPhysics = null;
		draggedBody = null;
		if ( Networking.IsHost ) DragStrength = 0f;
	}
}
