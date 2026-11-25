using System.Runtime.CompilerServices;

namespace Halo2;

[Sandbox.ModelEditor.GameData( "model_target", AllowMultiple = true )]
public sealed class ModelTarget
{
	public string Marker { get; set; }
	public float Radius { get; set; }
	public float ConeAngle { get; set; }
	public bool IsWeak { get; set; }

	static readonly ConditionalWeakTable<Model, ModelTarget[]> Targets = new();

	internal static bool HitsWeakSpot( SkinnedModelRenderer renderer, Ray ray, float range )
	{
		if ( !renderer.IsValid() || renderer.Model is not {} model ) return false;
		ModelTarget nearest = null;
		float nearestDepth = float.PositiveInfinity;
		foreach ( var target in Targets.GetValue( model, static m => m.GetData<ModelTarget[]>() ?? [] ) )
		{
			if ( renderer.GetAttachment( target.Marker ) is not {} marker ) continue;
			float depth = Vector3.Dot( marker.Position - ray.Position, ray.Forward );
			if ( depth < 0 || depth > range || depth >= nearestDepth ) continue;
			if ( target.ConeAngle > 0 && Vector3.Dot( marker.Rotation.Forward, -ray.Forward ) < System.MathF.Cos( target.ConeAngle ) ) continue;
			var closest = ray.Position + ray.Forward * depth;
			if ( marker.PointToLocal( closest ).LengthSquared > target.Radius * target.Radius ) continue;
			nearestDepth = depth;
			nearest = target;
		}
		return nearest?.IsWeak == true;
	}
}
