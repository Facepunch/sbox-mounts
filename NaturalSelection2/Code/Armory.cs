using Sandbox.Npcs;
using System;

namespace NS2;

[Title( "Armory" ), Category( "Natural Selection 2" )]
public sealed class Armory : Ns2Npc, Component.IDamageable
{
	[Property, Sync( SyncFlags.FromHost )] public bool IsBuilt { get; set; } = true;
	[Property, Sync( SyncFlags.FromHost )] public bool IsPowered { get; set; } = true;

	[Sync( SyncFlags.FromHost )] float armor { get; set; } = 200;
	float IDamageable.Armour { get => armor; set => armor = Math.Clamp( value, 0, 200 ); }
	float IDamageable.MaxArmour => 200;

	protected override void OnDamaging( IDamageable.DamageEvent hit ) => SupportDamage.Absorb( hit, this, armor );

	protected override void OnDamaged( IDamageable.DamageEvent hit ) =>
		(armor, _) = SupportDamage.Apply( hit, this, armor, MaxHealth, default );

	protected override Vector3 GetEngagementPoint() =>
		Renderer.WorldTransform.PointToWorld( Renderer.GetAttachment( "target", false )?.Position ?? Vector3.Zero );

	internal static void Configure( GameObject root, SkinnedModelRenderer renderer )
	{
		root.Tags.Add( "structure" );
		var armory = root.AddComponent<Armory>();
		armory.DisplayName = "Armory";
		armory.Faction = Factions.Ally;
		armory.Renderer = renderer;
		armory.MaxHealth = armory.Health = 1500;
		if ( Sandbox.Mounting.Directory.Get( "ns2" )?.GetByPath( renderer.Model.ResourcePath ) is ModelLoader model )
			model.AddMovementCollision( renderer, trigger: false );
	}
}
