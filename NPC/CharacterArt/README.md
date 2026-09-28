# Medieval NPC and boss appearances

Enemies now use naturally proportioned, low-poly medieval armor rather than
school uniforms: enclosed great helms, breastplates, pauldrons, bracers, greaves,
leather gloves, colored surcoats, swords, heraldic shields and crossbows.
The boss has dark heavy armor, a crimson surcoat, gold trim and a crown.
Lineup.png shows the boss at left, two crossbowman variants, and the sword guard.

## Level and combat rules

EnemyCombatRules is the single source of truth:
damage = 1 + (max(1, level) - 1) * 0.2.

| Level | Damage | Equipment |
| --- | --- | --- |
| 1 | 1 | Sword and shield |
| 20 | 4.8 | Sword and shield |
| 21 | 5 | Crossbow; sword/shield at close range |
| 100 (boss) | 20.8 | Sword/shield, crossbow, enraged radial bolts |

Levels 1-20 cannot launch ranged attacks. Level 21 and above use ranged attacks
within 12 units when line of sight is clear, switching to melee within 1.5 units.
These ranges and attack intervals are configurable on EnemyAI. Damage is not
randomized; old serialized random-attack and boss-damage fields are retained only
for compatibility and no longer override the level formula.
Boss health and its half-health enraged radial attack pattern are unchanged.

Attacks have a 0.28-second windup, then recheck distance, target life and line of
sight. Bolts originate at the equipped crossbow muzzle and travel through the
world. Swept sphere collision prevents tunneling through thin walls, ignores
the shooter, and consumes a bolt on its first hit. Scenery/other enemies block
bolts; other enemies do not take friendly-fire damage. Shields are appearance
equipment, not a new blocking/armor-damage mechanic.

## Assets and animation

The source skeleton and natural proportions are retained from the existing CC0
Sendagaya Shino / VRoid rig. Faces/hair are hidden by closed helmets; original
ribbon/hair geometry is removed from the copied under-armor mesh. Armor and
equipment are authored meshes, not Unity primitive body parts. The player
appearance and source assets are not modified.

Generated visual-only prefabs live in NPC/Resources/Characters/Enemies.
Npc1 is the level 1-20 guard; Npc2 and Npc3 are level 21-50 and level 51+ variants.
Boss classification uses BossController. EnemyCharacterVisual fits the visual to
the original collider, preserves navigation and hides the root placeholder.
Sword/Shield/Crossbow objects are attached to hand bones. Idle, Walk, Run, Attack,
Shoot and Cast are skeletal clips. Enemy death still uses the existing immediate
removal and loot behavior.

Keep the player AnimeGirl source meshes/animation assets and AnimeToon shader:
the enemy prefabs share these dependencies. Material variants and new armor
meshes do not overwrite player materials.

## Rebuild and validation

Unity menus:
- Tools > Character > Rebuild NPC and Boss Appearances
- Tools > Character > Validate Enemy Combat Rules

EnemyAppearanceBuilder and MedievalEquipmentBuilder rebuild assets while
preserving GUIDs. Previews are rendered in an isolated preview scene.
Runtime/editor scripts compile with no errors. Isolated Unity Play Mode smoke
tests exercise the actual EnemyAI, EnemyProjectile and EnemyCombatRules with
minimal player/health/visual test collaborators: level boundaries and increments,
melee range recheck, ranged gate, direct projectile hit, owner exclusion,
single-hit damage, thin-wall blocking and line-of-sight checks.
This is not a full gameplay test of the main dungeon scene; navigation, animation
transitions and per-weapon alignment should still be checked there.

## License

Source provenance and license: ../../Player/CharacterArt/README.md.
Original underlying rig/body: Sendagaya Shino, VRoid Project / pixiv Inc., CC0.
Local additions: medieval armor/equipment meshes, palettes, combat clips and
level-based melee/ranged integration.
