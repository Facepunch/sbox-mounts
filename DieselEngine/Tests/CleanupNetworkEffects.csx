// Run on the test host; peer disappearance is checked separately.
if ( !Networking.IsHost ) throw new Exception( "Cleanup must run on the host." );
var scene = Game.ActiveScene ?? throw new Exception( "No active scene." );
var targets = scene.GetAllObjects( true ).Where( g => g.Name == "PAYDAY network effect fixture"
	|| g.Components.GetAll().Any( c => c.GetType().Name == "PaydayDebris" ) ).ToArray();
foreach ( var target in targets ) target.Destroy();
return new { Removed = targets.Length };
