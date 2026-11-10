using System;
using System.Collections.Generic;

namespace TinyExpedition.Core.MiniGame
{
    public enum Judgement { Perfect, Good, Miss }

    public sealed class TimingTapConfig
    {
        /// <summary>막대가 0→1→0 한 번 왕복하는 데 걸리는 시간(초).</summary>
        public double CycleSeconds { get; set; } = 1.6;
        /// <summary>목표 구간 중심 (0~1).</summary>
        public double TargetCenter { get; set; } = 0.5;
        /// <summary>Perfect 판정 반폭 (중심에서 ±).</summary>
        public double PerfectHalfWidth { get; set; } = 0.04;
        /// <summary>Good 판정 반폭 (중심에서 ±). PerfectHalfWidth 이상이어야 한다.</summary>
        public double GoodHalfWidth { get; set; } = 0.12;
        /// <summary>한 판의 탭 횟수.</summary>
        public int Rounds { get; set; } = 10;
        public int PerfectScore { get; set; } = 100;
        public int GoodScore { get; set; } = 50;
        /// <summary>콤보 1당 가산점(%). 콤보는 Miss에서 끊긴다.</summary>
        public int ComboBonusPercent { get; set; } = 10;
        /// <summary>콤보 보너스 최대치(%).</summary>
        public int MaxComboBonusPercent { get; set; } = 100;
        /// <summary>판이 끝났을 때 점수 → 보석 환산 비율 (점수 / 이 값, 내림).</summary>
        public int ScorePerGem { get; set; } = 20;
        /// <summary>한 판 보석 보상 상한.</summary>
        public int MaxGemsPerPlay { get; set; } = 100;

        public void Validate()
        {
            if (CycleSeconds <= 0) throw new ArgumentException("CycleSeconds는 0보다 커야 합니다.");
            if (PerfectHalfWidth < 0 || GoodHalfWidth < PerfectHalfWidth) throw new ArgumentException("판정 폭은 0 ≤ Perfect ≤ Good 이어야 합니다.");
            if (Rounds <= 0) throw new ArgumentException("Rounds는 1 이상이어야 합니다.");
            if (ScorePerGem <= 0) throw new ArgumentException("ScorePerGem은 1 이상이어야 합니다.");
        }
    }

    public sealed class TapRecord
    {
        public double Time { get; }
        public double Position { get; }
        public Judgement Judgement { get; }
        public int Combo { get; }
        public int Gained { get; }

        public TapRecord(double time, double position, Judgement judgement, int combo, int gained)
        {
            Time = time; Position = position; Judgement = judgement; Combo = combo; Gained = gained;
        }
    }

    /// <summary>
    /// 미니게임 "별빛 타이밍": 좌우로 왕복하는 막대가 목표 구간에 들어왔을 때 탭한다.
    /// 시간은 외부에서 넣는다(Unity는 Time.time, 테스트는 숫자). 그래서 프레임·기기 속도와 무관하게
    /// 입력 기록(탭 시각 목록)만으로 같은 결과를 재현할 수 있다.
    /// </summary>
    public sealed class TimingTapGame
    {
        private readonly TimingTapConfig _cfg;
        private readonly List<TapRecord> _taps = new List<TapRecord>();
        private double _lastTapTime = double.NegativeInfinity;

        public int Score { get; private set; }
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public bool IsFinished => _taps.Count >= _cfg.Rounds;
        /// <summary>보상을 이미 받았는지. 같은 판으로 두 번 받는 걸 막는다.</summary>
        public bool RewardClaimed { get; internal set; }
        public IReadOnlyList<TapRecord> Taps => _taps;
        public TimingTapConfig Config => _cfg;

        public TimingTapGame(TimingTapConfig config)
        {
            _cfg = config ?? throw new ArgumentNullException(nameof(config));
            _cfg.Validate();
        }

        /// <summary>시각 t(초, 판 시작=0)에서 막대 위치 0~1. 삼각파.</summary>
        public double PositionAt(double t)
        {
            if (t < 0) t = 0;
            double phase = (t % _cfg.CycleSeconds) / _cfg.CycleSeconds; // 0~1
            return phase < 0.5 ? phase * 2 : (1 - phase) * 2;
        }

        public Judgement Judge(double position)
        {
            double d = Math.Abs(position - _cfg.TargetCenter);
            // 부동소수 오차로 경계값이 밀리지 않도록 아주 작은 여유를 둔다.
            const double eps = 1e-9;
            if (d <= _cfg.PerfectHalfWidth + eps) return Judgement.Perfect;
            if (d <= _cfg.GoodHalfWidth + eps) return Judgement.Good;
            return Judgement.Miss;
        }

        /// <summary>시각 t에 탭. 판이 끝났거나 시간이 거꾸로 가면 예외.</summary>
        public TapRecord Tap(double t)
        {
            if (IsFinished) throw new InvalidOperationException("이미 끝난 판입니다.");
            if (t < _lastTapTime) throw new ArgumentException("탭 시각은 이전 탭보다 앞설 수 없습니다.", nameof(t));
            _lastTapTime = t;

            double pos = PositionAt(t);
            var j = Judge(pos);
            int gained = 0;
            if (j == Judgement.Miss)
            {
                Combo = 0;
            }
            else
            {
                Combo++;
                if (Combo > MaxCombo) MaxCombo = Combo;
                int baseScore = j == Judgement.Perfect ? _cfg.PerfectScore : _cfg.GoodScore;
                int bonusPercent = Math.Min(_cfg.MaxComboBonusPercent, (Combo - 1) * _cfg.ComboBonusPercent);
                gained = baseScore * (100 + bonusPercent) / 100;
                Score += gained;
            }

            var rec = new TapRecord(t, pos, j, Combo, gained);
            _taps.Add(rec);
            return rec;
        }

        /// <summary>판이 끝났을 때 받을 보석.</summary>
        public int RewardGems()
        {
            if (!IsFinished) return 0;
            return Math.Min(_cfg.MaxGemsPerPlay, Score / _cfg.ScorePerGem);
        }

        /// <summary>입력 기록(탭 시각 목록)으로 판을 다시 돌린다. 버그 재현·리플레이 검증용.</summary>
        public static TimingTapGame Replay(TimingTapConfig config, IEnumerable<double> tapTimes)
        {
            var g = new TimingTapGame(config);
            foreach (var t in tapTimes)
            {
                if (g.IsFinished) break;
                g.Tap(t);
            }
            return g;
        }
    }
}
