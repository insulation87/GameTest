# Equipment appearance

The default equipment set is generated locally from mesh geometry. No downloads,
scene edits, prefab assignments or Editor-only asset lookups are required.

- Rifle: steel receiver, vented handguard, magazine and shoulder stock.
- SMG: short teal receiver, compact stock and straight magazine.
- Shotgun: wood furniture, ribbed pump, twin tubes and spare shells.
- RPG: olive launch tube, flared rear, warhead and raised sight.
- Pistol: silver slide, short barrel and textured grip.
- Sniper rifle: long barrel, scope with blue lenses and bipod.
- Light machine gun: box magazine, ammunition belt, carry handle and bipod.
- Health pack: red case, white medical panels, latches and handle.

`ItemAppearance.cs` supplies shared, cached meshes for held weapons and loot.
`Resources/ItemSurface.shader` provides a packaged, vertex-colored surface for
URP and Built-in. Each model uses a single renderer and has no visual colliders.
`ItemIconGraphic.cs` projects the same geometry into inventory and hotbar icons,
without creating cameras or render textures during gameplay.

`PlayerShooter` enables the new appearance by default. The existing Weapon Model
Position, Rotation and Scale settings still control the first-person placement.
Disable Use New Weapon Appearance to use the previously assigned held models.
Loot and inventory continue to use the new equipment set.

Health pickup physics, inventory stacking and right-click healing are retained.
Weapon stats, quality colors, purchases, selling and slot selection are retained.
