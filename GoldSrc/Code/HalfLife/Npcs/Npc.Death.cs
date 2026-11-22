namespace GoldSrc.HalfLife;

public partial class Npc
{
	[Property] public bool FadeCorpse { get; set; }

	const int DieSimple = 36, DieBackward = 37, DieForward = 38, DieHeadshot = 66, DieGutshot = 68;
	bool _gibbed;
	float _healthBeforeHit, _remainingHealth;
	DamageInfo _lastHit;

	protected sealed override void OnDamaging( Component.IDamageable.DamageEvent hit )
	{
		_healthBeforeHit = Health;
		ModifyDamage( hit );
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		var info = death.DamageInfo;
		var remaining = info == _lastHit ? _remainingHealth : Health;
		var direction = DamageDirection( info, death.Instigator, HullCenter );

		_gibbed = Definition?.Gibs is GibSet.Human or GibSet.Alien
			&& (info.Tags.Has( "alwaysgib" ) || (remaining < -30 && !info.Tags.Has( "nevergib" )));
		if ( _gibbed )
		{
			var radius = Definition.Radius;
			var hull = new BBox( new Vector3( -radius, -radius, 0 ), new Vector3( radius, radius, Definition.Height ) );
			Gib.Scatter( hull + WorldPosition, EyePosition, direction, remaining, Definition.Gibs == GibSet.Human, Definition.Blood, sourceModel: ModelPath );
			_audio?.Emit( "common/bodysplat.wav", WorldPosition );
			OnGibbed();
		}
		else
		{
			DeathSound();
			if ( _loaded ) _death = ChooseDeathSequence( HitGroups( info ), direction );
		}
		base.OnDeath( death );
	}

	internal static Vector3 DamageDirection( DamageInfo info, GameObject instigator, Vector3 center )
	{
		var inflictor = info.Weapon.IsValid() ? info.Weapon : instigator;
		return inflictor.IsValid()
			? (center - inflictor.GetBounds().Center + Vector3.Up * 10).Normal
			: (info.Position - info.Origin).Normal;
	}

	internal virtual void DeathSound() => EmitSound( VoiceSample( Sounds.Death ), WorldPosition, VoiceVolume, VoicePitch );

	StudioSequence ChooseDeathSequence( ITagSet groups, Vector3 direction )
	{
		var regional = IsHeadHit( groups ) ? WeightedActivity( DieHeadshot )
			: groups.Has( "hitgroup_3" ) ? WeightedActivity( DieGutshot ) : null;
		if ( regional is not null ) return regional;

		var forward = WorldRotation.Forward;
		var dot = Vector3.Dot( forward, direction );
		var directional = dot > 0.3f ? WeightedActivity( DieForward ) : dot <= -0.3f ? WeightedActivity( DieBackward ) : null;
		if ( directional is not null )
		{
			var center = HullCenter;
			var half = new Vector3( 16, 16, 18 );
			var trace = Scene.Trace.Box( new BBox( -half, half ), center, center + forward * (dot > 0 ? 64 : -64) )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "debris" ).Run();
			if ( !trace.Hit && !trace.StartedSolid ) return directional;
		}
		return WeightedActivity( DieSimple ) ?? Find( "die" ) ?? Find( "death" ) ?? Find( "Engine_death1" );
	}

	internal virtual void OnGibbed() { }

	protected sealed override GameObject CreateRagdoll() => _gibbed ? null : CreateCorpse();

	internal virtual NpcCorpse AddCorpseComponent( GameObject go ) => go.AddComponent<NpcCorpse>();
	internal virtual bool AnchoredCorpse => false;

	protected virtual GameObject CreateCorpse()
	{
		return _death is null ? base.CreateRagdoll() : NpcCorpse.Spawn( this, _death, AnchoredCorpse );
	}
}
