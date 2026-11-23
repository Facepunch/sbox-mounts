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
		flyingStraight = false;
		var body = collision.Self.Body;
		if ( !body.IsValid() || !body.MotionEnabled || collision.Other.GameObject?.GetComponentInParent<RabbitGuard>() is not null ) return;
		var surface = collision.Other.Surface?.ResourceName;
		var point = collision.Contact.Point;
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		if ( mount.AllowsPenetration( surface ) && !incomingVelocity.IsNearlyZero() )
			TryEmbed( mount, body, collision.Contact );
		float impulse = pendingEmbed.HasValue ? body.Mass : RabbitGuard.BodyImpactImpulse( collision ) / 40f;
		if ( !pendingEmbed.HasValue && (impactSoundDelay > 0f || body.Velocity.LengthSquared < 160f || impulse <= 0.1f) ) return;
		string step = body.Mass < 1f ? "weapon_drop_light" : body.Mass < 3f ? "weapon_drop_medium" : "weapon_drop_heavy";
		PlayImpact( surface, step, point, impulse );
		impactSoundDelay = 0.2f;
		if ( guard.IsValid() ) guard.ReportItemImpact( point );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayImpact( string surface, string step, Vector3 point, float gain )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount )
			mount.PlaySurfaceEvent( GameObject, surface, step, point, gain, Definition?.Element( "physics" )?.Attribute( "sound_modifier" ) );
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

	void HitFighter( RabbitGuard fighter, Rigidbody body, GameObject attacker, SceneTraceResult hit, Transform sample, Vector3 end, ItemLine line )
	{
		var velocity = body.Velocity;
		float mass = body.Mass;
		var loader = Sandbox.Mounting.Directory.Get( "overgrowth" )?.GetByPath( GetComponent<ModelRenderer>().Model.ResourcePath ) as ObjectLoader;
		bool cut = line.Material == "metal" && loader?.Sharpness?.At( WorldTransform.PointToLocal( hit.EndPosition + velocity.Normal * 40f ) ) > 0.3f;
		var axis = sample.Rotation * (line.End - line.Start).Normal;
		string label = Definition.Element( "label" )?.Value;
		bool embedded = cut && (label == "spear" || MathF.Abs( axis.Dot( hit.Normal ) ) > 0.4f);
		if ( embedded && hit.Hitbox?.Bone is { } bone && fighter.Renderer.TryGetBoneTransform( bone.Name, out var bonePose ) )
		{
			body.Enabled = false;
			GetComponent<ModelCollider>().Enabled = false;
			GameObject.SetParent( fighter.GameObject, true );
			WorldTransform = sample.WithPosition( sample.Position + hit.EndPosition - end + axis * 16f );
			Bone = bone.Name;
			Offset = bonePose.ToLocal( WorldTransform );
			StuckIn = fighter.Renderer;
		}
		else
		{
			var normal = Vector3.Lerp( Vector3.Random.Normal, (-velocity.Normal).WithZ( -velocity.Normal.z * 0.2f ), 0.6f ).Normal;
			body.Velocity = Vector3.Reflect( velocity, normal ) * Random.Shared.Float( 0.1f, 0.25f );
		}
		fighter.HitByItem( this, attacker, hit.EndPosition, velocity, mass, label, cut, embedded );
	}
}
