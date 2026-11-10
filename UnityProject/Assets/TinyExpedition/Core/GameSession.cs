using System;
using System.Collections.Generic;
using System.Linq;
using TinyExpedition.Core.Battle;
using TinyExpedition.Core.Data;
using TinyExpedition.Core.Gacha;
using TinyExpedition.Core.MiniGame;
using TinyExpedition.Core.Progress;
using TinyExpedition.Core.Random;

namespace TinyExpedition.Core
{
    public enum ActionError
    {
        None,
        NotEnoughGems,
        StageLocked,
        CharacterNotOwned,
        InvalidParty,
    }

    public sealed class ActionResult<T>
    {
        public ActionError Error { get; }
        public T Value { get; }
        public bool Ok => Error == ActionError.None;

        private ActionResult(ActionError error, T value) { Error = error; Value = value; }
        public static ActionResult<T> Success(T value) => new ActionResult<T>(ActionError.None, value);
        public static ActionResult<T> Fail(ActionError error) => new ActionResult<T>(error, default(T));
    }

    /// <summary>
    /// 게임 규칙의 입구. Unity UI와 테스트가 모두 이 클래스만 부른다.
    /// 재화 차감 → 결과 지급 순서를 한곳에서 지켜, 실패한 행동은 상태를 바꾸지 않는다.
    /// </summary>
    public sealed class GameSession
    {
        public GameDatabase Db { get; }
        public PlayerState State { get; }
        private readonly GachaService _gacha;
        private Pcg32 _rng;

        public GameSession(GameDatabase db, PlayerState state)
        {
            Db = db ?? throw new ArgumentNullException(nameof(db));
            State = state ?? throw new ArgumentNullException(nameof(state));
            _gacha = new GachaService(db);
            _rng = new Pcg32(state.RngSeed);
        }

        /// <summary>QA용: 이후 난수를 고정한다 (디버그 패널의 "시드 고정").</summary>
        public void Reseed(ulong seed)
        {
            State.RngSeed = seed;
            _rng = new Pcg32(seed);
        }

        private void AdvanceSeed()
        {
            // 다음 행동이 쓸 시드를 저장해, 세이브→로드 후에도 같은 흐름이 되게 한다.
            State.RngSeed = ((ulong)_rng.NextUInt() << 32) | _rng.NextUInt();
            _rng = new Pcg32(State.RngSeed);
        }

        public ActionResult<IReadOnlyList<PullResult>> Pull(string bannerId, int count)
        {
            if (count != 1 && count != GachaService.TenPullCount)
                throw new ArgumentOutOfRangeException(nameof(count), "1회 또는 10회만 뽑을 수 있습니다.");
            var banner = Db.GetBanner(bannerId);
            long cost = (long)banner.CostPerPull * count;
            if (State.Gems < cost) return ActionResult<IReadOnlyList<PullResult>>.Fail(ActionError.NotEnoughGems);

            if (!State.Pity.TryGetValue(bannerId, out var pity))
            {
                pity = new PityState();
                State.Pity[bannerId] = pity;
            }

            State.Gems -= (int)cost;
            IReadOnlyList<PullResult> results = count == 1
                ? new[] { _gacha.PullOne(banner, pity, _rng) }
                : _gacha.PullTen(banner, pity, _rng);

            foreach (var r in results)
            {
                State.Owned.TryGetValue(r.CharacterId, out int n);
                State.Owned[r.CharacterId] = n + 1;
            }
            AdvanceSeed();
            return ActionResult<IReadOnlyList<PullResult>>.Success(results);
        }

        public bool IsStageUnlocked(string stageId)
        {
            var stage = Db.GetStage(stageId);
            return stage.RequiresStageId == null || State.ClearedStages.ContainsKey(stage.RequiresStageId);
        }

        public ActionResult<BattleResult> Battle(string stageId, IReadOnlyList<string> partyIds)
        {
            if (!IsStageUnlocked(stageId)) return ActionResult<BattleResult>.Fail(ActionError.StageLocked);
            if (partyIds == null || partyIds.Count == 0 || partyIds.Count > BattleSimulator.MaxPartySize
                || partyIds.Distinct().Count() != partyIds.Count)
                return ActionResult<BattleResult>.Fail(ActionError.InvalidParty);
            if (partyIds.Any(id => !State.Owned.ContainsKey(id)))
                return ActionResult<BattleResult>.Fail(ActionError.CharacterNotOwned);

            var stage = Db.GetStage(stageId);
            var party = partyIds.Select(Db.GetCharacter).ToList();
            var result = BattleSimulator.Run(party, stage, _rng);

            if (result.Outcome == BattleOutcome.Victory)
            {
                bool firstClear = !State.ClearedStages.TryGetValue(stageId, out int prevStars);
                if (firstClear) State.Gems += stage.FirstClearGems;
                if (result.Stars > prevStars) State.ClearedStages[stageId] = result.Stars;
            }
            AdvanceSeed();
            return ActionResult<BattleResult>.Success(result);
        }

        /// <summary>미니게임 한 판이 끝나면 보상을 받는다. 끝나지 않았거나 이미 받은 판은 0.</summary>
        public int ClaimMiniGame(TimingTapGame game)
        {
            if (game == null || !game.IsFinished || game.RewardClaimed) return 0;
            game.RewardClaimed = true;
            int gems = game.RewardGems();
            State.Gems += gems;
            if (game.Score > State.MiniGameBestScore) State.MiniGameBestScore = game.Score;
            return gems;
        }
    }
}
