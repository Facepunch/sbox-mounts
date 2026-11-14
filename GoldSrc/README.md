# GoldSrc map import

## Map loader

`Code/Resource/Map.cs` orchestrates the import. Geometry, textures, materials, lightmaps,
rendering, physics, brush entities, pathing and sounds live in focused partials; `Code/Bsp/`
owns format parsing. Runtime entity components live in `Code/Components/`.
The editor registers these addon types. Packaged clients still need engine-side
registration of mount component types before maps using them can load correctly.
Fixed-size BSP lumps are decoded through `ByteStream`, and entity vectors and
lights are parsed without temporary strings or token arrays.

Only textures referenced by rendered faces are resolved from WADs, decoded and uploaded.
Lit and fullbright materials share constant masks instead of allocating a mask per texture.
Missing WAD textures warn and use opaque black. Geometry reuses its face-vertex buffer,
keeps the existing triangulator and gives each render mesh its own tight bounds.

Opaque static world and brush geometry is batched across objects by material and tint
into `AggregateRenderer` models. World fragments follow the BSP's existing leaf references
without duplicating faces or introducing an arbitrary spatial grid. Each fragment retains
its own bounds and transform for native GPU culling, and batches respect the engine's
fragment limit. Translucent/liquid surfaces keep regular renderers; doors, buttons, trains,
breakables, pushables and animated models are not baked into the static batches.

World and brush triangle collision lives in named `PhysicsGroupDescription` resources,
assigned to `PhysicsCollider`, rather than duplicated in render models. Per-triangle
surface indices, sky collision, liquid exclusion and moving-brush pivots are preserved.
Trigger volumes use cached box-shaped physics resources and retain their trigger behavior.
Generated models and physics resources are retained by the map loader so their references
can be resolved when the serialized scene is instantiated.

Map vertices use attributed `VertexLayout` fields and generic buffer creation. Lightstyle
indices travel as exact float values matching the local shader's vertex input; reimport
existing maps to regenerate their vertex buffers with this layout.

## Surface audio

`sound/materials.txt` supplies concrete, metal, dirt, vent, grate, tile, slosh, wood,
computer, glass and flesh classifications, plus Counter-Strike's snow/grass extensions.
Lookup follows GoldSrc's animation/special
texture prefixes and case-insensitive 12-character names; unlisted textures use concrete.
Conflicting duplicate keys are warned about and use the original swap-sort/binary-search
selection, including Day of Defeat's conflicting entries.
Mounted `.surface` and `.sound` resources use the original footstep and impact WAV pools,
including separate left/right variants and the occasional fifth tile step. Jump/landing
slots use the matching step pool for Half-Life, Counter-Strike and Ricochet.
Team Fortress Classic uses its original jump grunt. Deathmatch Classic has no ordinary
footsteps or landing steps and uses its jump grunt instead. Day of Defeat uses its own
material alphabet, wood/gravel/snow footsteps, running volumes, and jump/landing WAVs.
Its grass is not Half-Life's computer material, and heavy metal is not ceiling tile.
Physical impacts use the original debris collision pools at normal pitch and attenuation
1.0, not the randomized pitch of breakable damage sounds. Ceiling tile has no debris
impact sound. Break sounds use the original WAVs, volume range and pitch range.
Generic scraping is silent: GoldSrc has no general surface scrape definitions.
Sound falloff is GoldSrc's linear attenuation.
Grass uses the default GoldSrc footstep pool; snow uses Counter-Strike's snow footsteps.
Their bullet/physical impacts use the generic GoldSrc pool.
Missing scripts/WAVs produce warnings and fall back to concrete classification or
remaining variants; slots without any available original WAV remain silent rather than
playing unrelated Source 2 sounds. Resource loading failures are explicit errors.

World and brush collision meshes carry a surface index for each triangle. Lit, unlit,
liquid, WAD and studio-model materials also expose their mounted physics surface.
`func_breakable` and `func_pushable` honor the `material` override, including ceiling
tile, computer, rocks and unbreakable glass. Liquid collision behavior is unchanged.
Studio collision boxes default to concrete: MDLs do not encode an entity's flesh-sound
flag, and props are not indiscriminately treated as flesh. These bindings expose audio
to normal player/weapon/physics consumers; they do not add breakable or NPC simulation.
Footstep timing, walking/crouching volume, water-depth/ladder sounds, damage-dependent
break volume and exact discrete pitch probabilities still depend on gameplay code;
the standard engine consumers do not reproduce GoldSrc movement or damage logic.
Opposing Force and Condition Zero use their game-first assets and shared HL/CS rules.

Game/HD definitions and sounds take priority, with targeted `valve` fallback for shared
surface assets in expansions/mods. This does not add Valve maps to their browsers.
Surfaces explicitly inherit a mounted audio-only base and contain no particle effect
prefabs or particle lists, preventing default weapon particle fallback. Existing imported
scenes need reimporting to acquire the collision/material bindings.

## Decals

`decals.wad` is mounted by name with game/HD overrides and per-texture Valve fallback.
Original `{shot1`–`{shot5` impacts, `{break1`–`{break3` glass marks and `{bproof1`
bulletproof-glass marks use decal-only prefabs, never particle effects. Impact decals
are transient and use the engine's decal limit; placed `infodecal` marks are persistent.
Impact prefabs are built in a disabled temporary scene; only spawned instances activate their effects.
Placed marks use the standard `Decal` component, without a custom runtime component.
Triggered/named infodecals are omitted with a warning rather than revealed prematurely.
Marks attach to world/brush receivers and follow moving brushes. Missing assets or
unmatched placement produce explicit warnings.

Classic gradient decals use palette entry 255 as tint and the pixel index as opacity,
not the wall texture's index-255 cutout rule. Blue-base full-color decals retain that
cutout rule. Colors follow GoldSrc's texture-gamma response and use anisotropic filtering.
Decals are composited before baked/emissive shading so they affect lightmapped world
geometry and fullbright models without enabling indirect probe lighting on the world.
Like the Source1 mount shader, a mount-local shading path composites lighting without
applying decals a second time. No core shader modifications are required.
Reimport existing scenes to acquire the decal resources and placed marks.

## Lighting

World and brush surfaces retain their original RGB lightmaps and animated lightstyles.
Each map instance owns 64 lightstyles. Patterns advance in discrete 10 Hz steps,
loop at their own length and retain their phase when switched. Empty styles have
intensity 256; pattern letters contribute `(letter - 'a') * 22`. Custom patterns
use at most 63 characters. Named `light`, `light_spot` and `light_environment`
entities support start-off, custom patterns and on/off/toggle inputs for styles
32–63. Shared style IDs use the last write, and removing a light leaves its style
unchanged. Inputs do not fire the light's targets. Pattern and switch state are
serialized and host-synchronized when the map objects are networked.
Renderer attributes keep instances independent without changing shared materials.
`goldsrc_generic` applies the bake as emission and disables indirect diffuse/specular
through material ambient occlusion, so model probes do not brighten world geometry.
GoldSrc and Quake opaque baked world surfaces apply SSAO after conversion to linear
space, using the engine's albedo-dependent diffuse AO compensation. Fullbright texels
and translucent surfaces are not darkened. Models retain the engine's standard SSAO path.
Surface textures use anisotropic filtering, not nearest filtering.
Model normals are normalized per pixel before probe lighting and depth-normal output,
avoiding interpolation-induced darkening and invalid normals in SSAO.
The GoldSrc skybox is visual only: skybox indirect lighting is disabled so it does not
add ambient or reflection lighting on top of the imported model probes.
Liquid surfaces retain their existing emissive rendering.

`LightStyleProbes` supplies the engine's `LightProbeVolume` with instance-owned textures.
Probe samples descend the BSP tree to the first lightmapped surface below them,
retaining each style's RGB contribution. A volume updates only when one of its
styles changes brightness, so flickering and switched lights affect models too.
The six native ambient layers
approximate studio ambient plus modified hemispherical shading (direct fraction 0.9,
Lambert 1.4953241). They preserve RGB color without a room-average brightness boost.
Model textures also use the default `texgamma / gamma = 2.0 / 2.5` palette response,
matching the world shader rather than compensating with brighter probes.

The grid targets 64-unit spacing, with at most 24 volumes and 1,024 probes per volume.
Six RGBA half-float layers cost 48 bytes per probe: at most 1.125 MiB of texture payload
per map, excluding GPU allocation overhead. Raw style samples are serialized;
runtime textures and probe helpers are recreated for each instance.
Each volume also gets a box-projected realtime `EnvmapProbe` covering
the same bounds, captured once on enable at 128x128 with multiple bounces disabled.
The capture origin uses the nearest valid grid sample to the volume center, avoiding
solid space. A volume with no valid sample logs a warning and omits its reflection probe.
Cubemap color textures add approximately 1 MiB per volume (at most 24 MiB), excluding
allocation overhead and render scratch resources. They provide metallic/specular
reflections alongside the existing diffuse probes, not skybox indirect lighting.
There are no DDGI irradiance/distance atlases or periodic/per-frame captures.
Import work is bounded to 24,576 floor traces plus short BSP
point lookups to move probes out of solid space.

This is an approximation: sparse-grid interpolation can blend across room boundaries,
studio directional shading is represented by six cube directions, lightmap layers are
filtered before they are combined, and reflections remain startup captures. It does not reproduce
GoldSrc's per-entity inverted lighting, skylight override, or dynamic-light accumulation.
Chrome textures retain diffuse lighting instead of becoming PBR metal requiring a
reflection capture. Existing scenes need reimporting; old serialized DDGI components
are not migrated.
