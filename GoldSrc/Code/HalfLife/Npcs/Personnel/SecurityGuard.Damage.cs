using Sandbox.Npcs;

namespace GoldSrc.HalfLife;

public partial class SecurityGuard
{
	bool _suspicious;

	internal override string HelmetHitGroup => "hitgroup_10";

	internal override void ModifyDamage( Component.IDamageable.DamageEvent damage )
	{
		var info = damage.DamageInfo;
		var groups = HitGroups( info );
		var tags = info.Tags;
		if ( groups.Has( HelmetHitGroup ) )
		{
			if ( tags.Has( "bullet" ) || tags.Has( "slash" ) || tags.Has( "club" ) )
				AbsorbHelmetHit( damage );
		}
		else if ( (groups.Has( "hitgroup_2" ) || groups.Has( "hitgroup_3" ))
			&& (tags.Has( "bullet" ) || tags.Has( "slash" ) || tags.Has( "blast" ) || tags.Has( "explosion" )) ) damage.AcceptedDamage *= 0.5f;
		base.ModifyDamage( damage );
	}

	protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
	{
		base.OnDamaged( damage );
		if ( Health <= 0 || IsBarnacleVictim || damage.Instigator?.GetComponentInParent<PlayerController>() is not { } player ) return;
		var attacker = player.GameObject;
		if ( GetDisposition( attacker ) == Disposition.Hostile ) return;

		if ( FindVisibleEnemy() is { } enemy )
		{
			if ( ClassOf( enemy ) != MonsterClass.Player ) Say( "SHOT", attacker, 4, attenuation: GoldSrcSoundEvents.Normal );
			return;
		}

		var direction = (WorldPosition - player.WorldPosition).WithZ( 0 ).Normal;
		var facing = Rotation.FromYaw( player.EyeAngles.yaw ).Forward;
		if ( !_suspicious && facing.Dot( direction ) <= 0.96f )
		{
			_suspicious = true;
			Say( "SHOT", attacker, 4, attenuation: GoldSrcSoundEvents.Normal );
			return;
		}

		SetRelationship( attacker, Disposition.Hostile );
		if ( Leader.IsValid() ) StopFollowing( false );
		Say( "MAD", attacker, 4, attenuation: GoldSrcSoundEvents.Normal );
	}
}
