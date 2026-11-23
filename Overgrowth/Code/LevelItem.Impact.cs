using System;

namespace Overgrowth;

public sealed partial class LevelItem
{
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
