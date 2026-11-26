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

## Inspecting source metadata

Metadata is available independently of model/clip creation:

```csharp
var mount = (Diesel.PaydayMount)await Sandbox.Mounting.Directory.Mount( "payday" );
var animation = mount.GetAnimationInfo( "anims/fps/beretta/beretta_reload" );
foreach ( var cue in animation.Cues )
	Log.Info( $"{cue.Time:F3}s: {cue.Name}" );

var timeline = mount.GetUnitAnimationInfo(
	"units/vehicles/animated_cars/vehicle_street_getaway_event_van/vehicle_street_getaway_event_van" );
foreach ( var group in timeline.Groups )
	Log.Info( $"{group.Name}: {group.TrackCount} tracks, {group.StartTime}–{group.EndTime}s" );
```

Clip cues use seconds, preserve distinct names at the same timestamp and repeated names at
different times, and omit exact duplicate name/time pairs. Cue names are authored markers
(for example `mag_in`), not necessarily mounted sound-event paths.

Group metadata includes its object names, explicitly selected controller count, key-time range,
and raw `Enabled`, `Speed` and `Time` expressions. Omitted attributes remain null. A group may
exist without timed controllers; its count and time range are then zero. Key-time ranges can
include preroll and are not inferred sequence-manager playback ranges.

Collections are read-only. Results are cached until mount refresh/shutdown. Missing sources
throw `InvalidDataException`; these queries do not load NPC assets or dispatch events.
The full `/embedded.anim` resource combines the unit's controllers. Select a named group
through `/groups/<name>.anim` or `PaydayScriptedProp.AnimationGroup` as described below.

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

### Selecting an authored group

Use `/groups/<name>.anim` to animate only the nodes explicitly listed in that source group:

```csharp
string van = "mount://payday/units/vehicles/animated_cars/"
	+ "vehicle_street_getaway_event_van/vehicle_street_getaway_event_van.unit.vmdl";
var doors = await ResourceLibrary.LoadAsync<Sandbox.Animation.Clip>( van + "/groups/doors.anim" );
```

Unselected nodes keep their bind-local transforms (and naturally follow an animated parent).
Groups do not implicitly enable every descendant's controller. Group clips run from source
time zero to the last selected key. Groups without positive-time keys cannot produce a timed clip.
Authored `enabled`, `speed` and `time` settings remain metadata; requesting a group explicitly
does not evaluate those expressions.

Prefer a named action group for gameplay. The combined timeline can include mutually exclusive
crash/drive branches or independent doors. All 58 timed groups across the 17 scripted units loaded
successfully; 28 other groups contain no timed controllers. Van tests verify door/getaway isolation.

### Moving collision

Add `Diesel.PaydayScriptedProp` to a game object and assign its `Renderer` to a
`SkinnedModelRenderer` using one of the timed scripted-vehicle models. The component owns
the renderer's animation graph and samples collision from the same timeline:

```csharp
var prop = gameObject.AddComponent<Diesel.PaydayScriptedProp>();
prop.Renderer = renderer;
prop.AnimationGroup = "getaway"; // for the getaway-van model; empty means all controllers
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
Changing `AnimationGroup` rebuilds the graph and collision with the selected tracks while retaining
the normalized `Cycle`. Both rendering and collision use the same selection.

Do not add a second static model collider for the same moving geometry. This component
does not supply multiplayer timeline synchronization, collision-driven stopping, or mission
sequence/event execution. Replacing its renderer/model rebuilds the owned animation/collision.

Resource checks cover collision construction on all 17 scripted units. Moving trace checks
cover the hanging truck (22 parts), getaway van (1), and garbage truck (21), including vacated
van space and disable/re-enable cleanup.
