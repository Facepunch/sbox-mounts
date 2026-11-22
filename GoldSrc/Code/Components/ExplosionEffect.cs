using System;

namespace GoldSrc.Maps;

[Title( "GoldSrc Explosion" ), Icon( "local_fire_department" )]
public sealed class ExplosionEffect : MapEntity
{
	[Property] public int Magnitude { get; set; }
	[Property] public bool NoDamage { get; set; }
	[Property] public bool Repeatable { get; set; }
	[Property] public bool NoFireball { get; set; }
	[Property] public bool NoSmoke { get; set; }
	[Property] public bool NoDecal { get; set; }
	[Property] public bool NoSparks { get; set; }
	[Property] public EffectSprite Fireball { get; set; }
	[Property] public EffectSprite Smoke { get; set; }
	[Property] public SoundEvent[] ExplosionSounds { get; set; } = [];
	[Property] public DecalDefinition[] Scorches { get; set; } = [];
	[Property] public ExplosionEnvironment Environment { get; set; }
	[Property] public DecalSurfaces Surfaces { get; set; }

	public int SpriteScale => (int)MathF.Max( 10, (Magnitude - 50f) * 0.6f );
	private TimeUntil _smokeAt;
	private bool _pendingSmoke;

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		var start = WorldPosition + Vector3.Up * 8;
		var trace = Scene.Trace.Ray( start, start + Vector3.Down * 40 )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "player", "npc" ).Run();
		if ( trace.Hit )
			WorldPosition = trace.EndPosition + trace.Normal * (Magnitude - 24) * 0.6f;

		if ( !NoDecal && Scorches.Length > 0 )
		{
			var index = Game.Random.Float( 0, 1 ) < 0.5f ? 0 : 1;
			if ( trace.Hit && index < Scorches.Length && Scorches[index] is not null )
				PlaceScorch( trace, Scorches[index] );
		}

		var scale = unchecked((byte)SpriteScale) / 10f;
		if ( !NoFireball ) Fireball?.Spawn( Map, WorldPosition, scale, 15, 5, Color.White );
		if ( ExplosionSounds.Length > 0 )
		{
			var sound = ExplosionSounds[Game.Random.Int( 0, ExplosionSounds.Length - 1 )];
			if ( sound is not null ) Sound.Play( sound, WorldPosition );
		}
		if ( !NoDamage ) ApplyRadiusDamage();
		if ( !CanTrigger ) return;
		_pendingSmoke = true;
		_smokeAt = 0.3f;
		if ( !NoSparks )
		{
			var count = Game.Random.Int( 0, 3 );
			for ( var i = 0; i < count; i++ )
				SparkShower.Spawn( Map, WorldPosition, trace.Normal );
		}
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	protected override void OnUpdate()
	{
		if ( !_pendingSmoke || _smokeAt > 0 ) return;
		_pendingSmoke = false;
		if ( !NoSmoke )
		{
			var shade = Game.Random.Int( 20, 35 ) / 255f;
			Smoke?.Spawn( Map, WorldPosition + Vector3.Up * 20, unchecked((byte)SpriteScale) / 10f, 12, 2,
				new Color( shade, shade, shade ), Vector3.Up * 30 );
		}
		if ( !Repeatable ) Kill();
	}

	private void PlaceScorch( SceneTraceResult trace, DecalDefinition definition )
	{
		if ( !trace.GameObject.IsValid() ) return;
		var t = Surfaces.IsValid() ? Surfaces.TextureAxis( trace.GameObject, trace.EndPosition, trace.Normal ) : Vector3.Zero;
		if ( t.LengthSquared < 0.000001f )
		{
			var s = MathF.Abs( trace.Normal.z ) > 0.70710678f
				? Vector3.Cross( trace.Normal, Vector3.Cross( Vector3.Forward, trace.Normal ) ).Normal
				: Vector3.Cross( trace.Normal, Vector3.Down ).Normal;
			t = Vector3.Cross( s, trace.Normal );
		}
		var go = new GameObject( true, "GoldSrc scorch" ) { Parent = trace.GameObject };
		go.WorldPosition = trace.EndPosition;
		go.WorldRotation = Rotation.LookAt( -trace.Normal, -t.Normal );
		var decal = go.AddComponent<Decal>( false );
		decal.Decals = [definition];
		decal.Rotation = 0;
		decal.Transient = true;
		decal.Enabled = true;
	}

	private void ApplyRadiusDamage()
	{
		var radius = Magnitude * 2.5f;
		if ( radius < 0 ) return;
		var falloff = radius != 0 ? Magnitude / radius : 1;
		var inWater = Environment.IsValid() && Environment.Contents( WorldPosition ) == -3;
		var origin = WorldPosition + Vector3.Up;
		var targets = Scene.GetAllComponents<IDamageable>().OfType<Component>().ToArray();
		foreach ( var component in targets )
		{
			if ( !component.IsValid() || !component.GameObject.Active || component.GameObject == GameObject ) continue;
			var target = component.GameObject;
			var bounds = target.GetBounds();
			var closest = new Vector3(
				Math.Clamp( origin.x, bounds.Mins.x, bounds.Maxs.x ),
				Math.Clamp( origin.y, bounds.Mins.y, bounds.Maxs.y ),
				Math.Clamp( origin.z, bounds.Mins.z, bounds.Maxs.z ) );
			if ( (closest - origin).LengthSquared > radius * radius ) continue;
			var details = target.GetComponent<IGoldSrcBlastTarget>();
			var waterLevel = details?.WaterLevel ?? (Environment.IsValid() ? Environment.WaterLevel( bounds ) : 0);
			if ( (inWater && waterLevel == 0) || (!inWater && waterLevel == 3) ) continue;
			var point = details?.BodyTarget ?? bounds.Center;
			var trace = Scene.Trace.Ray( origin, point ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
			if ( trace.Hit && !HitsTarget( trace.GameObject, target ) ) continue;
			var position = trace.StartedSolid ? origin : trace.EndPosition;
			var damage = new EntityDamage( MathF.Max( 0, Magnitude - (position - origin).Length * falloff ), 1 << 6, GameObject, position )
			{
				Origin = origin,
				Shape = trace.Shape,
				Hitbox = trace.Hitbox
			};
			damage.Tags.Add( "blast" );
			((IDamageable)component).OnDamage( damage );
		}
	}

	private static bool HitsTarget( GameObject hit, GameObject target )
	{
		for ( var current = hit; current.IsValid(); current = current.Parent )
			if ( current == target ) return true;
		return false;
	}
}
