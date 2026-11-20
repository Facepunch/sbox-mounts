namespace Renderware;

public sealed class GTA3Mount : GTAMount
{
	public override string Ident => "gta3";
	public override string Title => "Grand Theft Auto III";
	public override long AppId => 12100;
	public override GameVersion Game => GameVersion.III;

	protected override string MainDat => "data/gta3.dat";

	public override RestartPoint[] RestartPoints =>
	[
		new( new( 1144.2f, -596.9f, 13.9f ), 90 ),
		new( new( 183.5f, -17.8f, 16.2f ), 180 ),
		new( new( -1253.0f, -138.2f, 57.8f ), 90 ),
		new( new( 1143.9f, -675.2f, -100.0f ), 90 ),
		new( new( 340.2f, -1123.4f, 25.0f ), 180 ),
		new( new( -1259.5f, -44.5f, 57.8f ), 90 ),
	];
}

public sealed class ViceCityMount : GTAMount
{
	public override string Ident => "gtavc";
	public override string Title => "Grand Theft Auto: Vice City";
	public override long AppId => 12110;
	public override GameVersion Game => GameVersion.ViceCity;

	protected override string MainDat => "data/gta_vc.dat";

	public override RestartPoint[] RestartPoints =>
	[
		// hospital
		new( new( 493.5f, 703.1f, 11.1f ), 90 ),
		new( new( -108.3f, -974.4f, 9.4f ), 284 ),
		new( new( -822.7f, 1157.9f, 10.1f ), 4 ),
		new( new( -885.2f, -470.4f, 12.1f ), 276 ),

		// police
		new( new( 508.9f, 506.8f, 10.3f ), 174 ),
		new( new( 398.8f, -469.7f, 10.7f ), 323 ),
		new( new( -659.5f, 760.4f, 10.5f ), 133 ),
		new( new( -871.9f, -682.3f, 10.2f ), 328 ),
	];
}

public sealed class SanAndreasMount : GTAMount
{
	public override string Ident => "gtasa";
	public override string Title => "Grand Theft Auto: San Andreas";
	public override long AppId => 12120;
	public override GameVersion Game => GameVersion.SanAndreas;

	protected override string MainDat => "data/gta.dat";

	public override RestartPoint[] RestartPoints =>
	[
		// hospital
		new( new( 2027.8f, -1420.5f, 16.0f ), 137 ),
		new( new( 1180.8f, -1325.6f, 12.6f ), 271 ),
		new( new( 1244.4f, 331.2f, 18.6f ), 8 ),
		new( new( -2199.7f, -2308.1f, 29.6f ), 323 ),
		new( new( -2670.3f, 616.4f, 13.5f ), 183 ),
		new( new( -316.4f, 1056.0f, 18.7f ), 2 ),
		new( new( -1514.8f, 2527.1f, 54.7f ), 2 ),
		new( new( 1578.4f, 1770.7f, 9.8f ), 100 ),

		// police
		new( new( 1550.7f, -1675.5f, 14.5f ), 90 ),
		new( new( 632.2f, -571.7f, 15.4f ), 267 ),
		new( new( -2163.8f, -2387.8f, 29.6f ), 134 ),
		new( new( -1605.8f, 716.9f, 11.0f ), 355 ),
		new( new( -212.2f, 979.4f, 18.3f ), 278 ),
		new( new( -1393.1f, 2633.1f, 54.9f ), 86 ),
		new( new( 2337.1f, 2453.8f, 14.0f ), 91 ),
	];
}
