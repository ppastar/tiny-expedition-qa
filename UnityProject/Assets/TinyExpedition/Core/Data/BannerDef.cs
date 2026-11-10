using System.Collections.Generic;

namespace TinyExpedition.Core.Data
{
    /// <summary>
    /// 모집(가챠) 배너. 확률은 basis point(1bp = 0.01%) 정수로 저장해 부동소수 오차를 없앤다.
    /// </summary>
    public sealed class BannerDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int CostPerPull { get; set; }

        /// <summary>등급별 기본 확률(bp). 합계는 10000이어야 한다.</summary>
        public Dictionary<Rarity, int> RateBp { get; set; } = new Dictionary<Rarity, int>();

        /// <summary>등급별 후보 캐릭터 id. 같은 등급 안에서는 균등 확률.</summary>
        public Dictionary<Rarity, List<string>> Pool { get; set; } = new Dictionary<Rarity, List<string>>();

        /// <summary>픽업 캐릭터 id (3성). 없으면 null.</summary>
        public string PickupId { get; set; }

        /// <summary>3성이 나왔을 때 그것이 픽업일 확률(bp).</summary>
        public int PickupShareBp { get; set; }

        /// <summary>이 횟수째 뽑기에서 3성을 확정한다(천장). 0이면 없음.</summary>
        public int HardPity { get; set; }

        /// <summary>10연차의 마지막 장은 2성 이상 확정.</summary>
        public bool TenPullGuarantee { get; set; }

        /// <summary>천장을 포함한 3성 실제 획득률(bp, 공시값). 0이면 공시 안 함.</summary>
        public int DisclosedEffectiveThreeStarBp { get; set; }
    }
}
