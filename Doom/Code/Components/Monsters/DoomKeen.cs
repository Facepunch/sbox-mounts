namespace Doom;

/// <summary>
/// Commander Keen, hanging in Doom II's secret levels. Every hit makes him cry out (painchance 256);
/// when his health runs out he plays his death and stops blocking, and when the last Keen in the
/// map dies, the doors tagged 666 open (A_KeenDie).
/// </summary>
[Title( "Doom Keen" )]
[Category( "Doom" )]
[Icon( "child_care" )]
public sealed class DoomKeen : Component, Component.IDamageable
{
	/// <summary>The sector tag A_KeenDie opens.</summary>
	public const int DoorTag = 666;

	[Property] public float Health { get; set; } = 100f;
	[Property] public SpriteRenderer Renderer { get; set; }
	[Property] public SoundEvent PainSound { get; set; }
	[Property] public SoundEvent DeathSound { get; set; }

	[Sync( SyncFlags.FromHost )] public bool IsDead { get; private set; }

	private bool _shownDeath;

	void IDamageable.OnDamage( in DamageInfo damage )
	{
		if ( IsProxy || IsDead || damage.Damage <= 0 ) return;

		Health -= damage.Damage;
		if ( Health > 0 )
		{
			PlaySound( PainSound );
			return;
		}

		IsDead = true;
		PlaySound( DeathSound );

		foreach ( var collider in GetComponents<Collider>() )
			collider.Enabled = false;

		// A_KeenDie: the last one standing opens the door.
		var root = GameObject.Root;
		bool anyLeft = Scene.GetAllComponents<DoomKeen>().Any( k => k != this && !k.IsDead && k.GameObject.Root == root );
		if ( anyLeft ) return;

		foreach ( var sector in Scene.GetAllComponents<DoomSector>() )
		{
			if ( sector.Tag == DoorTag && sector.GameObject.Root == root )
				sector.Start( SectorAction.DoorOpen );
		}
	}

	protected override void OnUpdate()
	{
		if ( !IsDead || _shownDeath ) return;

		_shownDeath = true;
		if ( Renderer.IsValid() )
			Renderer.PlayAnimation( SpriteLoader.DeathAnimation );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void PlaySound( SoundEvent sound )
	{
		if ( sound is not null )
			Sound.Play( sound, WorldPosition );
	}
}
