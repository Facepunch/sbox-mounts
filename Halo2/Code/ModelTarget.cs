using System.Runtime.CompilerServices;

namespace Halo2;

[Sandbox.ModelEditor.GameData( "model_target", AllowMultiple = true )]
public sealed class ModelTarget
{
	public string Marker { get; set; }
	public float Radius { get; set; }
	public float ConeAngle { get; set; }
	public float MagnetismWeight { get; set; } = 1;
	public bool Inconsequential { get; set; }
	public bool IsWeak { get; set; }
	public bool Lockable { get; set; }

	static readonly ConditionalWeakTable<Model, ModelTarget[]> Targets = new();
	internal static ModelTarget[] For( Model model ) => Targets.GetValue( model, static m => m.GetData<ModelTarget[]>() ?? [] );

	internal bool TryAimPoint( SkinnedModelRenderer renderer, Ray ray, out Vector3 point, out bool direct )
		=> TryAimPoint( renderer.GetAttachment( Marker ), ray, out point, out direct );

	internal bool TryAimPoint( Transform? attachment, Ray ray, out Vector3 point, out bool direct )
	{
		point = default;
		direct = false;
		if ( attachment is not {} marker ) return false;
		float depth = Vector3.Dot( marker.Position - ray.Position, ray.Forward );
		if ( depth <= 0 ) return false;
		var nearest = ray.Position + ray.Forward * depth;
		var local = marker.PointToLocal( nearest );
		direct = local.LengthSquared <= Radius * Radius;
		point = direct ? nearest : marker.PointToWorld( local.Normal * Radius );
		var direction = direct ? ray.Forward : (point - ray.Position).Normal;
		return ConeAngle <= 0 || Vector3.Dot( marker.Rotation.Forward, -direction ) >= System.MathF.Cos( ConeAngle );
	}

	internal static ModelTarget FindDirect( SkinnedModelRenderer renderer, Ray ray, out float nearestDepth )
	{
		nearestDepth = float.PositiveInfinity;
		if ( !renderer.IsValid() || renderer.Model is not {} model ) return null;
		ModelTarget nearest = null;
		foreach ( var target in For( model ) )
		{
			if ( !target.TryAimPoint( renderer, ray, out var point, out bool direct ) || !direct ) continue;
			float depth = Vector3.Dot( point - ray.Position, ray.Forward );
			if ( depth >= nearestDepth ) continue;
			nearestDepth = depth;
			nearest = target;
		}
		return nearest;
	}
}
