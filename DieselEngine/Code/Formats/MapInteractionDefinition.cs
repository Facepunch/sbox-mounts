namespace Diesel;

/// <summary>Explicit installed unit identities, rather than matching decorative phone/pole names.</summary>
static class MapInteractionDefinition
{
	public static bool IsApartmentDoor( string unit, string variation ) =>
		unit == "units/world/architecture/apartment/door/apartment_door_03"
		&& variation is "close_in" or "close_out";

	// Known suburban open states; damaged/missing-panel variants remain static imports.
	public static bool IsFramedDoor( string unit, string variation ) =>
		unit == "units/world/props/door_suburban_01/door_suburban_01"
		&& variation is "a_open_90" or "a_open_full" or "b_open_90" or "b_open_full" or "c_open_90" or "c_open_full";

	// These variants contain a single leaf under an authored hinge at the orientation root.
	// Bank's list_* variants include wall trim that must not rotate with the leaf.
	public static bool IsSwingDoor( string unit, string variation ) =>
		(unit == "units/world/architecture/hospital/hospital_door_1"
			&& (string.IsNullOrEmpty( variation ) || variation is "default" or "closed"))
		|| (unit == "units/world/architecture/bank/bank_door_toilet/bank_door_toilet"
			&& (string.IsNullOrEmpty( variation ) || variation is "default" or "man" or "woman"));

	public static bool IsCamera( string unit ) => unit is
		"units/world/props/security_camera/security_camera"
		or "units/world/props/security_camera/security_camera_white"
		or "units/world/props/security_camera/security_camera_hospital";

	public static bool IsAlarm( string unit ) => unit is
		"units/world/props/office/desk_accessories/desk_phone_02"
		or "units/world/props/office/phone/phone"
		or "units/world/props/suburbia_phone_02/suburbia_phone_02"
		or "units/world/props/hospital_phone/hospital_phone"
		or "units/world/architecture/hospital/props/panic_button/panic_button";

	public static bool IsExit( string unit ) =>
		unit == "units/dev_tools/mission_elements/point_fleepoint/point_fleepoint";
}
