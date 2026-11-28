using System;

namespace Source1.Hl2;

/// <summary>
/// How the antlion guard takes damage, the hits that make it flinch and roar in pain, and its death clip before it falls.
/// </summary>
public sealed partial class AntlionGuard : INpcPart, Component.IDamageable.IEvents, IThrownHit
{
	// All from the guard's own rules
	const float BulletScale = 0.25f;
	const float BuckshotScale = 0.5f;
	const float CrushCap = 100;
	const float CombineBallDamage = 50;
	const float HeavyBlast = 25;
	const float HeavyThrow = 20;
	const float HeavyShakeAmplitude = 32, HeavyShakeFrequency = 8, HeavyShakeDuration = 0.5f, HeavyShakeRadius = 512;

	bool _firstMilestone;
	bool _lastHeavy;

	bool _finishing;
	DamageInfo _deathInfo;

	/// <summary>
	/// Bullets do a quarter (buckshot half), never under 1; another guard does nothing; a physics hit only counts from an object
	/// a player launched, by the guard's own impact table (see <see cref="IThrownHit.OnThrownHit"/>), or from a vehicle, at
	/// most 100; a combine ball does 50. The gravity gun punting the guard itself does nothing.
	/// </summary>
	void INpcPart.OnNpcDamaging( Component.IDamageable.DamageEvent e )
	{
		if ( _finishing )
			return;
		if ( Busy == Doing.Dying )
		{
			e.Cancelled = true;
			return;
		}

		var info = e.DamageInfo;
		var tags = info.Tags;
		var raw = e.AcceptedDamage;
		_lastHeavy = false;

		if ( info.Attacker.IsValid() && info.Attacker.GetComponentInParent<AntlionGuard>() is { } other && other != this )
		{
			e.Cancelled = true;
			return;
		}

		if ( tags.Contains( "dissolve" ) )
		{
			e.AcceptedDamage = CombineBallDamage;
			_lastHeavy = true;
		}
		else if ( (tags.Contains( DamageTags.Crush ) || tags.Contains( "impact" )) && !tags.Contains( "vehicle" ) )
		{
			// Props that fall on it, that it knocked about itself, or that a player only dropped do nothing, nor does a punt
			if ( !tags.Contains( TableTag ) )
			{
				e.Cancelled = true;
				return;
			}
			e.AcceptedDamage = MathF.Min( e.AcceptedDamage, CrushCap );
			_lastHeavy = e.AcceptedDamage >= HeavyThrow && Npc.Health >= Npc.MaxHealth * BadlyHurt;
		}
		else if ( tags.Contains( DamageTags.Bullet ) )
		{
			e.AcceptedDamage = MathF.Max( 1, raw * (tags.Contains( "buckshot" ) ? BuckshotScale : BulletScale) );
		}
		else if ( tags.Contains( DamageTags.Explosion ) || tags.Contains( "blast" ) )
		{
			_lastHeavy = raw > HeavyBlast;
		}

		if ( e.AcceptedDamage <= 0 )
			e.Cancelled = true;
	}

	/// <summary>
	/// Heavy damage shakes the screen and makes it roar in pain, and flinches it unless it flinched in the last 8 seconds; then
	/// it only drops what it was doing. Bullets never count as heavy, but its first drop below 95 % and every fifth of its health
	/// they take make it flinch and roar too.
	/// </summary>
	void INpcPart.OnNpcHurt( Component.IDamageable.DamageEvent e )
	{
		if ( _finishing || Npc.Health < 1 )
			return;

		var tags = e.DamageInfo.Tags;
		var max = MathF.Max( Npc.MaxHealth, 1 );
		var after = Npc.Health;
		var before = after + e.AcceptedDamage;
		var milestone = false;
		if ( tags.Contains( DamageTags.Bullet ) && !_lastHeavy )
		{
			if ( !_firstMilestone && after < max * 0.95f )
			{
				_firstMilestone = true;
				milestone = after > max * 0.9f;
			}
			for ( var fifth = 4; fifth >= 1 && !milestone; fifth-- )
				milestone = before > max * fifth / 5 && after <= max * fifth / 5;
		}

		if ( !_lastHeavy && !milestone )
			return;

		// Where the hit landed decides the reel; a hit reported at its middle (a blast) by where it came from
		var info = e.DamageInfo;
		var at = !info.Position.IsNearlyZero() && (info.Position - Npc.Centre).WithZ( 0 ).Length > 8 ? info.Position
			: !info.Origin.IsNearlyZero() ? info.Origin : e.Instigator.IsValid() ? e.Instigator.WorldPosition : WorldPosition + WorldRotation.Forward;
		if ( _lastHeavy )
		{
			GetComponent<Charge>()?.Shake( HeavyShakeAmplitude, HeavyShakeRadius, HeavyShakeFrequency, HeavyShakeDuration );
			if ( tags.Contains( DamageTags.Crush ) || tags.Contains( "impact" ) )
				PlaySound( "NPC_AntlionGuard.ShellCrack" );
		}
		PlaySound( "NPC_AntlionGuard.Pain_Roar" );

		if ( Busy == Doing.Flinch )
			return;
		if ( _nextFlinch )
		{
			_flinch = (true, at);
		}
		else
		{
			_interrupt = true;
			// Hit hard by an enemy it can't reach, sees, and can't roar at yet: it runs for cover
			_wantCover = _lastHeavy && !_reachable && _enemy.IsValid() && Sees( _enemy ) && !(_nextRoar && _roar is not null);
		}
	}

	const string TableTag = "guard_impact";

	/// <summary>
	/// The guard's impact table. It reads the energy the object lost in the hit as a speed of the guard's own mass: the speed
	/// it lost, squared, times its mass over the guard's, doubled from 45 kg up. 100 of that does 10, 250 does 25, 350 does
	/// 50, 500 does 75 and 1000 does 100. Nothing from objects under 15 kg or slower than 200 before the hit.
	/// </summary>
	static float ImpactDamage( float before, float after, float mass, float guardMass )
	{
		if ( mass < 15 || before < 200 || guardMass <= 0 )
			return 0;
		var lost = MathF.Max( 0, before - after );
		var speed = MathF.Sqrt( lost * lost * mass / guardMass * (mass >= 45 ? 2 : 1) );
		return speed >= 1000 ? 100 : speed >= 500 ? 75 : speed >= 350 ? 50 : speed >= 250 ? 25 : speed >= 100 ? 10 : 0;
	}

	/// <summary>The guard's mass: its model's, else 1000 kg.</summary>
	float GuardMass => Npc.Renderer?.Model?.Physics?.Parts.Sum( x => x.Mass ) is > 0 and var mass ? mass : 1000;

	/// <summary>An object a player launched strikes it before hitting anything else: damage by the guard's table.</summary>
	void IThrownHit.OnThrownHit( GameObject thing, float mass, GameObject thrower, float speedBefore, float speedAfter, Vector3 point )
	{
		if ( !Networking.IsHost || Busy == Doing.Dying || !Npc.IsValid() || Npc.Health < 1 )
			return;

		var amount = ImpactDamage( speedBefore, speedAfter, mass, GuardMass );
		if ( amount <= 0 )
			return;

		var info = new DamageInfo( amount, thrower.IsValid() ? thrower : thing, thing ) { Position = point, Origin = thing.IsValid() ? thing.WorldPosition : point };
		info.Tags.Add( DamageTags.Crush );
		info.Tags.Add( "impact" );
		info.Tags.Add( TableTag );
		((Component.IDamageable)Npc).OnDamage( info );
	}

	// Death

	/// <summary>It doesn't fall at once: it plays its death clip, untouchable, and only drops as a ragdoll at the clip's end.</summary>
	void Component.IDamageable.IEvents.OnDying( Component.IDamageable.DyingEvent e )
	{
		if ( e.Victim != GameObject || _finishing || _death is null || !Networking.IsHost )
			return;

		e.Cancelled = true;
		if ( Busy == Doing.Dying )
			return;

		_deathInfo = e.DamageInfo;
		GetComponent<Charge>()?.Interrupt();
		End( false );
		Npc.Movement?.Stop();
		Begin( Doing.Dying, _death );
		PlaySound( "NPC_AntlionGuard.Die" );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || Busy != Doing.Dying || _finishing || _actionTime < _deathEvent * Duration( _death, 2.87f ) )
			return;

		// The killing blow lands again, now that it can fall
		_finishing = true;
		var last = _deathInfo;
		var info = new DamageInfo( Npc.Health + 1000, last?.Attacker, last?.Weapon ) { Position = last?.Position ?? WorldPosition, Origin = last?.Origin ?? WorldPosition };
		if ( last is not null )
			foreach ( var tag in last.Tags.TryGetAll() )
				info.Tags.Add( tag );
		((Component.IDamageable)Npc).OnDamage( info );
	}

	void INpcPart.OnNpcDeath( Component.IDamageable.DeathEvent death )
	{
		PlaySound( "NPC_AntlionGuard.Fallover" );
		if ( _prop.IsValid() )
			Claimed.Remove( _prop );
	}
}
