using DamageEvent = Sandbox.Component.IDamageable.DamageEvent;

namespace Source1.Hl2;

public sealed class Strider : CreatureNpc
{
	float _missileHits = 7;
	float _ballDamage = 25;

	protected override async void OnStart()
	{
		base.OnStart();
		if ( IsProxy ) return;
		var skill = await SkillData.Read( MountContent.Of( GameObject ) );
		_missileHits = System.Math.Max( 1, skill.Get( "sk_strider_num_missiles2", 7 ) );
		_ballDamage = skill.Get( "strider_ar2_altfire_dmg", 25 );
	}

	protected override void OnDamaging( DamageEvent hit )
	{
		var info = hit.DamageInfo;
		if ( info.Attacker == GameObject )
			hit.Cancelled = true;
		else if ( info.Tags.Has( "combineball" ) )
			hit.AcceptedDamage = _ballDamage;
		else if ( info.Tags.IsEmpty )
			base.OnDamaging( hit );
		else if ( info.Tags.Has( DamageTags.Explosion ) && (info.Weapon?.GetComponent<Projectile>()?.Damage ?? info.Damage) > 50 && NearHead( info.Origin ) )
			hit.AcceptedDamage = Npcs.IsPlayer( info.Attacker ) ? (int)(MaxHealth / _missileHits) : 20;
		else
			hit.Cancelled = true;
	}

	bool NearHead( Vector3 origin )
	{
		var head = Renderer.IsValid() && Renderer.TryGetBoneTransform( "combine_strider.neck_bone", out var bone )
			? bone.Position : WorldPosition + Vector3.Up * EyeHeight;
		var hull = new BBox( head - 38, head + 38 );
		return hull.ClosestPoint( origin ).Distance( origin ) < 200;
	}
}
