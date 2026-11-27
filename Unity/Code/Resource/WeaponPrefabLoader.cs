namespace UnityMount;

public sealed record PostalWeaponDef(
	string Id,
	string Title,
	PostalWeaponArchetype Archetype,
	string ModelPath,
	string ViewModelRigName,
	string IconPath,
	string FireSoundPath,
	string AltFireSoundPath,
	string ReloadSoundPath,
	string ImpactSoundPath,
	int PreferredSlot,
	float PrimaryDelay,
	bool Automatic,
	int ClipSize,
	float Damage,
	int Pellets,
	float Spread,
	float TargetLength,
	Vector3 ViewOffset,
	Color TracerColor,
	float ExplosionRadius = 0f,
	float ProjectileSpeed = 1650f,
	string ProjectileModelPath = "",
	float ViewModelScale = 1f,
	string ResolvedViewModelPath = "" );

/// <summary>
/// Builds a registered s&amp;box PrefabFile for a playable POSTAL: Brain Damaged weapon using Sandbox.Mounting.PrefabBuilder,
/// tagged with "weapon" so it automatically appears in Sandbox's Weapons -> POSTAL: Brain Damaged tab!
/// </summary>
public sealed class WeaponPrefabLoader( PostalWeaponDef def ) : ResourceLoader<UnityGameMount>
{
	PrefabFile _prefab;

	protected override object Load()
	{
		var builder = new PrefabBuilder()
			.WithName( Path )
			.WithMetadata( "Title", def.Title )
			.WithMetadata( "PreviewModel", def.ModelPath );

		using ( builder.Scope() )
		{
			var root = new GameObject( true, def.Id );
			root.Tags.Add( "weapon" );

			var wep = root.AddComponent<PostalWeapon>();
			wep.DisplayName = def.Title;
			wep.Archetype = def.Archetype;
			wep.WeaponModelPath = def.ModelPath;
			wep.ViewModelPath = def.ResolvedViewModelPath;
			wep.IconTexturePath = def.IconPath;
			wep.FireSoundPath = def.FireSoundPath;
			wep.AltFireSoundPath = def.AltFireSoundPath;
			wep.ReloadSoundPath = def.ReloadSoundPath;
			wep.ImpactSoundPath = def.ImpactSoundPath;
			wep.ProjectileModelPath = def.ProjectileModelPath;
			wep.PreferredSlot = def.PreferredSlot;
			wep.PrimaryDelay = def.PrimaryDelay;
			wep.PrimaryAutomatic = def.Automatic;
			wep.TargetLengthInches = def.TargetLength;
			wep.ViewOffset = def.ViewOffset;
			wep.ViewModelScale = def.ViewModelScale;
			wep.TracerColor = def.TracerColor;

			if ( def.Archetype == PostalWeaponArchetype.Shovel )
			{
				wep.UsesAmmo = false;
				wep.UsesClips = false;
				wep.MeleeDamage = def.Damage;
				wep.HoldType = "melee";
			}
			else
			{
				wep.UsesAmmo = true;
				wep.UsesClips = true;
				wep.ClipMaxSize = def.ClipSize;
				wep.StartingAmmo = def.ClipSize * 5;
				wep.ReloadTime = 1.25f;
				wep.AutoReload = true;
				wep.HoldType = def.PreferredSlot <= 1 ? "pistol" : (def.PreferredSlot == 2 ? "shotgun" : "rifle");
				wep.Ballistics = new()
				{
					Damage = def.Damage,
					Pellets = Math.Max( 1, def.Pellets ),
					Range = 4500f,
					Radius = 1.5f,
					Force = def.Pellets > 1 ? 650f : 340f,
					SpreadBase = new Vector2( def.Spread, def.Spread ),
					SpreadGrowth = new Vector2( def.Spread * 0.45f, def.Spread * 0.45f ),
					SpreadRecovery = 0.2f,
				};
				wep.ExplosionDamage = def.Damage;
				wep.ExplosionRadius = def.ExplosionRadius;
				wep.ProjectileSpeed = def.ProjectileSpeed;
			}

			var model = Model.Load( def.ModelPath );
			if ( model is not null && !model.IsError )
			{
				var vis = PostalWeapon.BuildNormalizedModelChild( root, "WorldVisual", model, def.TargetLength );
				vis.Flags &= ~GameObjectFlags.NotSaved;
				foreach ( var ch in vis.Children ) ch.Flags &= ~GameObjectFlags.NotSaved;
			}

			var col = root.AddComponent<BoxCollider>();
			col.Scale = new Vector3( def.TargetLength * 0.85f, 10f, 12f );
			root.AddComponent<Rigidbody>();
		}

		_prefab = builder.Create();
		return _prefab;
	}

	protected override void Shutdown()
	{
		if ( _prefab is not null )
		{
			PrefabBuilder.Destroy( _prefab );
			_prefab = null;
		}
		base.Shutdown();
	}
}
