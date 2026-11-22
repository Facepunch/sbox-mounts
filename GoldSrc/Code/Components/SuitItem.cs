namespace GoldSrc.Maps;

public sealed class SuitItem : MapEntity, Component.ITriggerListener
{
	[ConVar( "hl1_skill" )] public static int Skill { get; set; } = 1;
	[Property] public string Item { get; set; }
	[Property] public bool ShortLogon { get; set; }
	[Property] public Dictionary<string, int> Ammo { get; set; } = [];

	protected override void OnStart()
	{
		if ( !Networking.IsHost ) return;
		var start = WorldPosition;
		var drop = Scene.Trace.Ray( start + Vector3.Up, start + Vector3.Down * 256 ).WithTag( "world" );
		if ( Item != "weaponbox" ) drop = drop.Size( new BBox( new Vector3( -16, -16, 0 ), new Vector3( 16, 16, 16 ) ) );
		var floor = drop.Run();
		if ( floor.Hit && !floor.StartedSolid ) WorldPosition = floor.EndPosition;
	}

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( !CanTrigger || !Networking.IsHost ) return;
		var player = other.GetComponentInParent<global::Player>();
		if ( !player.IsValid() || player.Health <= 0 ) return;
		var suit = player.GetComponent<HevSuit>();
		string sound = null;
		switch ( Item )
		{
			case "item_suit" when !suit.IsValid():
				player.GameObject.AddComponent<HevSuit>().Speak( ShortLogon ? "!HEV_A0" : "!HEV_AAx" );
				break;
			case "item_battery" when suit.IsValid() && player.Armour < 100:
				player.Armour = System.Math.Min( 100, player.Armour + (Skill >= 3 ? 10 : 15) );
				suit.ReportCharge( player.Armour );
				sound = "items/gunpickup2.wav";
				break;
			case "item_healthkit" when player.Health < player.MaxHealth:
				player.Health = System.Math.Min( player.MaxHealth, player.Health + (Skill >= 3 ? 10 : 15) );
				sound = "items/smallmedkit1.wav";
				break;
			case "item_longjump" when suit.IsValid() && !player.GetComponent<LongJumpModule>().IsValid():
				player.GameObject.AddComponent<LongJumpModule>();
				suit.Speak( "!HEV_A1" );
				break;
			case "weaponbox" when player.GetComponentInChildren<BaseInventoryComponent>() is {} inventory:
				foreach ( var (path, count) in Ammo )
					if ( GameResource.Load<BaseAmmoResource>( path ) is {} ammo ) inventory.GiveAmmo( ammo, count );
				sound = "items/gunpickup2.wav";
				break;
			default:
				return;
		}
		if ( sound is not null && Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
			mount.NpcAudio.Emit( sound, player.WorldPosition );
		Fire( OnTrigger, player.GameObject, detached: true );
		Kill();
	}
}
