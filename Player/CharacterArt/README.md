# Blonde anime girl

The default in-game appearance now comes from `PixelArt/PixelCharacterFactory.cs`:
ten selectable voxel adventurers with stepped animation, role-specific palettes,
headgear and matching first-person sleeve colors. See `../Characters.md` for skills.
The VRoid assets and rebuild workflow below remain available as legacy art sources.

The active character uses a full rigged VRoid mesh with natural anime proportions.
The original head and limb proportions are preserved; the model is scaled uniformly
to fit the existing two-unit player collider. It is not assembled from primitives.

## Assets

- Source: `SendagayaShino.vrm.bytes` (original VRM/GLB data, retained for rebuilding).
- Game prefab: `../Resources/Characters/AnimeGirl/AnimeGirlModel.prefab`.
- First-person prefab: `../Resources/Characters/AnimeGirl/AnimeGirlArms.prefab`.
- Appearance preview: `Preview.png` (rendered from the generated idle clip).
- Materials: `AnimeToon.shader` provides the texture-preserving gold hair recolor
  and an anime shading pass compatible with URP. Textures are extracted unchanged.
- Animation: nine baked skeletal clips for idle, walk, run, jump, fall, crouch,
  crouch-walk, slide and death, authored for this skeleton. The face retains a blink
  blend shape. The combat outfit combines the uniform top with opaque navy athletic
  shorts, using the original weighted body geometry, to accommodate crouching and sliding.

`AnimeCharacterVisual` in `Player.cs` loads the full character automatically;
`PlayerShooter` loads its first-person arms. Left Ctrl controls crouch and sliding;
the first-person camera keeps its existing +0.7 local-forward offset.

To regenerate assets, use **Tools > Character > Rebuild Blonde Anime Girl** in Unity.
The builder is scoped to this documented VRoid source, not a general-purpose VRM
importer. It preserves existing output GUIDs when rebuilding. Previews are written
to the project's `Temp/AnimeGirlReview` folder. No VRM runtime package is required.

The generated prefabs, material references and serialized animation poses were
checked in an isolated Unity 6000.3.10f1 editor, with rendered pose previews.
The project scripts compile successfully. Full in-scene gameplay and per-weapon
first-person grip alignment still need a Play Mode check in the main project.

## Source and license

Original model: **Sendagaya Shino / 千駄ヶ谷篠**, VRoid Project / pixiv Inc.

The original author explicitly released this sample under CC0, permitting editing
and reuse. Official license statement:
https://vroid.pixiv.help/hc/en-us/articles/360013482714-Sendagaya-Shino

Download mirror and source collection:
https://opengameart.org/content/vroid-studio-cc0-models
https://opengameart.org/sites/default/files/sendagaya_shino.zip

CC0 1.0 Universal: https://creativecommons.org/publicdomain/zero/1.0/

Downloaded 2026-09-20. The mirrored VRM's metadata also permits commercial use,
modification, redistribution and combat depiction. Local modifications include
blonde material colors, Unity mesh/prefab conversion, custom movement clips,
the athletic-shorts outfit and the matching first-person arm mesh.
