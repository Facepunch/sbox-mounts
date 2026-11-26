# Archived mission experiment

These `.cs.txt` files preserve the former heist interpreter, spawn tracking and NPC orders
for research. They are deliberately outside compiled source and are **not a working addon**.
The `payday_mission` console option no longer enables them in the core mount.

Reintegration would require an explicit optional addon using the current asset-state API.
The old `PaydayMissionUnit` and `PaydayMissionSync` implementations have been replaced by
`PaydayUnitState` and `PaydayMapState`; their mission interaction interfaces were removed.
Compatibility aliases retain the old component type names for serialized asset-state data.

Core `Formats/MissionData.cs` remains a read-only parser used for sound-name harvesting and
source-format verification. Ordinary scene construction no longer parses mission graphs or
attaches mission interpreters. Unit damage, geometry/collision changes and sequence sounds
remain in the core mount.
