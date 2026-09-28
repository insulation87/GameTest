# NPC room boundaries

Room footprints are extracted from `Assets/MedievalDungeon/Data/dungeon.json`
into `Resources/DungeonRoomLayout.json` (48 rooms, including the boss room).
`DungeonRoomBoundary.GetOrCreate` matches Room markers by room ID, such as
`L1_R01_Hall`. Bounds follow the marker's translation, rotation and scale.
The map's 14, 18 and 22 metre half-widths are retained.

NPCSpawner binds each NPC to its spawn room. Both room-triggered and global
spawning validate the sampled NavMesh point against the room and agent radius.
Corridor positions no longer count as entering a room.

EnemyAI only accepts complete paths whose corners remain inside its home room,
with clearance for the body and wall inset. A route that exits into a corridor
and returns to the room is rejected. Automatic replanning and automatic off-mesh
link traversal are disabled so they cannot bypass this validation. A player
outside the room is not pursued; returning to the room resumes pursuit.
Existing attack ranges and line-of-sight rules remain in effect.

If avoidance or external movement pushes an NPC outside its room, it is restored
to its last valid position. NPCs placed directly in the scene find their containing
room at startup. An NPC without a known room stays stationary and reports a warning.

For a new room layout, add a Dungeon Room Boundary component to its Room marker
and configure Half Size, Wall Inset and the vertical range. Existing generated
layout dimensions can be updated in the compact JSON resource. Select the marker
to see its boundary gizmo. No global navigation blockers are added to doorways.
