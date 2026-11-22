using Sandbox.Npcs;

namespace GoldSrc.HalfLife;

[Hide]
[Alias( "GoldSrc.HalfLifeNpcProjectile" )]
public class NpcProjectile : Component
{
	[Property] public GameObject Shooter { get; set; }
	[Property] public string ModelPath { get; set; }
	[Property] public Vector3 Velocity { get; set; }
	[Property] public float Damage { get; set; }
	[Property] public string DamageTag { get; set; }
	[Sync( SyncFlags.FromHost )] internal TimeSince Age { get; set; }

	internal virtual float LaunchSpeed => 625;
	internal virtual string HitTag => "projectile";
	internal virtual float Lifetime => 4;
	internal virtual void PrepareLaunch() { }
	internal virtual void Guide() { }
	[Property] public GameObject Target { get; set; }

	internal static void Fire<T>( Npc npc, GameObject target, Vector3 origin, Vector3 direction, float? speed = null ) where T : NpcProjectile, new()
		=> Fire<T>( npc.GameObject, npc.ProjectilePath, npc.Definition.Damage, target, origin + direction * 6, direction, speed );

	internal static T Fire<T>( GameObject shooter, string model, float damage, GameObject target, Vector3 origin, Vector3 direction, float? speed ) where T : NpcProjectile, new()
	{
		var go = new GameObject( false, typeof( T ).Name );
		go.WorldPosition = origin;
		go.WorldRotation = Rotation.LookAt( direction );
		var projectile = go.AddComponent<T>();
		projectile.Shooter = shooter;
		projectile.ModelPath = model;
		projectile.Target = target;
		projectile.Velocity = direction * (speed ?? projectile.LaunchSpeed);
		projectile.Damage = damage;
		projectile.DamageTag = projectile.HitTag;
		projectile.Age = 0;
		projectile.PrepareLaunch();
		go.Enabled = true;
		go.NetworkSpawn( true, null );
		return projectile;
	}

	protected override async void OnStart()
	{
		var model = await Model.LoadAsync( ModelPath );
		if ( !IsValid || !model.IsValid() || model.IsError )
			return;
		if ( model.AnimationCount == 0 )
		{
			GameObject.AddComponent<ModelRenderer>().Model = model;
			return;
		}

		// Studio projectiles get their flight orientation from sequence zero.
		var renderer = GameObject.AddComponent<SkinnedModelRenderer>();
		renderer.Model = model;
		renderer.UseAnimGraph = false;
		renderer.Sequence.Name = model.GetAnimationName( 0 );
		renderer.Sequence.Looping = true;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost )
			return;
		if ( Age > Lifetime )
		{
			GameObject.Destroy();
			return;
		}

		Guide();
		if ( GameObject.IsDestroyed ) return;
		var start = WorldPosition;
		var end = start + Velocity * Time.Delta;
		var trace = FlightTrace( start, end ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes();
		if ( Shooter.IsValid() ) trace = trace.IgnoreGameObjectHierarchy( Shooter );
		var hit = trace.Run();
		if ( !hit.Hit )
		{
			WorldPosition = end;
			return;
		}

		HandleContact( hit );
	}

	internal virtual void HandleContact( SceneTraceResult hit )
	{
		if ( Bounce( hit ) ) return;
		var victim = hit.GameObject?.GetComponentInParent<Component.IDamageable>();
		OnImpact( victim, hit.EndPosition, hit.Normal );
		GameObject.Destroy();
	}

	internal virtual SceneTrace FlightTrace( Vector3 start, Vector3 end ) => Scene.Trace.Ray( start, end );

	internal virtual bool Bounce( SceneTraceResult hit ) => false;

	internal virtual void OnImpact( IDamageable victim, Vector3 position, Vector3 normal ) => DamageVictim( victim, position );

	internal void DamageVictim( IDamageable victim, Vector3 position, Hitbox hitbox = null, float? amount = null )
	{
		if ( victim is not null )
		{
			var damage = new DamageInfo { Damage = amount ?? Damage, Attacker = Shooter, Weapon = GameObject, Origin = WorldPosition, Position = position, Hitbox = hitbox };
			damage.Tags.Add( DamageTag );
			victim.OnDamage( damage );
		}
	}
}
