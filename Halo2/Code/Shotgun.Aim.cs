namespace Halo2;

public sealed partial class Shotgun
{
	[Property] public float AutoAimAngle { get; set; } = 8 * System.MathF.PI / 180;
	[Property] public float MagnetismAngle { get; set; } = 16 * System.MathF.PI / 180;
	[Property] public float MagnetismRange { get; set; } = 720;
	[Property] public float MagnetismFriction { get; set; } = 0.6f;

	void PlayerController.IEvents.OnEyeAngles( ref Angles angles )
	{
		if ( !Input.UsingController || Input.MouseDelta != Vector2.Zero || angles == Angles.Zero ) return;
		if ( Holder is PlayerController player && MagnetismFriction > 0 )
			angles *= 1 - MagnetismFriction.Clamp( 0, 1 ) * LookConfidence( player );
	}

	// Both angular and distance confidence are full up to half the authored limit.
	static float Falloff( float value, float limit ) => limit > 0 ? (2 * (1 - value / limit)).Clamp( 0, 1 ) : 0;

	float LookConfidence( PlayerController player )
	{
		var ray = player.EyeTransform.ForwardRay;
		float bestAutoAim = 0, bestMagnetism = 0;
		foreach ( var obj in Scene.FindInPhysics( new Sphere( ray.Position, System.MathF.Max( AutoAimRange, MagnetismRange ) ) ) )
		{
			var npc = obj.GetComponentInParent<Sandbox.Npcs.BaseNpc>();
			if ( !npc.IsValid() || npc.Health <= 0 || !npc.Renderer.IsValid() ) continue;
			if ( npc.GetDisposition( player.GameObject ) != Sandbox.Npcs.Disposition.Hostile ) continue;
			var renderer = npc.Renderer;
			if ( renderer.Model is not {} model ) continue;
			foreach ( var target in ModelTarget.For( model ) )
			{
				if ( !target.TryAimPoint( renderer, ray, out var point, out _ ) ) continue;
				var offset = point - ray.Position;
				float distance = offset.Length;
				var direction = offset.Normal;
				float angle = System.MathF.Acos( Vector3.Dot( ray.Forward, direction ).Clamp( -1, 1 ) );
				float autoAim = Falloff( angle, AutoAimAngle ) * Falloff( distance, AutoAimRange );
				float magnetism = Falloff( angle, MagnetismAngle ) * Falloff( distance, MagnetismRange );
				if ( autoAim < bestAutoAim || (autoAim == bestAutoAim && magnetism <= bestMagnetism) ) continue;
				var obstruction = BulletTrace( new Ray( ray.Position, direction ), distance, 0 ).IgnoreGameObjectHierarchy( npc.GameObject ).Run();
				if ( obstruction.Hit || obstruction.StartedSolid ) continue;
				bestAutoAim = autoAim;
				bestMagnetism = magnetism;
				if ( autoAim == 1 && magnetism == 1 ) return 1;
			}
		}
		return bestMagnetism;
	}
}
