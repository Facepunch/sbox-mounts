using Sandbox.Npcs;
using System;

namespace NS2;

public sealed partial class Drifter
{
	[Property] public bool ShadeHive { get; set; }
	[Sync( SyncFlags.FromHost )] float cloakFraction { get; set; }
	internal float CloakFraction => cloakFraction;
	TimeUntil inCombat, revealed, nextScan, firstCloakCheck;
	bool camouflageActive, hasCloaked;
	SkinnedModelRenderer cloakOverlay;
	bool enemyOverlay;
	float appliedCloak;

	void Reveal()
	{
		revealed = Math.Max( revealed, camouflageActive ? 2.5f : 2.75f );
	}

	void UpdateCamouflage( float delta )
	{
		if ( nextScan < 0 )
		{
			nextScan = 0.5f;
			const float range = 3 * Scale;
			foreach ( var obj in Scene.FindInPhysics( new Sphere( WorldPosition, range ) ) )
			{
				if ( obj.GetComponentInParent<IDamageable>() is not Component actor ) continue;
				var enemy = actor.GameObject;
				if ( !enemy.Tags.HasAny( "ns2_marine", "ns2_exo", "ns2_lifeform" ) && !enemy.GetComponent<PlayerController>().IsValid() ) continue;
				if ( actor.WorldPosition.DistanceSquared( WorldPosition ) >= range * range || GetDisposition( enemy ) != Disposition.Hostile || AlienTargeting.IsIgnoredPlayer( enemy ) ) continue;
				Reveal();
				break;
			}
		}

		if ( !hasCloaked && firstCloakCheck > 0 ) return;
		camouflageActive = ShadeHive && !hasOrder && inCombat <= 0 && Health > 0;
		hasCloaked |= camouflageActive;
		if ( !hasCloaked ) firstCloakCheck = 1;
		float target = camouflageActive && revealed <= 0 ? 0.97f : 0;
		cloakFraction = cloakFraction.Approach( target, delta * (target > cloakFraction ? 3 : 5) );
	}

	void UpdateCloakMaterial()
	{
		if ( cloakFraction == 0 )
		{
			if ( cloakOverlay.IsValid() ) ClearCloakMaterial();
			return;
		}
		bool enemy = PlayerGait.IsEnemyListener( this );
		if ( !cloakOverlay.IsValid() || enemy != enemyOverlay )
		{
			ClearCloakMaterial();
			enemyOverlay = enemy;
			var layer = new GameObject( Renderer.GameObject, true, "Camouflage" ) { NetworkMode = NetworkMode.Never };
			cloakOverlay = layer.AddComponent<SkinnedModelRenderer>();
			cloakOverlay.Model = Renderer.Model;
			cloakOverlay.BoneMergeTarget = Renderer;
			var name = enemy ? "distort" : "cloaked";
			var material = Material.Load( $"mount://ns2/ns2/cinematics/vfx_materials/{name}.material.vmat" ).CreateCopy();
			if ( enemy )
			{
				material.Set( "g_flMaxRange", 1f );
				if ( Renderer.Materials.GetOriginal( 0 )?.GetTexture( "g_tNormalMap" ) is { } normal ) material.Set( "g_tNormalMap", normal );
			}
			cloakOverlay.MaterialOverride = material;
		}
		if ( appliedCloak == cloakFraction ) return;
		appliedCloak = cloakFraction;
		Renderer.Attributes.Set( "Ns2Dissolve", Math.Min( 1, cloakFraction / 0.125f ) );
		cloakOverlay.MaterialOverride.Set( enemy ? "g_flDistortAmount" : "g_flCloakAmount", cloakFraction );
	}

	void ClearCloakMaterial()
	{
		if ( !cloakOverlay.IsValid() ) return;
		cloakOverlay.GameObject.Destroy();
		cloakOverlay = null;
		appliedCloak = 0;
		if ( Renderer.IsValid() ) Renderer.Attributes.Set( "Ns2Dissolve", 0f );
	}
}
