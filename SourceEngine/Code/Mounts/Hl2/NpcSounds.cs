using Sandbox.Npcs;

namespace Source1.Hl2;

public sealed partial class Mount
{
	internal override void PlayNpcEvent( BaseNpc npc, SourceModelSequenceData sequence, SourceModelSequenceData.Event evt )
	{
		if ( evt.Id is >= 6006 and <= 6009 )
		{
			MaterialFootstep( npc, evt.Id is 6006 or 6008, evt.Id >= 6008 );
			return;
		}

		var voice = (npc as CreatureNpc)?.Voice;
		var steps = voice == "Zombine" ? "Zombie" : voice;
		var name = evt.Name switch
		{
			"AE_ZOMBIE_STEP_LEFT" => $"{steps}.FootstepLeft",
			"AE_ZOMBIE_STEP_RIGHT" => $"{steps}.FootstepRight",
			"AE_ZOMBIE_SCUFF_LEFT" => $"{voice}.ScuffLeft",
			"AE_ZOMBIE_SCUFF_RIGHT" => $"{voice}.ScuffRight",
			"AE_FASTZOMBIE_GALLOP_LEFT" => "NPC_FastZombie.GallopLeft",
			"AE_FASTZOMBIE_GALLOP_RIGHT" => "NPC_FastZombie.GallopRight",
			"AE_NPC_LEFTFOOT" when voice == "NPC_Vortigaunt" => "NPC_Vortigaunt.FootstepLeft",
			"AE_NPC_RIGHTFOOT" when voice == "NPC_Vortigaunt" => "NPC_Vortigaunt.FootstepRight",
			"AE_POISONHEADCRAB_FOOTSTEP" => sequence.Activity == "ACT_WALK" ? "NPC_BlackHeadcrab.FootstepWalk" : "NPC_BlackHeadcrab.Footstep",
			"AE_ANTLION_WALK_FOOTSTEP" => "NPC_Antlion.Footstep",
			"AE_ANTLION_FOOTSTEP_SOFT" => "NPC_Antlion.FootstepSoft",
			"AE_ANTLION_FOOTSTEP_HEAVY" => "NPC_Antlion.FootstepHeavy",
			"AE_ANTLION_MELEE1_SOUND" => "NPC_Antlion.MeleeAttackSingle",
			"AE_ANTLION_MELEE2_SOUND" => "NPC_Antlion.MeleeAttackDouble",
			"AE_ANTLIONGUARD_FOOTSTEP_LIGHT" => "NPC_AntlionGuard.StepLight",
			"AE_ANTLIONGUARD_FOOTSTEP_HEAVY" => "NPC_AntlionGuard.StepHeavy",
			"AE_HUNTER_FOOTSTEP_LEFT" or "AE_HUNTER_FOOTSTEP_RIGHT" => "NPC_Hunter.Footstep",
			"AE_HUNTER_FOOTSTEP_BACK" => "NPC_Hunter.BackFootstep",
			"AE_NPC_BODYDROP_HEAVY" when npc.Animation.IsGrounded => "AI_BaseNPC.BodyDrop_Heavy",
			_ => null
		};

		var position = npc.WorldPosition;
		if ( evt.Id is 6004 or 6005 )
		{
			var prefix = string.IsNullOrWhiteSpace( evt.Options ) ? "NPC_CombineS" : evt.Options;
			var run = npc.Animation.MoveVelocity.WithZ( 0 ).Length > 150 ? "Run" : "";
			name = $"{prefix}.{run}Footstep{(evt.Id == 6004 ? "Left" : "Right")}";
		}
		else if ( voice == "NPC_Strider" )
		{
			var foot = evt.Id switch
			{
				1 or 4 or 7 or 11 => "left foot",
				2 or 5 or 8 or 12 => "right foot",
				3 or 6 or 9 or 13 => "back foot",
				_ => null
			};
			if ( foot is not null ) position = npc.Renderer.GetAttachment( foot )?.Position ?? position;
			name = evt.Id switch
			{
				>= 1 and <= 9 => "NPC_Strider.Footstep",
				>= 11 and <= 13 => "NPC_Strider.Whoosh",
				>= 21 and <= 23 => "NPC_Strider.Creak",
				_ => name
			};
		}

		if ( name is not null && Sounds.Sound( name, Ident ) is { } sound )
			Sound.Play( sound, position );
	}

	static void MaterialFootstep( BaseNpc npc, bool left, bool loud )
	{
		if ( npc.Renderer.GetAttachment( left ? "LeftFoot" : "RightFoot" ) is not { } foot ) return;
		var hit = npc.Scene.Trace.Ray( foot.Position, foot.Position + Vector3.Down * 48 )
			.IgnoreGameObjectHierarchy( npc.GameObject ).WithoutTags( "trigger" ).Run();
		if ( !hit.Hit || hit.Surface is null ) return;
		var sound = left ? hit.Surface.SoundCollection.FootLeft : hit.Surface.SoundCollection.FootRight;
		if ( sound is not null && Sound.Play( sound, foot.Position ) is { } handle && !loud )
			handle.Volume *= 0.5f;
	}
}
