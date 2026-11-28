using System;

namespace Magicka;

/// <summary>
/// A spell or thrown projectile: flies with optional gravity, traces for a target, and deals
/// direct or splash damage (or healing) with impact effects and elemental status effects.
/// </summary>
[Title( "Magicka Projectile" )]
[Category( "Magicka" )]
public sealed class MagickaProjectile : Component
{
	[Property] public float Speed { get; set; } = 900f;
	[Property] public float Gravity { get; set; } = 0f;
	[Property] public float Damage { get; set; } = 15f;
	[Property] public float SplashRadius { get; set; } = 0f;
	[Property] public float KnockbackForce { get; set; } = 0f;
	[Property] public float HealAmount { get; set; } = 0f;
	[Property] public float Life { get; set; } = 4f;
	[Property] public Vector3 Direction { get; set; } = Vector3.Forward;
	[Property] public string ImpactCue { get; set; }
	[Property] public string TrailEffect { get; set; }
	[Property] public string HitEffect { get; set; }
	[Property] public Color EffectColor { get; set; } = Color.White;
	[Property] public MagickaElementStatus StatusEffect { get; set; } = MagickaElementStatus.None;

	public GameObject Owner { get; set; }

	private Vector3 _last;
	private Vector3 _velocity;
	private bool _trailAttached;
	[Sync( SyncFlags.FromHost )] private TimeSince Age { get; set; }

	public static MagickaProjectile Spawn( GameObject owner, Model model, Vector3 position, Vector3 direction, float damage, Color? tint = null )
	{
		var dir = direction.Normal;
		var go = new GameObject( false, "magicka_projectile" );
		go.WorldPosition = position;
		if ( dir.LengthSquared > 0.001f )
			go.WorldRotation = Rotation.LookAt( dir );
		go.NetworkMode = NetworkMode.Object;

		var projectile = go.AddComponent<MagickaProjectile>();
		projectile.Owner = owner;
		projectile.Direction = dir;
		projectile.Damage = damage;
		projectile.EffectColor = tint ?? new Color( 1.0f, 0.55f, 0.2f );

		if ( model.IsValid() && !model.IsError )
		{
			var mr = go.AddComponent<ModelRenderer>();
			mr.Model = model;
			if ( tint.HasValue )
				mr.Tint = tint.Value;
		}

		if ( tint.HasValue )
		{
			var light = go.AddComponent<PointLight>();
			light.LightColor = tint.Value * 2.5f;
			light.Radius = 96f;
			light.Shadows = false;
		}

		go.Enabled = true;
		go.NetworkSpawn( true, null );
		return projectile;
	}

	protected override void OnStart()
	{
		_last = WorldPosition;
		_velocity = Direction.Normal * Speed;
		Age = 0;
	}

	protected override void OnUpdate()
	{
		if ( !_trailAttached && !string.IsNullOrEmpty( TrailEffect ) )
		{
			_trailAttached = true;
			MagickaEffectPlayer.Attach( GameObject, TrailEffect );
		}

		if ( !Networking.IsHost )
			return;

		if ( Age > Life )
		{
			GameObject.Destroy();
			return;
		}

		if ( _velocity.LengthSquared < 1f )
			_velocity = Direction.Normal * Speed;

		if ( Gravity > 0f )
			_velocity += Vector3.Down * Gravity * Time.Delta;

		var next = WorldPosition + _velocity * Time.Delta;
		var trace = Scene.Trace.Ray( _last, next )
			.IgnoreGameObjectHierarchy( Owner )
			.WithoutTags( "trigger" )
			.Run();

		if ( trace.Hit )
		{
			OnImpact( trace );
			GameObject.Destroy();
			return;
		}

		WorldPosition = next;
		if ( _velocity.LengthSquared > 1f )
			WorldRotation = Rotation.LookAt( _velocity.Normal );
		_last = next;
	}

	private void OnImpact( in SceneTraceResult trace )
	{
		var hitPos = trace.EndPosition;

		if ( SplashRadius > 1f )
		{
			BroadcastImpact( hitPos, EffectColor, SplashRadius * 1.4f, 0.3f, ImpactCue, HitEffect );

			var seen = new HashSet<GameObject>();
			foreach ( var obj in Scene.FindInPhysics( new Sandbox.Sphere( hitPos, SplashRadius ) ) )
			{
				var root = obj.Root;
				if ( !root.IsValid() || root == Owner?.Root || !seen.Add( root ) )
					continue;

				var delta = root.WorldPosition + Vector3.Up * 24f - hitPos;
				var dist = delta.Length;
				var falloff = Math.Clamp( 1f - dist / SplashRadius, 0.25f, 1f );

				ApplyDirectEffect( root, hitPos, delta.Normal, Damage * falloff, KnockbackForce * falloff );
			}
		}
		else
		{
			BroadcastImpact( hitPos, EffectColor, 100f, 0.22f, ImpactCue, HitEffect );
			if ( trace.GameObject.IsValid() && trace.GameObject.Root != Owner?.Root )
				ApplyDirectEffect( trace.GameObject, hitPos, _velocity.Normal, Damage, KnockbackForce );
		}
	}

	[Rpc.Broadcast]
	private void BroadcastImpact( Vector3 hitPos, Color color, float radius, float duration, string cueName, string effectPath ) =>
		MagickaSpellEffects.SpawnImpact( hitPos, color, radius, duration, cueName, effectPath );

	private void ApplyDirectEffect( GameObject target, Vector3 hitPos, Vector3 pushDir, float amount, float knockback )
	{
		if ( HealAmount > 0f )
		{
			if ( MagickaSpellEffects.TryHeal( target, HealAmount ) )
				return;
		}

		if ( amount > 0f )
		{
			var damage = new DamageInfo
			{
				Damage = amount,
				Attacker = Owner,
				Weapon = Owner,
				Position = hitPos,
				Origin = _last,
			};
			damage.Tags.Add( "magic" );
			target.GetComponentInParent<Component.IDamageable>()?.OnDamage( damage );
		}

		if ( StatusEffect != MagickaElementStatus.None )
			MagickaSpellEffects.ApplyStatus( target, Owner, StatusEffect );

		if ( knockback > 0f )
			MagickaSpellEffects.ApplyKnockback( target, pushDir * knockback + Vector3.Up * (knockback * 0.35f) );
	}
}
