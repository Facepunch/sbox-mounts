using System;

namespace Overgrowth;

public sealed partial class LevelItem
{
	Vector3 incomingVelocity;
	Transform? pendingEmbed;
	TimeUntil impactSoundDelay;

	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( !Networking.IsHost || collision.Other.IsTrigger || Holder.IsValid() || StuckIn.IsValid() || pendingEmbed.HasValue ) return;
		if ( flight is FlightState.Thrown or FlightState.Straight ) flight = FlightState.Bounced;
		var body = collision.Self.Body;
		if ( !body.IsValid() || !body.MotionEnabled || collision.Other.GameObject?.GetComponentInParent<RabbitGuard>() is not null ) return;
		var surface = TerrainLoader.ContactSurface( collision.Other.Collider as ModelCollider, collision.Contact.Point, collision.Other.Surface, out var terrainHit )?.ResourceName;
		var point = collision.Contact.Point;
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		if ( mount.AllowsPenetration( surface ) && !incomingVelocity.IsNearlyZero() )
			TryEmbed( mount, body, collision.Contact );
		float impulse = pendingEmbed.HasValue ? body.Mass : RabbitGuard.BodyImpactImpulse( collision ) / 40f;
		if ( !pendingEmbed.HasValue )
		{
			if ( body.Velocity.LengthSquared < 160f || impulse <= 0.1f ) return;
			BillboardParticle.EmitSkid( surface, point, terrainHit );
			if ( impactSoundDelay > 0f ) return;
		}
		string step = body.Mass < 1f ? "weapon_drop_light" : body.Mass < 3f ? "weapon_drop_medium" : "weapon_drop_heavy";
		PlayImpact( surface, step, point, impulse, Definition?.Element( "physics" )?.Attribute( "sound_modifier" ) );
		impactSoundDelay = 0.2f;
		if ( guard.IsValid() ) guard.ReportItemImpact( point );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayImpact( string surface, string step, Vector3 point, float gain, string modifier )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount )
			mount.PlaySurfaceEvent( GameObject, surface, step, point, gain, modifier );
	}

	void TryEmbed( GameMount mount, PhysicsBody body, PhysicsContact contact )
	{
		var model = GetComponent<ModelRenderer>().Model;
		if ( mount.GetByPath( model.ResourcePath ) is not ObjectLoader { Sharpness: { } sharpness } ) return;
		var pose = new Transform( body.Position, body.Rotation, WorldScale );

		var vertex = sharpness.ClosestVertex( pose.PointToLocal( contact.Point ) );
		var point = pose.PointToWorld( vertex.Position );
		var direction = (point - body.MassCenter).Normal;
		var normal = contact.Normal;
		if ( normal.Dot( body.MassCenter - contact.Point ) < 0f ) normal = -normal;
		if ( vertex.Sharpness <= 0.3f || direction.Dot( incomingVelocity.Normal ) <= 0.7f || direction.Dot( normal ) >= -0.5f ) return;
		pendingEmbed = pose.WithPosition( pose.Position + contact.Point - point - normal * (6f * MathF.Sqrt( body.Mass )) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayCharacterImpact( float mass, Vector3 point )
	{
		var group = mass < 1f ? "weapon_knife_hilt" : mass < 3f ? "weapon_drop_medium_dirt" : "weapon_drop_heavy_dirt";
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount )
			mount.PlayGroup( $"Data/Sounds/weapon_foley/impact/{group}.xml", point );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayCutImpact( float mass, bool embedded, string label, Vector3 point )
	{
		var sound = mass < 1f ? (embedded ? "weapon_foley/impact/weapon_knife_hit" : "weapon_foley/impact/weapon_knife_hit_neck") : mass < 3f ? "hit/hit_medium" : "hit/hit_hard";
		if ( embedded && label == "spear" ) sound = "hit/spear_hit_flesh";
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount )
			mount.PlayGroup( $"Data/Sounds/{sound}.xml", point );
	}

	void HitCharacter( RabbitGuard fighter, CharacterRemains remains, Rigidbody body, GameObject attacker, SceneTraceResult hit, Transform sample, Vector3 start, Vector3 end, ItemLine line )
	{
		var renderer = remains.IsValid() ? remains.GetComponent<SkinnedModelRenderer>() : fighter.Renderer;
		var velocity = body.Velocity;
		float mass = body.Mass;
		var loader = Sandbox.Mounting.Directory.Get( "overgrowth" )?.GetByPath( GetComponent<ModelRenderer>().Model.ResourcePath ) as ObjectLoader;
		bool cut = line.Material == "metal" && loader?.Sharpness?.At( WorldTransform.PointToLocal( hit.EndPosition + velocity.Normal * 40f ) ) > 0.3f;
		var axis = (end - start).Normal;
		if ( cut )
		{
			renderer.GetOrAddComponent<CutMarks>().Stab( hit.EndPosition, axis, false );
			MarkBlood( Vector3.Lerp( line.Start, line.End, hit.Fraction ), WorldRotation.Inverse * axis, 8f );
		}
		string label = Definition.Element( "label" )?.Value;
		bool embedded = cut && (label == "spear" || MathF.Abs( axis.Dot( hit.Normal ) ) > 0.4f);
		if ( embedded && hit.Hitbox?.Bone is { } bone && renderer.TryGetBoneTransform( bone.Name, out var bonePose ) )
		{
			body.Enabled = false;
			GetComponent<ModelCollider>().Enabled = false;
			GameObject.SetParent( hit.GameObject, true );
			WorldTransform = sample.WithPosition( sample.Position + hit.EndPosition - end + axis * 16f );
			Bone = bone.Name;
			Offset = bonePose.ToLocal( WorldTransform );
			StuckIn = renderer;
		}
		if ( cut ) PlayCutImpact( mass, embedded, label, hit.EndPosition );
		if ( remains.IsValid() )
		{
			var relative = velocity - (CharacterLoader.RagdollVelocity( remains.Physics ) ?? Vector3.Zero);
			RabbitGuard.ApplyHitImpulse( remains.Physics, hit.EndPosition, relative * (mass * 200f / 120f) );
		}
		else fighter.HitByItem( this, attacker, hit.EndPosition, velocity, mass, label, cut, embedded );
	}
}
