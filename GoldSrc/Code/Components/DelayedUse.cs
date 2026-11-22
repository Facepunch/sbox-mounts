using System;
using System.Globalization;

namespace GoldSrc.Maps;

[Hide]
public sealed class DelayedUse : MapEntity
{
	[Property] public GameObject Activator { get; set; }
	[Property] public float FireAt { get; set; }

	public void Begin( Doo output, GameObject activator )
	{
		float delay = 0f;
		var skip = 0;
		if ( output.Body.FirstOrDefault() is Doo.DelayBlock { Seconds: Doo.LiteralExpression literal } )
		{
			delay = Convert.ToSingle( literal.LiteralValue.Value, CultureInfo.InvariantCulture );
			skip = 1;
		}
		Activator = activator;
		FireAt = Time.Now + MathF.Max( delay, 0f );
		OnTrigger = new Doo { Body = output.Body.Skip( skip ).ToList() };
	}

	protected override void OnFixedUpdate()
	{
		if ( !CanTrigger || Time.Now < FireAt ) return;
		Fire( OnTrigger, Activator.IsValid() ? Activator : null, detached: true );
		Kill();
	}

	protected override void OnDestroy()
	{
		if ( Map.IsValid() ) Map.Entities.Remove( this );
		base.OnDestroy();
	}
}
