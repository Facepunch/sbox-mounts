using Sandbox;

namespace GoldSrc.Maps;

[Title( "GoldSrc Button Target" )]
[Category( "GoldSrc" )]
public sealed class ButtonTarget : MapEntity, Component.IPressable, Component.IDamageable
{
	[Property] public BrushAppearance Appearance { get; set; }
	[Property] public bool UseEnabled { get; set; }
	[Property] public bool StartOn { get; set; }
	[Property] public Doo OnTurnOn { get; set; }
	[Property] public Doo OnTurnOff { get; set; }
	private bool _on;

	protected override void OnStart()
	{
		_on = StartOn;
		if ( Appearance.IsValid() ) Appearance.Frame = _on ? 1 : 0;
	}

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		_on = !_on;
		if ( Appearance.IsValid() ) Appearance.Frame = _on ? 1 : 0;
		Fire( _on ? OnTurnOn : OnTurnOff, activator );
	}

	public override void TurnOn( GameObject activator = null )
	{
		if ( !_on ) Trigger( activator );
	}

	public override void TurnOff( GameObject activator = null )
	{
		if ( _on ) Trigger( activator );
	}

	public override void ChangeTarget( string target )
	{
		Target = target;
		if ( !Map.IsValid() ) return;
		OnTurnOn = Map.BuildOutput( Target, UseType.On );
		OnTurnOff = Map.BuildOutput( Target, UseType.Off );
	}

	void IDamageable.OnDamage( in DamageInfo damage ) => Trigger( damage.Attacker );
	bool IPressable.CanPress( IPressable.Event e ) => UseEnabled && CanTrigger;
	bool IPressable.Press( IPressable.Event e )
	{
		if ( !UseEnabled || !CanTrigger ) return false;
		Trigger( e.Source.GameObject );
		return true;
	}
}
