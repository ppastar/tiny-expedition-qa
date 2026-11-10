using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TinyExpedition.Core.Progress
{
    public sealed class SaveCorruptedException : Exception
    {
        public SaveCorruptedException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>세이브 JSON 직렬화. 깨진 파일·미래 버전·음수 재화 같은 이상값은 읽지 않고 예외로 알린다.</summary>
    public static class SaveSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            Formatting = Formatting.Indented,
        };

        public static string Serialize(PlayerState state) => JsonConvert.SerializeObject(state, Settings);

        public static PlayerState Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new SaveCorruptedException("세이브가 비어 있습니다.");
            PlayerState state;
            try
            {
                state = JsonConvert.DeserializeObject<PlayerState>(json, Settings);
            }
            catch (JsonException e)
            {
                throw new SaveCorruptedException("세이브 JSON을 읽을 수 없습니다.", e);
            }
            if (state == null) throw new SaveCorruptedException("세이브가 비어 있습니다.");
            if (state.Version > PlayerState.CurrentVersion)
                throw new SaveCorruptedException("더 새 버전(" + state.Version + ")의 세이브입니다. 게임을 업데이트하세요.");
            if (state.Version < 1) throw new SaveCorruptedException("알 수 없는 세이브 버전: " + state.Version);
            if (state.Gems < 0) throw new SaveCorruptedException("보석이 음수입니다: " + state.Gems);
            foreach (var kv in state.Owned)
                if (kv.Value <= 0) throw new SaveCorruptedException("보유 수가 0 이하인 캐릭터: " + kv.Key);
            foreach (var kv in state.ClearedStages)
                if (kv.Value < 1 || kv.Value > 3) throw new SaveCorruptedException("별 수가 잘못된 스테이지: " + kv.Key);
            return state;
        }
    }
}
