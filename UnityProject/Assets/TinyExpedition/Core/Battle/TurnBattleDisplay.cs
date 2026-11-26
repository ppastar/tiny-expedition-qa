using System.Collections.Generic;
using System.Linq;
using TinyExpedition.Core.Data;

namespace TinyExpedition.Core.Battle
{
    /// <summary>화면에 보이는 유닛 한 명의 상태. 로그를 한 줄씩 읽을 때만 바뀐다.</summary>
    public sealed class DisplayUnit
    {
        public string Name { get; }
        public Side Side { get; }
        public int Slot { get; }
        /// <summary>적이면 몇 번째 웨이브의 적인지(1부터). 아군은 0.</summary>
        public int Wave { get; }
        public int MaxHp { get; }
        public int Hp { get; internal set; }
        public int StunLeft { get; internal set; }
        public int PoisonStacks { get; internal set; }
        public bool Guarding { get; internal set; }
        public bool IsAlive => Hp > 0;

        internal DisplayUnit(string name, Side side, int slot, int wave, int maxHp)
        {
            Name = name; Side = side; Slot = slot; Wave = wave; MaxHp = maxHp; Hp = maxHp;
        }
    }

    /// <summary>
    /// 턴제 전투의 "화면 상태". Core(TurnBattle)는 명령을 받으면 다음 아군 차례까지 한 번에 계산해 두고,
    /// 화면은 그 사이에 쌓인 로그를 한 줄씩 재생한다. 이 클래스는 재생한 줄까지의 HP·생존·상태이상을 들고 있다.
    ///
    /// 화면은 TurnBattle의 현재 값을 직접 읽지 않고 이 상태만 그린다. 그래서
    /// - 연출 도중에는 "아직 맞지 않은 공격"의 결과가 미리 보이지 않고,
    /// - 로그를 끝까지 재생하면 Core 상태와 같아야 한다 (Mismatches가 비어야 함 — TURN-025).
    ///   다르면 로그에 빠진 정보가 있거나 화면이 로그를 잘못 읽는 것이다.
    ///
    /// 엔진 참조가 없어서 dotnet 테스트에서 시드 수백 개로 돌려 볼 수 있다.
    /// </summary>
    public sealed class TurnBattleDisplay
    {
        private readonly TurnBattle _battle;
        private readonly List<DisplayUnit> _allies;
        private List<DisplayUnit> _enemies;

        public IReadOnlyList<DisplayUnit> Allies => _allies;
        /// <summary>화면에 서 있는 웨이브의 적.</summary>
        public IReadOnlyList<DisplayUnit> Enemies => _enemies;
        /// <summary>화면에 보이는 웨이브 (0부터).</summary>
        public int WaveIndex { get; private set; }
        /// <summary>마지막으로 재생한 줄의 라운드.</summary>
        public int Round { get; private set; } = 1;
        /// <summary>끝(End) 줄까지 재생했는지.</summary>
        public bool Finished { get; private set; }
        /// <summary>다음에 재생할 로그 줄 번호.</summary>
        public int Cursor { get; private set; }
        public bool HasPending => Cursor < _battle.Log.Count;

        public TurnBattleDisplay(TurnBattle battle)
        {
            // 전투를 만들 때 적이 먼저 행동했을 수 있으므로(로그가 이미 있음) 현재 HP가 아니라 처음 상태에서 시작한다.
            _battle = battle;
            _allies = battle.Allies.Select(a => new DisplayUnit(a.Name, Side.Ally, a.Slot, 0, a.MaxHp)).ToList();
            _enemies = Spawn(0);
        }

        private List<DisplayUnit> Spawn(int waveIndex) =>
            _battle.Stage.Waves[waveIndex].Enemies.Select((e, i) => new DisplayUnit(e.Name, Side.Enemy, i, waveIndex + 1, e.Hp)).ToList();

        public DisplayUnit Find(Side side, int slot)
        {
            var list = side == Side.Ally ? _allies : _enemies;
            return slot >= 0 && slot < list.Count ? list[slot] : null;
        }

        /// <summary>아직 재생하지 않은 다음 줄. 없으면 null.</summary>
        public TurnLogEntry Peek() => HasPending ? _battle.Log[Cursor] : null;

        /// <summary>다음 줄 하나를 화면 상태에 반영하고 그 줄을 돌려준다. 없으면 null.</summary>
        public TurnLogEntry Step()
        {
            if (!HasPending) return null;
            var e = _battle.Log[Cursor++];
            Apply(e);
            return e;
        }

        /// <summary>남은 줄을 모두 반영한다 (연출 건너뛰기).</summary>
        public void StepAll()
        {
            while (HasPending) Step();
        }

        private void Apply(TurnLogEntry e)
        {
            Round = e.Round;
            var actor = e.ActorSlot >= 0 ? Find(e.ActorSide, e.ActorSlot) : null;
            var target = e.TargetSlot >= 0 ? Find(e.TargetSide, e.TargetSlot) : null;

            // 방어 표시는 그 유닛의 다음 차례가 오면 사라진다. 차례가 온 유닛은 행동·방어·기절 줄의 행동자로 나온다.
            if (actor != null && e.Kind != TurnLogKind.Defeated) actor.Guarding = e.Kind == TurnLogKind.Guard;

            var subject = target ?? actor;
            if (subject != null)
            {
                subject.Hp = e.HpAfter;
                subject.StunLeft = e.StunAfter;
                subject.PoisonStacks = e.PoisonAfter;
                if (!subject.IsAlive) { subject.StunLeft = 0; subject.PoisonStacks = 0; subject.Guarding = false; }
            }

            if (e.Kind == TurnLogKind.WaveCleared && e.Amount < _battle.WaveCount)
            {
                // 다음 웨이브: 새 적이 서고, 아군의 상태이상·방어는 풀린다(HP는 그대로)
                WaveIndex = e.Amount;
                _enemies = Spawn(WaveIndex);
                foreach (var a in _allies) { a.StunLeft = 0; a.PoisonStacks = 0; a.Guarding = false; }
            }
            else if (e.Kind == TurnLogKind.End)
            {
                Finished = true;
            }
        }

        /// <summary>
        /// 화면 상태와 Core 상태가 다른 곳. 로그를 끝까지 재생한 뒤(HasPending = false)에만 뜻이 있다.
        /// 명령을 기다리는 아군은 차례가 시작되면서 방어가 풀렸지만 아직 로그 줄이 없으므로 여기서 맞춘다.
        /// </summary>
        public List<string> Mismatches()
        {
            var issues = new List<string>();
            if (HasPending)
            {
                issues.Add("아직 재생하지 않은 로그 " + (_battle.Log.Count - Cursor) + "줄");
                return issues;
            }
            if (_battle.CurrentActor != null) _allies[_battle.CurrentActor.Slot].Guarding = false;

            if (WaveIndex != _battle.WaveIndex) issues.Add("웨이브: 화면 " + (WaveIndex + 1) + " / 실제 " + (_battle.WaveIndex + 1));
            if (Finished != _battle.IsOver) issues.Add("종료: 화면 " + Finished + " / 실제 " + _battle.IsOver);
            Compare(issues, _allies, _battle.Allies);
            if (WaveIndex == _battle.WaveIndex) Compare(issues, _enemies, _battle.Enemies);
            return issues;
        }

        private static void Compare(List<string> issues, List<DisplayUnit> shown, IReadOnlyList<TurnUnit> real)
        {
            if (shown.Count != real.Count)
            {
                issues.Add("유닛 수: 화면 " + shown.Count + " / 실제 " + real.Count);
                return;
            }
            for (int i = 0; i < shown.Count; i++)
            {
                var s = shown[i];
                var r = real[i];
                string who = (r.Side == Side.Ally ? "아군" : "적") + i + " " + r.Name;
                if (s.Hp != r.Hp) issues.Add(who + " HP: 화면 " + s.Hp + " / 실제 " + r.Hp);
                if (!r.IsAlive) continue; // 쓰러진 유닛의 상태이상은 화면에 그리지 않는다
                if (s.StunLeft != r.StunLeft) issues.Add(who + " 기절: 화면 " + s.StunLeft + " / 실제 " + r.StunLeft);
                if (s.PoisonStacks != r.PoisonStacks) issues.Add(who + " 독: 화면 " + s.PoisonStacks + " / 실제 " + r.PoisonStacks);
                if (s.Guarding != r.Guarding) issues.Add(who + " 방어: 화면 " + s.Guarding + " / 실제 " + r.Guarding);
            }
        }
    }
}
