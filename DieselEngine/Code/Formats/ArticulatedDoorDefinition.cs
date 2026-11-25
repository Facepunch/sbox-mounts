namespace Diesel;

/// <summary>Audited moving roots and open endpoints from installed door sequences.</summary>
sealed record ArticulatedDoorDefinition( string[] Roots, float End, bool StartsOpen = false, float Initial = 1, string[] Omit = null, float[] Angles = null, bool AlignPair = false, bool Reverse = false, int GroupSize = 0 )
{
	public static ArticulatedDoorDefinition Find( string unit, string state )
	{
		bool open = state is "open" or "open_in" or "open_out" or "open_door";
		return unit switch
		{
			"units/world/architecture/slaughterhouse/slaughterhouse_int_fence_door" when state is "" or "default" or "show" => new( ["rp_int_fence_door"], 0, Angles: [95] ),
			"units/world/props/bank/security_door/security_door_02" => new( ["a_security_door"], 4, open, Reverse: true ),
			"units/world/props/container/container_two_doors/container_two_doors" => new(
				["a_left_door", "a_right_door", "a_left_door_back", "a_right_door_back"], state == "ajar" ? 13f / 30 : 5f / 30,
				state is "wide" or "ajar", Angles: [-95, 95, float.NaN, float.NaN], AlignPair: true, GroupSize: 2 ),
			"units/world/props/container/slaughterhouse_container_two_doors" => new(
				["a_left_door", "a_right_door", "a_left_door_back", "a_right_door_back"], 1, state?.EndsWith( "_open" ) == true, GroupSize: 2 ),
			"units/world/architecture/secret_stash/props/secret_stash_door_wooden_1" => new( ["rp_secret_stash_door_wooden_1"], 0, Angles: [95] ),
			"units/world/architecture/hospital/hospital_door_2" => new( ["rp_hospital_door_2"], 0, Angles: [95] ),
			"units/world/architecture/hospital/hospital_door_3" => new( ["rp_hospital_door_3"], 0, Angles: [95] ),
			"units/world/architecture/bank/glass_door/bank_glass_door_single" => new( ["rp_glass_officedoor"], 0, Angles: [95] ),
			"units/world/architecture/bank/glass_door/bank_glass_door_single_green" => new( ["rp_glass_officedoor_green"], 0, Angles: [95] ),
			"units/world/architecture/bank/glass_door/bank_glass_doors"
				or "units/world/architecture/diamondheist/glass_door/glass_doors_closed_diamondheist"
				or "units/world/architecture/bank/bank_lobby/bank_entrance_door/bank_entrance_door"
				or "units/world/architecture/bank/bank_lobby/bank_entrance_door/bank_entrance_door_closed"
				=> new( ["a_door_left", "a_door_right"], 0, Angles: [-95, 95], AlignPair: true ),
			"units/world/architecture/hospital/hospital_elevator_doors" => new( ["a_elevator_door_l", "a_elevator_door_r"], 130f / 30, open, Omit: ["c_box_player_1"] ),
			"units/world/props/diamond_heist/elevator_doors/diamond_heist_elevator_doors" => new( ["a_left_door_1", "a_left_door_2", "a_right_door_1", "a_right_door_2"], 2, open ),
			"units/world/props/sliding_security_door_suburbia/sliding_security_door_suburbia" => new( ["a_door"], -1 ),
			"units/world/architecture/hospital/hospital_securitydoor_1" => new( ["a_door"], 65f / 30, open ),
			"units/world/props/street/plywood_fence/plywood_fence_door" => new( ["a_hinge"], 2, open ),
			"units/world/props/street/street_garage_door/street_garage_door" => new( ["a_door"], 98f / 30, open ),
			"units/world/props/suburbia_door_secret/suburbia_door_secret" => new( ["a_door"], 132f / 30, open ),
			"units/world/architecture/street/consulate_exit/consulate_door" => new( ["a_door"], 75f / 30, open ),
			"units/world/props/bank/vault/vault_door_set" => new( ["a_vault_door_a", "a_vault_door_b"], 460f / 30, open ),
			"units/world/props/bank/vault/vault_door_set_hidable/vault_door_set_hidable" when state is "show" or "open_door" or "close_door" => new( ["a_vault_door_a", "a_vault_door_b"], 460f / 30, open ),
			"units/world/props/bank/vault/vault_hall_bar_door" => new( ["a_vault_hall_bar_door_l", "a_vault_hall_bar_door_r"], 4, open, Omit: ["c_box_glitch_blocker"] ),
			"units/world/props/gym/gym_fence_door/gym_fence_door" => new( [state?.EndsWith( "_out" ) == true ? "a_out" : "a_in"], -1, open ),
			"units/world/architecture/slaughterhouse/slaughterhouse_yard_fence_door" => new( [state == "open_out" ? "a_anim_out" : "a_anim_in"], -1, open ),
			_ => null
		};
	}
}
