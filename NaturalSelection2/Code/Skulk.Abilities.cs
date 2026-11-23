using System;
using Sandbox.Npcs;

namespace NS2;

/// <summary>A live enemy revealed by a Skulk's parasite. Another hit refreshes the mark.</summary>
public sealed class Parasited : Component
{
	internal TimeUntil Expires { get; set; }
	protected override void OnUpdate()
	{
		if ( Networking.IsHost && Expires <= 0 ) Destroy();
	}
}

public sealed partial class Skulk
{
	const float ParasiteEnergy = 30;
	TimeUntil parasiteReady, detonation;
	bool parasiteHeld, xenociding;
	[Sync( SyncFlags.FromHost )] bool FiringParasite { get; set; }
	[Property] public bool XenocideUnlocked { get; set; }

	void TryParasite( Vector3 aim )
	{
		if ( parasiteHeld || parasiteReady > 0 || sightedFor <= 1 || energy <= (LeapUnlocked ? LeapEnergy + ParasiteEnergy : 60) ) return;
		if ( Vector3.Dot( ViewRotation.Forward, aim ) <= 0.95f ) return;
		FiringParasite = parasiteHeld = true;
		parasiteReady = 0.54f;
		energy -= ParasiteEnergy;
		PlayVoice( "parasite" );
		var ray = Scene.Trace.Ray( EyePosition, EyePosition + aim * (1000 * MapLoader.Scale) )
			.IgnoreGameObjectHierarchy( GameObject ).UseHitboxes();
		var hit = ray.Run();
		if ( hit.GameObject?.GetComponentInParent<IDamageable>() is null )
		{
			var extent = new Vector3( aim.WithX( 0 ).Length, aim.WithY( 0 ).Length, aim.WithZ( 0 ).Length ) * (0.15f * MapLoader.Scale);
			hit = ray.Size( -extent, extent ).Run();
		}
		if ( hit.GameObject?.GetComponentInParent<IDamageable>() is not Component victim || GetDisposition( victim.GameObject ) != Disposition.Hostile ) return;
		DealDamage( (IDamageable)victim, 10, hit.HitPosition, "parasite" );
		if ( victim.IsValid() && !victim.GameObject.IsDestroyed )
			victim.GameObject.GetOrAddComponent<Parasited>().Expires = 44;
	}

	bool TryXenocide()
	{
		if ( xenociding ) return detonation > 1.6f;
		if ( !XenocideUnlocked || energy < 30 ) return false;
		int nearby = 0;
		foreach ( var enemy in Senses.VisibleHostiles )
		{
			if ( WorldPosition.DistanceSquared( enemy.WorldPosition ) >= 25 * MapLoader.Scale * MapLoader.Scale ) continue;
			if ( ++nearby < 2 ) continue;
			xenociding = true;
			detonation = 2;
			PlayCombatSound( "alien/common/xenocide_start", EyePosition );
			return true;
		}
		return false;
	}

	void Detonate()
	{
		const float radius = 14 * MapLoader.Scale;
		var center = WorldPosition + Vector3.Up * SkulkMovement.Radius;
		PlayCombatSound( "alien/common/xenocide_end", center );
		var damaged = new HashSet<Component>();
		foreach ( var obj in Scene.FindInPhysics( new Sphere( center, radius ) ).ToArray() )
		{
			if ( obj.GetComponentInParent<IDamageable>() is not Component victim || !damaged.Add( victim )
				|| victim == this || GetDisposition( victim.GameObject ) != Disposition.Hostile ) continue;
			var point = CalculateAimPoint( victim.GameObject );
			var fraction = center.DistanceSquared( point ) / (radius * radius);
			if ( fraction >= 1 || Scene.Trace.Ray( center, point ).WithTag( "world" ).Run().Hit ) continue;
			DealDamage( (IDamageable)victim, 200 * (1 - fraction), point, "xenocide" );
		}
		Health = 0;
	}

	protected override GameObject CreateRagdoll() => xenociding && detonation <= 0 ? null : base.CreateRagdoll();

	void DealDamage( IDamageable victim, float amount, Vector3 position, string tag )
	{
		var damage = new DamageInfo { Damage = amount, Attacker = GameObject, Weapon = GameObject, Origin = EyePosition, Position = position };
		damage.Tags.Add( tag );
		victim.OnDamage( damage );
	}
}
