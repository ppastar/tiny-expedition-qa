using System;
using TinyExpedition.Core.MiniGame;
using UnityEngine;
using UnityEngine.UI;

namespace TinyExpedition.Game
{
    /// <summary>
    /// 미니게임 "별빛 타이밍" 화면. 판정·점수는 전부 Core의 TimingTapGame이 하고,
    /// 여기서는 막대를 그리고 탭 시각을 넘기기만 한다.
    /// </summary>
    public sealed class MiniGameView : MonoBehaviour
    {
        /// <summary>판 시작 후 경과 시간(초). 테스트에서 바꿔 끼울 수 있다.</summary>
        public Func<double> Clock;

        private TimingTapGame _game;
        private Action<TimingTapGame> _onFinished;
        private RectTransform _track;
        private RectTransform _marker;
        private Text _feedback;
        private Text _status;
        private double _startTime;
        private bool _done;

        public Button TapButton { get; private set; }

        public void Build(TimingTapGame game, Action<TimingTapGame> onFinished)
        {
            _game = game;
            _onFinished = onFinished;
            _startTime = Time.realtimeSinceStartupAsDouble;
            Clock = () => Time.realtimeSinceStartupAsDouble - _startTime;

            var col = UiKit.Column("Col", transform, 24, 0);
            _status = UiKit.Label("Label_Round", col, "", 40);
            _feedback = UiKit.Label("Label_Judgement", col, "준비", 64, UiKit.Gold, TextAnchor.MiddleCenter, 110);

            // 트랙 + 목표 구간 + 움직이는 막대
            var trackGo = new GameObject("Track", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            trackGo.transform.SetParent(col, false);
            trackGo.GetComponent<Image>().color = UiKit.Panel;
            trackGo.GetComponent<LayoutElement>().preferredHeight = 120;
            _track = (RectTransform)trackGo.transform;

            var cfg = game.Config;
            Zone("GoodZone", cfg.TargetCenter, cfg.GoodHalfWidth, new Color(1f, 0.78f, 0.34f, 0.35f));
            Zone("PerfectZone", cfg.TargetCenter, cfg.PerfectHalfWidth, UiKit.Gold);

            var markerGo = new GameObject("Marker", typeof(RectTransform), typeof(Image));
            markerGo.transform.SetParent(_track, false);
            markerGo.GetComponent<Image>().color = Color.white;
            _marker = (RectTransform)markerGo.transform;
            _marker.anchorMin = new Vector2(0, 0);
            _marker.anchorMax = new Vector2(0, 1);
            _marker.sizeDelta = new Vector2(16, 0);

            TapButton = UiKit.Button("Btn_Tap", col, "탭!", Tap, UiKit.Accent, 260);
            UpdateStatus();
        }

        private void Zone(string name, double center, double halfWidth, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_track, false);
            go.GetComponent<Image>().color = color;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2((float)(center - halfWidth), 0);
            rt.anchorMax = new Vector2((float)(center + halfWidth), 1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private void Update()
        {
            if (_game == null || _done) return;
            float x = (float)_game.PositionAt(Clock());
            _marker.anchorMin = new Vector2(x, 0);
            _marker.anchorMax = new Vector2(x, 1);
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Space)) Tap();
#endif
        }

        public void Tap()
        {
            if (_done) return;
            var rec = _game.Tap(Clock());
            _feedback.text = rec.Judgement == Judgement.Miss ? "Miss" : rec.Judgement + (rec.Combo > 1 ? "  " + rec.Combo + " 콤보" : "");
            _feedback.color = rec.Judgement == Judgement.Perfect ? UiKit.Gold : rec.Judgement == Judgement.Good ? UiKit.TextColor : UiKit.Muted;
            UpdateStatus();
            if (_game.IsFinished)
            {
                _done = true;
                _onFinished?.Invoke(_game);
            }
        }

        private void UpdateStatus()
        {
            _status.text = _game.Taps.Count + " / " + _game.Config.Rounds + "   점수 " + _game.Score;
        }
    }
}
