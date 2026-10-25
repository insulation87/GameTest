using UnityEngine;

public static partial class PixelCharacterFactory
{
    static GameObject CreateMonster(Transform parent, string species)
    {
        bool goblin = species == "Goblin", orc = species == "Orc";
        Color skin = goblin ? new Color(.39f, .66f, .24f) : orc ? new Color(.25f, .45f, .25f) : new Color(.49f, .32f, .66f);
        Color leather = new Color(.35f, .23f, .16f);
        string palette = "Monster " + species;
        var root = new GameObject(species + " Pixel Model", typeof(Animation)); root.transform.SetParent(parent, false);
        var hips = Joint(root.transform, "Hips", new Vector3(0, .92f, 0));
        var chest = Joint(hips, "Chest", new Vector3(0, .18f, 0));
        Box(chest, "Monster Torso", new Vector3(0, .20f, 0), new Vector3(orc ? .70f : .50f, .47f, .36f), skin, palette + " Skin");
        Box(hips, "Leather Belt", Vector3.zero, new Vector3(.60f, .15f, .38f), leather, "Monster Leather");
        Box(chest, "Leather Harness", new Vector3(.17f, .24f, .20f), new Vector3(.12f, .43f, .05f), leather, "Monster Leather");
        var head = Joint(chest, "Head", new Vector3(0, .61f, goblin ? .07f : 0));
        Box(head, "Monster Head", Vector3.zero, new Vector3(goblin ? .55f : .49f, .40f, .42f), skin, palette + " Skin");
        Box(head, "Snout", new Vector3(0, -.06f, .25f), new Vector3(.28f, .16f, .18f), skin, palette + " Skin");
        Box(head, "Nose", new Vector3(0, .01f, .32f), new Vector3(.12f, .12f, .12f), skin, palette + " Skin");
        for (int side = -1; side <= 1; side += 2)
        {
            var ear = Joint(head, "Pointed Ear", new Vector3(side * .31f, .07f, 0)); ear.localRotation = Quaternion.Euler(0, 0, side * -25);
            Box(ear, "Ear Base", Vector3.zero, new Vector3(.24f, .16f, .10f), skin, palette + " Skin");
            Box(ear, "Ear Tip", new Vector3(side * .12f, .02f, 0), new Vector3(.11f, .08f, .08f), skin, palette + " Skin");
            Box(head, "Gold Eye", new Vector3(side * .13f, .055f, .226f), new Vector3(.10f, .075f, .035f), new Color(1, .77f, .16f), "Monster Eyes");
            Box(head, "Eye Pupil", new Vector3(side * .13f, .05f, .248f), new Vector3(.034f, .065f, .02f), Navy, "Pixel Navy");
            Box(head, "Tusk", new Vector3(side * .11f, -.09f, .36f), new Vector3(.052f, orc ? .17f : .10f, .05f), Color.white, "Pixel Ivory");
            if (!goblin)
            {
                var horn = Joint(head, "Forest Horn", new Vector3(side * .19f, .25f, -.03f)); horn.localRotation = Quaternion.Euler(0, 0, side * -18);
                Box(horn, "Horn", Vector3.zero, new Vector3(.09f, orc ? .18f : .36f, .10f), orc ? Steel : Color.white, orc ? "Pixel Steel" : "Pixel Ivory");
            }
            string prefix = side < 0 ? "Left" : "Right";
            var leg = Joint(hips, prefix + "Leg", new Vector3(side * .16f, -.04f, 0));
            Box(leg, "Leg", new Vector3(0, -.34f, 0), new Vector3(.23f, .65f, .25f), skin, palette + " Skin");
            Box(leg, "Boot", new Vector3(0, -.77f, .06f), new Vector3(.25f, .20f, .36f), leather, "Monster Leather");
            var arm = Joint(chest, prefix + "Arm", new Vector3(side * (orc ? .45f : .37f), .40f, 0));
            Box(arm, "Upper Arm", new Vector3(0, -.12f, 0), new Vector3(orc ? .29f : .22f, .29f, .28f), skin, palette + " Skin");
            Box(arm, "Forearm", new Vector3(0, -.39f, 0), new Vector3(.21f, .30f, .24f), skin, palette + " Skin");
            Box(arm, "Wrist Wrap", new Vector3(0, -.48f, 0), new Vector3(.24f, .12f, .27f), leather, "Monster Leather");
            var hand = Joint(arm, "Hand", new Vector3(0, -.57f, 0));
            Box(hand, "Claw Hand", Vector3.zero, new Vector3(.22f, .18f, .24f), skin, palette + " Skin");
            Equipment(hand, side > 0, false);
        }
        var animation = root.GetComponent<Animation>();
        foreach (string clip in new[] { "Idle", "Walk", "Run", "Death", "Attack", "Shoot", "Cast" }) animation.AddClip(Clip(clip), clip);
        animation.playAutomatically = false;
        return root;
    }
}
