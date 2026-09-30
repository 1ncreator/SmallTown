using UnityEngine;

namespace SmallTown.View
{
    /// <summary>Soft pastel "toy diorama" palette.</summary>
    public static class Palette
    {
        public static Color32 C(int hex)
        {
            return new Color32((byte)((hex >> 16) & 255), (byte)((hex >> 8) & 255), (byte)(hex & 255), 255);
        }

        public static readonly Color32[] Facades =
        {
            C(0xF4E3C9), C(0xF6D5C3), C(0xDDEBDC), C(0xD6E6F2), C(0xE8DDF0), C(0xF7EBB6), C(0xF2C9C2), C(0xCFE3D4),
            C(0xFBF1E4), C(0xE6D3BE), C(0xC9DCEB), C(0xF3DDE6)
        };

        public static readonly Color32[] Roofs =
        {
            C(0xC96F5B), C(0x7A8FA6), C(0x4F6F73), C(0xB35B4F), C(0x5E5A66), C(0xD08A5C), C(0x8C6B5A), C(0x6E8C7A)
        };

        public static readonly Color32[] CarColors =
        {
            C(0xE8604C), C(0x4E8CD9), C(0xF2C14E), C(0x6FBF8E), C(0xF4F1EA), C(0x7C6FC4), C(0xE98BB0), C(0x5DB7C4),
            C(0x3D4A5C), C(0xF09A55), C(0xA4C96B), C(0xC7CCD4)
        };

        public static readonly Color32[] Shirts =
        {
            C(0xE8604C), C(0x4E8CD9), C(0xF2C14E), C(0x6FBF8E), C(0x8A7FD1), C(0xF09A55), C(0xE98BB0), C(0x5DB7C4),
            C(0xFFFFFF), C(0x3D4A5C), C(0xB0D16B), C(0xD9534F)
        };

        public static readonly Color32[] Skins =
        {
            C(0xF5D2B8), C(0xE9B894), C(0xD29A73), C(0xA86E4E), C(0x7A4B33), C(0xF1C7A5)
        };

        public static readonly Color32 Asphalt = C(0x8C939E);
        public static readonly Color32 AsphaltDark = C(0x7B828D);
        public static readonly Color32 Sidewalk = C(0xDCD6CC);
        public static readonly Color32 Curb = C(0xC9C2B6);
        public static readonly Color32 Plaza = C(0xE6DCCB);
        public static readonly Color32 PlazaDark = C(0xD8CBB5);
        public static readonly Color32 Marking = C(0xF7F5EE);
        public static readonly Color32 MarkingYellow = C(0xF1D37A);
        public static readonly Color32 GrassWhite = C(0xFFFFFF);
        public static readonly Color32 Stone = C(0xD9CDBB);
        public static readonly Color32 StoneDark = C(0xB9AC98);
        public static readonly Color32 Wall = C(0xC8BCA9);
        public static readonly Color32 Sand = C(0xB7A27F);
        public static readonly Color32 Glass = C(0x9DB8CC);
        public static readonly Color32 GlassDark = C(0x5E7488);
        public static readonly Color32 Door = C(0x7B5A45);
        public static readonly Color32 Wood = C(0x9C7456);
        public static readonly Color32 Trunk = C(0x8A6A50);
        public static readonly Color32 Metal = C(0x5F6874);
        public static readonly Color32 MetalLight = C(0xAEB6C0);
        public static readonly Color32 White = C(0xFBFAF7);
        public static readonly Color32 Solar = C(0x2E4A7A);
        public static readonly Color32 SoilTop = C(0x9C7A57);
        public static readonly Color32 SoilMid = C(0xB08C66);
        public static readonly Color32 Rock = C(0x8E8A85);
        public static readonly Color32 RockDark = C(0x6E6A66);
        public static readonly Color32 GrassEdge = C(0x8DBF6A);
        public static readonly Color32 Plinth = C(0x55504B);
        public static readonly Color32 Red = C(0xD9534F);
        public static readonly Color32 FireRed = C(0xC8413A);
        public static readonly Color32 PoliceBlue = C(0x3F63B5);
        public static readonly Color32 Awning1 = C(0xE8604C);
        public static readonly Color32 Awning2 = C(0x4E8CD9);
        public static readonly Color32 Awning3 = C(0x6FBF8E);
        public static readonly Color32 Awning4 = C(0xF2C14E);
        public static readonly Color32 Pants = C(0x4A5163);
        public static readonly Color32 Hair = C(0x5A4636);
        public static readonly Color32 Tire = C(0x2F3238);
        public static readonly Color32 Headlight = C(0xFFF4D6);
        public static readonly Color32 Court = C(0xC98C6B);
    }
}
