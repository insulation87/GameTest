# Pickup magnet

`PlayerShooter.Awake` adds `PickupMagnet` when it is absent. No scene or prefab
changes are needed. To save custom settings, add Pickup Magnet to the player in
Edit mode and adjust its Inspector fields.

Defaults: 3 m attraction radius, 3 m/s starting speed, 14 m/s maximum speed,
24 m/s² acceleration, and 0.35 m final collection distance. The destination is
the player's body collider center, so it also follows crouching.

Weapons, health packs, experience, grenades and floor keys share their existing
contact collection logic with the magnet. The magnet pauses weapon hovering
while attracting, then resumes hovering at the current position if interrupted.
Dynamic pickups temporarily become kinematic; their gravity and body mode are
restored when attraction ends. Lifetime timers remain active.

Solid obstacles block attraction. Trigger volumes and other pickups do not.
Weapons remain available when the inventory is full. Other stackable items can
still be collected. Disabled/dead players cannot attract items, and normal game
pause stops movement. Duplicate pickup callbacks cannot award an item twice.
