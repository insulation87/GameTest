# Blonde Girl character

Complete imported, rigged character model; no Unity primitive body parts are generated.

- `BlondeGirl.fbx`: original **Casual_Female.fbx**, Quaternius, Ultimate Animated Character Pack.
- `BlondeGirlModel.prefab`: full character with a single skinned mesh, skeleton, six materials and animations. Feet at local Y = 0; standing height approximately 2 Unity units; faces +Z.
- `BlondeGirlArms.prefab` / `FirstPersonArms.asset`: first-person arm triangles extracted from the same character mesh and posed for the existing weapon mount.
- `Crouch.anim`, `CrouchWalk.anim`, `Slide.anim`, `Fall.anim`: additional skeletal clips authored for this game's controls.
- Original FBX includes Idle, Walk, Run, Jump, Death and other animation clips. Import as Legacy, with Idle/Walk/Run looping; preserve the supplied `.meta` file.
- Material changes: blonde hair, peach skin, dark eyes, blue shirt, navy shorts. Materials target the project's Universal Render Pipeline.

This earlier chibi model is retained as a backup. The active character now loads from `Characters/AnimeGirl/AnimeGirlModel`, with more realistic proportions and a Japanese anime appearance.

## Source and license

Author: Quaternius — https://quaternius.com/packs/ultimatedanimatedcharacter.html

Official download folder: https://drive.google.com/drive/folders/1sNi1AfenfPRrvRt5yfaj5QMMd6KKcUJ5

Original FBX: https://drive.google.com/file/d/1AYI79-CtvncVV0DW0Mg6_9hRKpX8Y1Pe/view

License: CC0 1.0 Universal / Public Domain Dedication. The author's original license is included as `LICENSE.txt`. Original FBX downloaded 2026-09-20. Recolored materials, first-person mesh extraction, extra poses and Unity integration are local modifications.

This is a stylized low-poly character with a large head and swept-back long blonde hair. It is not the earlier primitive-based twin-tail/dress prototype.
