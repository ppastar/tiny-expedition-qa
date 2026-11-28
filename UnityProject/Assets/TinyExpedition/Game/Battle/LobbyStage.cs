using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace TinyExpedition.Game.Battle
{
    /// <summary>
    /// 홈 화면의 2.5D 로비. 전투와 같은 무대 부품(StageKit)과 같은 캐릭터 일러스트로 원정대 야영지를 세우고,
    /// 파티(보유 캐릭터 중 원정에 나갈 3명)가 모닥불 둘레에서 대기 동작을 한다. 캐릭터를 누르면 뛰어오르며 한마디 한다.
    ///
    /// 보여 주기만 한다. 누가 서는지는 GameApp이 Core 상태(보유 캐릭터 → 자동 편성)로 정해서 넘기고,
    /// 탭 반응은 세이브·게임 난수를 건드리지 않는다(연출 시간만 씀). 보유 캐릭터가 없으면 모집 중인 픽업 캐릭터를 대신 세운다.
    ///
    /// 요소 이름 계약: Btn_LobbyUnit_&lt;i&gt;(캐릭터 위 투명 버튼), Label_LobbyName_&lt;i&gt;, Label_Speech.
    /// </summary>
    public sealed class LobbyStage : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        /// <summary>무대에 선 캐릭터 id (왼쪽 자리부터). 테스트가 Core의 자동 편성과 비교한다.</summary>
        public IReadOnlyList<string> ShownIds => _units.Select(u => u.Id).ToList();
        /// <summary>true면 보유 캐릭터가 없어 픽업 캐릭터를 대신 세운 것.</summary>
        public bool IsShowcase { get; private set; }
        /// <summary>탭 반응 횟수 (QA 확인용).</summary>
        public int Reactions { get; private set; }
        /// <summary>마지막으로 반응한 자리. 없으면 -1.</summary>
        public int LastReacted { get; private set; } = -1;

        private sealed class Unit
        {
            public string Id;
            public int Slot;
            public BattleArt.UnitArt Art;
            public Transform Root;
            public SpriteRenderer Body, Back, Wings, Head, Shadow;
            public Vector3 Home;
            public float Phase, Height, Hop, HopT = -1f, Joy;
            public bool FaceLeft;
            public RectTransform BtnRt, Tag;
            public int Lines;
        }

        private readonly List<Unit> _units = new List<Unit>();
        private readonly List<Transform> _fireflies = new List<Transform>();
        private SpriteRenderer _fireBody, _fireHead, _fireGlow;
        private BattleArt.UnitArt _fireArt;
        private RectTransform _hud, _speech;
        private Text _speechText;
        private Unit _speaker;
        private float _time, _speechLeft;

        // 자리: 파티 셋은 가운데에 살짝 휜 줄로, 모닥불은 왼쪽 천막 앞. 오른쪽(모집 패널)과 아래(메뉴)는 비워 둔다.
        private static readonly Vector3[] Spots = { new Vector3(-3.0f, 0, 1.3f), new Vector3(-0.7f, 0, 0.6f), new Vector3(1.6f, 0, 1.3f) };
        private static readonly Vector3 ShowcaseSpot = new Vector3(-0.7f, 0, 0.6f);
        private static readonly Vector3 FireSpot = new Vector3(-5.2f, 0, 3.6f);
        private const float CamX = 1.0f;

        /// <param name="partyIds">무대에 세울 캐릭터 id. 비어 있으면 showcaseId 한 명을 세운다.</param>
        public static LobbyStage Create(Transform parent, IReadOnlyList<string> partyIds, string showcaseId, RectTransform hud, Func<string, string> nameOf)
        {
            var go = new GameObject("LobbyStage");
            go.transform.SetParent(parent, false);
            var stage = go.AddComponent<LobbyStage>();
            stage.Build(partyIds, showcaseId, hud, nameOf);
            return stage;
        }

        private void Build(IReadOnlyList<string> partyIds, string showcaseId, RectTransform hud, Func<string, string> nameOf)
        {
            _hud = hud;
            var theme = BattleArt.ThemeFor(BattleArt.LobbyTheme);
            Cam = StageKit.CreateCamera(transform, "LobbyCamera", theme.SkyTop);
            PlaceCamera();

            // 소품은 화면 가장자리와 먼 곳에만 둬서 캐릭터를 가리지 않게
            StageKit.BuildScenery(transform, BattleArt.LobbyTheme, _fireflies, new[]
            {
                new Vector4(-3.6f, 12f, 1, 1.5f), new Vector4(1.4f, 13.5f, 3, 1.5f), new Vector4(8.5f, 10f, 2, 1.6f),
                new Vector4(12.5f, 6.0f, 3, 1.5f), new Vector4(-12.5f, 4.5f, 2, 1.3f), new Vector4(-9.0f, 13.5f, 0, 2.0f), new Vector4(5.4f, 14.5f, 1, 1.8f),
            });

            Prop("Prop_Tent", "p_tent", new Vector3(-7.4f, 0, 7.0f), 1.3f, false);
            Prop("Prop_Signpost", "p_signpost", new Vector3(3.9f, 0, 5.2f), 1.0f, false);
            _fireArt = BattleArt.PropArt("p_campfire");
            _fireGlow = StageKit.MakeSprite(transform, "Fx_FireGlow", BattleArt.Dot(), FireSpot + new Vector3(0, 0.03f, 0), -4400);
            _fireGlow.transform.localRotation = Quaternion.Euler(90, 0, 0);
            _fireGlow.color = Paint.Alpha(Paint.Hex(0xffc860), 0.45f);
            _fireBody = StageKit.MakeSprite(transform, "Prop_Campfire", _fireArt.Body, FireSpot, StageKit.Order(FireSpot.z) + 1000);
            if (_fireArt.Head != null) _fireHead = StageKit.MakeSprite(_fireBody.transform, "Flame", _fireArt.Head, Vector3.zero, _fireBody.sortingOrder + 1);

            var ids = partyIds != null && partyIds.Count > 0 ? partyIds.Take(Spots.Length).ToList() : new List<string> { showcaseId };
            IsShowcase = partyIds == null || partyIds.Count == 0;
            for (int i = 0; i < ids.Count; i++)
            {
                var home = IsShowcase ? ShowcaseSpot : Spots[i];
                // 오른쪽 끝 자리는 가운데를 본다 (일러스트 원본은 오른쪽을 봄)
                var u = MakeUnit(ids[i], i, home, !IsShowcase && i == Spots.Length - 1);
                BuildUnitHud(u, nameOf(ids[i]) + (IsShowcase ? "  · 모집 중" : ""));
                _units.Add(u);
            }
            BuildSpeech();
        }

        private void Prop(string name, string key, Vector3 at, float scale, bool flip)
        {
            var art = BattleArt.PropArt(key);
            var sr = StageKit.MakeSprite(transform, name, art.Full, at, StageKit.Order(at.z) + 1000);
            sr.transform.localScale = Vector3.one * scale;
            sr.flipX = flip;
        }

        private Unit MakeUnit(string id, int slot, Vector3 home, bool faceLeft)
        {
            var art = BattleArt.AllyArt(id);
            var u = new Unit { Id = id, Slot = slot, Art = art, Home = home, Phase = slot * 1.7f, FaceLeft = faceLeft };
            var root = new GameObject("LobbyUnit_" + slot);
            root.transform.SetParent(transform, false);
            root.transform.localPosition = home;
            u.Root = root.transform;
            u.Shadow = StageKit.MakeSprite(u.Root, "Shadow", BattleArt.Shadow(), new Vector3(0, 0.02f, 0), -4500);
            u.Shadow.transform.localRotation = Quaternion.Euler(90, 0, 0);
            float width = art.Full.bounds.size.x;
            u.Shadow.transform.localScale = new Vector3(width * 0.75f, width * 0.36f, 1f);
            u.Body = StageKit.MakeSprite(u.Root, "Body", art.Body, Vector3.zero, 0);
            if (art.Back != null) u.Back = StageKit.MakeSprite(u.Body.transform, "Back", art.Back, Vector3.zero, 0);
            if (art.Wings != null) u.Wings = StageKit.MakeSprite(u.Body.transform, "Wings", art.Wings, Vector3.zero, 0);
            if (art.Head != null) u.Head = StageKit.MakeSprite(u.Body.transform, "Head", art.Head, Vector3.zero, 0);
            foreach (var sr in Layers(u)) sr.flipX = faceLeft;
            u.Height = art.Full.bounds.size.y;
            return u;
        }

        private static IEnumerable<SpriteRenderer> Layers(Unit u)
        {
            if (u.Back != null) yield return u.Back;
            if (u.Wings != null) yield return u.Wings;
            yield return u.Body;
            if (u.Head != null) yield return u.Head;
        }

        private void BuildUnitHud(Unit u, string name)
        {
            if (_hud == null) return;
            var btn = UiKit.Button("Btn_LobbyUnit_" + u.Slot, _hud, "", () => React(u.Slot), new Color(1, 1, 1, 0));
            u.BtnRt = (RectTransform)btn.transform;
            u.BtnRt.anchorMin = u.BtnRt.anchorMax = u.BtnRt.pivot = new Vector2(0.5f, 0.5f);

            var tag = UiKit.Panel_("NameTag_" + u.Slot, _hud, new Color(0.1f, 0.09f, 0.2f, 0.62f));
            UiKit.Round(tag.GetComponent<Image>());
            tag.GetComponent<Image>().raycastTarget = false;
            tag.anchorMin = tag.anchorMax = tag.pivot = new Vector2(0.5f, 0.5f);
            tag.sizeDelta = new Vector2(Mathf.Max(150, name.Length * 26 + 36), 46);
            var label = UiKit.Label("Label_LobbyName_" + u.Slot, tag, name, 24, Color.white, TextAnchor.MiddleCenter);
            UiKit.Stretch(label.rectTransform);
            label.raycastTarget = false;
            u.Tag = tag;
        }

        private void BuildSpeech()
        {
            if (_hud == null) return;
            _speech = UiKit.Panel_("SpeechBubble", _hud, new Color(1f, 0.98f, 0.93f, 0.96f));
            UiKit.Round(_speech.GetComponent<Image>());
            _speech.GetComponent<Image>().raycastTarget = false;
            _speech.anchorMin = _speech.anchorMax = new Vector2(0.5f, 0.5f);
            _speech.pivot = new Vector2(0.5f, 0f);
            _speech.sizeDelta = new Vector2(420, 76);
            _speechText = UiKit.Label("Label_Speech", _speech, "", 28, Paint.Hex(0x3a2f58), TextAnchor.MiddleCenter);
            UiKit.Stretch(_speechText.rectTransform);
            _speechText.raycastTarget = false;
            _speech.gameObject.SetActive(false);
        }

        /// <summary>그 자리의 캐릭터가 뛰어오르며 한마디 한다. 게임 상태는 바꾸지 않는다.</summary>
        public void React(int slot)
        {
            var u = _units.FirstOrDefault(x => x.Slot == slot);
            if (u == null) return;
            Reactions++;
            LastReacted = slot;
            u.HopT = 0f;
            u.Joy = 1f;
            var lines = LinesFor(u.Id);
            string line = lines[u.Lines++ % lines.Length]; // 누를 때마다 다음 대사 (난수 없음)
            _speaker = u;
            _speechLeft = 1.8f;
            if (_speech != null)
            {
                _speechText.text = line;
                _speech.sizeDelta = new Vector2(Mathf.Max(260, line.Length * 27 + 60), 76);
                _speech.gameObject.SetActive(true);
            }
        }

        /// <summary>캐릭터별 짧은 대사 (이 프로젝트에서 쓴 글).</summary>
        private static string[] LinesFor(string id)
        {
            switch (id)
            {
                case "c_acorn": return new[] { "길은 제가 먼저 살펴볼게요!", "도토리 모자, 오늘도 반짝!" };
                case "c_moss": return new[] { "뒤는 걱정 마세요.", "이끼 방패는 푹신해요." };
                case "c_reed": return new[] { "한 곡 불어 드릴까요?", "삐리리~ 출발 신호예요!" };
                case "c_firefly": return new[] { "밤길은 제 불빛으로!", "과녁 한가운데, 맞혔어요!" };
                case "c_mushroom": return new[] { "따뜻한 수프 끓여 놨어요.", "다치면 바로 말해요!" };
                case "c_pebble": return new[] { "조약돌처럼 단단하게!", "방패 손질 끝났습니다." };
                case "c_lumi": return new[] { "달이 뜨면 힘이 나요.", "별빛 마법 보여 줄까요?" };
                case "c_kai": return new[] { "바람이 좋네. 가 볼까?", "한 번에 베어 낼게." };
                case "c_ella": return new[] { "새벽이 올 때까지 지킬게요.", "모두 무사히 돌아와요." };
                default: return new[] { "준비됐어요!", "다음 원정은 어디예요?" };
            }
        }

        private void PlaceCamera()
        {
            var pos = StageKit.CameraPosition(Cam, CamX + Mathf.Sin(_time * 0.3f) * 0.25f);
            Cam.transform.localPosition = pos;
            Cam.transform.localRotation = Quaternion.Euler(StageKit.CamPitch, 0, 0);
        }

        private Vector2 ToHud(Vector3 world)
        {
            var sp = Cam.WorldToScreenPoint(transform.TransformPoint(world));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_hud, sp, null, out var local);
            return local;
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            _time += dt;
            foreach (var u in _units)
            {
                if (u.HopT >= 0f)
                {
                    u.HopT += dt / 0.5f;
                    u.Hop = Mathf.Sin(Mathf.Clamp01(u.HopT) * Mathf.PI) * 0.6f;
                    if (u.HopT >= 1f) { u.HopT = -1f; u.Hop = 0f; }
                }
                u.Joy = Mathf.MoveTowards(u.Joy, 0f, dt * 0.9f);
            }
            if (_speechLeft > 0f)
            {
                _speechLeft -= dt;
                if (_speechLeft <= 0f && _speech != null) _speech.gameObject.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            if (Cam == null) return;
            PlaceCamera();
            var face = Cam.transform.rotation;
            StageKit.DriftFireflies(_fireflies, face, _time);

            // 모닥불: 불꽃이 일렁이고 바닥 빛이 숨 쉬듯
            _fireBody.transform.rotation = face;
            _fireBody.transform.localScale = Vector3.one * 1.1f;
            if (_fireHead != null)
            {
                float flick = Mathf.Sin(_time * 11f) * 0.05f + Mathf.Sin(_time * 7.3f + 1f) * 0.04f;
                StageKit.Pose(_fireHead, _fireArt.Neck, Mathf.Sin(_time * 5f) * 3f, new Vector2(1f - flick * 0.6f, 1f + flick), 0f);
            }
            float glow = 2.6f + Mathf.Sin(_time * 6f) * 0.12f;
            _fireGlow.transform.localScale = new Vector3(glow * 2.2f, glow, 1f);

            foreach (var u in _units)
            {
                float breathe = 1f + Mathf.Sin(_time * 3f + u.Phase) * (u.Art.Illustrated ? 0.014f : 0.025f);
                float squash = 1f + (u.HopT >= 0f && u.HopT < 0.15f ? -0.06f : 0f);
                var body = u.Body.transform;
                body.localPosition = new Vector3(0, u.Hop, 0);
                body.rotation = face * Quaternion.Euler(0, 0, Mathf.Sin(_time * 14f) * 4f * u.Joy);
                body.localScale = new Vector3(2f - squash * breathe, squash * breathe, 1f);
                int order = StageKit.Order(u.Home.z) + 1000;
                u.Body.sortingOrder = order;
                if (u.Art.Illustrated)
                    StageKit.IdleParts(u.Art, u.Body, u.Head, u.Back, u.Wings, _time, u.Phase, 0f, order, u.Joy > 0.55f);
                u.Shadow.transform.localScale = new Vector3(1f, 0.48f, 1f) * (u.Art.Full.bounds.size.x * 0.75f) * (1f - u.Hop * 0.4f);
            }
            UpdateHud();
        }

        private void UpdateHud()
        {
            if (_hud == null) return;
            foreach (var u in _units)
            {
                if (u.BtnRt == null) continue;
                var head = ToHud(u.Home + Vector3.up * (u.Height + u.Hop));
                var feet = ToHud(u.Home);
                float h = Mathf.Max(120f, head.y - feet.y);
                u.BtnRt.anchoredPosition = (head + feet) * 0.5f;
                u.BtnRt.sizeDelta = new Vector2(Mathf.Max(150f, h * 0.7f), h);
                u.Tag.anchoredPosition = feet + new Vector2(0, -22);
                if (u == _speaker && _speech != null) _speech.anchoredPosition = head + new Vector2(0, 16);
            }
        }
    }
}
