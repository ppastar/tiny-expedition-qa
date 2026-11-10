using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TinyExpedition.Core.Data
{
    /// <summary>
    /// 기획 데이터 테이블(JSON) 묶음. Unity와 dotnet 도구가 같은 파일을 읽는다.
    /// </summary>
    public sealed class GameDatabase
    {
        public const string CharactersFile = "characters.json";
        public const string BannersFile = "banners.json";
        public const string StagesFile = "stages.json";

        public IReadOnlyList<CharacterDef> Characters { get; }
        public IReadOnlyList<BannerDef> Banners { get; }
        public IReadOnlyList<StageDef> Stages { get; }

        private readonly Dictionary<string, CharacterDef> _charById = new Dictionary<string, CharacterDef>();

        public GameDatabase(IEnumerable<CharacterDef> characters, IEnumerable<BannerDef> banners, IEnumerable<StageDef> stages)
        {
            Characters = characters.ToList();
            Banners = banners.ToList();
            Stages = stages.ToList();
            foreach (var c in Characters)
            {
                // 중복 id는 DataValidator가 잡는다. 여기서는 처음 것만 쓴다.
                if (!_charById.ContainsKey(c.Id)) _charById[c.Id] = c;
            }
        }

        public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            MissingMemberHandling = MissingMemberHandling.Error,
        };

        /// <summary>파일 이름 → JSON 문자열을 주는 함수로 읽는다 (Unity: StreamingAssets, dotnet: 파일).</summary>
        public static GameDatabase Load(Func<string, string> readFile)
        {
            var chars = Parse<List<CharacterDef>>(readFile(CharactersFile), CharactersFile);
            var banners = Parse<List<BannerDef>>(readFile(BannersFile), BannersFile);
            var stages = Parse<List<StageDef>>(readFile(StagesFile), StagesFile);
            return new GameDatabase(chars, banners, stages);
        }

        private static T Parse<T>(string json, string fileName) where T : class
        {
            T value;
            try
            {
                value = JsonConvert.DeserializeObject<T>(json, JsonSettings);
            }
            catch (JsonException e)
            {
                throw new GameDataException(fileName + ": JSON 형식 오류 - " + e.Message, e);
            }
            if (value == null) throw new GameDataException(fileName + ": 내용이 비어 있습니다.");
            return value;
        }

        public CharacterDef GetCharacter(string id)
        {
            if (_charById.TryGetValue(id, out var c)) return c;
            throw new KeyNotFoundException("캐릭터 없음: " + id);
        }

        public bool HasCharacter(string id) => _charById.ContainsKey(id);

        public BannerDef GetBanner(string id) =>
            Banners.FirstOrDefault(b => b.Id == id) ?? throw new KeyNotFoundException("배너 없음: " + id);

        public StageDef GetStage(string id) =>
            Stages.FirstOrDefault(s => s.Id == id) ?? throw new KeyNotFoundException("스테이지 없음: " + id);
    }

    public sealed class GameDataException : Exception
    {
        public GameDataException(string message) : base(message) { }
        public GameDataException(string message, Exception inner) : base(message, inner) { }
    }
}
