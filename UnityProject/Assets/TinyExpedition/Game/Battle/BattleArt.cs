using System.Collections.Generic;
using UnityEngine;

namespace TinyExpedition.Game.Battle
{
    /// <summary>
    /// 2.5D 전투에 쓰는 그림을 전부 코드로 만든다 (외부 그림 파일·에셋 없음).
    /// 머리가 큰 SD 비율의 아군 9명, 적 6종, 배경·바닥·이펙트. 디자인은 이 프로젝트용으로 새로 만든 것이다.
    /// 한 번 만든 그림은 캐시에 두고 다시 쓴다.
    /// </summary>
    public static class BattleArt
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Texture2D> TexCache = new Dictionary<string, Texture2D>();
        private static Material _material;

        private static readonly Color Ink = Paint.Hex(0x2b2238);
        private static readonly Color White = Color.white;

        /// <summary>스프라이트·바닥이 함께 쓰는 재질 (빌드에 항상 들어가는 기본 스프라이트 셰이더).</summary>
        public static Material Material
        {
            get
            {
                if (_material == null) _material = new Material(Shader.Find("Sprites/Default")) { name = "BattleSprite" };
                return _material;
            }
        }

        private delegate Sprite Maker();

        private static Sprite Get(string key, Maker make)
        {
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            s = make();
            s.name = key;
            s.texture.name = key;
            Cache[key] = s;
            return s;
        }

        // =====================================================================
        // 아군 (SD 캐릭터)
        // =====================================================================

        private enum Hair { Short, Long, Twin, Pony, Spiky }
        private enum Hat { None, AcornCap, Leaf, Beret, Antennae, Mushroom, Helmet, Witch, Headband, Halo }
        private enum Prop { None, Pinecone, LeafShield, Flute, Bow, Potion, StoneShieldSword, MoonStaff, Sword, SunShieldSpear }

        private struct Look
        {
            public Color Skin, HairColor, Outfit, Accent, Eye, Boot;
            public Hair Hair;
            public Hat Hat;
            public Prop Prop;
            public bool Cape;
        }

        private static Look LookFor(string id)
        {
            var skinA = Paint.Hex(0xffe3cf);
            var skinB = Paint.Hex(0xf6cfae);
            switch (id)
            {
                case "c_acorn": return new Look { Skin = skinB, HairColor = Paint.Hex(0x9a6238), Outfit = Paint.Hex(0x7fa34a), Accent = Paint.Hex(0xe9c46a), Eye = Paint.Hex(0x7a4a22), Boot = Paint.Hex(0x6b4a2f), Hair = Hair.Short, Hat = Hat.AcornCap, Prop = Prop.Pinecone };
                case "c_moss": return new Look { Skin = skinA, HairColor = Paint.Hex(0x6fae6a), Outfit = Paint.Hex(0x3f7f78), Accent = Paint.Hex(0xcfe8a0), Eye = Paint.Hex(0x2f7a55), Boot = Paint.Hex(0x35524d), Hair = Hair.Short, Hat = Hat.Leaf, Prop = Prop.LeafShield };
                case "c_reed": return new Look { Skin = skinA, HairColor = Paint.Hex(0xb9dd7a), Outfit = Paint.Hex(0xfff3d6), Accent = Paint.Hex(0xe8795a), Eye = Paint.Hex(0x4f9a3a), Boot = Paint.Hex(0xa9794a), Hair = Hair.Twin, Hat = Hat.Beret, Prop = Prop.Flute };
                case "c_firefly": return new Look { Skin = skinA, HairColor = Paint.Hex(0xffd866), Outfit = Paint.Hex(0x35407a), Accent = Paint.Hex(0xfff08a), Eye = Paint.Hex(0xe08a1e), Boot = Paint.Hex(0x262c52), Hair = Hair.Pony, Hat = Hat.Antennae, Prop = Prop.Bow };
                case "c_mushroom": return new Look { Skin = skinA, HairColor = Paint.Hex(0xfaf3ea), Outfit = Paint.Hex(0xfff7ee), Accent = Paint.Hex(0xe0584f), Eye = Paint.Hex(0xc0483f), Boot = Paint.Hex(0xb0483f), Hair = Hair.Long, Hat = Hat.Mushroom, Prop = Prop.Potion };
                case "c_pebble": return new Look { Skin = skinB, HairColor = Paint.Hex(0x8d95a8), Outfit = Paint.Hex(0x6f86b8), Accent = Paint.Hex(0xdfe5f2), Eye = Paint.Hex(0x4a6ea8), Boot = Paint.Hex(0x4b556e), Hair = Hair.Short, Hat = Hat.Helmet, Prop = Prop.StoneShieldSword };
                case "c_lumi": return new Look { Skin = skinA, HairColor = Paint.Hex(0xcdb8ff), Outfit = Paint.Hex(0x6a4fc4), Accent = Paint.Hex(0xffe27a), Eye = Paint.Hex(0x7a5ad8), Boot = Paint.Hex(0x3c2f7a), Hair = Hair.Long, Hat = Hat.Witch, Prop = Prop.MoonStaff, Cape = true };
                case "c_kai": return new Look { Skin = skinB, HairColor = Paint.Hex(0x4fc6c0), Outfit = Paint.Hex(0xf4f8ff), Accent = Paint.Hex(0xff7a66), Eye = Paint.Hex(0x1f9a94), Boot = Paint.Hex(0x2f6f7a), Hair = Hair.Spiky, Hat = Hat.Headband, Prop = Prop.Sword };
                case "c_ella": return new Look { Skin = skinA, HairColor = Paint.Hex(0xffc2a8), Outfit = Paint.Hex(0xfffaf0), Accent = Paint.Hex(0xffc94a), Eye = Paint.Hex(0xe0803a), Boot = Paint.Hex(0xd8a53a), Hair = Hair.Long, Hat = Hat.Halo, Prop = Prop.SunShieldSpear, Cape = true };
            }
            // 데이터에 캐릭터가 늘어나도 그림이 비지 않게: id로 색·머리 모양을 정한다
            uint h = Hash(id);
            return new Look
            {
                Skin = (h & 1) == 0 ? skinA : skinB,
                HairColor = Color.HSVToRGB((h % 360) / 360f, 0.45f, 0.95f),
                Outfit = Color.HSVToRGB(((h >> 8) % 360) / 360f, 0.5f, 0.8f),
                Accent = Paint.Hex(0xffe27a), Eye = Paint.Hex(0x5a6ad8), Boot = Paint.Hex(0x5b5068),
                Hair = (Hair)((h >> 16) % 5), Hat = Hat.None, Prop = Prop.None,
            };
        }

        private static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s ?? "") h = (h ^ c) * 16777619;
            return h;
        }

        public const float AllyPixelsPerUnit = 176f;

        /// <summary>아군 전신 그림. 발이 아래쪽 가운데(피벗).</summary>
        public static Sprite Ally(string characterId) => Get("ally:" + characterId, () =>
        {
            var p = new Paint(288, 352);
            DrawChibi(p, LookFor(characterId));
            p.Outline(4, Ink);
            return p.ToSprite(AllyPixelsPerUnit);
        });

        private static void DrawChibi(Paint p, Look k)
        {
            const float cx = 144;
            var hairDark = Paint.Dark(k.HairColor, 0.8f);
            var hairLight = Paint.Light(k.HairColor, 0.45f);
            var outfitDark = Paint.Dark(k.Outfit, 0.82f);

            // ---- 뒤쪽: 망토·뒷머리 ----
            if (k.Cape) p.Poly(Paint.Dark(k.Accent, 0.85f), cx - 46, 112, cx + 46, 112, cx + 66, 34, cx - 66, 34);
            if (k.Prop == Prop.Sword)
            {
                // 뒤로 날리는 목도리
                p.Path(18, k.Accent, cx - 26, 110, cx - 84, 128, cx - 118, 110);
                p.Path(12, Paint.Dark(k.Accent, 0.85f), cx - 30, 100, cx - 78, 98, cx - 104, 80);
            }
            switch (k.Hair)
            {
                case Hair.Long:
                    p.Circle(cx, 184, 80, hairDark);
                    p.RoundRect(cx, 126, 76, 66, 36, hairDark);
                    break;
                case Hair.Twin:
                    p.Circle(cx, 184, 78, hairDark);
                    p.Ellipse(cx - 94, 146, 24, 56, k.HairColor, 14);
                    p.Ellipse(cx + 94, 146, 24, 56, k.HairColor, -14);
                    break;
                case Hair.Pony:
                    p.Circle(cx, 184, 78, hairDark);
                    p.Ellipse(cx - 86, 168, 22, 58, k.HairColor, 28);
                    break;
                case Hair.Spiky:
                    p.Circle(cx, 182, 78, hairDark);
                    p.Poly(k.HairColor, cx - 76, 214, cx - 40, 240, cx - 96, 262);
                    p.Poly(k.HairColor, cx + 76, 214, cx + 40, 240, cx + 96, 262);
                    p.Poly(k.HairColor, cx - 44, 240, cx - 4, 252, cx - 36, 290);
                    p.Poly(k.HairColor, cx + 44, 240, cx + 4, 252, cx + 36, 290);
                    p.Poly(k.HairColor, cx - 18, 250, cx + 18, 250, cx, 298);
                    break;
                default:
                    p.Circle(cx, 184, 78, hairDark);
                    break;
            }

            // ---- 다리·몸 ----
            p.RoundRect(cx - 17, 36, 12, 19, 10, k.Boot);
            p.RoundRect(cx + 17, 36, 12, 19, 10, k.Boot);
            p.Poly(k.Outfit, cx - 32, 96, cx + 32, 96, cx + 48, 52, cx - 48, 52);
            p.RoundRect(cx, 88, 36, 28, 22, k.Outfit);
            p.Ellipse(cx, 53, 48, 9, outfitDark);
            p.RoundRect(cx, 76, 37, 5, 4, k.Accent);
            p.Circle(cx, 98, 7, k.Accent);

            // ---- 팔 ----
            p.Line(cx - 34, 104, cx - 52, 76, 21, k.Outfit);
            p.Line(cx + 34, 104, cx + 52, 76, 21, k.Outfit);
            p.Circle(cx - 54, 68, 10.5f, k.Skin);
            p.Circle(cx + 54, 68, 10.5f, k.Skin);

            // ---- 머리·얼굴 ----
            p.Circle(cx, 180, 70, k.Skin);
            p.Ellipse(cx, 152, 54, 30, Paint.Alpha(Paint.Dark(k.Skin, 0.93f), 0.35f)); // 턱 쪽 살짝 그늘
            Face(p, cx, 170, 27, 1f, k.Eye, true);

            // ---- 앞머리 ----
            if (k.Hat != Hat.Helmet)
            {
                p.Ellipse(cx, 226, 73, 31, k.HairColor);
                p.Poly(k.HairColor, cx - 60, 216, cx - 22, 216, cx - 46, 186);
                p.Poly(k.HairColor, cx - 26, 218, cx + 26, 218, cx, 192);
                p.Poly(k.HairColor, cx + 22, 216, cx + 60, 216, cx + 46, 186);
                p.Ellipse(cx - 67, 186, 13, 36, k.HairColor, -5);
                p.Ellipse(cx + 67, 186, 13, 36, k.HairColor, 5);
                p.Ellipse(cx - 24, 240, 28, 6, Paint.Alpha(hairLight, 0.8f), 10);
            }

            DrawHat(p, k, cx);
            DrawProp(p, k, cx);
        }

        /// <summary>큰 눈·볼터치·입. size 1 = 아군 얼굴 크기.</summary>
        private static void Face(Paint p, float cx, float eyeY, float eyeDx, float size, Color iris, bool smile)
        {
            foreach (float sx in new[] { -1f, 1f })
            {
                float ex = cx + sx * eyeDx * size;
                p.Ellipse(ex, eyeY, 12.5f * size, 17f * size, Ink);
                p.Ellipse(ex, eyeY - 2 * size, 9.5f * size, 13f * size, iris);
                p.Ellipse(ex, eyeY - 4 * size, 5.5f * size, 8f * size, Paint.Dark(iris, 0.45f));
                p.Circle(ex - 4 * size, eyeY + 7 * size, 5f * size, White);
                p.Circle(ex + 4.5f * size, eyeY - 8 * size, 2.6f * size, Paint.Alpha(White, 0.9f));
                p.Ellipse(cx + sx * (eyeDx + 17) * size, eyeY - 21 * size, 12 * size, 7 * size, Paint.Hex(0xff8fa3, 0.5f));
            }
            var mouth = Paint.Hex(0xb5475c);
            if (smile) p.Path(3.2f * size, mouth, cx - 7 * size, eyeY - 24 * size, cx, eyeY - 28 * size, cx + 7 * size, eyeY - 24 * size);
            else p.Ellipse(cx, eyeY - 27 * size, 6 * size, 5 * size, mouth);
        }

        private static void DrawHat(Paint p, Look k, float cx)
        {
            switch (k.Hat)
            {
                case Hat.AcornCap:
                {
                    var cap = Paint.Hex(0x8a5a34);
                    p.Ellipse(cx, 242, 80, 30, cap);
                    p.Ellipse(cx, 252, 66, 30, Paint.Light(cap, 0.12f));
                    for (int i = -2; i <= 2; i++) p.Circle(cx + i * 26, 248 + (i % 2 == 0 ? 6 : -4), 5, Paint.Dark(cap, 0.8f));
                    p.Line(cx, 276, cx + 7, 296, 9, Paint.Dark(cap, 0.7f));
                    break;
                }
                case Hat.Leaf:
                    p.Line(cx + 4, 250, cx + 12, 270, 5, Paint.Hex(0x4f8a3a));
                    p.Ellipse(cx + 26, 284, 12, 28, Paint.Hex(0x8fd16a), -38);
                    p.Line(cx + 14, 270, cx + 38, 300, 2.5f, Paint.Hex(0x4f8a3a));
                    break;
                case Hat.Beret:
                    p.Ellipse(cx - 12, 252, 62, 26, k.Accent, 8);
                    p.Ellipse(cx - 22, 262, 30, 8, Paint.Alpha(Paint.Light(k.Accent, 0.4f), 0.8f), 8);
                    p.Circle(cx - 12, 278, 6.5f, Paint.Dark(k.Accent, 0.8f));
                    break;
                case Hat.Antennae:
                    p.Path(4.5f, Ink, cx - 22, 246, cx - 34, 280, cx - 50, 296);
                    p.Path(4.5f, Ink, cx + 22, 246, cx + 34, 280, cx + 50, 296);
                    p.Circle(cx - 52, 298, 10, k.Accent);
                    p.Circle(cx + 52, 298, 10, k.Accent);
                    p.Circle(cx - 55, 301, 4, White);
                    p.Circle(cx + 49, 301, 4, White);
                    break;
                case Hat.Mushroom:
                {
                    var red = k.Accent;
                    p.Ellipse(cx, 232, 92, 14, Paint.Hex(0xffeedd));
                    p.Ellipse(cx, 256, 100, 44, red);
                    p.Ellipse(cx, 228, 96, 16, Paint.Hex(0xffeedd));
                    p.Ellipse(cx, 262, 96, 40, red);
                    p.Circle(cx - 50, 264, 13, White);
                    p.Circle(cx + 6, 282, 15, White);
                    p.Circle(cx + 58, 258, 11, White);
                    p.Circle(cx - 12, 250, 7, White);
                    break;
                }
                case Hat.Helmet:
                {
                    var steel = Paint.Hex(0xaab4c8);
                    p.Ellipse(cx, 222, 77, 48, steel);
                    p.RoundRect(cx - 68, 184, 11, 34, 8, steel);
                    p.RoundRect(cx + 68, 184, 11, 34, 8, steel);
                    p.RoundRect(cx, 204, 76, 7, 4, Paint.Dark(steel, 0.75f));
                    p.Ellipse(cx - 26, 244, 26, 8, Paint.Alpha(White, 0.55f), 14);
                    p.Poly(k.Outfit, cx - 9, 262, cx + 9, 262, cx + 6, 300, cx - 6, 300);
                    p.Circle(cx, 300, 8, k.Outfit);
                    break;
                }
                case Hat.Witch:
                {
                    var navy = Paint.Hex(0x3b3478);
                    p.Ellipse(cx, 238, 106, 20, navy);
                    p.Poly(navy, cx - 60, 240, cx + 60, 240, cx + 20, 332);
                    p.Line(cx + 20, 330, cx + 46, 312, 15, navy);
                    p.Poly(k.Accent, cx - 58, 240, cx + 58, 240, cx + 51, 256, cx - 51, 256);
                    p.Circle(cx + 2, 284, 13, k.Accent);
                    p.Circle(cx + 9, 288, 11, navy);
                    p.Ellipse(cx - 40, 240, 30, 5, Paint.Alpha(Paint.Light(navy, 0.4f), 0.7f));
                    break;
                }
                case Hat.Headband:
                    p.RoundRect(cx, 216, 72, 7, 4, k.Accent);
                    p.Line(cx + 68, 216, cx + 108, 204, 8, k.Accent);
                    p.Line(cx + 68, 214, cx + 100, 232, 8, Paint.Dark(k.Accent, 0.85f));
                    break;
                case Hat.Halo:
                    p.Ring(cx, 286, 46, 13, 8, k.Accent);
                    p.Ring(cx, 286, 46, 13, 3, Paint.Light(k.Accent, 0.6f));
                    p.Poly(k.Accent, cx - 9, 222, cx + 9, 222, cx, 240);
                    break;
            }
        }

        private static void DrawProp(Paint p, Look k, float cx)
        {
            var wood = Paint.Hex(0x9a6a3c);
            var steel = Paint.Hex(0xdfe7f5);
            switch (k.Prop)
            {
                case Prop.Pinecone:
                    p.Ellipse(cx + 60, 82, 14, 18, wood, -20);
                    for (int i = 0; i < 3; i++) p.Line(cx + 50 + i * 4, 92 - i * 9, cx + 66 + i * 4, 88 - i * 9, 3, Paint.Dark(wood, 0.7f));
                    break;
                case Prop.LeafShield:
                    p.Circle(cx - 58, 78, 36, wood);
                    p.Ring(cx - 58, 78, 30, 30, 6, Paint.Dark(wood, 0.75f));
                    p.Ellipse(cx - 58, 78, 11, 22, Paint.Hex(0x8fd16a), -25);
                    p.Line(cx - 66, 62, cx - 50, 94, 2.5f, Paint.Hex(0x4f8a3a));
                    break;
                case Prop.Flute:
                    p.Line(cx + 34, 92, cx + 92, 120, 9, Paint.Hex(0xcfe58a));
                    for (int i = 0; i < 4; i++) p.Circle(cx + 48 + i * 11, 99 + i * 5.3f, 2.4f, Paint.Hex(0x5f7a2a));
                    p.Circle(cx + 106, 150, 6, k.Accent);
                    p.Line(cx + 111, 150, cx + 111, 172, 3, k.Accent);
                    break;
                case Prop.Bow:
                    p.Line(cx + 66, 138, cx + 66, 20, 2.2f, White);
                    p.Path(8, wood, cx + 66, 140, cx + 86, 112, cx + 92, 79, cx + 86, 46, cx + 66, 18);
                    p.Circle(cx + 92, 79, 7, k.Accent);
                    break;
                case Prop.Potion:
                    p.RoundRect(cx + 60, 98, 6, 9, 3, Paint.Hex(0xcfe9f5));
                    p.RoundRect(cx + 60, 109, 8, 4, 2, wood);
                    p.Circle(cx + 60, 78, 16, Paint.Hex(0xcfe9f5));
                    p.Circle(cx + 60, 75, 12, Paint.Hex(0xff8fb0));
                    p.Circle(cx + 55, 82, 4, White);
                    break;
                case Prop.StoneShieldSword:
                    p.Line(cx + 56, 64, cx + 92, 168, 11, steel);
                    p.Line(cx + 44, 92, cx + 76, 80, 8, Paint.Hex(0x8a93a8));
                    p.RoundRect(cx - 60, 76, 28, 36, 14, Paint.Hex(0x9aa3b4));
                    p.RoundRect(cx - 60, 76, 19, 27, 10, Paint.Hex(0xb9c1cf));
                    p.Path(3, Paint.Hex(0x7c8496), cx - 68, 92, cx - 58, 78, cx - 64, 66, cx - 52, 56);
                    break;
                case Prop.MoonStaff:
                    p.Line(cx + 62, 22, cx + 78, 206, 8, wood);
                    p.Glow(cx + 80, 230, 44, Paint.Alpha(k.Accent, 0.55f));
                    p.Circle(cx + 80, 230, 21, k.Accent);
                    p.Circle(cx + 88, 236, 15, Paint.Light(k.Accent, 0.65f));
                    p.Circle(cx + 62, 258, 4, White);
                    p.Circle(cx + 104, 208, 3, White);
                    break;
                case Prop.Sword:
                    p.Line(cx + 58, 70, cx + 104, 200, 13, steel);
                    p.Line(cx + 62, 82, cx + 100, 190, 4, White);
                    p.Line(cx + 40, 98, cx + 80, 84, 9, Paint.Hex(0xe9c46a));
                    break;
                case Prop.SunShieldSpear:
                    p.Line(cx + 60, 22, cx + 76, 250, 7, k.Accent);
                    p.Poly(steel, cx + 66, 250, cx + 86, 250, cx + 79, 292);
                    p.RoundRect(cx - 62, 76, 32, 42, 16, White);
                    p.RoundRect(cx - 62, 76, 24, 34, 12, k.Accent);
                    p.Circle(cx - 62, 78, 12, Paint.Hex(0xff9a4a));
                    p.Circle(cx - 66, 82, 4, White);
                    break;
            }
        }

        // =====================================================================
        // 적
        // =====================================================================

        public struct EnemyArt
        {
            public Sprite Sprite;
            /// <summary>공중에 떠 있는 적은 발밑이 아니라 이만큼 띄워 그린다.</summary>
            public float Hover;
        }

        /// <summary>적 이름(데이터)에서 종류를 골라 그림을 만든다. 모르는 이름이면 말랑한 덩어리.</summary>
        public static EnemyArt Enemy(string name)
        {
            name = name ?? "";
            if (name.Contains("박쥐")) return new EnemyArt { Sprite = Get("enemy:bat", Bat), Hover = 0.75f };
            if (name.Contains("거미")) return new EnemyArt { Sprite = Get("enemy:spider", Spider) };
            if (name.Contains("달팽이")) return new EnemyArt { Sprite = Get("enemy:snail", Snail) };
            if (name.Contains("개구리")) return new EnemyArt { Sprite = Get("enemy:frog", Frog) };
            if (name.Contains("요정")) return new EnemyArt { Sprite = Get("enemy:fairy", Fairy), Hover = 0.55f };
            if (name.Contains("골렘")) return new EnemyArt { Sprite = Get("enemy:golem", Golem) };
            uint h = Hash(name);
            return new EnemyArt { Sprite = Get("enemy:blob:" + (h % 6), () => Blob(Color.HSVToRGB((h % 6) / 6f, 0.45f, 0.9f))) };
        }

        private static Sprite Bat()
        {
            var p = new Paint(320, 240);
            const float cx = 160;
            var body = Paint.Hex(0x7a5fb5);
            var wing = Paint.Hex(0x5a4590);
            foreach (float s in new[] { -1f, 1f })
            {
                p.Poly(wing, cx + s * 36, 132, cx + s * 96, 206, cx + s * 150, 150, cx + s * 120, 112);
                p.Poly(wing, cx + s * 36, 132, cx + s * 120, 112, cx + s * 104, 76, cx + s * 44, 92);
                p.Poly(body, cx + s * 28, 176, cx + s * 58, 176, cx + s * 52, 224);
                p.Poly(Paint.Hex(0xffb3c7), cx + s * 36, 180, cx + s * 52, 180, cx + s * 48, 208);
            }
            p.Circle(cx, 124, 62, body);
            p.Ellipse(cx, 100, 38, 26, Paint.Light(body, 0.3f));
            Face(p, cx, 136, 24, 0.95f, Paint.Hex(0xffd23f), false);
            p.Poly(White, cx - 12, 104, cx - 4, 104, cx - 8, 92);
            p.Poly(White, cx + 4, 104, cx + 12, 104, cx + 8, 92);
            p.Outline(4, Ink);
            return p.ToSprite(165);
        }

        private static Sprite Spider()
        {
            var p = new Paint(320, 250);
            const float cx = 160;
            var body = Paint.Hex(0x3f8f86);
            var leg = Paint.Hex(0x2f5f66);
            foreach (float s in new[] { -1f, 1f })
            {
                p.Path(10, leg, cx + s * 44, 92, cx + s * 96, 138, cx + s * 132, 40);
                p.Path(10, leg, cx + s * 48, 78, cx + s * 104, 96, cx + s * 148, 24);
                p.Path(10, leg, cx + s * 40, 64, cx + s * 80, 70, cx + s * 104, 22);
                p.Path(9, leg, cx + s * 36, 110, cx + s * 76, 170, cx + s * 110, 108);
            }
            p.Ellipse(cx, 150, 66, 60, Paint.Hex(0x5a3f8f));
            p.Circle(cx - 26, 172, 13, Paint.Hex(0xb6f25a));
            p.Circle(cx + 22, 184, 10, Paint.Hex(0xb6f25a));
            p.Circle(cx + 34, 152, 8, Paint.Hex(0xb6f25a));
            p.Circle(cx, 84, 54, body);
            Face(p, cx, 96, 22, 0.9f, Paint.Hex(0xff5a6e), false);
            p.Circle(cx - 44, 116, 6, Ink);
            p.Circle(cx + 44, 116, 6, Ink);
            p.Poly(Paint.Hex(0xb6f25a), cx - 14, 62, cx - 4, 62, cx - 9, 46);
            p.Poly(Paint.Hex(0xb6f25a), cx + 4, 62, cx + 14, 62, cx + 9, 46);
            p.Outline(4, Ink);
            return p.ToSprite(165);
        }

        private static Sprite Snail()
        {
            var p = new Paint(340, 280);
            var skin = Paint.Hex(0xf2d9a0);
            var shell = Paint.Hex(0x9aa3b4);
            p.Ellipse(150, 44, 130, 30, skin);
            p.Line(74, 60, 62, 150, 14, skin);
            p.Line(112, 60, 116, 150, 14, skin);
            p.Circle(62, 156, 17, White);
            p.Circle(116, 156, 17, White);
            p.Circle(58, 156, 8, Ink);
            p.Circle(112, 156, 8, Ink);
            p.Circle(90, 78, 46, skin);
            p.Ellipse(70, 64, 11, 6, Paint.Hex(0xff8fa3, 0.5f));
            p.Path(3.5f, Paint.Hex(0xb5475c), 78, 60, 88, 55, 98, 60);
            p.Circle(206, 142, 104, shell);
            p.Ring(206, 142, 76, 76, 9, Paint.Dark(shell, 0.78f));
            p.Ring(214, 138, 46, 46, 9, Paint.Dark(shell, 0.78f));
            p.Circle(220, 134, 16, Paint.Dark(shell, 0.78f));
            p.Ellipse(170, 204, 34, 12, Paint.Alpha(White, 0.5f), 24);
            p.Poly(Paint.Dark(shell, 0.7f), 262, 220, 286, 232, 276, 200);
            p.Poly(Paint.Dark(shell, 0.7f), 292, 170, 318, 172, 300, 148);
            p.Outline(4, Ink);
            return p.ToSprite(150);
        }

        private static Sprite Frog()
        {
            var p = new Paint(320, 270);
            const float cx = 160;
            var green = Paint.Hex(0x7fcf5a);
            foreach (float s in new[] { -1f, 1f })
            {
                p.Ellipse(cx + s * 96, 52, 42, 30, Paint.Dark(green, 0.85f), s * 20);
                p.Ellipse(cx + s * 52, 28, 30, 14, Paint.Dark(green, 0.8f));
            }
            p.Ellipse(cx, 104, 112, 86, green);
            p.Ellipse(cx, 76, 80, 52, Paint.Hex(0xf3f7c8));
            foreach (float s in new[] { -1f, 1f })
            {
                p.Circle(cx + s * 56, 196, 40, green);
                p.Circle(cx + s * 56, 200, 28, White);
                p.Ellipse(cx + s * 56, 198, 13, 19, Ink);
                p.Circle(cx + s * 51, 207, 6, White);
                p.Ellipse(cx + s * 86, 128, 15, 9, Paint.Hex(0xff8fa3, 0.55f));
            }
            p.Path(5, Paint.Hex(0x3f7a3a), cx - 58, 128, cx - 30, 110, cx, 106, cx + 30, 110, cx + 58, 128);
            p.Circle(cx - 46, 66, 9, Paint.Dark(green, 0.85f));
            p.Circle(cx + 52, 84, 7, Paint.Dark(green, 0.85f));
            p.Outline(4, Ink);
            return p.ToSprite(150);
        }

        private static Sprite Fairy()
        {
            var p = new Paint(300, 300);
            const float cx = 150;
            var amber = Paint.Hex(0xffc24a);
            foreach (float s in new[] { -1f, 1f })
            {
                p.Ellipse(cx + s * 74, 176, 54, 26, Paint.Hex(0xdff7ff, 0.85f), s * 28);
                p.Ellipse(cx + s * 64, 118, 40, 20, Paint.Hex(0xc8eeff, 0.8f), s * -22);
            }
            p.Poly(amber, cx - 44, 150, cx + 44, 150, cx, 262);
            p.Circle(cx, 124, 70, amber);
            p.Ellipse(cx - 26, 156, 20, 30, Paint.Alpha(White, 0.45f), 18);
            Face(p, cx, 128, 25, 0.95f, Paint.Hex(0xc0661a), true);
            p.Ellipse(cx + 20, 262, 12, 26, Paint.Hex(0x8fd16a), -40);
            p.Circle(cx, 52, 8, Paint.Dark(amber, 0.85f));
            p.Outline(4, Ink);
            return p.ToSprite(175);
        }

        private static Sprite Golem()
        {
            var p = new Paint(360, 400);
            const float cx = 180;
            var bark = Paint.Hex(0x9a7250);
            var barkDark = Paint.Dark(bark, 0.78f);
            var moss = Paint.Hex(0x86c45a);
            p.RoundRect(cx - 52, 44, 40, 40, 18, barkDark);
            p.RoundRect(cx + 52, 44, 40, 40, 18, barkDark);
            p.RoundRect(cx - 132, 150, 38, 92, 30, bark);
            p.RoundRect(cx + 132, 150, 38, 92, 30, bark);
            p.Circle(cx - 132, 66, 42, barkDark);
            p.Circle(cx + 132, 66, 42, barkDark);
            p.RoundRect(cx, 170, 106, 110, 44, bark);
            p.RoundRect(cx, 300, 80, 62, 34, bark);
            p.Ellipse(cx, 356, 84, 24, moss);
            p.Circle(cx - 60, 348, 26, moss);
            p.Circle(cx + 56, 352, 22, moss);
            p.Ellipse(cx - 130, 238, 40, 16, moss);
            p.Ellipse(cx + 132, 238, 36, 14, moss);
            p.RoundRect(cx, 300, 58, 26, 14, Paint.Hex(0x4a3628));
            p.Glow(cx - 28, 300, 30, Paint.Hex(0xffe27a, 0.9f));
            p.Glow(cx + 28, 300, 30, Paint.Hex(0xffe27a, 0.9f));
            p.Circle(cx - 28, 300, 11, Paint.Hex(0xfff3b0));
            p.Circle(cx + 28, 300, 11, Paint.Hex(0xfff3b0));
            p.Path(6, barkDark, cx - 60, 220, cx - 20, 190, cx - 40, 150, cx + 10, 110);
            p.Path(6, barkDark, cx + 70, 230, cx + 40, 180, cx + 66, 130);
            p.Circle(cx + 10, 150, 18, Paint.Hex(0xffb347));
            p.Circle(cx + 10, 150, 9, Paint.Hex(0xfff3b0));
            p.Outline(5, Ink);
            return p.ToSprite(125);
        }

        private static Sprite Blob(Color c)
        {
            var p = new Paint(256, 220);
            p.Ellipse(128, 70, 100, 56, c);
            p.Circle(128, 104, 78, c);
            p.Ellipse(96, 150, 22, 12, Paint.Alpha(White, 0.5f), 20);
            Face(p, 128, 104, 25, 0.95f, Paint.Dark(c, 0.5f), true);
            p.Outline(4, Ink);
            return p.ToSprite(165);
        }

        // =====================================================================
        // 배경·바닥
        // =====================================================================

        public struct Theme
        {
            public Color SkyTop, SkyBottom, Far, Mid, GroundA, GroundB, Haze, Glow;
        }

        /// <summary>스테이지마다 색만 다른 같은 무대. 스테이지 id로 고른다.</summary>
        public static Theme ThemeFor(string stageId)
        {
            int theme = stageId == "T-1" ? 0 : stageId == "T-2" ? 1 : stageId == "T-3" ? 2 : (int)(Hash(stageId) % 3);
            switch (theme)
            {
                case 1: // 독버섯 늪: 연두·노랑
                    return new Theme { SkyTop = Paint.Hex(0x7fd6c2), SkyBottom = Paint.Hex(0xf6f3b0), Far = Paint.Hex(0x7cc7a0), Mid = Paint.Hex(0x4f9f7a), GroundA = Paint.Hex(0x9bd66a), GroundB = Paint.Hex(0x86c85c), Haze = Paint.Hex(0xe9f7c0), Glow = Paint.Hex(0xfff3a0) };
                case 2: // 뿌리 깊은 굴: 살구·주황
                    return new Theme { SkyTop = Paint.Hex(0xffb38a), SkyBottom = Paint.Hex(0xffe9c0), Far = Paint.Hex(0xe59a78), Mid = Paint.Hex(0xb86f5a), GroundA = Paint.Hex(0xe8c88a), GroundB = Paint.Hex(0xdcb878), Haze = Paint.Hex(0xfff0d0), Glow = Paint.Hex(0xfff0b0) };
                default: // 반딧불 동굴: 하늘·민트
                    return new Theme { SkyTop = Paint.Hex(0x6aa8f0), SkyBottom = Paint.Hex(0xc8f3e6), Far = Paint.Hex(0x7fb8e6), Mid = Paint.Hex(0x5a8fd0), GroundA = Paint.Hex(0x8fdcb0), GroundB = Paint.Hex(0x7cd0a4), Haze = Paint.Hex(0xd8f7ec), Glow = Paint.Hex(0xfff7a0) };
            }
        }

        /// <summary>가장 먼 배경: 하늘 그라데이션 + 멀리 있는 봉우리·기둥.</summary>
        public static Sprite Backdrop(string stageId) => Get("backdrop:" + stageId, () =>
        {
            var t = ThemeFor(stageId);
            var p = new Paint(1024, 512);
            p.VGradient(t.SkyBottom, t.SkyTop);
            p.Glow(700, 400, 300, Paint.Alpha(White, 0.55f));
            p.Glow(700, 400, 120, Paint.Alpha(t.Glow, 0.8f));
            var rng = new System.Random(7);
            var farLight = Color.Lerp(t.Far, t.SkyBottom, 0.55f);
            for (int i = 0; i < 12; i++)
            {
                float x = i * 92 + rng.Next(-20, 20), h = 150 + rng.Next(0, 150), w = 70 + rng.Next(0, 50);
                p.Poly(farLight, x - w, 0, x + w, 0, x + w * 0.25f, h, x - w * 0.25f, h);
                p.Circle(x, h, w * 0.3f, farLight);
            }
            for (int i = 0; i < 9; i++)
            {
                float x = i * 128 + rng.Next(-30, 30), h = 90 + rng.Next(0, 110), w = 90 + rng.Next(0, 60);
                p.Poly(t.Far, x - w, 0, x + w, 0, x + w * 0.2f, h, x - w * 0.2f, h);
                p.Circle(x, h, w * 0.26f, t.Far);
                p.Ellipse(x - w * 0.1f, h * 0.6f, w * 0.12f, h * 0.3f, Paint.Alpha(White, 0.18f));
            }
            // 위에서 내려오는 종유석
            for (int i = 0; i < 10; i++)
            {
                float x = i * 110 + rng.Next(-30, 30), h = 60 + rng.Next(0, 120), w = 30 + rng.Next(0, 40);
                p.Poly(Paint.Alpha(t.Mid, 0.55f), x - w, 512, x + w, 512, x, 512 - h);
            }
            p.Ellipse(512, 0, 620, 70, Paint.Alpha(t.Haze, 0.85f));
            return p.ToSprite(1024f / 44f);
        });

        /// <summary>중간 거리 소품: 큰 버섯·수정·바위. kind 0~3.</summary>
        public static Sprite PropSprite(string stageId, int kind) => Get("prop:" + stageId + ":" + kind, () =>
        {
            var t = ThemeFor(stageId);
            var p = new Paint(256, 320);
            switch (kind)
            {
                case 0: // 큰 버섯
                    p.RoundRect(128, 90, 26, 90, 20, Paint.Hex(0xfff3e0));
                    p.Ellipse(128, 206, 118, 74, Paint.Hex(0xff8f8f));
                    p.Ellipse(128, 178, 112, 22, Paint.Hex(0xffe0d0));
                    p.Circle(84, 224, 18, White);
                    p.Circle(150, 248, 22, White);
                    p.Circle(190, 212, 14, White);
                    break;
                case 1: // 수정
                    p.Poly(Paint.Light(t.Mid, 0.45f), 96, 0, 160, 0, 176, 200, 128, 300, 84, 200);
                    p.Poly(Paint.Light(t.Mid, 0.7f), 128, 300, 84, 200, 128, 0);
                    p.Poly(Paint.Light(t.Mid, 0.3f), 30, 0, 86, 0, 78, 120, 52, 170, 30, 120);
                    p.Poly(Paint.Light(t.Mid, 0.55f), 170, 0, 226, 0, 228, 96, 200, 150, 176, 96);
                    break;
                case 2: // 둥근 바위 + 풀
                    p.Ellipse(128, 70, 116, 74, Paint.Light(t.Mid, 0.2f));
                    p.Ellipse(96, 98, 60, 34, Paint.Alpha(White, 0.22f));
                    p.Ellipse(70, 24, 16, 46, Paint.Hex(0x7fcf6a), 22);
                    p.Ellipse(96, 30, 14, 52, Paint.Hex(0x9be07a), 4);
                    p.Ellipse(120, 22, 14, 40, Paint.Hex(0x7fcf6a), -20);
                    break;
                default: // 작은 버섯 무리
                    p.RoundRect(86, 50, 14, 50, 10, Paint.Hex(0xfff3e0));
                    p.Ellipse(86, 112, 62, 40, Paint.Hex(0xffc24a));
                    p.Circle(66, 124, 9, White);
                    p.Circle(104, 132, 11, White);
                    p.RoundRect(176, 36, 11, 36, 8, Paint.Hex(0xfff3e0));
                    p.Ellipse(176, 82, 46, 30, Paint.Hex(0xb89aff));
                    p.Circle(166, 90, 7, White);
                    p.Circle(192, 86, 6, White);
                    break;
            }
            p.Outline(3, Paint.Dark(t.Mid, 0.6f));
            return p.ToSprite(96);
        });

        /// <summary>화면 앞쪽을 살짝 가리는 큰 잎 (깊이감).</summary>
        public static Sprite Foreground(string stageId) => Get("fore:" + stageId, () =>
        {
            var t = ThemeFor(stageId);
            var c = Paint.Dark(t.Mid, 0.62f);
            var p = new Paint(256, 256);
            p.Ellipse(70, 110, 40, 130, c, 28);
            p.Ellipse(130, 90, 34, 120, Paint.Dark(c, 0.9f), -6);
            p.Ellipse(190, 100, 38, 124, c, -32);
            return p.ToSprite(80);
        });

        /// <summary>바닥에 반복해서 까는 무늬.</summary>
        public static Texture2D Ground(string stageId)
        {
            string key = "ground:" + stageId;
            if (TexCache.TryGetValue(key, out var tex) && tex != null) return tex;
            var t = ThemeFor(stageId);
            var p = new Paint(256, 256);
            p.FillAll(t.GroundA);
            // 바둑판처럼 번갈아 놓인 둥근 타일
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    if ((x + y) % 2 == 0) p.RoundRect(64 + x * 128, 64 + y * 128, 60, 60, 26, t.GroundB);
            var rng = new System.Random(11);
            for (int i = 0; i < 26; i++)
            {
                float x = rng.Next(8, 248), y = rng.Next(8, 248);
                if (i % 3 == 0) p.Circle(x, y, 3 + rng.Next(0, 4), Paint.Alpha(White, 0.35f));
                else p.Ellipse(x, y, 3, 7, Paint.Alpha(Paint.Dark(t.GroundB, 0.82f), 0.7f), rng.Next(-30, 30));
            }
            tex = p.ToTexture(true);
            tex.name = key;
            TexCache[key] = tex;
            return tex;
        }

        // =====================================================================
        // 그림자·표식·이펙트
        // =====================================================================

        public static Sprite Shadow() => Get("fx:shadow", () =>
        {
            var p = new Paint(128, 128);
            p.Glow(64, 64, 62, Paint.Hex(0x1f2a44, 0.75f), 1.2f);
            return p.ToSprite(128, 0.5f);
        });

        /// <summary>발밑 표식 고리 (차례·대상 표시). 색은 SpriteRenderer.color로 입힌다.</summary>
        public static Sprite RingMark() => Get("fx:ring", () =>
        {
            var p = new Paint(192, 192);
            p.FillAll(new Color(1, 1, 1, 0));
            p.Ring(96, 96, 80, 80, 12, White);
            p.Ring(96, 96, 62, 62, 4, Paint.Alpha(White, 0.6f));
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2f;
                p.Circle(96 + Mathf.Cos(a) * 80, 96 + Mathf.Sin(a) * 80, 11, White);
            }
            return p.ToSprite(192, 0.5f);
        });

        public static Sprite Dot() => Get("fx:dot", () =>
        {
            var p = new Paint(64, 64);
            p.FillAll(new Color(1, 1, 1, 0));
            p.Glow(32, 32, 31, White, 1.6f);
            return p.ToSprite(64, 0.5f);
        });

        /// <summary>베기 자국.</summary>
        public static Sprite Slash() => Get("fx:slash", () =>
        {
            var p = new Paint(256, 256);
            p.FillAll(new Color(1, 1, 1, 0));
            for (int i = 0; i < 40; i++)
            {
                float t = i / 39f, a = Mathf.Lerp(-0.9f, 0.9f, t);
                float w = Mathf.Sin(t * Mathf.PI) * 26 + 2;
                p.Circle(128 + Mathf.Sin(a) * 96, 128 + Mathf.Cos(a) * 60 - 30, w * 0.5f, White);
            }
            return p.ToSprite(128, 0.5f);
        });

        /// <summary>맞았을 때 터지는 별 모양.</summary>
        public static Sprite Burst() => Get("fx:burst", () =>
        {
            var p = new Paint(256, 256);
            p.FillAll(new Color(1, 1, 1, 0));
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f, r = i % 2 == 0 ? 118 : 78;
                float bx = Mathf.Cos(a + 0.28f) * 34, by = Mathf.Sin(a + 0.28f) * 34, cx2 = Mathf.Cos(a - 0.28f) * 34, cy2 = Mathf.Sin(a - 0.28f) * 34;
                p.Poly(White, 128 + bx, 128 + by, 128 + cx2, 128 + cy2, 128 + Mathf.Cos(a) * r, 128 + Mathf.Sin(a) * r);
            }
            p.Circle(128, 128, 44, White);
            return p.ToSprite(128, 0.5f);
        });

        public static Sprite Plus() => Get("fx:plus", () =>
        {
            var p = new Paint(96, 96);
            p.RoundRect(48, 48, 36, 13, 8, Paint.Hex(0x7dffb0));
            p.RoundRect(48, 48, 13, 36, 8, Paint.Hex(0x7dffb0));
            p.Outline(3, Paint.Hex(0x1f7a4f));
            return p.ToSprite(128, 0.5f);
        });

        public static Sprite Star() => Get("fx:star", () =>
        {
            var p = new Paint(96, 96);
            var pts = new float[20];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI / 5f, r = i % 2 == 0 ? 42 : 18;
                pts[i * 2] = 48 + Mathf.Cos(a) * r;
                pts[i * 2 + 1] = 48 + Mathf.Sin(a) * r;
            }
            // 별은 오목해서 삼각형 다섯 개(뾰족한 끝 + 양옆 안쪽 점) + 가운데 원으로 나눠 그린다
            for (int i = 0; i < 10; i += 2)
            {
                int prev = (i + 9) % 10, next = (i + 1) % 10;
                p.Poly(Paint.Hex(0xffe14a), pts[i * 2], pts[i * 2 + 1], pts[prev * 2], pts[prev * 2 + 1], pts[next * 2], pts[next * 2 + 1]);
            }
            p.Circle(48, 48, 20, Paint.Hex(0xffe14a));
            p.Outline(3, Paint.Hex(0x9a6a10));
            return p.ToSprite(128, 0.5f);
        });

        public static Sprite Bubble() => Get("fx:bubble", () =>
        {
            var p = new Paint(96, 96);
            p.Circle(48, 48, 34, Paint.Hex(0xb46bff, 0.85f));
            p.Circle(38, 58, 9, Paint.Alpha(White, 0.8f));
            p.Outline(3, Paint.Hex(0x5a2a9a));
            return p.ToSprite(128, 0.5f);
        });

        public static Sprite Shield() => Get("fx:shield", () =>
        {
            var p = new Paint(160, 192);
            p.Poly(Paint.Hex(0x8fd0ff, 0.9f), 20, 176, 140, 176, 140, 90, 80, 14, 20, 90);
            p.Poly(Paint.Hex(0xdff3ff, 0.9f), 40, 160, 80, 160, 80, 40, 40, 96);
            p.Outline(4, Paint.Hex(0x2a5a9a));
            return p.ToSprite(128, 0.5f);
        });
    }
}
