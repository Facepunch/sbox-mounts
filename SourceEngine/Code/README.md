# Source 1

The Source 1 mount puts every type in the `Source1` namespace and ships its shaders in
`game\mount\source1\Assets\shaders\source1`. Public types are named for where they come from:
`Source…` for code shared by the Source games, `Hl2…` for Half-Life 2 and `Css…` for Counter-Strike: Source.
Components are titled the same way (`Source …`, `HL2 …`, `CS:S …`) under the categories `Source 1`,
`Half-Life 2` and `Counter-Strike: Source`.

The `gmod` mount registers supported resources by scanning GMA/VPK archives and
loose files inside the Garry's Mod installation, including bundled Source game content.
The mount prioritizes its main game folder (`garrysmod`).
There is no gameinfo, depot, external Workshop or gamemode discovery.
Every installation subdirectory is scanned, including loose addons, gamemode
content, downloads and cache folders; there is no folder-name allowlist.
Directory links/reparse points are skipped to avoid cycles and content outside
the installation. Local `.gma` addon archives, including Workshop downloads
stored within the installation, are indexed without extracting their contents.
The first indexed match wins; later content sources never register duplicate entries
within a mount. Dependencies are registered through the mounting API and requested
through the normal model, material, texture and sound resource APIs. The mount
loaders own resource caching; there is no private dependency resource cache or
requirement for another game mount to be active.
Installation paths come from the mount host. GMod scans stay inside its
installation; addons referencing content found only in another game's installation
cannot resolve it through the GMod mount.

For example: `mount://gmod/models/props_c17/oildrum001.mdl.vmdl`.

The `hl2` mount scans the Half-Life 2 installation the same way, with `hl2` first; the Episodes
and Lost Coast are folders of that installation. Without it, the mount falls back to the copy
Garry's Mod ships: `sourceengine/hl2_*_dir.vpk`, then `sourceengine/content_hl2_dir.vpk` (Episode
One/Two models, materials, particles and some sound scripts), then GMod's own sound manifest and the
HL2 sound scripts it moved to `scripts/sounds/`, then the loose `sourceengine/scripts` (HL2's weapon,
surface and prop scripts), `sourceengine/resource` (its scheme and HUD fonts) and `garrysmod/cfg/skill.cfg` (HL2's with the Episodes' values merged in).
That copy has no maps, Lost Coast, Episode voice lines or music, `cfg/skill_episodic.cfg`, the
player stunstick, hopwire, harpoon or proto1 weapon scripts, or the Episode Two-only particle files
(advisor, hunter intro/shield, grenade and explosion effects among them). Whenever Garry's Mod is
installed, its handless `models/weapons/c_*` viewmodels (not its `c_arms` models) and the
`v_stunstick` materials they need are added last. Standalone files always win; with Half-Life 2
installed, everything else resolves exactly as without Garry's Mod. Game code detects availability
per resource, e.g. `Directory.Get( "hl2" ).GetByPath( "mount://hl2/models/weapons/c_pistol.mdl.vmdl" )`.

The `css` mount (Counter-Strike: Source, app 240; `cstrike` is the GoldSrc CS 1.6 mount)
scans the standalone installation with `cstrike` first, so its `hl2` folder supplies the
shared HL2 archives. Without it, the mount falls back to the same content as shipped by
Garry's Mod: `sourceengine/content_cstrike_dir.vpk` followed by `sourceengine/hl2_*_dir.vpk`.
That copy has no weapon scripts, particles, radio or bot sounds. Whenever Garry's Mod is
installed, a filtered `garrysmod/garrysmod_dir.vpk` is added last: only its handless
`models/weapons/cstrike/c_*` viewmodels, `scripts/sounds/cs_game_sounds_*.txt` and the sound
manifest that lists them. Standalone files always win, so with both installed the `c_`
models resolve their materials from the standalone archives. Game code detects either
case per resource, e.g. `mount://css/models/weapons/cstrike/c_rif_ak47.mdl.vmdl`
(handless viewmodel) or `mount://css/scripts/weapon_ak47.ctx.json` (weapon script).

## Code layout

`SourceGameMount` owns indexing and mount lifecycle; `Hl2Mount.cs`, `CssMount.cs` and
`SourceGarrysModMount.cs` define game mounts. A mount that reads more than its own installation overrides `FindContent`.
`SourceGameMount.Files.cs` inventories loose files and archives in one filesystem pass;
there is no search-path configuration layer. Loose and archive-backed files share one file reference
representation and one content-opening API.
GMA and VPK readers share only a small archive interface and the existing leased,
random-access stream implementation. Indexing reads archive directories, not
payloads; file contents are read lazily without whole-archive buffers or extraction.
`Formats` contains internal source readers, with standalone BSP readers under
`Formats/Bsp`. `Resource` contains internal mount loaders and focused model/material
builders; map construction is grouped under `Resource/Map` into geometry, lighting,
sky and entity builders. BSP readers do not construct engine scenes. Only mount
types and map runtime components are public. Model readers are temporary: the mounted loader caches the built
native model without retaining parsed MDL/VVD/VTX/PHY geometry. Material and texture
source caches support variants and are retired with their content index.

## Scope

Includes MDL/VVD/VTX geometry, bodygroups, skins, render LODs, bind skeletons,
bone bounds, morph targets, hitboxes, attachments, PHY collision/ragdolls,
physical surfaces with impact decals, footsteps, bullet/physics impact,
scrape and break sounds, plus prop/break-piece data. Dependencies resolve
through archives and loose files inside the selected game's installation. Archives precede loose
files, with the main game folder first and the other installation folders in
alphabetical order. Split VPK data chunks with a matching directory archive are
skipped. Other VPK files are validated, including standalone archives with
numeric suffixes. GMA addons precede VPKs within each archive priority group;
equal-priority archive paths are sorted case-insensitively for stable first-match
resolution. GMA versions 1–3 retain their authored paths and reject invalid names,
duplicate entries and out-of-bounds payloads. Invalid or unreadable archive directories
produce a warning and a mount error without preventing other content from mounting.
Loose paths are relative to their installation subfolder; files directly in the
installation root are also indexed. Top-level `materials`, `models`, `sound`,
`scripts` and `maps` folders retain their canonical prefixes rather than becoming
separate content roots. Paths are registered as found, without
gamemode or addon aliases.
Loose models resolve their MDL material paths inside their enclosing content root
first, then the ordinary mounted content. Loose VMTs keep the same root for patch
includes, KeyValues directives and texture dependencies, including HDR cubemaps.
For example, a model under `addons/example/models/` uses
`addons/example/materials/` without registering any unprefixed addon aliases.
Resource names and caches retain the full indexed path, so separate addons cannot
overwrite each other's materials or textures. Remount after changing the importer;
previously imported models need reimporting to replace their error-material bindings.
There is no basename or cross-mount fallback.
Model registration checks only indexed filenames, never opens model files.
MDLs require either a VVD/DX90 VTX pair or a PHY companion; other entries are
excluded with summary and per-model trace diagnostics. Source files
remain indexed for dependencies. PHY-backed entries can be geometry-free;
render geometry requirements and malformed data are validated lazily on load.

VMT/VTF support retains model surface materials, eyes, teeth, two-texture,
modulate, Refract, Core, VortWarp, MonitorScreen, VolumeClouds and Aftershock,
mipmaps, per-texture sampling flags, texture frames and supported material
proxies. LightmappedGeneric can be used on models with
ordinary scene lighting; imported BSP surfaces use baked map lighting.
Ordered material proxy chains support scalar/vector arithmetic, component writes,
Equals, Abs, Frac, Int, Clamp, LessOrEqual, Exponential, Sine, CurrentTime,
GMod's time-wrapped LinearRamp, TextureScroll and TextureTransform. Material-variable
inputs retain the values visible at each proxy's position in the block. Supported
outputs include alpha, combined color modulation, generic environment/self-illumination
tints, detail blend strength, primary texture frames and the existing UV-transform slots.
Constant expressions are folded on import. Animated expression programs are shared
by material variants, bounded to 128 scalar instructions, and evaluated in the shaders;
they require no gamemode callbacks or per-frame CPU material updates. Existing simple
scroll, alpha-sine and texture-animation blocks retain their specialized shader paths.
Unsupported proxies with identifiable outputs invalidate only dependent expressions,
so independent animations continue. Untargetable proxies and persistent per-bind
feedback retain authored block values with trace diagnostics. Entity-dependent proxies,
random-noise proxies, reverse/non-looping texture playback and other shader parameters
remain unsupported; this is not a complete Source proxy runtime.
Mounting reads each VTF's fixed header to register nonzero frame loaders and
animation-array loaders. Map-packed textures decode only that header, including
LZMA entries, without reading or decompressing the remaining image payload.
Pixel decoding and GPU creation remain lazy. All frames
and arrays are requested through `Texture.Load` and cached by their mounted loaders;
the source metadata cache does not own or dispose native textures.
Authored 2D environment textures use Source's legacy reflection-XY projection;
cube textures retain cubemap reflection. Refraction uses the native framebuffer
copy or an authored base texture, normal/tint inputs, blur and environment
reflection. Core adds flow mapping, its color ramp and EntityOrigin/Sine proxies.
VortWarp retains its vertex deformation and scrolling emission, including
per-instance EntityOrigin. MonitorScreen retains its second texture, contrast,
saturation and tint. VolumeClouds combines three rotating, parallax-layered
textures; Aftershock retains its masked, blurred refraction and silhouette tint.
Their time controls use authored positive times or runtime time, including
CurrentTime proxies.
These use native scene reflection/lighting, not Source's exact multipass renderer.
Environment textures prefer an indexed HDR variant. GMod uses its indexed
`engine/defaultcubemap` when an authored environment texture is missing, matching
the native GMod fallback and reporting the missing dependency. Other missing
textures still fail rather than silently receiving replacement pixels.
Material paths allow parents within the materials root and Source's leading
asset-root slashes. Texture names discard non-HDR source extensions, such as
`.psd`. Numeric values retain Source's prefix parsing, vector-to-scalar conversion
and integer truncation; nonfinite values remain invalid.
Teeth materials use MDL mouth bones when available; models without mouth
records retain the material's world-space `$forward` and `$illumfactor`,
including Source's defaults, rather than requiring facial metadata.

## Maps

Includes Source BSP versions 19-21, compressed lumps and embedded ZIP/LZMA paks.
Maps register as scenes, for example `mount://gmod/maps/gm_construct.bsp.scene`.
Opaque world geometry uses spatially grouped native aggregates, translucent
surfaces retain ordinary renderers, and collision uses native physics resources,
including displacement terrain and brush submodels. There is no runtime BSP visibility
system. Water uses mount-specific components for capture views and underwater volume selection.
Displacements use `WorldVertexTransition` to blend their two material layers with
the BSP's painted vertex alpha and optional blend-modulation texture. Compiled
`LightmappedGeneric` materials with `$basetexture2` use this path too.
`func_lod` brushes retain their geometry and authored solidity (`Solid=0` is
solid). Source distance fading is unsupported, so they remain visible.
The missing distance behavior is diagnosed during import.

Mounted maps include an infinite native `PostProcessVolume` with Linear
`Tonemapping`, allowing Sandbox's `DefaultTonemapping` to yield instead of applying
AgX. HDR maps retain Source's default exposure scale limits (0.5-2) and bloom;
LDR maps use fixed exposure without bloom. The first `env_tonemap_controller`,
or the last master controller, supplies the global settings. Direct,
unconditional `logic_auto` OnNewGame/OnMapSpawn/OnMultiNewMap inputs import
SetAutoExposureMin/Max, UseDefaultAutoExposure, SetTonemapRate, SetBloomScale and
UseDefaultBloomScale. These inputs are applied in delay order to bake the initial
look, not replayed as gameplay events. Other runtime/conditional I/O remains
explicitly unsupported.

`trigger_tonemap`, radial `color_correction` and brush
`color_correction_volume` use native post-process volumes. Brush volumes use
their oriented model bounds, not arbitrary Source trigger solids; selection uses
camera position rather than player-origin touch tests. Temporal fades and trigger
filters are not reproduced. Client-selected color-correction controllers remain
inactive without Source's player/controller selection.

Color-correction `.raw` files load lazily through mounted texture resources,
preserving map-pak/search-path precedence. Global lookups are combined with Source's
exclusive-weight suppression, four-lookup limit, weight normalization and identity
remainder. Source's 32-cube RGB tables are resampled into the native grader's
16-cube flattened LUT in linear space. Local LUT overlap follows native volume
selection, not Source's additive multi-lookup blending. Exposure metering/adaptation
and bloom filtering use s&box's native implementations, not Source's exact
histogram or blur kernel. These are native approximations, not pixel-exact
Source post-processing.

Map dependencies resolve through their own pak, then the enclosing loose content
root, then the ordinary mount index. Pak entries keep map-scoped resource paths;
two maps cannot overwrite each other's materials, textures or sounds. Mount-time
map indexing reads headers, entities and pak directories, not render geometry.
Map parsing and static-prop model parsing remain local to scene import. Loaders
retain the native resources referenced by the resulting scene.

Baked lighting includes HDR/LDR lightmap atlases, bumped lightmaps, authored
lightstyle values at time zero, static-prop VHV/PPL lighting and ambient fallback,
leaf ambient probe volumes and indexed native lights. Static prop imports preserve
imported geometry, skins, LODs and reference poses. Six-face skies and separate native
3D skybox scenes retain authored transforms and use native analytic fog. WorldVertexTransition,
water, authored decals and native soundscapes are included. Map and model materials
keep their Source shader identity; baked-lighting variants enable lightmaps or
static-prop lighting on the same shader.
Within each imported scene, world variants are shared by resolved source material,
lightmap page and decal state. Unlit surfaces do not split materials by unused
lightmap pages. Opaque faces using the same effective material share spatial mesh
groups; translucent geometry retains its original grouping for sorting.
Vertex-lit static props share material variants across instances and meshes:
their individual baked colors remain in vertex buffers, not material parameters.
Texel-lit props and ambient-cube material lighting retain their individual bindings,
and material slots with different skin mappings remain distinct.
Baked prop, lightmapped world and unlit variants do not request native light-probe
selection: they set the existing `UsesBakedLightingFromProbe` material attribute
to false, which the renderer honors before probe lookup and per-draw binding.
Baked props retain their imported lighting, dynamic lights, ambient
occlusion and Source environment-map reflections without evaluating native probe
lighting first. Leaf probe volumes remain available to normally lit dynamic models.
Horizontal BSP water retains its authored meshes, UVs and bottom-material faces.
`SourceWater` supplies clipped planar reflection and refraction views through native
command lists, retaining the VMT normal maps, tints and distortion. Refraction depth
reconstructs the submerged path for water absorption. Atmospheric fog applies to the
completed water blend, including planar reflections. Captures honor `$forcecheap`,
`$forceexpensive`, reflection/refraction texture presence and sky reflection flags.
Entity reflections are enabled by default and can be disabled on `SourceWater`.
Compatible coplanar surfaces share a renderer while retaining their materials and UVs.
Only the camera-facing side submits captures; orthographic and custom-projection
cameras retain framebuffer/cubemap fallback.
`SourceWaterMap` retains a pruned BSP tree for exact leaf-water membership.
`SourceWaterVolume` selects native underwater fog from the volume's bottom material,
without approximating the water region with a bounding box.
`SourceUnderwater` draws the bottom material's `$underwateroverlay` inside the same
region. Refract overlays retain their normal maps, scrolling, tint and blur, leaving
later UI undistorted; other overlay shader types are reported as unsupported.
`SourceWaterAudio` plays the authored `Player.AmbientUnderWater` sound while the camera
is submerged and stops it on exit or disable. Sandbox retains ownership of the water
DSP, whose `water.small` preset matches Source's preset 14.

This is not pixel-exact Source water parity. Distance-based cheap-water transitions,
Source's special near-waterline intersection handling, flow maps, base-texture water
and lightmapped water fog remain unsupported. Water in child views, including 3D
skyboxes, retains framebuffer/cubemap fallback rather than recursive planar captures.
Compiled BSP `fluid` solids retain their convex collision shapes as water-tagged
`PhysicsCollider` triggers. Fluids with matching planes and materials share one
trigger. A disabled `BoxCollider` supplies bounds whose top matches the authored
surface, without adding collision or replacing the BSP hulls. Sandbox's existing
water loader uses that box and attaches its own `WaterVolume` for buoyancy and
water effects; the mount does not add a second buoyancy component. The triggers
also feed player swimming. Physics requires the host game's water support;
Sandbox assumes horizontal water and does not apply Source currents or custom
fluid density and damping.

Map fog uses the native `Fog` component, selecting the last master
`env_fog_controller` or the first controller when none is marked as master.
The generic Distance mode imports the signed distance range, primary/secondary colors,
HDR color scale and initial enable state. It uses exponent 2 and the squared, clamped
`fogmaxdensity` as its maximum opacity. Distance uses camera-forward view depth,
matching GMod's DX9 range-fog path, including maps with the unused legacy `fogradial` key.
There is no distance multiplier. As in Source's `BlendPixelFog`, the range factor
is capped at `fogmaxdensity` and then squared before color blending; capping the
squared ramp at the squared density is equivalent. Equal start/end values import as
a one-unit ramp, matching Source's inverse-range fallback. Both constant and directional
endpoint colors are scaled, clipped and converted to linear color during import.
The engine blends directional colors in linear space for each rendered view, so intermediate
colors can differ from Source's gamma-space blend while preserving the imported endpoints.
No engine-specific fog mode is needed. Runtime controller switching/transitions are not imported.
The importer configures the advanced fog controls automatically; enable advanced component properties
to inspect or edit them.
Native `Skybox3D.FogOverride` imports independent `sky_camera` fog, with engine-managed
scale conversion rather than separate Source fog shaders or per-skybox material copies.
Its settings component is disabled so it does not also affect the main world.
The 2D sky texture stays unfogged, matching Source's `Sky_DX9` and `Sky_HDR_DX9` shaders;
sky-camera fog applies to 3D sky geometry, not the sky texture. Water retains its
material-specific underwater absorption; atmosphere and reflection-probe sampling
use native fog and environment-map helpers. `$nofog` uses the native material
flag consistently across shader families.

Leaf ambient loading follows GMod's format selection: version-zero leaves use
their embedded cubes; newer leaves use indexed version-one samples when an index
exists, otherwise legacy per-leaf cubes. Legacy cube and index-table version fields
are not format constraints, but record sizes, sample ranges and finite lighting
remain validated.

Constant direct-light index and weight grids use shared one-texel volume textures,
keyed by their format and packed texel value rather than hashes or retained pixel
buffers. Native normalized-coordinate sampling produces the same values.
Ambient grids, leaf bounds, priorities and all lighting samples are retained.
This avoids spending three texture handles per leaf on maps with thousands of
leaves; the engine's texture-manager limit is a handle count, not a VRAM budget.
Import diagnostics report unique texture counts and their actual payload size.

Entity behavior is restricted to engine components and Doo. Supported brush doors
and buttons use `Sandbox.Mapping.Door` and `Sandbox.Mapping.Button`; supported
Source `OnOpen`, `OnClose` and `OnPressed` outputs become serialized Doo connections
to native door `Open`, `Close` and `Toggle` methods. Repeated outputs, named and
wildcard targets, `!self`/`!caller`, delays and available user context are preserved.
Unsupported events, receivers, parameters and finite fire counts are diagnosed
and omitted. There are no custom relays, generic trigger scripts, Source logic
simulation, light switches or animated entity components. Unsupported moving
brushes retain their static geometry/collision with a diagnostic, not approximated
simulation. Native spawn points, static props, physical props and ragdolls are
imported; living NPCs, enemies and particles are not.
Entity loaders are selected by the case-insensitive class-name switch in
`FindEntityLoader`, independently of the current game or editor context.
Register new class names and aliases there. Loaders own their spawn angles, visibility,
solidity and native interactions; shared builders handle geometry and physics
without identifying entity classes.
Entity angles follow Source's key order and legacy single-angle rules. Brush
spawn-angle resets preserve GMod's parented/directed-gib breakable exceptions.
Rotating door axes follow Source's Euler-angle addition, including pitched and
rolled doors. Map ragdoll `angleOverride` values are absolute map-space physics-body
rotations, applied in authored order with parent-body bind offsets. These poses
are stored on native bone objects and survive scene serialization; they are not
extra entity rotations or runtime-only renderer overrides.

WAV/MP3 files are mounted directly; surface sound
events retain their authored wave variants, volume, pitch and attenuation.
Footstep slots use separate events with Source's running-step volume (0.5 by
default, 0.55 for dirt, 0.7 for vents), rather than full sound-script volume.
Surfaces sharing a sound script and footstep gain share the same event.
The engine player controller owns footstep and landing playback and volume scaling.
Impact, scrape, break and ambient events keep their authored volume.
Every sound level falls off on Source's own gain curve (inverse distance referenced to
36 units at 60 dB, 4 dB extra loss per 1200 units, compressed towards full volume close by),
sampled into the event's falloff curve out to where the gain reaches 0.001.
`attenuation` and `CompatibilityAttenuation` values become sound levels through
`50 + 20 / attenuation`; GoldSrc's linear compatibility curve, which Counter-Strike: Source
weapons select, is not reproduced.
Unavailable wave references are reported once per sound during mounting.
Available variants remain usable; a sound with no available waves leaves its
surface slot empty without preventing the physical surface from loading.
Source decal-script groups use native projected decal definitions and surface
impact prefabs, including game-material translations, atlas subrects and
multiply/alpha/additive blending. Only script-referenced decal materials are
parsed during mounting, not every VMT. Group variants currently use equal
selection chances even when Source specifies unequal weights. Unsupported material
shaders and render targets report an error and use the error material.
`_rt_FullFrameFB` uses the native framebuffer copy; `_rt_Camera` and `_rt_FullScreen`
bind a shared, initially black render target. No live camera entities, feeds or
captured UI views are imported.
Refraction destination-alpha masks are not reproduced.
Every model also registers an opt-in `<path>.animated` loader (developer-only in browsing)
that imports its sequences, including `$includemodel` animation files, as lazily decoded
clips with sequence data: activities, anim events, blend grids, autolayers, pose parameters
and sequence weight lists as bone masks. The plain loader imports no animation.
There is no flex-controller/rule evaluation.

Source-shaped model data is stored with `ModelBuilder.WithData` under the GameData names
`sequence_data_list`, `pose_layer_data_list`, `pose_parameter_data_list`, `bone_surface_list`
and `weapon_data_list` (`Resource/ModelData.cs`). The engine defines no types for it: game code
declares its own classes with the same `[ModelEditor.GameData]` names and member names and reads
them with `Model.GetData<T>()`.

Weapon scripts (`scripts/weapon_*.txt`, or ICE-encrypted `.ctx` for mounts that know the
game's key, `css` among them; a `.txt` takes precedence) become `weapon_data` on their
view and world models and `scripts/weapon_*.<ext>.json` text resources. Damage comes from
the script's `Damage` or else `cfg/skill.cfg`. Each ammo type a script names becomes `scripts/ammo/<type>.ammo` with its carry limit from `cfg/skill.cfg`'s
`sk_max_*`, taking precedence over game-specific defaults. Counter-Strike: Source weapon scripts supply ammo identifiers and clip sizes, but its reserve limits are compiled `ammo_*_max` defaults, absent from its configuration and weapon scripts. `CssMount.cs` supplies those verified defaults as a fallback. Counter-Strike keys fill `CycleTime`, `Bullets`,
`FullAuto`, `Range`, `RangeModifier`, `WeaponType`, `Team`, `PlayerAnimationExtension`,
`BuiltRightHanded`, `SilencerModel`, `MuzzleFlashStyle` and `MuzzleFlashScale`; they stay null for games without them. Accuracy,
recoil, economy and movement keys are not imported.
A script's `TextureData` "weapon" icon becomes `scripts/weapon_*.<ext>.icon.vtex` (the `Icon` member): the scheme font's glyph
(logical font to family through `resource/clientscheme.res`, family to a `resource/` font file), or a `"file"` material region, white on transparent.
Sound events come from
`scripts/game_sounds_manifest.txt`; every PCF's systems register as particle prefabs.
Speech data becomes JSON text resources for game code to read: `scripts/sentences.txt.json`,
`scripts/talker/response_rules.txt.json` and Counter-Strike's `botchatter.db.json` (chatter
phrases with their radio fallback and count/place-qualified `sound/bot` waves; location phrases are left out).

### Shaders

Shader entry points use the normalized Source names: `vertexlitgeneric`,
`lightmappedgeneric`, `unlitgeneric`, `worldvertextransition`, `vortwarp`,
`monitorscreen`, `eyes`, `eyerefract`, `teeth`, `unlittwotexture`, `modulate`,
`refract`, `core`, `aftershock`, `volumeclouds`, `water` and `worldtwotextureblend`. Related entry points
share stage includes rather than duplicating shader implementations. Shader identity
selects transition, VortWarp, monitor, eye and refraction-family code at compile time.

Generic materials use s&box's native lighting model, material combiner, clustered
lights, attenuation, cookies, shadows, ambient/reflection probes, decals, depth/tool
passes and fog. There is no replacement Source per-light BRDF or baked-light loop.
Source Phong exponents and masks map to perceptual roughness; boost and tint scale
bounded native dielectric reflectance. Source Half-Lambert, diffuse/specular warps
and rim-light overrides do not replace native lighting and are reported at trace
level when authored. This is native lighting integration, not exact Source BRDF parity.
Authored cubemaps retain their Source masks, tints and environment Fresnel.
Without `$phong` the generic shader uses one roughness for native direct specular, which
Source does not have, and for scene-probe reflections. Authored environment textures are
sampled separately, so their surfaces use roughness 1. Lit model `env_cubemap` surfaces derive
roughness from their brightest linear `$envmaptint` component: at most 0.25 is matte (1), at
least 0.75 keeps a sharp mirror (0), with a smoothstep between. Dim sheens such as Counter-Strike
player cloth no longer get a full-surface sun highlight, while chrome and glass keep crisp
reflections. Phong, unlit and lightmapped surfaces keep sharp probe reflections.
Self-illumination Fresnel uses the vertex normal and Source's scale/bias/brightness
convention. Imported BSP lightmaps and static-prop lighting remain baked inputs;
additional clustered lights use the native light evaluator without adding the baked
sun or indexed baked lights a second time.
Baked surfaces clear the native initializer's white diffuse term before accumulating
clustered lights, so an empty light list does not brighten authored lightmaps.
Generic surfaces transform Source normal-map vectors directly through the imported
tangent basis. The native `TransformNormal` helper's green-channel flip is for
native normal maps and must not be applied to unconverted Source textures.
Water and Refract use the same Source tangent convention. Native water-probe
lookups retain the surface's world/screen position and tangent frame. Refract's
normal alpha controls reflection strength when an envmap is present, not opacity;
without an envmap it retains its translucency behavior.

Eyes and EyeRefract use `ShadingModelStandard` with the authored mesh normals and
native corneal specular lighting. Their imported neutral iris projection follows
`R_StudioEyeballPosition` with eye movement disabled: negate the stored forward
axis, apply the doubled `zoffset`, then rebuild the right/up projection axes.
Iris sampling uses a fixed clamped sampler so projected coordinates outside the
iris texture cannot repeat pupils at the corners of the eye.
The projection remains fixed to the eye mesh; runtime gaze targets and Source's
generated glint texture are not implemented.

WorldTwoTextureBlend keeps its linear detail samples, alpha-driven replacement
and detail-alpha mask equations, diffuse-bump albedo substitution, shared
detail/bump coordinates, unsquared radiosity-basis weights and fixed lightmap
overbright factor. It does not acquire generic environment or Phong parameters.
LightmappedGeneric and WorldVertexTransition seamless mapping projects world-space
`zy`, `xz` and `xy` coordinates with interpolated squared vertex-normal weights,
matching Source's PC path rather than the sharpened Xbox weights. Base transforms
and vertex RGB do not affect that projection. Unavailable Source detail/bump
combinations are rejected explicitly; WorldTwoTextureBlend seamless mapping is
still unsupported.
WorldVertexTransition applies shared `$color * $color2` modulation after blending
both base layers and detail. Blend-modulation endpoints are clamped before
`smoothstep`; zero-width ranges use a finite threshold instead.

This is not complete shader parity. Generic local lighting is evaluated per pixel
rather than Source's non-bumped vertex-lighting path. Native light attenuation and
probe filtering follow s&box. EyeRefract optics and the existing effect shaders
remain partial implementations; Source's destination-alpha refraction masks are not
available through the current mount implementation.

Models use the authored MDL bind pose, except non-static rigid props with one
physics body and no constraints. These read frame zero of local animation zero,
matching `studiomdl`'s `ConvertToWorldSpace`/`CalcBoneTransforms` collision reference.
Non-static models without physics instead read frame zero of sequence zero's
animation, so unanimated dynamic props retain their authored initial appearance
rather than their construction pose. Initial sequences requiring animation blends
or layers retain the bind pose with a trace diagnostic.
Each bone's reference transform is composed with its authored `poseToBone` matrix;
vertices use Source's weighted skin matrices, not a shared root correction.
Mesh geometry, normals, tangents, morphs and the skeleton use that pose together.
Independent roots and child bones retain their individual transforms.
The reference pose itself reads no animation clips (see `.animated` above). Additive, IK,
root-motion and reparented references retain the bind pose with a trace diagnostic.
PHY coordinates and ragdoll bind poses remain unchanged.
Geometry-free animation helpers retain empty geometry. Studiomdl's untouched
empty hull-bound sentinel is represented as a native point bound, not an inverted
box; other invalid bounds still fail.
Ragdolls retain the existing importer's native physics limitations, including
approximate ball-joint swing cones, angular friction and collision exclusions.
Model import diagnostics are reported at trace verbosity. Map limitations,
unsupported connections and missing scene dependencies produce warnings.
Errors remain visible.

## Gameplay

`Gameplay` holds the games' weapons, NPCs, pickups and effects as ordinary components: `Gameplay/Source`
for what they share (`SourceWeapon` and its bullet, projectile and melee kinds, `SourceCombatNpc`,
`SourceCreatureNpc`, viewmodels and world models, mounted models and effects, speech), `Gameplay/Hl2`
and `Gameplay/Css` for each game's own. They run on the engine's `BaseCombatWeapon`, `BaseNpc`,
`BaseCombatNpc` and inventory components and load their art from the mount at runtime.

### Entity prefabs

Each game mount builds its entities as prefabs from the installed game when they're first loaded, such as
`mount://hl2/entities/npcs/combine_soldier.prefab`:

| Path | What |
|------|------|
| `entities/weapons/<name>.prefab` | Weapons, tagged `weapon` |
| `entities/npcs/<name>.prefab` | NPCs, tagged `npc` |
| `entities/items/<name>.prefab` | Pickups and chargers, tagged `item` |
| `entities/parts/<name>_vm.prefab`, `<name>_wm.prefab` | A weapon's viewmodel and world model |
| `entities/effects/<name>.prefab` | Explosions and impacts the entities spawn |

`Gameplay/Entities/SourceEntities.Table.cs` lists them: a template per kind in `SourceEntities.Templates.cs`,
and per entity only what differs from it. An entity is registered when its models (or their fallbacks)
are in the game's index and the entities it can't work without are too (a weapon's parts, a nested weapon,
the headcrab a zombie drops); optional ones, such as one of an NPC's weapon choices, are left out of
its prefab instead. Nothing is written to disk.

A game finds the spawnable ones by tag (`SourceEntities.NpcTag`, `WeaponTag`, `ItemTag`) without
loading them:

```csharp
var npcs = Sandbox.Mounting.Directory.Get( "hl2" ).GetAll( ResourceType.PrefabFile ).Where( x => x.Tags.Contains( "npc" ) );
```

Loading one (`PrefabFile.Load`, `GameObject.GetPrefab`) builds it. Its metadata, read with
`PrefabFile.GetMetadata`, holds `Title`, `Group` (the game's title), `Description` (the Episode, for
Episode NPCs) and `PreviewModel` and `PreviewSequence`, a loadable model path and sequence for an icon.
The mount's own code finds another entity by name with `SourceEntities.Prefab( mount, name )`.

### What a game provides

- Players with a component implementing `Component.IDamageable`: NPCs aim at, hurt and heal it through
  `Health` and `MaxHealth`, and chargers and batteries fill `Armour` and `MaxArmour` when it implements them.
  It sits beside the engine's `PlayerController`, which is how NPCs tell players from other targets.
- An inventory derived from `BaseInventoryComponent` on the player, for picking weapons up and switching
  between them; weapons fire from the engine's weapon input or a game's own controls.
- Optionally its own first-person arms: the mount's `source1_viewmodel_hands` console variable
  (`SourceViewModel.Hands`), which exists while a Source mount is mounted, so a game sets it by name once it
  appears. It takes a plain `.vmdl` path in the game's files with the citizen first-person arms skeleton; anything
  else, or an empty value, keeps the viewmodels' own gloves.

A prefab a client hasn't got is built on the client from its own installed game, so clients need the game mounted
before the host's entities arrive: a reference to a mount prefab in an unmounted game resolves to nothing.
The mount's components do nothing random and add nothing in `OnValidate`, which runs wherever an object is read or
cloned, prefab builds included. What a spawned entity picks for itself happens on the host as it's network spawned
(`Component.INetworkSpawn`), or in `OnStart` when it never is: the engine's `BaseCombatNpc` chooses its starting weapon
from `WeaponChoices`, and `SourceNpcSpawnChoice`, placed before it, picks a model from `ModelChoices` and a weapon its
`Team` may carry, unless the game handed the NPC a weapon first.

## Validation

Build the managed engine and compile the shaders before checking mounted maps.
Validate from a project without the staging importer or shader assets, using
`mount://gmod/maps/gm_construct.bsp.scene` and other installed maps. Check material
bindings, collision, authored prop LODs, 3D skyboxes and water in the editor.
