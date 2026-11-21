namespace IdTech3;

public sealed class Quake3Mount : IdTech3Mount
{
	public override string Ident => "quake3";
	public override string Title => "Quake III Arena";
	protected override long AppId => 2200;
	protected override string BaseDirectory => "baseq3";
	protected override bool MountOtherDirectories => true;
	protected override string[] IgnoredDirectories => ["Docs", "Extras", "Punkbuster", "pb"];

	static readonly GameProfile _profile = Profiles.Quake3();
	public override GameProfile Profile => _profile;
}

public sealed class RtcwMount : IdTech3Mount
{
	public override string Ident => "rtcw";
	public override string Title => "Return to Castle Wolfenstein";
	protected override long AppId => 9010;
	protected override string BaseDirectory => "Main";

	static readonly GameProfile _profile = Profiles.Rtcw();
	public override GameProfile Profile => _profile;
}

public sealed class WolfETMount : IdTech3Mount
{
	public override string Ident => "wolfet";
	public override string Title => "Wolfenstein: Enemy Territory";
	protected override long AppId => 1873030;
	protected override string BaseDirectory => "etmain";

	static readonly GameProfile _profile = Profiles.Rtcw();
	public override GameProfile Profile => _profile;
}

/*
public sealed class CallOfDutyMount : IdTech3Mount
{
	public override string Ident => "cod";
	public override string Title => "Call of Duty (2003)";
	protected override long AppId => 2620;
	protected override string BaseDirectory => "Main";

	// the Steam release ships most of its data (and maps) as pak*.zip
	protected override string[] ArchiveExtensions => [".pk3", ".zip"];

	static readonly GameProfile _profile = Profiles.CallOfDuty();
	public override GameProfile Profile => _profile;
}

public sealed class JediOutcastMount : IdTech3Mount
{
	public override string Ident => "jk2";
	public override string Title => "Jedi Knight II: Jedi Outcast";
	protected override long AppId => 6030;
	protected override string BaseDirectory => "GameData/base";

	static readonly GameProfile _profile = Profiles.Raven();
	public override GameProfile Profile => _profile;
}

public sealed class JediAcademyMount : IdTech3Mount
{
	public override string Ident => "jka";
	public override string Title => "Jedi Knight: Jedi Academy";
	protected override long AppId => 6020;
	protected override string BaseDirectory => "GameData/base";

	static readonly GameProfile _profile = Profiles.Raven();
	public override GameProfile Profile => _profile;
}
*/

static class Profiles
{
	public static GameProfile Quake3() => new ()
	{
		LightShift = 2,
		SurfaceFlags = SurfaceFlagStyle.Quake3
	};

	public static GameProfile Rtcw() => new ()
	{
		LightShift = 2,
		SurfaceFlags = SurfaceFlagStyle.Rtcw,
		Has3dSkyboxes = true
	};

	public static GameProfile CallOfDuty() => new ()
	{
		LightShift = 1,
		CallOfDuty = true,
		SurfaceFlags = SurfaceFlagStyle.CallOfDuty,
		ImageExtensions = [".dds", ".tga", ".jpg", ".png"],
	};

	public static GameProfile Raven() => new ()
	{
		LightShift = 1
	};
}
