# Browser character animations

Ordinary character models expose native s&box animation resources without NPC prefabs:

```csharp
string modelPath = "mount://payday/units/characters/civilians/builder_1/builder_1.unit.vmdl";
string source = "anims/shared/unarmed/neutral/ntl_std_walk_fwd";
var model = await ResourceLibrary.LoadAsync<Model>( modelPath );
var skeleton = await ResourceLibrary.LoadAsync<Sandbox.Animation.Skeleton>( modelPath + "/rig.skel" );
var clip = await ResourceLibrary.LoadAsync<Sandbox.Animation.Clip>( $"{modelPath}/clips/{source}.anim" );

var builder = new Sandbox.Animation.GraphBuilder().WithSkeleton( skeleton );
var cycle = builder.FloatParameter( "cycle", 0, 1 );
var graph = builder.Create( builder.TimeControlledClip( clip, cycle, new() { SampleRootMotion = false } ) );
renderer.Model = model;
renderer.UseAnimGraph = true;
renderer.Animation.Graph = graph;
renderer.Animation.Parameters.Set( "cycle", 0.5f );
```

Keep the procedural graph alive while the renderer uses it; dispose it when finished.
Update `cycle` to play the clip, or compose it into your own graph.

`Diesel.PaydayMount.AnimationPaths` lists the installed game's named animation sources
(1,802 in the tested installation). Clips load on demand for the requested character rig,
not eagerly for every model. A source path is not a compatibility guarantee: weapon,
vehicle and partial-body tracks also occur in the list. Clips with no matching rig tracks
are rejected; unmatched bones retain the source model's rest pose.

Frames are resampled at approximately 30 Hz with both endpoints included and the exact
source duration retained. Skin poses use `root_point` space; authored root travel is stored
separately as native root motion. There is no automatic gameplay-event dispatch, sound
playback, loop selection or animation state machine. NPC prefabs retain their existing
animator and physics setup.

Verified locally: idle/walk resource loading on builder and police rigs, native/source
pose agreement, duration, moving bone tracks, and graph playback/rendering on the builder.
The whole source-animation library has not been exhaustively checked.

## Scripted vehicles

The 17 units under `units/vehicles/animated_cars/` also expose rigged browser models
and `/rig.skel`. Sixteen contain embedded controllers, available as `/embedded.anim`.
The separate `prisoner_truck_door` has a rig but no timed embedded motion.

```csharp
string vehicle = "mount://payday/units/vehicles/animated_cars/"
	+ "vehicle_slaughterhouse_hanging_armored_truck/"
	+ "vehicle_slaughterhouse_hanging_armored_truck.unit.vmdl";
var motion = await ResourceLibrary.LoadAsync<Sandbox.Animation.Clip>( vehicle + "/embedded.anim" );
```

Use the same graph setup shown above. Embedded clips represent the entire authored controller
timeline, not individual sequence-manager events. They preserve translation and rotation in
the unit's authored orientation space, including vehicle travel in bone poses. They do not use
character proportion retargeting or strip travel into a separate root-motion channel.
Render rigs retain graphics/skin bones and their ancestors; unrelated collision helpers are omitted.
The clip resources themselves provide visual animation without driving AI or event triggers.
Map placements continue to use the existing placed-unit importer.

All 17 rigs and 16 timelines passed load/source-pose checks at the start, midpoint and end.
The hanging armored truck additionally passed native graph-pose and rendered-appearance checks.

### Moving collision

Add `Diesel.PaydayScriptedProp` to a game object and assign its `Renderer` to a
`SkinnedModelRenderer` using one of the timed scripted-vehicle models. The component owns
the renderer's animation graph and samples collision from the same timeline:

```csharp
var prop = gameObject.AddComponent<Diesel.PaydayScriptedProp>();
prop.Renderer = renderer;
prop.Cycle = 0.25f; // normalized position in the full embedded timeline
prop.Playing = true;
prop.PlaybackRate = 1;
prop.Loop = false;
```

Collision uses one convex hull per enabled authored collision object, including primitive
shapes. Concave mesh objects therefore become convex approximations, as with moving doors.
Generated keyframed colliders follow the source collision-node transforms, even for nodes
omitted from the render skeleton. They are transient, excluded from navmesh generation,
and removed when the component is disabled or destroyed.

Do not add a second static model collider for the same moving geometry. This component
does not supply multiplayer timeline synchronization, collision-driven stopping, or mission
sequence/event execution. Replacing its renderer/model rebuilds the owned animation/collision.

Resource checks cover collision construction on all 17 scripted units. Moving trace checks
cover the hanging truck (22 parts), getaway van (1), and garbage truck (21), including vacated
van space and disable/re-enable cleanup.
