# Equipment appearance

Equipment now uses a 0.0125-unit block grid, square panels and faceted geometry,
with a limited-color, three-tone shader. Inventory projections snap to a pixel grid.
Held weapons always use this set, including scenes saved with the old FBX option.

The default equipment set is generated locally from mesh geometry. No downloads,
scene edits, prefab assignments or Editor-only asset lookups are required.

- Ten character weapons: war hammer, sword and shield, repeating crossbow, light staff,
  paired daggers, fire staff, arcane focus, frost staff, scythe and longbow.
- Four armor silhouettes: helmet, chest armor, pants and shoes.
- Health pack: red case, white medical panels, latches and handle.
- Treasure chest and fragments retain their existing mesh definitions.

Legacy gun mesh IDs resolve to character weapons for save compatibility.
See [Character equipment](CharacterEquipment.md) for roles, attacks, levels and backpack controls.

`ItemAppearance.cs` supplies shared, cached meshes for held weapons and loot.
`Resources/ItemSurface.shader` provides a packaged, vertex-colored surface for
URP and Built-in. Each model uses a single renderer and has no visual colliders.
`ItemIconGraphic.cs` projects the same geometry into inventory and hotbar icons,
without creating cameras or render textures during gameplay.

`PlayerShooter` enables the new appearance by default. The existing Weapon Model
Position, Rotation and Scale settings still control the first-person placement.
The legacy Use New Weapon Appearance field is migrated to the pixel set when equipping.
Loot and inventory continue to use the new equipment set.

Health pickup physics, inventory stacking and right-click healing are retained.
Weapon stats, quality colors, purchases, selling and slot selection are retained.
