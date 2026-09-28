GameTest model exports

Character/PlayerCharacter.prefab   Complete third-person player character
Character/FirstPersonArms.prefab   First-person arms
Weapons/*.prefab                   Seven independent firearm models
Items/MedicalKit.prefab            Independent medical-kit model
Meshes/*.asset                     Baked equipment mesh assets
Materials/EquipmentSurface.mat     Shared vertex-colour material

Packages/CharacterModels.unitypackage
  Portable character package with all required dependencies.

Packages/WeaponAndItemModels.unitypackage
  Portable weapon/item package with meshes, material and shader.

The equipment prefabs contain visual models only. Gameplay scripts,
colliders and pickup behaviour are intentionally excluded.
