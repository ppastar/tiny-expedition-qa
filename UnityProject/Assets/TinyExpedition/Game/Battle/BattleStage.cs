using System;
using System.Collections.Generic;
using System.Linq;
using TinyExpedition.Core.Battle;
using TinyExpedition.Core.Data;
using UnityEngine;
using UnityEngine.UI;

namespace TinyExpedition.Game.Battle
{
    /// <summary>
    /// 턴제 전투의 2.5D 무대. 3D 공간(원근 카메라·바닥·깊이가 다른 배경)에 2D 캐릭터 그림을 세워 놓고,
    /// Core가 남긴 전투 로그를 한 줄씩 재생한다(전진 → 타격 → 복귀, 피격, 숫자, 상태이상, 쓰러짐, 웨이브 전환).
    ///
    /// 규칙 계산은 하지 않는다. 화면에 보이는 HP·생존·상태는 전부 <see cref="TurnBattleDisplay"/>(로그 재생 결과)에서 오고,
    /// 재생이 끝날 때마다 Core 상태와 대조한다(<see cref="MismatchCount"/>). 배속·건너뛰기는 재생 속도만 바꾸므로 전투 결과와 무관하다.
    ///
    /// 유닛 머리 위 HUD 이름 계약: Label_Hp_A&lt;i&gt; / Label_Hp_E&lt;i&gt; ("현재/최대"), Label_Status_*, Btn_Ally_&lt;i&gt;, Btn_Target_&lt;i&gt;.
    /// </summary>
    public sealed class BattleStage : MonoBehaviour
    {
        public TurnBattle Battle { get; private set; }
        public TurnBattleDisplay Display { get; private set; }
        public Camera Cam { get; private set; }

        /// <summary>재생 배속. 1 = 기본.</summary>
        public float Speed = 1f;
        /// <summary>로그를 재생하는 중인지. 재생 중에는 명령을 받지 않는다.</summary>
        public bool IsPlaying => _seq != null;
        /// <summary>재생이 끝났을 때 화면 상태가 Core와 달랐던 항목 수 (0이어야 정상).</summary>
        public int MismatchCount { get; private set; }
        /// <summary>대조한 횟수.</summary>
        public int SyncChecks { get; private set; }
        /// <summary>지금까지 연출로 재생한(건너뛰지 않은) 로그 줄 수.</summary>
        public int AnimatedEntries { get; private set; }

        /// <summary>발밑 표식을 띄울 대상. -1이면 없음.</summary>
        public int SelectedEnemy = -1, SelectedAlly = -1;
        public Action<int> OnAllyClicked, OnEnemyClicked;
        public Action OnPlaybackFinished;

        private sealed class UnitView
        {
            public DisplayUnit Model;
            public Transform Root;
            public SpriteRenderer Body, Shadow, Shield;
            public SpriteRenderer[] Stars, Bubbles;
            public Vector3 Home, Offset;
            public float Hover, Height, Squash = 1f, Tilt, Fade = 1f, Phase, Flash;
            public Color FlashColor = Color.red;
            public bool Fallen;
            // 머리 위 HUD
            public RectTransform Hud, HpFill, BtnRt;
            public Text NameLabel, HpLabel, StatusLabel;
            public Image HpFillImage;
            public Button Btn;
            public float ShownFill = 1f;
        }

        private sealed class Tween
        {
            public float T, Duration;
            public Action<float> Apply;
            public Action Done;
        }

        private IReadOnlyList<string> _partyIds;
        private RectTransform _hud;
        private BattleArt.Theme _theme;
        private readonly List<UnitView> _allies = new List<UnitView>();
        private List<UnitView> _enemies = new List<UnitView>();
        private int _shownWave;
        private readonly List<Tween> _tweens = new List<Tween>();
        private IEnumerator<float> _seq;
        private float _wait, _time, _shake, _focusX, _camX;
        private bool _cheer;
        private int _popupCount;
        private SpriteRenderer _ringActor, _ringEnemy, _ringAlly;
        private readonly List<Transform> _fireflies = new List<Transform>();
        private readonly List<float> _frameMs = new List<float>();
        private int _frames;

        private static readonly Vector3[] AllySpots = { new Vector3(-1.9f, 0, 0.2f), new Vector3(-3.6f, 0, 1.5f), new Vector3(-4.0f, 0, -0.9f) };
        private static readonly Vector3 CamBase = new Vector3(0, 3.6f, -12.5f);
        private const float CamPitch = 11f;

        public static BattleStage Create(Transform parent, TurnBattle battle, IReadOnlyList<string> partyIds, RectTransform hud)
        {
            var go = new GameObject("BattleStage");
            go.transform.SetParent(parent, false);
            var stage = go.AddComponent<BattleStage>();
            stage.Build(battle, partyIds, hud);
            return stage;
        }

        // ---------------------------------------------------------------------
        // 무대 만들기
        // ---------------------------------------------------------------------

        private void Build(TurnBattle battle, IReadOnlyList<string> partyIds, RectTransform hud)
        {
            Battle = battle;
            Display = new TurnBattleDisplay(battle);
            _partyIds = partyIds;
            _hud = hud;
            _theme = BattleArt.ThemeFor(battle.Stage.Id);

            var camGo = new GameObject("BattleCamera");
            camGo.transform.SetParent(transform, false);
            Cam = camGo.AddComponent<Camera>();
            Cam.orthographic = false;
            Cam.fieldOfView = 28f;
            Cam.nearClipPlane = 0.3f;
            Cam.farClipPlane = 120f;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = _theme.SkyTop;
            Cam.depth = 10; // Main 씬의 기본 카메라 위에 그린다
            PlaceCamera();

            BuildScenery();

            _ringActor = Mark("Mark_Actor", Paint.Hex(0xffd23f));
            _ringEnemy = Mark("Mark_Target", Paint.Hex(0xff5a6e));
            _ringAlly = Mark("Mark_AllyTarget", Paint.Hex(0x5cf2a0));

            for (int i = 0; i < Display.Allies.Count; i++)
            {
                var art = BattleArt.Ally(i < partyIds.Count ? partyIds[i] : Display.Allies[i].Name);
                var v = MakeUnit(Display.Allies[i], art, 0f, AllySpots[i], false);
                _allies.Add(v);
                Enter(v, -7f, i * 0.08f);
            }
            SpawnEnemyViews(true);
        }

        private void BuildScenery()
        {
            string id = Battle.Stage.Id;

            var back = MakeSprite("Backdrop", BattleArt.Backdrop(id), new Vector3(0, -2.5f, 17f), -6000);
            back.transform.localScale = Vector3.one;

            // 바닥: 무늬를 반복해서 깐 평면. 멀어질수록 안개 색으로.
            var ground = new GameObject("Ground", typeof(MeshFilter), typeof(MeshRenderer));
            ground.transform.SetParent(transform, false);
            var mesh = new Mesh { name = "Ground" };
            float[] zs = { -8f, 3f, 17f };
            var verts = new Vector3[6];
            var uvs = new Vector2[6];
            var cols = new Color[6];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 2; c++)
                {
                    float x = c == 0 ? -26f : 26f;
                    verts[r * 2 + c] = new Vector3(x, 0, zs[r]);
                    uvs[r * 2 + c] = new Vector2(x / 3f, zs[r] / 3f);
                    cols[r * 2 + c] = r == 0 ? Color.white : r == 1 ? Color.Lerp(Color.white, _theme.Haze, 0.15f) : Color.Lerp(Color.white, _theme.Haze, 0.8f);
                }
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors = cols;
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 2, 4, 3, 3, 4, 5 };
            mesh.RecalculateBounds();
            ground.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = ground.GetComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(BattleArt.Material) { mainTexture = BattleArt.Ground(id), name = "Ground" };
            mr.sortingOrder = -5000;

            // 깊이가 다른 소품: 카메라가 움직이면 가까운 것이 더 많이 움직여 보인다 (패럴랙스)
            var props = new[]
            {
                new Vector4(-8.0f, 6.5f, 0, 1.7f), new Vector4(-4.2f, 10f, 1, 1.5f), new Vector4(0.6f, 12f, 3, 1.5f), new Vector4(5.2f, 8.5f, 2, 1.6f),
                new Vector4(9.0f, 6.0f, 3, 1.5f), new Vector4(-10.5f, 3.5f, 2, 1.3f), new Vector4(10.8f, 11f, 0, 2.0f), new Vector4(-1.5f, 14f, 1, 1.8f),
                new Vector4(7.0f, 13.5f, 1, 1.6f), new Vector4(-7.0f, 13f, 0, 2.0f),
            };
            foreach (var pr in props)
            {
                var sr = MakeSprite("Prop", BattleArt.PropSprite(id, (int)pr.z), new Vector3(pr.x, 0, pr.y), Order(pr.y));
                sr.transform.localScale = Vector3.one * pr.w;
                // 멀수록 안개에 묻힌다
                sr.color = Color.Lerp(Color.white, _theme.Haze, Mathf.InverseLerp(4f, 18f, pr.y) * 0.6f);
            }
            foreach (float side in new[] { -1f, 1f })
            {
                var fg = MakeSprite("Foreground", BattleArt.Foreground(id), new Vector3(side * 6.6f, -0.9f, -4.6f), Order(-4.6f));
                fg.flipX = side > 0;
                fg.transform.localScale = Vector3.one * 1.5f;
            }

            var rng = new System.Random(3);
            for (int i = 0; i < 18; i++)
            {
                var pos = new Vector3((float)rng.NextDouble() * 18f - 9f, 0.6f + (float)rng.NextDouble() * 3.6f, (float)rng.NextDouble() * 11f - 1.5f);
                var sr = MakeSprite("Firefly", BattleArt.Dot(), pos, Order(pos.z) + 5);
                sr.color = Paint.Alpha(_theme.Glow, 0.9f);
                sr.transform.localScale = Vector3.one * (0.14f + (float)rng.NextDouble() * 0.2f);
                _fireflies.Add(sr.transform);
            }
        }

        private static int Order(float z) => Mathf.RoundToInt(-z * 20f);

        private SpriteRenderer MakeSprite(string name, Sprite sprite, Vector3 pos, int order, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.transform.localPosition = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = BattleArt.Material;
            sr.sortingOrder = order;
            return sr;
        }

        private SpriteRenderer Mark(string name, Color color)
        {
            var sr = MakeSprite(name, BattleArt.RingMark(), Vector3.zero, -4000);
            sr.transform.localRotation = Quaternion.Euler(90, 0, 0);
            sr.color = color;
            sr.enabled = false;
            return sr;
        }

        private Vector3 EnemySpot(int slot, int count)
        {
            if (count == 1) return new Vector3(2.9f, 0, 0.3f);
            var a = AllySpots[slot];
            return new Vector3(-a.x, 0, a.z);
        }

        private UnitView MakeUnit(DisplayUnit model, Sprite art, float hover, Vector3 home, bool enemy)
        {
            var v = new UnitView { Model = model, Home = home, Hover = hover, Phase = model.Slot * 1.7f + (enemy ? 0.9f : 0f) };
            var root = new GameObject((enemy ? "Enemy_" : "Ally_") + model.Slot);
            root.transform.SetParent(transform, false);
            root.transform.localPosition = home;
            v.Root = root.transform;

            v.Shadow = MakeSprite("Shadow", BattleArt.Shadow(), new Vector3(0, 0.02f, 0), -4500, v.Root);
            v.Shadow.transform.localRotation = Quaternion.Euler(90, 0, 0);
            float width = art.bounds.size.x;
            v.Shadow.transform.localScale = new Vector3(width * 0.75f, width * 0.36f, 1f) * (hover > 0 ? 0.7f : 1f);

            v.Body = MakeSprite("Body", art, Vector3.zero, 0, v.Root);
            v.Body.flipX = enemy; // 적은 왼쪽(아군 쪽)을 본다
            v.Height = art.bounds.size.y;

            v.Shield = MakeSprite("Fx_Shield", BattleArt.Shield(), Vector3.zero, 0, v.Root);
            v.Stars = Enumerable.Range(0, 3).Select(i => MakeSprite("Fx_Star", BattleArt.Star(), Vector3.zero, 0, v.Root)).ToArray();
            v.Bubbles = Enumerable.Range(0, 3).Select(i => MakeSprite("Fx_Bubble", BattleArt.Bubble(), Vector3.zero, 0, v.Root)).ToArray();

            BuildUnitHud(v, enemy);
            return v;
        }

        private void BuildUnitHud(UnitView v, bool enemy)
        {
            if (_hud == null) return;
            string key = (enemy ? "E" : "A") + v.Model.Slot;

            // 누를 수 있는 영역 (캐릭터 몸 위의 투명 버튼)
            v.Btn = UiKit.Button(enemy ? "Btn_Target_" + v.Model.Slot : "Btn_Ally_" + v.Model.Slot, _hud, "", () =>
            {
                if (enemy) OnEnemyClicked?.Invoke(v.Model.Slot);
                else OnAllyClicked?.Invoke(v.Model.Slot);
            }, new Color(1, 1, 1, 0));
            v.BtnRt = (RectTransform)v.Btn.transform;
            Center(v.BtnRt, new Vector2(180, 240));

            var hud = new GameObject("UnitHud_" + key, typeof(RectTransform));
            hud.transform.SetParent(_hud, false);
            v.Hud = (RectTransform)hud.transform;
            Center(v.Hud, new Vector2(220, 84));

            v.NameLabel = UiKit.Label("Label_Name_" + key, v.Hud, v.Model.Name, 22, Color.white, TextAnchor.MiddleCenter);
            Anchor(v.NameLabel.rectTransform, 0, 0.62f, 1, 1);
            Shadowed(v.NameLabel);

            var barBg = UiKit.Panel_("HpBar", v.Hud, new Color(0.08f, 0.09f, 0.15f, 0.85f));
            barBg.GetComponent<Image>().raycastTarget = false;
            UiKit.Round(barBg.GetComponent<Image>());
            Anchor(barBg, 0.08f, 0.34f, 0.92f, 0.6f);
            var fill = UiKit.Panel_("Fill", barBg, enemy ? (Color)Paint.Hex(0xff6b7a) : (Color)Paint.Hex(0x5ce08a));
            v.HpFillImage = fill.GetComponent<Image>();
            v.HpFillImage.raycastTarget = false;
            UiKit.Round(v.HpFillImage);
            v.HpFill = fill;
            v.HpLabel = UiKit.Label("Label_Hp_" + key, barBg, "", 17, Color.white, TextAnchor.MiddleCenter);
            UiKit.Stretch(v.HpLabel.rectTransform);
            Shadowed(v.HpLabel);

            v.StatusLabel = UiKit.Label("Label_Status_" + key, v.Hud, "", 19, Paint.Hex(0xffe27a), TextAnchor.MiddleCenter);
            Anchor(v.StatusLabel.rectTransform, 0, 0, 1, 0.32f);
            Shadowed(v.StatusLabel);
            foreach (var t in new[] { v.NameLabel, v.HpLabel, v.StatusLabel }) t.raycastTarget = false;
        }

        private static void Center(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
        }

        private static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static Outline Shadowed(Text t)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.1f, 0.08f, 0.18f, 0.95f);
            o.effectDistance = new Vector2(1.5f, -1.5f);
            return o;
        }

        /// <summary>화면에 보이는 웨이브의 적을 세운다. 이전 웨이브의 적(쓰러진 상태)은 치운다.</summary>
        private void SpawnEnemyViews(bool animate)
        {
            foreach (var old in _enemies) DestroyView(old);
            _enemies = new List<UnitView>();
            _shownWave = Display.WaveIndex;
            int n = Display.Enemies.Count;
            for (int i = 0; i < n; i++)
            {
                var m = Display.Enemies[i];
                var art = BattleArt.Enemy(m.Name);
                var v = MakeUnit(m, art.Sprite, art.Hover, EnemySpot(i, n), true);
                _enemies.Add(v);
                if (animate) Enter(v, 7f, i * 0.1f);
            }
        }

        private void DestroyView(UnitView v)
        {
            if (v.Root != null) Destroy(v.Root.gameObject);
            if (v.Hud != null) Destroy(v.Hud.gameObject);
            if (v.Btn != null) Destroy(v.Btn.gameObject);
        }

        private UnitView View(Side side, int slot)
        {
            var list = side == Side.Ally ? _allies : _enemies;
            return slot >= 0 && slot < list.Count ? list[slot] : null;
        }

        // ---------------------------------------------------------------------
        // 재생 제어
        // ---------------------------------------------------------------------

        /// <summary>아직 재생하지 않은 로그가 있으면 연출을 시작한다. 없으면 바로 Core와 대조한다.</summary>
        public void Play()
        {
            if (_seq != null) return;
            if (!Display.HasPending)
            {
                Verify();
                return;
            }
            _seq = Sequence();
            _wait = 0f;
        }

        /// <summary>남은 연출을 건너뛰고 화면을 로그 끝 상태로 맞춘다.</summary>
        public void SkipToEnd()
        {
            foreach (var t in _tweens.ToArray())
            {
                t.Apply?.Invoke(1f);
                t.Done?.Invoke();
            }
            _tweens.Clear();
            _seq = null;
            Display.StepAll();
            if (_shownWave != Display.WaveIndex) SpawnEnemyViews(false);
            foreach (var v in _allies.Concat(_enemies))
            {
                v.Offset = Vector3.zero;
                v.Tilt = 0f;
                v.Squash = 1f;
                v.Flash = 0f;
                v.Fallen = !v.Model.IsAlive;
                v.Fade = v.Fallen ? 0f : 1f;
            }
            _shake = 0f;
            if (Display.Finished) _cheer = Battle.Outcome == TurnOutcome.Victory;
            Verify();
            LateUpdate();
        }

        private void FinishPlayback()
        {
            _seq = null;
            UpdateHud(); // 머리 위 글자를 바로 맞춘다 (LateUpdate를 기다리면 같은 프레임에 읽는 쪽이 한 박자 늦은 값을 본다)
            Verify();
            OnPlaybackFinished?.Invoke();
        }

        /// <summary>재생이 끝난 화면 상태를 Core와 대조한다. 다르면 오류 로그를 남긴다(테스트·자동 플레이가 잡는다).</summary>
        private void Verify()
        {
            SyncChecks++;
            var issues = Display.Mismatches();
            if (issues.Count == 0) return;
            MismatchCount += issues.Count;
            Debug.LogError("[TurnView] 화면과 전투 상태가 다릅니다 (시드 " + Battle.Seed + ", 명령 " + Battle.Commands.Count + "개): " + string.Join(" / ", issues));
        }

        private void Update()
        {
            float raw = Time.unscaledDeltaTime;
            if (++_frames > 10 && _frameMs.Count < 36000) _frameMs.Add(raw * 1000f);

            float dt = Mathf.Min(Time.deltaTime, 0.1f) * Mathf.Max(0.1f, Speed);
            _time += dt;

            for (int i = _tweens.Count - 1; i >= 0; i--)
            {
                var t = _tweens[i];
                t.T += dt;
                float k = t.Duration <= 0 ? 1f : Mathf.Clamp01(t.T / t.Duration);
                t.Apply?.Invoke(k);
                if (k >= 1f)
                {
                    _tweens.RemoveAt(i);
                    t.Done?.Invoke();
                }
            }

            if (_seq != null)
            {
                _wait -= dt;
                int guard = 0;
                while (_seq != null && _wait <= 0f && guard++ < 200)
                {
                    if (_seq.MoveNext()) _wait += _seq.Current;
                    else FinishPlayback();
                }
            }
            _shake = Mathf.MoveTowards(_shake, 0f, dt * 0.9f);
        }

        /// <summary>프레임 시간 요약 (ms). 자동 플레이가 전투 화면의 대략적인 성능을 기록할 때 쓴다.</summary>
        public void FrameStats(out int frames, out float avgMs, out float p95Ms, out float maxMs)
        {
            frames = _frameMs.Count;
            avgMs = p95Ms = maxMs = 0f;
            if (frames == 0) return;
            var sorted = _frameMs.OrderBy(x => x).ToList();
            avgMs = sorted.Average();
            p95Ms = sorted[Mathf.Min(frames - 1, (int)(frames * 0.95f))];
            maxMs = sorted[frames - 1];
        }

        // ---------------------------------------------------------------------
        // 로그 한 줄 → 연출
        // ---------------------------------------------------------------------

        private IEnumerator<float> Sequence()
        {
            UnitView caster = null;
            while (Display.HasPending)
            {
                var e = Display.Peek();
                var actor = e.ActorSlot >= 0 ? View(e.ActorSide, e.ActorSlot) : null;
                var target = e.TargetSlot >= 0 ? View(e.TargetSide, e.TargetSlot) : null;
                if (e.Kind != TurnLogKind.Skill && e.Kind != TurnLogKind.Heal && e.Kind != TurnLogKind.Defeated && e.Kind != TurnLogKind.StatusApplied) caster = null;
                AnimatedEntries++;

                switch (e.Kind)
                {
                    case TurnLogKind.Attack:
                        _focusX = actor.Home.x * 0.06f;
                        Dash(actor, target, 0.2f);
                        yield return 0.2f;
                        Display.Step();
                        Impact(e, target, true);
                        yield return 0.17f;
                        Return(actor, 0.22f);
                        yield return 0.27f;
                        break;

                    case TurnLogKind.Skill:
                    case TurnLogKind.Heal:
                        if (caster != actor)
                        {
                            caster = actor;
                            Banner(e.SkillName + "!", e.ActorSide == Side.Ally ? Paint.Hex(0x7fd0ff) : Paint.Hex(0xff9a8a), 0.9f);
                            Hop(actor, 0.55f, 0.38f);
                            Fx(BattleArt.Dot(), Center(actor), Paint.Alpha(e.Kind == TurnLogKind.Heal ? Paint.Hex(0x7dffb0) : Paint.Hex(0xfff3a0), 0.9f), 0.6f, 3.4f, 0.45f);
                            yield return 0.42f;
                        }
                        Display.Step();
                        if (e.Kind == TurnLogKind.Heal)
                        {
                            Popup(target, "+" + e.Amount, Paint.Hex(0x7dffb0), 54);
                            Fx(BattleArt.Plus(), Center(target), Color.white, 0.7f, 1.3f, 0.6f, 1.1f);
                            Hop(target, 0.2f, 0.3f);
                        }
                        else
                        {
                            Impact(e, target, false);
                        }
                        yield return 0.22f;
                        break;

                    case TurnLogKind.Guard:
                        Display.Step();
                        Popup(actor, "방어", Paint.Hex(0x8fd0ff), 44);
                        Hop(actor, 0.12f, 0.25f);
                        yield return 0.4f;
                        break;

                    case TurnLogKind.Stunned:
                        Display.Step();
                        Popup(actor, "기절", Paint.Hex(0xffe14a), 44);
                        Wobble(actor, 0.4f);
                        yield return 0.45f;
                        break;

                    case TurnLogKind.StatusApplied:
                        Display.Step();
                        if (e.Status == StatusKind.Stun) Popup(target, "기절!", Paint.Hex(0xffe14a), 46);
                        else Popup(target, "독 ×" + e.Amount, Paint.Hex(0xd29bff), 46);
                        yield return 0.24f;
                        break;

                    case TurnLogKind.PoisonTick:
                    {
                        Display.Step();
                        Popup(target, "-" + e.Amount, Paint.Hex(0xd29bff), 50);
                        HitReact(target, Paint.Hex(0xb46bff), 0.3f);
                        var next = Display.Peek();
                        yield return next != null && next.Kind == TurnLogKind.PoisonTick ? 0.14f : 0.45f;
                        break;
                    }

                    case TurnLogKind.Defeated:
                        Display.Step();
                        Fall(target, 0.45f);
                        yield return 0.4f;
                        break;

                    case TurnLogKind.WaveCleared:
                        Display.Step();
                        if (Display.WaveIndex != _shownWave)
                        {
                            yield return 0.3f;
                            Banner("WAVE " + (Display.WaveIndex + 1) + " / " + Battle.WaveCount, Paint.Hex(0xffe27a), 1.1f);
                            SpawnEnemyViews(true);
                            foreach (var a in _allies.Where(a => a.Model.IsAlive)) Hop(a, 0.25f, 0.35f);
                            yield return 0.95f;
                        }
                        break;

                    case TurnLogKind.End:
                        Display.Step();
                        _cheer = Battle.Outcome == TurnOutcome.Victory;
                        Banner(Battle.Outcome == TurnOutcome.Victory ? "승리!" : Battle.Outcome == TurnOutcome.Timeout ? "시간 초과" : "패배…",
                            Battle.Outcome == TurnOutcome.Victory ? Paint.Hex(0xffe27a) : Paint.Hex(0xc8cbe0), 1.2f);
                        yield return 0.9f;
                        break;

                    default:
                        Display.Step();
                        break;
                }
            }
        }

        private Vector3 Center(UnitView v) => v.Home + v.Offset + Vector3.up * (v.Hover + v.Height * 0.5f);
        private Vector3 Head(UnitView v) => v.Home + Vector3.up * (v.Hover + v.Height);

        private void Run(float duration, Action<float> apply, Action done = null) =>
            _tweens.Add(new Tween { Duration = duration, Apply = apply, Done = done });

        private static float EaseOut(float k) => 1f - (1f - k) * (1f - k);

        private void Enter(UnitView v, float fromX, float delay)
        {
            v.Offset = new Vector3(fromX, 0, 0);
            Run(0.45f + delay, k =>
            {
                float t = Mathf.Clamp01((k * (0.45f + delay) - delay) / 0.45f);
                v.Offset = new Vector3(fromX * (1f - EaseOut(t)), Mathf.Sin(t * Mathf.PI) * 0.35f, 0);
            }, () => v.Offset = Vector3.zero);
        }

        private void Dash(UnitView actor, UnitView target, float duration)
        {
            if (actor == null || target == null) return;
            // 대상 바로 앞까지 달려간다
            var to = target.Home - actor.Home;
            to.x -= Mathf.Sign(to.x) * 1.15f;
            to.z -= 0.05f;
            var from = actor.Offset;
            Run(duration, k => actor.Offset = Vector3.Lerp(from, to, EaseOut(k)) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.3f);
            Run(duration + 0.15f, k => actor.Tilt = Mathf.Sin(k * Mathf.PI) * (actor.Home.x < 0 ? -14f : 14f), () => actor.Tilt = 0f);
        }

        private void Return(UnitView actor, float duration)
        {
            if (actor == null) return;
            var from = actor.Offset;
            Run(duration, k => actor.Offset = Vector3.Lerp(from, Vector3.zero, EaseOut(k)) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.2f, () => actor.Offset = Vector3.zero);
        }

        private void Hop(UnitView v, float height, float duration)
        {
            if (v == null) return;
            Run(duration, k =>
            {
                v.Offset = new Vector3(v.Offset.x, Mathf.Sin(k * Mathf.PI) * height, v.Offset.z);
                v.Squash = 1f + Mathf.Sin(k * Mathf.PI * 2f) * 0.08f;
            }, () => { v.Offset = new Vector3(v.Offset.x, 0, v.Offset.z); v.Squash = 1f; });
        }

        private void Wobble(UnitView v, float duration)
        {
            if (v == null) return;
            Run(duration, k => v.Tilt = Mathf.Sin(k * Mathf.PI * 4f) * 9f * (1f - k), () => v.Tilt = 0f);
        }

        private void HitReact(UnitView v, Color flash, float duration)
        {
            if (v == null) return;
            v.FlashColor = flash;
            float dir = v.Home.x < 0 ? -1f : 1f;
            Run(duration, k =>
            {
                v.Flash = 1f - k;
                v.Squash = 1f - Mathf.Sin(k * Mathf.PI) * 0.12f;
                v.Offset = new Vector3(dir * Mathf.Sin(k * Mathf.PI) * 0.28f + Mathf.Sin(k * 40f) * 0.05f * (1f - k), v.Offset.y, v.Offset.z);
            }, () => { v.Flash = 0f; v.Squash = 1f; v.Offset = new Vector3(0, v.Offset.y, v.Offset.z); });
        }

        private void Fall(UnitView v, float duration)
        {
            if (v == null) return;
            v.Fallen = true;
            float dir = v.Home.x < 0 ? 1f : -1f;
            Run(duration, k =>
            {
                v.Tilt = dir * 80f * EaseOut(k);
                v.Fade = 1f - k * k;
                v.Offset = new Vector3(-dir * 0.5f * k, Mathf.Sin(k * Mathf.PI) * 0.4f, 0);
            }, () => { v.Fade = 0f; v.Tilt = 0f; v.Offset = Vector3.zero; });
        }

        private void Impact(TurnLogEntry e, UnitView target, bool melee)
        {
            if (target == null) return;
            var color = e.Crit ? Paint.Hex(0xffd23f) : e.Guarded ? Paint.Hex(0x9ad4ff) : Color.white;
            Popup(target, e.Amount + (e.Crit ? "!" : ""), color, e.Crit ? 78 : e.Guarded ? 44 : 58);
            if (e.Guarded) Popup(target, "방어", Paint.Hex(0x9ad4ff), 30);
            HitReact(target, Paint.Hex(0xff5a5a), 0.32f);
            var at = Center(target);
            Fx(BattleArt.Burst(), at, e.Crit ? (Color)Paint.Hex(0xffe27a) : Color.white, 0.5f, e.Crit ? 1.9f : 1.3f, 0.26f);
            if (melee) Fx(BattleArt.Slash(), at, Color.white, 1.0f, 1.5f, 0.24f, 0f, target.Home.x < 0 ? 140f : 40f);
            _shake = Mathf.Max(_shake, e.Crit ? 0.2f : 0.09f);
        }

        /// <summary>잠깐 커졌다 사라지는 이펙트 그림.</summary>
        private void Fx(Sprite sprite, Vector3 at, Color color, float fromScale, float toScale, float duration, float rise = 0f, float roll = 0f)
        {
            var sr = MakeSprite("Fx", sprite, at, 4000);
            sr.color = color;
            var tr = sr.transform;
            Run(duration, k =>
            {
                if (tr == null) return;
                tr.localPosition = at + Vector3.up * rise * k - Vector3.forward * 0.3f;
                tr.rotation = Cam.transform.rotation * Quaternion.Euler(0, 0, roll);
                tr.localScale = Vector3.one * Mathf.Lerp(fromScale, toScale, EaseOut(k));
                sr.color = Paint.Alpha(color, color.a * (1f - k * k));
            }, () => { if (tr != null) Destroy(tr.gameObject); });
        }

        /// <summary>유닛 머리 위로 떠오르는 숫자·글자.</summary>
        private void Popup(UnitView v, string text, Color color, int size)
        {
            if (v == null || _hud == null) return;
            var label = UiKit.Label("Popup", _hud, text, size, color, TextAnchor.MiddleCenter);
            label.raycastTarget = false;
            label.fontStyle = FontStyle.Bold;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            Shadowed(label).effectDistance = new Vector2(3, -3);
            var rt = label.rectTransform;
            Center(rt, new Vector2(400, 100));
            int n = _popupCount++;
            var world = Head(v) + new Vector3(((n % 3) - 1) * 0.35f, -0.35f + (n % 2) * 0.25f, 0);
            Run(0.8f, k =>
            {
                if (rt == null) return;
                rt.anchoredPosition = ToHud(world + Vector3.up * (0.9f * EaseOut(k)));
                rt.localScale = Vector3.one * (k < 0.15f ? Mathf.Lerp(1.6f, 1f, k / 0.15f) : 1f);
                label.color = Paint.Alpha(color, k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f);
            }, () => { if (rt != null) Destroy(rt.gameObject); });
        }

        /// <summary>화면 위쪽 가운데에 잠깐 뜨는 띠 글자 (스킬 이름·웨이브·승패).</summary>
        private void Banner(string text, Color color, float duration)
        {
            if (_hud == null) return;
            var bg = UiKit.Panel_("Banner", _hud, new Color(0.1f, 0.09f, 0.2f, 0.78f));
            UiKit.Round(bg.GetComponent<Image>());
            bg.GetComponent<Image>().raycastTarget = false;
            bg.anchorMin = bg.anchorMax = new Vector2(0.5f, 0.78f);
            bg.pivot = new Vector2(0.5f, 0.5f);
            bg.sizeDelta = new Vector2(Mathf.Max(420, text.Length * 62 + 160), 96);
            var label = UiKit.Label("Label_Banner", bg, text, 54, color, TextAnchor.MiddleCenter);
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;
            UiKit.Stretch(label.rectTransform);
            var group = bg.gameObject.AddComponent<CanvasGroup>();
            Run(duration, k =>
            {
                if (bg == null) return;
                group.alpha = k < 0.12f ? k / 0.12f : k > 0.8f ? 1f - (k - 0.8f) / 0.2f : 1f;
                bg.localScale = new Vector3(k < 0.12f ? Mathf.Lerp(1.3f, 1f, k / 0.12f) : 1f, 1f, 1f);
            }, () => { if (bg != null) Destroy(bg.gameObject); });
        }

        // ---------------------------------------------------------------------
        // 매 프레임: 카메라·자세·HUD
        // ---------------------------------------------------------------------

        private void PlaceCamera()
        {
            // 화면이 16:9보다 좁으면 양옆이 잘리지 않게 뒤로 물러난다
            float aspect = Cam.aspect <= 0 ? 16f / 9f : Cam.aspect;
            float back = Mathf.Max(1f, (16f / 9f) / aspect);
            _camX = Mathf.Lerp(_camX, _focusX, 0.05f);
            var pos = new Vector3(Mathf.Sin(_time * 0.4f) * 0.32f + _camX, CamBase.y * back, CamBase.z * back);
            if (_shake > 0) pos += new Vector3(Mathf.Sin(_time * 95f), Mathf.Cos(_time * 81f), 0) * _shake;
            Cam.transform.localPosition = pos;
            Cam.transform.localRotation = Quaternion.Euler(CamPitch, 0, 0);
        }

        private Vector2 ToHud(Vector3 world)
        {
            var sp = Cam.WorldToScreenPoint(transform.TransformPoint(world));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_hud, sp, null, out var local);
            return local;
        }

        private void LateUpdate()
        {
            if (Cam == null) return;
            PlaceCamera();
            var face = Cam.transform.rotation;

            for (int i = 0; i < _fireflies.Count; i++)
            {
                var f = _fireflies[i];
                f.rotation = face;
                f.localPosition += new Vector3(Mathf.Sin(_time * 0.7f + i), Mathf.Cos(_time * 0.9f + i * 2f), 0) * 0.0025f;
            }

            foreach (var v in _allies.Concat(_enemies))
            {
                if (v.Root == null) continue;
                var m = v.Model;
                bool show = v.Fade > 0.01f;
                float cheer = _cheer && m.Side == Side.Ally && m.IsAlive ? Mathf.Abs(Mathf.Sin(_time * 5f + v.Phase)) * 0.35f : 0f;
                v.Root.localPosition = v.Home + new Vector3(v.Offset.x, 0, v.Offset.z);
                float bob = v.Hover > 0 ? Mathf.Sin(_time * 2.4f + v.Phase) * 0.12f : 0f;
                float breathe = 1f + Mathf.Sin(_time * 3f + v.Phase) * 0.025f;

                var body = v.Body.transform;
                body.localPosition = new Vector3(0, v.Hover + bob + v.Offset.y + cheer, 0);
                body.rotation = face * Quaternion.Euler(0, 0, v.Tilt);
                body.localScale = new Vector3(2f - v.Squash * breathe, v.Squash * breathe, 1f);
                int order = Order(v.Root.localPosition.z) + 1000;
                v.Body.sortingOrder = order;
                v.Body.enabled = show;
                var tint = Color.Lerp(Color.white, v.FlashColor, v.Flash * 0.75f);
                if (m.PoisonStacks > 0 && m.IsAlive) tint = Color.Lerp(tint, Paint.Hex(0xd9b8ff), 0.45f);
                tint.a = v.Fade;
                v.Body.color = tint;
                v.Shadow.enabled = show;
                v.Shadow.color = new Color(1, 1, 1, v.Fade * 0.8f);

                // 상태 표시: 방어 = 방패, 기절 = 머리 위 별, 독 = 보라 방울
                bool alive = m.IsAlive && show;
                v.Shield.enabled = alive && m.Guarding;
                if (v.Shield.enabled)
                {
                    v.Shield.transform.localPosition = new Vector3(m.Side == Side.Ally ? 0.55f : -0.55f, v.Hover + v.Height * 0.4f, -0.1f);
                    v.Shield.transform.rotation = face;
                    v.Shield.transform.localScale = Vector3.one * (0.62f + Mathf.Sin(_time * 5f) * 0.03f);
                    v.Shield.sortingOrder = order + 2;
                }
                for (int i = 0; i < 3; i++)
                {
                    var star = v.Stars[i];
                    star.enabled = alive && m.StunLeft > 0;
                    if (star.enabled)
                    {
                        float a = _time * 4f + i * Mathf.PI * 2f / 3f;
                        star.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.45f, v.Hover + v.Height + 0.05f + Mathf.Sin(a) * 0.1f, 0);
                        star.transform.rotation = face;
                        star.transform.localScale = Vector3.one * 0.42f;
                        star.sortingOrder = order + (Mathf.Sin(a) > 0 ? -1 : 2);
                    }
                    var bubble = v.Bubbles[i];
                    bubble.enabled = alive && i < m.PoisonStacks;
                    if (bubble.enabled)
                    {
                        float t = Mathf.Repeat(_time * 0.6f + i * 0.33f, 1f);
                        bubble.transform.localPosition = new Vector3((i - 1) * 0.3f, v.Hover + v.Height * (0.45f + t * 0.5f), -0.05f);
                        bubble.transform.rotation = face;
                        bubble.transform.localScale = Vector3.one * (0.22f + t * 0.14f);
                        bubble.color = new Color(1, 1, 1, 1f - t);
                        bubble.sortingOrder = order + 2;
                    }
                }
            }

            PlaceMark(_ringActor, !IsPlaying && Battle.CurrentActor != null ? View(Side.Ally, Battle.CurrentActor.Slot) : null, 1.25f);
            PlaceMark(_ringEnemy, !IsPlaying ? View(Side.Enemy, SelectedEnemy) : null, 1.1f);
            PlaceMark(_ringAlly, !IsPlaying ? View(Side.Ally, SelectedAlly) : null, 1.45f);

            UpdateHud();
        }

        private void PlaceMark(SpriteRenderer mark, UnitView v, float scale)
        {
            mark.enabled = v != null && v.Model.IsAlive && !Display.Finished;
            if (!mark.enabled) return;
            mark.transform.localPosition = v.Home + new Vector3(0, 0.03f, 0);
            mark.transform.localRotation = Quaternion.Euler(90, _time * 60f, 0);
            float w = v.Body.sprite.bounds.size.x * 0.85f * scale * (1f + Mathf.Sin(_time * 4f) * 0.04f);
            mark.transform.localScale = new Vector3(w, w * 0.9f, 1f);
        }

        private void UpdateHud()
        {
            if (_hud == null) return;
            foreach (var v in _allies.Concat(_enemies))
            {
                if (v.Hud == null) continue;
                var m = v.Model;
                bool show = m.IsAlive || v.Fade > 0.3f;
                if (v.Hud.gameObject.activeSelf != show) v.Hud.gameObject.SetActive(show);
                v.Btn.interactable = m.IsAlive;
                if (v.Btn.gameObject.activeSelf != m.IsAlive) v.Btn.gameObject.SetActive(m.IsAlive);

                var head = ToHud(Head(v) + Vector3.up * 0.12f);
                var feet = ToHud(v.Home);
                v.Hud.anchoredPosition = head + new Vector2(0, 46);
                float h = Mathf.Max(120f, head.y - feet.y);
                v.BtnRt.anchoredPosition = (head + feet) * 0.5f;
                v.BtnRt.sizeDelta = new Vector2(Mathf.Max(150f, h * 0.8f), h);

                string hp = m.Hp + "/" + m.MaxHp;
                if (v.HpLabel.text != hp) v.HpLabel.text = hp;
                float fill = Mathf.Clamp01((float)m.Hp / m.MaxHp);
                v.ShownFill = Mathf.MoveTowards(v.ShownFill, fill, Time.deltaTime * Mathf.Max(1f, Speed) * 1.6f);
                v.HpFill.anchorMin = Vector2.zero;
                v.HpFill.anchorMax = new Vector2(v.ShownFill, 1f);
                v.HpFill.offsetMin = v.HpFill.offsetMax = Vector2.zero;
                v.HpFillImage.enabled = v.ShownFill > 0.01f;

                string status = (m.Guarding ? "방어 " : "") + (m.StunLeft > 0 ? "기절" + m.StunLeft + " " : "") + (m.PoisonStacks > 0 ? "독×" + m.PoisonStacks : "");
                if (v.StatusLabel.text != status) v.StatusLabel.text = status;
            }
        }

        /// <summary>화면에 적힌 HP 글자 ("현재/최대"). 테스트가 Core 값과 비교한다.</summary>
        public string ShownHpText(Side side, int slot)
        {
            var v = View(side, slot);
            return v == null || v.HpLabel == null ? null : v.HpLabel.text;
        }

        /// <summary>그 유닛의 그림이 화면에 서 있는지 (쓰러져 사라졌으면 false).</summary>
        public bool IsShownStanding(Side side, int slot)
        {
            var v = View(side, slot);
            return v != null && !v.Fallen && v.Fade > 0.5f;
        }
    }
}
