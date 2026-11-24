using System.Runtime.CompilerServices;

namespace Halo2;

[Sandbox.ModelEditor.GameData( "weak_target", AllowMultiple = true )]
public sealed class WeakTarget
{
	public string Marker { get; set; }
	public float Radius { get; set; }

	static readonly ConditionalWeakTable<Model, WeakTarget[]> Targets = new();

	internal static bool Hit( SkinnedModelRenderer renderer, Ray ray, float range )
	{
		if ( !renderer.IsValid() || renderer.Model is not {} model ) return false;
		foreach ( var target in Targets.GetValue( model, static m => m.GetData<WeakTarget[]>() ?? [] ) )
		{
			if ( renderer.GetAttachment( target.Marker ) is not {} marker ) continue;
			float depth = Vector3.Dot( marker.Position - ray.Position, ray.Forward );
			if ( depth < 0 || depth > range ) continue;
			var closest = ray.Position + ray.Forward * depth;
			if ( marker.PointToLocal( closest ).LengthSquared <= target.Radius * target.Radius ) return true;
		}
		return false;
	}
}
