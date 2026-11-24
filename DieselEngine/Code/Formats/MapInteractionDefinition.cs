namespace Diesel;

/// <summary>Explicit installed unit identities, rather than matching decorative phone/pole names.</summary>
static class MapInteractionDefinition
{
	public static bool IsAlarm( string unit ) => unit is
		"units/world/props/office/desk_accessories/desk_phone_02"
		or "units/world/props/office/phone/phone"
		or "units/world/props/suburbia_phone_02/suburbia_phone_02"
		or "units/world/props/hospital_phone/hospital_phone"
		or "units/world/architecture/hospital/props/panic_button/panic_button";

	public static bool IsExit( string unit ) =>
		unit == "units/dev_tools/mission_elements/point_fleepoint/point_fleepoint";
}
