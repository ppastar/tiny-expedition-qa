using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TinyExpedition.Core.Data;

namespace TinyExpedition.Tests
{
    /// <summary>
    /// 그림 리소스 검사. 데이터에 있는 캐릭터·적마다 그림 파일이 있는지, SVG 원본을 고치고 PNG를 다시 안 만든 것은 없는지 본다.
    /// 파일이 빠져도 게임은 기본 그림으로 돌아가므로(ART-006), 빠진 것은 화면이 아니라 이 테스트가 잡는다.
    /// </summary>
    [Category("Art")]
    public class ArtTests
    {
        private sealed class Unit
        {
            public string Key, Hash, Blink;
            public List<string> Layers;
            public IEnumerable<string> Files => Layers.Concat(new[] { "full" }).Concat(string.IsNullOrEmpty(Blink) ? new string[0] : new[] { "blink" });
        }

        private static string StreamingAssets => Path.GetDirectoryName(TestData.GameDataDir());
        private static string ArtDir => Path.Combine(StreamingAssets, "BattleArt");
        private static string SkinDir => Path.Combine(StreamingAssets, "Skins");
        /// <summary>SVG 원본 폴더 (저장소 루트의 art/units).</summary>
        private static string SourceDir => Path.GetFullPath(Path.Combine(StreamingAssets, "..", "..", "..", "art", "units"));
        /// <summary>무대 소품 SVG 원본 폴더 (art/props).</summary>
        private static string PropSourceDir => Path.GetFullPath(Path.Combine(StreamingAssets, "..", "..", "..", "art", "props"));

        /// <summary>홈 로비가 세우는 소품과, 따로 움직이는 부위(불꽃 = head)가 꼭 있어야 하는 것.</summary>
        public static readonly (string Key, bool NeedsHead)[] LobbyProps = { ("p_tent", false), ("p_signpost", false), ("p_campfire", true) };

        private static Dictionary<string, Unit> Manifest()
        {
            var path = Path.Combine(ArtDir, "manifest.json");
            Assert.That(File.Exists(path), Is.True, "그림 목록이 없습니다: " + path);
            return JObject.Parse(File.ReadAllText(path))["units"].Select(u => new Unit
            {
                Key = (string)u["key"],
                Hash = (string)u["hash"],
                Blink = (string)u["blink"],
                Layers = u["layers"].Select(l => (string)l).ToList(),
            }).ToDictionary(u => u.Key);
        }

        private static List<string> MissingFiles(Unit unit) =>
            unit.Files.Select(f => unit.Key + "." + f + ".png").Where(f => !File.Exists(Path.Combine(ArtDir, f))).ToList();

        /// <summary>PNG 머리말에서 가로·세로를 읽는다 (IHDR).</summary>
        private static (int W, int H) PngSize(string path)
        {
            var b = File.ReadAllBytes(path);
            Assert.That(b.Length > 24 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G', Is.True, "PNG가 아닙니다: " + path);
            int Be(int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
            return (Be(16), Be(20));
        }

        [Test, TC("ART-001")]
        public void EveryCharacter_HasBattleArtAndCard()
        {
            var manifest = Manifest();
            var problems = new List<string>();
            foreach (var c in TestData.LoadReal().Characters)
            {
                string key = ArtCatalog.AllyKey(c.Id);
                if (!manifest.TryGetValue(key, out var unit)) problems.Add(c.Id + " (" + c.Name + "): 전투 그림 없음");
                else
                {
                    problems.AddRange(MissingFiles(unit).Select(f => c.Id + ": " + f + " 없음"));
                    if (!unit.Layers.Contains("head")) problems.Add(c.Id + ": 머리 부위(head)가 없어 까딱임·깜빡임이 빠짐");
                    if (string.IsNullOrEmpty(unit.Blink)) problems.Add(c.Id + ": 감은 눈 그림 없음");
                }
                if (!File.Exists(Path.Combine(SkinDir, c.Id + ".png"))) problems.Add(c.Id + ": 모집 카드 그림(Skins) 없음");
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test, TC("ART-002")]
        public void EveryTurnEnemy_HasBattleArt()
        {
            var manifest = Manifest();
            var problems = new List<string>();
            var names = TestData.LoadReal().TurnStages.SelectMany(s => s.Waves).SelectMany(w => w.Enemies).Select(e => e.Name).Distinct();
            foreach (var name in names)
            {
                string key = ArtCatalog.EnemyKey(name);
                if (key == null) problems.Add(name + ": 그림 종류를 못 정함 (기본 덩어리로 보임)");
                else if (!manifest.TryGetValue(key, out var unit)) problems.Add(name + ": " + key + " 그림 없음");
                else problems.AddRange(MissingFiles(unit).Select(f => name + ": " + f + " 없음"));
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test, TC("ART-003")]
        public void EnemyKey_PicksKindByName_UnknownIsNull()
        {
            Assert.That(ArtCatalog.EnemyKey("동굴 박쥐"), Is.EqualTo("e_bat"));
            Assert.That(ArtCatalog.EnemyKey("독거미"), Is.EqualTo("e_spider"));
            Assert.That(ArtCatalog.EnemyKey("돌껍질 달팽이"), Is.EqualTo("e_snail"));
            Assert.That(ArtCatalog.EnemyKey("늪 개구리"), Is.EqualTo("e_frog"));
            Assert.That(ArtCatalog.EnemyKey("수액 요정"), Is.EqualTo("e_fairy"));
            Assert.That(ArtCatalog.EnemyKey("이끼 골렘"), Is.EqualTo("e_golem"));
            Assert.That(ArtCatalog.EnemyKey("들쥐"), Is.Null, "준비 안 된 종류는 null → 기본 그림");
            Assert.That(ArtCatalog.EnemyKey(""), Is.Null);
            Assert.That(ArtCatalog.EnemyKey(null), Is.Null);
            Assert.That(ArtCatalog.EnemyKinds.Select(k => k.Value).Distinct().Count(), Is.EqualTo(ArtCatalog.EnemyKinds.Count), "그림 키 중복");
        }

        [Test, TC("ART-004")]
        public void Png_IsUpToDateWithSvgSource()
        {
            var manifest = Manifest();
            Assert.That(Directory.Exists(SourceDir), Is.True, "SVG 원본 폴더가 없습니다: " + SourceDir);
            var problems = new List<string>();
            var sources = Directory.GetFiles(SourceDir, "*.svg")
                .Concat(Directory.Exists(PropSourceDir) ? Directory.GetFiles(PropSourceDir, "*.svg") : new string[0])
                .ToDictionary(Path.GetFileNameWithoutExtension);
            foreach (var src in sources)
            {
                if (!manifest.TryGetValue(src.Key, out var unit)) { problems.Add(src.Key + ": SVG는 있는데 PNG를 만들지 않음"); continue; }
                // render.mjs와 같은 방식: 줄바꿈을 LF로 맞춘 본문의 SHA-256 앞 16자
                var text = File.ReadAllText(src.Value, Encoding.UTF8).Replace("\r\n", "\n");
                string hash;
                using (var sha = SHA256.Create())
                    hash = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(x => x.ToString("x2"))).Substring(0, 16);
                if (hash != unit.Hash) problems.Add(src.Key + ": SVG를 고친 뒤 PNG를 다시 만들지 않음 (art 폴더에서 pnpm render)");
            }
            problems.AddRange(manifest.Keys.Where(k => !sources.ContainsKey(k)).Select(k => k + ": PNG는 있는데 SVG 원본이 없음"));
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test, TC("ART-005")]
        public void LayersOfOneUnit_ShareTheSameCanvas()
        {
            // 부위 그림은 같은 크기·같은 기준점이라는 전제로 겹쳐 그린다. 크기가 다르면 머리가 몸에서 어긋난다.
            var problems = new List<string>();
            foreach (var unit in Manifest().Values)
            {
                var sizes = unit.Files.Select(f => Path.Combine(ArtDir, unit.Key + "." + f + ".png")).Where(File.Exists).Select(PngSize).Distinct().ToList();
                if (sizes.Count != 1) problems.Add(unit.Key + ": 부위 그림 크기가 서로 다름 " + string.Join(", ", sizes));
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test, TC("ART-008")]
        public void LobbyProps_HaveArt()
        {
            var manifest = Manifest();
            var problems = new List<string>();
            foreach (var (key, needsHead) in LobbyProps)
            {
                if (!manifest.TryGetValue(key, out var unit)) { problems.Add(key + ": 소품 그림 없음 (art/props에서 pnpm render)"); continue; }
                problems.AddRange(MissingFiles(unit).Select(f => key + ": " + f + " 없음"));
                if (needsHead && !unit.Layers.Contains("head")) problems.Add(key + ": 따로 움직일 부위(head)가 없음");
                if (!File.Exists(Path.Combine(PropSourceDir, key + ".svg"))) problems.Add(key + ": SVG 원본이 art/props에 없음");
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }
    }
}
