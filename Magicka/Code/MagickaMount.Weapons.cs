using System;

namespace Magicka;

public sealed partial class MagickaMount
{
	public const string WeaponTag = "weapon";

	private readonly record struct WeaponDefinition( string Id, string Title, string ModelPath, Type WeaponType );

	private static WeaponDefinition[] GetPlayerWeapons() =>
	[
		new( "staff_basic", "Basic Staff", "models/items_wizard/staff_basic_0", typeof( MagickaStaffWeapon ) ),
		new( "staff_fire", "Staff of Fire", "models/items_wizard/staff_fire_0", typeof( MagickaFireStaff ) ),
		new( "staff_ice", "Staff of Ice", "models/items_wizard/staff_ice_0", typeof( MagickaIceStaff ) ),
		new( "staff_tesla", "Tesla Staff", "models/items_wizard/staff_tesla_0", typeof( MagickaTeslaStaff ) ),
		new( "staff_arcane", "Arcane Staff", "models/items_wizard/staff_purple_0", typeof( MagickaArcaneStaff ) ),
		new( "staff_life", "Staff of Life", "models/items_wizard/staff_life_0", typeof( MagickaLifeStaff ) ),
		new( "staff_earth", "Earth Staff", "models/items_wizard/staff_dragonclaw_0", typeof( MagickaEarthStaff ) ),
		new( "staff_dead", "Staff of the Dead", "models/items_wizard/staff_of_the_dead_0", typeof( MagickaDeadStaff ) ),
		new( "staff_qfasa", "Staff of QFASA", "models/items_wizard/staff_of_the_white_wizard_0", typeof( MagickaQfasaStaff ) ),
		new( "staff_frostbeam", "Frostbeam Staff", "models/items_wizard/staff_snowflake_0", typeof( MagickaFrostbeamStaff ) ),
		new( "staff_hellstaff", "Hellstaff", "models/items_wizard/staff_hellstaff_0", typeof( MagickaHellstaff ) ),
		new( "staff_thunderbolt", "Thunderbolt Staff", "models/items_wizard/staff_transmatterray_0", typeof( MagickaThunderboltStaff ) ),
		new( "excalibur", "Excalibur", "models/items_wizard/excalibur_0", typeof( MagickaExcalibur ) ),
		new( "m60", "Magicka M60", "models/items_wizard/m60_0", typeof( MagickaM60 ) ),
	];

	/// <summary>Registers spawnable player weapon prefabs tagged <c>weapon</c>.</summary>
	private int MountWeapons( MountContext context, HashSet<string> registeredModels )
	{
		var count = 0;
		foreach ( var def in GetPlayerWeapons() )
		{
			if ( !registeredModels.Contains( def.ModelPath ) )
				continue;

			var loader = new WeaponPrefabLoader( def.WeaponType, def.ModelPath, def.Title );
			loader.Tags.Add( WeaponTag );
			context.Add( ResourceType.PrefabFile, $"entities/weapons/{def.Id}.prefab", loader );
			count++;
		}

		return count;
	}
}
