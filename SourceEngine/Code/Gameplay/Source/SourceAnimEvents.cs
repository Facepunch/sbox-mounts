using System;

namespace Source1;

/// <summary>Source SDK animation event ids (eventlist.h and the weapon code) the mounted weapons react to.</summary>
static class SourceAnimEvents
{
	public const int ScriptSound = 1004;
	public const int ScriptSoundVoice = 1008;
	public const int WeaponThrow = 3005;
	public const int WeaponThrow2 = 3013;
	public const int WeaponThrow3 = 3016;
	public const int WeaponSequenceFinished = 3900;
	public const int ClientSound = 5004;
	/// <summary>CL_EVENT_MUZZLEFLASH0-3 are 5001, 5011, 5021 and 5031, for attachments 1-4.</summary>
	public const int MuzzleFlash0 = 5001;
	public const int MuzzleFlash3 = 5031;
	public const int EjectBrass1 = 6001;

	public static bool IsMuzzleFlash( int id ) => id is >= MuzzleFlash0 and <= MuzzleFlash3 && (id - MuzzleFlash0) % 10 == 0;

	public static bool IsMuzzleFlash( SourceModelSequenceData.Event e ) => e.Name == "AE_MUZZLEFLASH" || IsMuzzleFlash( e.Id );
}
