using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>Shared by the mounted NPCs and their behaviours: hit timing, grab points and knockback.</summary>
public static class SourceNpcs
{
	/// <summary>When a creature's attack clip lands its hits: the mounted impact markers when the author supplied them, else the fallback delay.</summary>
	public static float[] AttackTimes( Model model, string clip, float fallback )
	{
		if ( model?.AnimationClips().TryGetValue( clip ?? "", out var animation ) != true ) return [fallback];
		var impacts = SourceModelSequences.Of( model ).All.FirstOrDefault( s => s.Name == clip )?.Events
			.Where( e => e.Name?.StartsWith( "AE_ZOMBIE_ATTACK_", StringComparison.Ordinal ) == true
				|| e.Name?.Contains( "_MELEE_HIT", StringComparison.Ordinal ) == true
				|| e.Name?.StartsWith( "AE_HUNTER_MELEE_ATTACK_", StringComparison.Ordinal ) == true
				|| e.Name is "AE_VORTIGAUNT_ZAP_SHOOT" or "AE_VORTIGAUNT_CLAW_LEFT" or "AE_VORTIGAUNT_CLAW_RIGHT" or "AE_VORTIGAUNT_KICK"
				or "AE_ANTLION_WORKER_SPIT" or "AE_HUNTER_MELEE_HIT" )
			.Select( e => Math.Clamp( e.Cycle, 0, 1 ) * animation.Duration ).Distinct().Order().ToArray();
		return impacts is { Length: > 0 } ? impacts : [fallback];
	}

	/// <summary>
	/// The health on an object that others can read and restore, or null: its first damageable with a maximum.
	/// An object can carry several damageables, such as a seat that only forwards damage.
	/// </summary>
	public static Component.IDamageable HealthOf( GameObject target )
	{
		if ( !target.IsValid() ) return null;
		foreach ( var damageable in target.GetComponents<Component.IDamageable>() )
			if ( damageable.MaxHealth > 0 ) return damageable;
		return null;
	}

	/// <summary>The armour on an object that others can read and recharge, or null: its first damageable with a maximum armour.</summary>
	public static Component.IDamageable ArmourOf( GameObject target )
	{
		if ( !target.IsValid() ) return null;
		foreach ( var damageable in target.GetComponents<Component.IDamageable>() )
			if ( damageable.MaxArmour > 0 ) return damageable;
		return null;
	}

	/// <summary>Whether this is a player: it has the engine's player controller.</summary>
	public static bool IsPlayer( GameObject go ) => go.IsValid() && go.GetComponent<PlayerController>().IsValid();

	/// <summary>Where a victim is grabbed or aimed at: a player's middle, else the centre of its bounds.</summary>
	public static Vector3 CapturePoint( GameObject victim )
	{
		var player = victim.GetComponent<PlayerController>();
		return player.IsValid()
			? player.WorldPosition + Vector3.Up * player.CurrentHeight * 0.5f
			: victim.GetBounds().Center;
	}

	/// <summary>
	/// The nearest point of the victim's body to <paramref name="from"/>: where a line to its centre line meets its
	/// hitboxes or colliders. Its bounds are a box around its widest pose and reach well past the body.
	/// </summary>
	public static Vector3 BodyPoint( GameObject self, GameObject victim, Vector3 from )
	{
		var box = victim.GetBounds();
		var axis = box.Center.WithZ( Math.Clamp( from.z, box.Mins.z, box.Maxs.z ) );
		var tr = self.Scene.Trace.Ray( from, axis ).UseHitboxes().IgnoreGameObjectHierarchy( self ).WithoutTags( "trigger" ).Run();
		if ( tr.Hit && tr.GameObject.IsValid() && tr.GameObject.Root == victim.Root )
			return tr.StartedSolid ? from : tr.HitPosition;

		// Nothing of it to trace against: the box is all there is
		return box.ClosestPoint( from );
	}

	/// <summary>
	/// Whether a creature whose body reaches <paramref name="reach"/> from <paramref name="center"/> touches the victim's
	/// body at <paramref name="point"/>: the hitboxes are that close, or something solid of the victim's, such as a
	/// player's capsule, is all that keeps it from getting closer.
	/// </summary>
	public static bool Touches( GameObject self, GameObject victim, Vector3 center, float reach, out Vector3 point )
	{
		point = BodyPoint( self, victim, center );
		if ( point.Distance( center ) <= reach )
			return true;

		var toward = (point - center).Normal;
		var tr = self.Scene.Trace.Sphere( reach, center, center + toward * 4 ).IgnoreGameObjectHierarchy( self ).WithoutTags( "trigger" ).Run();
		return tr.Hit && tr.GameObject.IsValid() && tr.GameObject.Root == victim.Root;
	}

	/// <summary>Throws a player or NPC, like Source's ApplyAbsVelocityImpulse. Host only.</summary>
	public static void Knockback( GameObject victim, Vector3 velocity )
	{
		if ( !victim.IsValid() )
			return;

		if ( IsPlayer( victim ) )
		{
			SourceCreatureNpc.KnockbackPlayer( victim, velocity );
			return;
		}

		if ( victim.GetComponent<BaseNpc>() is { } npc && npc.IsValid() && npc.Health > 0 )
			npc.Launch( velocity );
	}
}
