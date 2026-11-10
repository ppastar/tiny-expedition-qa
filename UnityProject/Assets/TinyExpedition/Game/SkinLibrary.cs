using System.Collections.Generic;
using System.IO;
using TinyExpedition.Core.Data;
using UnityEngine;

namespace TinyExpedition.Game
{
    /// <summary>
    /// 캐릭터 그림을 StreamingAssets/Skins/&lt;캐릭터 id&gt;.png 에서 읽는다.
    /// 그림이 없으면 등급 색 + 이름 첫 글자 카드로 대신한다(리소스 누락으로 진행이 막히지 않게).
    /// 그림을 코드와 분리해 두어 아트 교체·누락 검사를 데이터로 다룰 수 있다.
    /// </summary>
    public sealed class SkinLibrary
    {
        private readonly string _dir;
        private readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        public int MissingCount { get; private set; }

        public SkinLibrary(string dir)
        {
            _dir = dir;
        }

        public static string FileFor(string dir, string characterId) => Path.Combine(dir, characterId + ".png");

        /// <summary>그림이 있으면 Sprite, 없으면 null.</summary>
        public Sprite Get(string characterId)
        {
            if (_cache.TryGetValue(characterId, out var cached)) return cached;
            Sprite sprite = null;
            var path = FileFor(_dir, characterId);
            if (File.Exists(path))
            {
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(File.ReadAllBytes(path)))
                    sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                else
                    Debug.LogWarning("그림 파일을 읽지 못했습니다: " + path);
            }
            if (sprite == null) MissingCount++;
            _cache[characterId] = sprite;
            return sprite;
        }

        public static Color RarityColor(Rarity r)
        {
            switch (r)
            {
                case Rarity.ThreeStar: return new Color32(0xff, 0xc8, 0x57, 0xff);
                case Rarity.TwoStar: return new Color32(0x6c, 0xb8, 0xff, 0xff);
                default: return new Color32(0x8a, 0x93, 0xa8, 0xff);
            }
        }
    }
}
