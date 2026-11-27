// Run on a live test host. Leaves named fixtures for the peer inspection script.
if ( !Networking.IsActive || !Networking.IsHost ) throw new Exception( "Requires a live host." );
await Sandbox.Mounting.Directory.Mount( "payday" );
var scene = Game.ActiveScene ?? throw new Exception( "No active scene." );
using var scope = scene.Push();
var root = GameObject.GetPrefab( "mount://payday/entities/effects/particles/equipment/vault_drill/drill_active.prefab" ).Clone( Vector3.Up * 200 );
root.Name = "PAYDAY network effect fixture";
root.NetworkSpawn();
var emitter = GameObject.GetPrefab( "mount://payday/entities/debris/units/world/architecture/secret_stash/props/planks_debris.prefab" ).Clone( Vector3.Up * 300 );
// Wait for the host emitter to create the authored bodies, then extend the inspection window.
await Task.Delay( 500, cancellationToken );
var fragments = scene.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() ).Where( c => c.GetType().Name == "PaydayDebris" ).ToArray();
foreach ( var fragment in fragments ) fragment.GetType().GetProperty( "Lifetime" ).SetValue( fragment, 60f );
if ( fragments.Length != 5 ) throw new Exception( $"Expected five host fragments, got {fragments.Length}." );
return new { Effect = root.Id, Fragments = fragments.Select( f => f.GameObject.Id ).ToArray(), Networking.IsHost };
