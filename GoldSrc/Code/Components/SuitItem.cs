namespace GoldSrc.Maps;

public sealed class HevSuit : Component
{
}

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
		var floor = Scene.Trace.Ray( start + Vector3.Up, start + Vector3.Down * 256 ).WithTag( "world" ).Run();
		if ( floor.Hit && !floor.StartedSolid ) WorldPosition = floor.EndPosition;
	}

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( !CanTrigger || !Networking.IsHost ) return;
		var player = other.GetComponentInParent<global::Player>();
		if ( !player.IsValid() || player.Health <= 0 ) return;
		var suited = player.GetComponent<HevSuit>().IsValid();
		string sound;
		switch ( Item )
		{
			case "item_suit" when !suited:
				player.GameObject.AddComponent<HevSuit>();
				sound = ShortLogon ? "!HEV_A0" : "!HEV_AAx";
				break;
			case "item_battery" when suited && player.Armour < 100:
				player.Armour = System.Math.Min( 100, player.Armour + (Skill >= 3 ? 10 : 15) );
				sound = "items/gunpickup2.wav";
				break;
			case "item_healthkit" when player.Health < player.MaxHealth:
				player.Health = System.Math.Min( player.MaxHealth, player.Health + (Skill >= 3 ? 10 : 15) );
				sound = "items/smallmedkit1.wav";
				break;
			case "item_longjump" when suited && !player.GetComponent<LongJumpModule>().IsValid():
				player.GameObject.AddComponent<LongJumpModule>();
				sound = "!HEV_A1";
				break;
			case "weaponbox" when player.GetComponentInChildren<BaseInventoryComponent>() is {} inventory:
				foreach ( var (path, count) in Ammo )
					if ( GameResource.Load<BaseAmmoResource>( path ) is {} ammo ) inventory.GiveAmmo( ammo, count );
				sound = "items/gunpickup2.wav";
				break;
			default:
				return;
		}
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
			mount.NpcAudio.Emit( sound, player.WorldPosition );
		Fire( OnTrigger, player.GameObject, detached: true );
		Kill();
	}
}
