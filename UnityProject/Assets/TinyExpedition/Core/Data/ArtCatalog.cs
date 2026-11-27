using System.Collections.Generic;

namespace TinyExpedition.Core.Data
{
    /// <summary>
    /// 데이터의 캐릭터·적이 어떤 그림 파일을 쓰는지 정하는 이름 규칙.
    /// 그림 자체는 화면 쪽(StreamingAssets/BattleArt)에 있고, 여기에는 "무엇이 어떤 키를 쓰는가"만 둔다.
    /// 엔진 없이도 "데이터에 있는 것마다 그림이 있는가"를 검사할 수 있게 Core에 둔다. 전투 규칙과는 무관하다.
    /// </summary>
    public static class ArtCatalog
    {
        /// <summary>그림이 준비된 적 종류: 이름에 들어 있는 낱말 → 그림 키.</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, string>> EnemyKinds = new[]
        {
            new KeyValuePair<string, string>("박쥐", "e_bat"),
            new KeyValuePair<string, string>("거미", "e_spider"),
            new KeyValuePair<string, string>("달팽이", "e_snail"),
            new KeyValuePair<string, string>("개구리", "e_frog"),
            new KeyValuePair<string, string>("요정", "e_fairy"),
            new KeyValuePair<string, string>("골렘", "e_golem"),
        };

        /// <summary>아군 그림 키 = 캐릭터 id.</summary>
        public static string AllyKey(string characterId) => characterId ?? "";

        /// <summary>적 이름에서 그림 키를 고른다. 준비된 종류가 아니면 null (화면은 기본 덩어리 그림을 쓴다).</summary>
        public static string EnemyKey(string enemyName)
        {
            if (string.IsNullOrEmpty(enemyName)) return null;
            foreach (var kind in EnemyKinds)
                if (enemyName.Contains(kind.Key)) return kind.Value;
            return null;
        }
    }
}
