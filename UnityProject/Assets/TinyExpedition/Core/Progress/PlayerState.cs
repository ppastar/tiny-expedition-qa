using System.Collections.Generic;
using TinyExpedition.Core.Gacha;

namespace TinyExpedition.Core.Progress
{
    /// <summary>세이브 파일에 들어가는 플레이어 상태 전체.</summary>
    public sealed class PlayerState
    {
        public const int CurrentVersion = 1;
        public const int StartingGems = 3000;

        public int Version { get; set; } = CurrentVersion;
        public int Gems { get; set; } = StartingGems;
        /// <summary>캐릭터 id → 보유 수(중복 포함).</summary>
        public Dictionary<string, int> Owned { get; set; } = new Dictionary<string, int>();
        /// <summary>배너 id → 천장 진행도.</summary>
        public Dictionary<string, PityState> Pity { get; set; } = new Dictionary<string, PityState>();
        /// <summary>스테이지 id → 최고 별 수(1~3).</summary>
        public Dictionary<string, int> ClearedStages { get; set; } = new Dictionary<string, int>();
        public int MiniGameBestScore { get; set; }
        /// <summary>다음 난수 시드. 세이브/로드 후에도 결과가 이어지도록 저장한다.</summary>
        public ulong RngSeed { get; set; } = 20261111UL;
    }
}
