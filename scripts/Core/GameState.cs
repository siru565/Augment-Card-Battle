using System;
using System.Collections.Generic;
using System.Linq;

namespace SpCardgame.Core;

/// <summary>
/// 판 설정입니다. 대기방에서 방장이 고릅니다.
/// </summary>
public sealed record GameOptions(bool SpecialAugments = true);

/// <summary>
/// 억지로 뽑아야 하는 카드 빚입니다. (+2, +4, 카드 폭풍, 심연의 부름 등)
/// 당한 사람이 직접 '뽑기'를 눌러서 한 장씩 갚습니다.
/// </summary>
public sealed record DrawDebt(int Player, int MaxCards, CardColor? UntilSuit, string Reason);

/// <summary>
/// 플레이어 한 명의 상태입니다.
/// </summary>
public sealed class PlayerState
{
    public int Id { get; }
    public List<Card> Hand { get; } = new();
    public List<SpecialAugmentId> Augments { get; } = new();

    /// <summary>봉인 상태입니다. 다음 내 차례에 숫자 카드만 낼 수 있습니다.</summary>
    public bool Sealed { get; set; }

    /// <summary>지금까지 시작한 내 차례 수입니다. 특수 증강을 고를 때를 정합니다.</summary>
    public int TurnsStarted { get; set; }

    /// <summary>문양 군주 증강으로 정해진 내 문양입니다.</summary>
    public CardColor LordSuit { get; set; } = CardColor.Wild;

    /// <summary>게임 시작 때 모두가 동시에 고르는 첫 특수 증강 선택지입니다. (비어 있으면 이미 골랐습니다)</summary>
    public List<SpecialAugmentId> DraftChoices { get; } = new();

    /// <summary>최종 순위입니다. 0이면 아직 게임 중입니다. (1등, 2등…)</summary>
    public int Rank { get; set; }

    /// <summary>손패가 너무 많아서 탈락했는지 표시합니다.</summary>
    public bool Eliminated { get; set; }

    public bool Active => Rank == 0;

    /// <summary>저격수에게 찍혀서 다음 차례를 건너뛰는지 표시합니다.</summary>
    public bool SkipNext { get; set; }

    /// <summary>컬링: 지금까지 모은 게이지(낸 카드 수)입니다.</summary>
    public int CurlingCharge { get; set; }

    /// <summary>도미노: 마지막으로 낸 숫자(-1이면 없음)와 지금 연속 수입니다.</summary>
    public int DominoLast { get; set; } = -1;

    public int DominoChain { get; set; }

    /// <summary>예언자: 예언한 문양(Wild면 아직 없음)과 연속 적중 수입니다.</summary>
    public CardColor OracleGuess { get; set; } = CardColor.Wild;

    public int OracleStreak { get; set; }

    /// <summary>정밀 사수: 연속 성공 수입니다.</summary>
    public int MarksmanStreak { get; set; }

    /// <summary>잭팟: 마지막으로 나온 릴입니다. (화면 표시용)</summary>
    public string LastJackpot { get; set; } = "";

    public PlayerState(int id) => Id = id;

    public bool Has(SpecialAugmentId id) => Augments.Contains(id);
}

/// <summary>
/// 한 판의 전체 상태입니다. 서버(호스트)만 이 객체를 가지며, 클라이언트와 봇에게는 PlayerView만 전달합니다.
/// </summary>
public sealed class GameState
{
    public List<PlayerState> Players { get; } = new();
    public List<Card> DrawPile { get; } = new();
    public List<Card> DiscardPile { get; } = new();

    public GameOptions Options { get; set; } = new();

    public int CurrentPlayer { get; set; }

    /// <summary>진행 방향입니다. 1이면 정방향, -1이면 역방향입니다.</summary>
    public int Direction { get; set; } = 1;

    /// <summary>현재 유효한 문양입니다. 프리즘을 낸 뒤에는 선택된 문양이 들어갑니다.</summary>
    public CardColor CurrentColor { get; set; }

    /// <summary>이번 턴에 이미 카드를 뽑았는지 표시합니다.</summary>
    public bool HasDrawnThisTurn { get; set; }

    /// <summary>이번 턴에 뽑은 카드입니다. 뽑은 뒤에는 이 카드만 낼 수 있습니다.</summary>
    public Card? DrawnCard { get; set; }

    /// <summary>연속 내기 중인지 표시합니다. (폭주 카드, 시간술사 증강)</summary>
    public bool FrenzyActive { get; set; }

    public CardColor FrenzyColor { get; set; }

    /// <summary>연속 내기로 더 낼 수 있는 장수입니다. 폭주는 사실상 무제한입니다.</summary>
    public int FrenzyRemaining { get; set; }

    /// <summary>도박사 증강으로 뽑은 2장입니다. 비어 있지 않으면 현재 플레이어가 한 장을 골라야 합니다.</summary>
    public List<Card> GambleCards { get; } = new();

    /// <summary>지배자 증강: 이번 각성에서 두 번째 능력을 고르는 중인지 표시합니다.</summary>
    public bool AwakenSecondPick { get; set; }

    /// <summary>쌍둥이 증강의 연속 내기 숫자입니다. -1이면 문양 기준 연속 내기(폭주, 시간술사)입니다.</summary>
    public int FrenzyNumber { get; set; } = -1;

    /// <summary>마지막으로 +카드를 얹은 사람입니다. (반사의 거울이 되돌려 보낼 대상)</summary>
    public int LastAttacker { get; set; } = -1;

    /// <summary>직전에 발동한 카드 효과입니다. 복사 카드가 이 효과를 다시 발동합니다.</summary>
    public CardKind LastEffect { get; set; } = CardKind.Number;

    public int TurnCount { get; set; }

    /// <summary>지금 라운드입니다. 모두가 한 번씩 차례를 가지면 1 올라갑니다. (1부터 시작)</summary>
    public int Round => TurnCount / PlayerCount + 1;


    /// <summary>각성 카드로 나온 능력 선택지입니다. 비어 있지 않으면 현재 플레이어가 하나를 골라야 합니다.</summary>
    public List<AbilityId> PendingAbilities { get; } = new();

    /// <summary>특수 증강 선택지입니다. 비어 있지 않으면 현재 플레이어가 하나를 골라야 합니다.</summary>
    public List<SpecialAugmentId> PendingAugments { get; } = new();

    /// <summary>억지로 뽑아야 하는 카드 빚 목록입니다. 맨 앞 사람부터 차례로 갚습니다.</summary>
    public List<DrawDebt> DrawQueue { get; } = new();

    /// <summary>맨 앞 빚에서 지금까지 뽑은 장수입니다.</summary>
    public int DebtProgress { get; set; }

    /// <summary>
    /// 쌓인 +카드 공격 장수입니다. 0보다 크면 지금 차례인 사람은 +카드로 넘기거나(합산), 뽑기를 눌러 모두 받아야 합니다.
    /// </summary>
    public int PendingPenalty { get; set; }

    /// <summary>빚을 다 갚은 뒤 진행할 자리 이동 수입니다.</summary>
    public int? DeferredSteps { get; set; }

    /// <summary>직업 카드 배정에 섞는 판별 값입니다.</summary>
    public int JobSalt { get; set; }

    /// <summary>지금 차례인 사람이 해야 하는 미니게임입니다. (컬링, 정밀 사수, 예언자)</summary>
    public MinigameInfo? PendingMinigame { get; set; }

    /// <summary>미니게임마다 붙이는 번호입니다. 화면이 새 미니게임인지 알아보는 데 씁니다.</summary>
    public int MinigameCounter { get; set; }

    public bool ChoosingAbility => PendingAbilities.Count > 0;

    public bool ChoosingAugment => PendingAugments.Count > 0;

    public bool PayingDebt => DrawQueue.Count > 0;

    /// <summary>게임 시작 직후, 아직 첫 특수 증강을 고르지 않은 사람이 있는지 표시합니다. (모두 동시에 고릅니다)</summary>
    public bool Drafting => Players.Any(p => p.DraftChoices.Count > 0);

    /// <summary>
    /// 지금 행동해야 하는 사람입니다. 첫 증강 고르기 중이면 아직 안 고른 사람, 빚이 있으면 빚진 사람, 아니면 현재 차례인 사람입니다.
    /// </summary>
    public int InputPlayer =>
        Drafting ? Players.First(p => p.DraftChoices.Count > 0).Id
        : PayingDebt ? DrawQueue[0].Player
        : CurrentPlayer;

    /// <summary>승자 Id입니다. 무승부(턴 제한 초과)면 -1, 진행 중이면 null입니다.</summary>
    public int? Winner { get; set; }

    public bool IsFinished => Winner.HasValue;

    public Card TopCard => DiscardPile[^1];

    public int PlayerCount => Players.Count;

    /// <summary>
    /// from 기준으로 steps만큼 진행 방향으로 이동한 자리를 구합니다.
    /// </summary>
    public int SeatAfter(int from, int steps = 1)
    {
        // 순위가 정해져 빠진 사람은 건너뜁니다.
        int n = PlayerCount;
        int seat = from;
        for (int k = 0; k < steps; k++)
        {
            for (int guard = 0; guard < n; guard++)
            {
                seat = ((seat + Direction) % n + n) % n;
                if (Players[seat].Active)
                {
                    break;
                }
            }
        }

        return seat;
    }

    public bool IsActive(int playerId) => playerId >= 0 && playerId < PlayerCount && Players[playerId].Active;

    /// <summary>아직 게임 중인 사람 수입니다.</summary>
    public int ActiveCount => Players.Count(p => p.Active);

    /// <summary>어벤져스 진행도입니다. 모은 직업 수를 돌려줍니다.</summary>
    public int JobsCollected(int playerId) =>
        Players[playerId].Hand.Where(SpecialAugments.HasJob).Select(c => SpecialAugments.JobOf(c.Id, JobSalt)).Distinct().Count();

    /// <summary>어벤져스 승리 조건을 채웠는지 확인합니다.</summary>
    public bool AvengersComplete(int playerId)
    {
        int jobs = SpecialAugments.AvengersJobCount;
        return JobsCollected(playerId) >= jobs && (!SpecialAugments.AvengersExact || Players[playerId].Hand.Count == jobs);
    }

    /// <summary>
    /// 특정 플레이어가 봐도 되는 정보만 담은 뷰를 만듭니다. 다른 사람의 손패는 장수만 들어갑니다.
    /// </summary>
    public PlayerView ViewFor(int playerId)
    {
        bool myTurn = CurrentPlayer == playerId && !IsFinished && Players[playerId].Active;
        bool blocked = ChoosingAbility || ChoosingAugment || PayingDebt || Drafting || GambleCards.Count > 0 || PendingMinigame != null;
        var me = Players[playerId];
        var hand = me.Hand.ToList();

        List<int> playable;
        if (!myTurn || blocked)
        {
            playable = new List<int>();
        }
        else if (HasDrawnThisTurn && !FrenzyActive)
        {
            playable = DrawnCard != null && me.Hand.Contains(DrawnCard) && Rules.CanPlay(DrawnCard, this, playerId)
                ? new List<int> { DrawnCard.Id }
                : new List<int>();
        }
        else
        {
            playable = hand.Where(c => Rules.CanPlay(c, this, playerId)).Select(c => c.Id).ToList();
        }

        var jobs = me.Has(SpecialAugmentId.Avengers)
            ? hand.Where(SpecialAugments.HasJob).Select(c => new CardJob(c.Id, SpecialAugments.JobOf(c.Id, JobSalt))).ToList()
            : new List<CardJob>();

        var debt = PayingDebt ? DrawQueue[0] : null;

        return new PlayerView(
            PlayerId: playerId,
            Hand: hand,
            HandCounts: Players.Select(p => p.Hand.Count).ToArray(),
            Augments: Players.Select(p => (IReadOnlyList<AugmentInfo>)p.Augments.Select(SpecialAugments.Info).ToList()).ToArray(),
            JobProgress: Players.Select(p => p.Has(SpecialAugmentId.Avengers) ? JobsCollected(p.Id) : -1).ToArray(),
            Sealed: Players.Select(p => p.Sealed).ToArray(),
            TopCard: TopCard,
            CurrentColor: CurrentColor,
            CurrentPlayer: CurrentPlayer,
            Direction: Direction,
            IsMyTurn: myTurn,
            HasDrawnThisTurn: HasDrawnThisTurn,
            DrawnCard: HasDrawnThisTurn ? DrawnCard : null,
            PlayableCardIds: playable,
            ColorRequiredIds: hand.Where(c => Rules.NeedsColor(c, this, playerId)).Select(c => c.Id).ToList(),
            TargetRequiredIds: hand.Where(c => Rules.NeedsTarget(c, this, playerId)).Select(c => c.Id).ToList(),
            CanDraw: myTurn && !blocked && !HasDrawnThisTurn && !FrenzyActive,
            PendingPenalty: PendingPenalty,
            CanPass: myTurn && !blocked && (HasDrawnThisTurn || FrenzyActive),
            AbilityChoices: myTurn && !PayingDebt ? PendingAbilities.Select(AbilityPool.Get).ToList() : new List<AbilityInfo>(),
            ChoosingAbility: ChoosingAbility,
            AugmentChoices: Drafting ? me.DraftChoices.Select(SpecialAugments.Info).ToList()
                : myTurn && !PayingDebt ? PendingAugments.Select(SpecialAugments.Info).ToList()
                : new List<AugmentInfo>(),
            ChoosingAugment: ChoosingAugment || Drafting,
            DraftPending: Players.Select(p => p.DraftChoices.Count > 0).ToArray(),
            DebtPlayer: debt?.Player ?? -1,
            DebtRemaining: debt == null ? 0 : debt.UntilSuit.HasValue ? -1 : debt.MaxCards - DebtProgress,
            DebtSuit: debt?.UntilSuit ?? CardColor.Wild,
            DebtDrawn: debt == null ? 0 : DebtProgress,
            DebtReason: debt?.Reason ?? "",
            MustDraw: debt != null && debt.Player == playerId && !IsFinished,
            Jobs: jobs,
            FrenzyActive: FrenzyActive,
            FrenzyColor: FrenzyColor,
            FrenzyNumber: FrenzyActive ? FrenzyNumber : -1,
            SkipNext: Players.Select(p => p.SkipNext).ToArray(),
            LastEffect: LastEffect,
            LordSuit: me.LordSuit,
            DrawPileCount: DrawPile.Count,
            TurnCount: TurnCount,
            Round: Round,
            Ranks: Players.Select(p => p.Rank).ToArray(),
            Eliminated: Players.Select(p => p.Eliminated).ToArray(),
            DrawChoices: myTurn ? GambleCards.ToList() : new List<Card>(),
            DrawChoicePlayable: myTurn ? GambleCards.Select(c => Rules.CanPlay(c, this, playerId)).ToArray() : Array.Empty<bool>(),
            Options: Options,
            AugmentCountdown: Options.SpecialAugments && me.Augments.Count < SpecialAugments.MaxPerPlayer
                ? SpecialAugments.TurnsUntilOffer(me.TurnsStarted)
                : -1,
            Winner: Winner)
        {
            // 특수 증강 고르기나 억지 뽑기가 먼저 끝나야 미니게임을 합니다. (엔진도 같은 순서로 받습니다)
            Minigame = ChoosingAugment || PayingDebt || Drafting || GambleCards.Count > 0 ? null : PendingMinigame,
            WinGoals = Players.Select(WinGoalsOf).ToArray(),
        };
    }

    /// <summary>
    /// 승리 조건 증강의 진행도입니다. 모두에게 공개됩니다. (다른 사람이 얼마나 가까운지 보고 견제할 수 있게)
    /// </summary>
    public static IReadOnlyList<WinGoal> WinGoalsOf(PlayerState p)
    {
        var goals = new List<WinGoal>();
        if (p.Has(SpecialAugmentId.Curling))
        {
            goals.Add(new WinGoal("컬링", Math.Min(p.CurlingCharge, StreakRules.CurlingCharge), StreakRules.CurlingCharge));
        }

        if (p.Has(SpecialAugmentId.Jackpot))
        {
            goals.Add(new WinGoal("잭팟", 0, 0, p.LastJackpot));
        }

        if (p.Has(SpecialAugmentId.Domino))
        {
            goals.Add(new WinGoal("도미노", p.DominoChain, StreakRules.DominoTarget,
                p.DominoLast < 0 ? "" : p.DominoLast == 0 ? "다음 1" : p.DominoLast == 9 ? "다음 8" : $"다음 {p.DominoLast - 1} 또는 {p.DominoLast + 1}"));
        }

        if (p.Has(SpecialAugmentId.Oracle))
        {
            goals.Add(new WinGoal("예언자", p.OracleStreak, StreakRules.OracleTarget,
                p.OracleGuess == CardColor.Wild ? "" : Card.ColorName(p.OracleGuess)));
        }

        if (p.Has(SpecialAugmentId.Marksman))
        {
            goals.Add(new WinGoal("정밀 사수", p.MarksmanStreak, MarksmanRules.Target));
        }

        return goals;
    }
}

/// <summary>어벤져스 증강을 가진 사람에게 보여 주는 카드별 직업입니다.</summary>
public sealed record CardJob(int CardId, Job Job);

/// <summary>
/// 한 플레이어 시점의 공개 정보입니다. 봇의 판단 입력이자, 멀티플레이에서 네트워크로 보내는 데이터입니다.
/// </summary>
public sealed record PlayerView(
    int PlayerId,
    IReadOnlyList<Card> Hand,
    int[] HandCounts,
    IReadOnlyList<AugmentInfo>[] Augments,
    int[] JobProgress,
    bool[] Sealed,
    Card TopCard,
    CardColor CurrentColor,
    int CurrentPlayer,
    int Direction,
    bool IsMyTurn,
    bool HasDrawnThisTurn,
    Card? DrawnCard,
    IReadOnlyList<int> PlayableCardIds,
    IReadOnlyList<int> ColorRequiredIds,
    IReadOnlyList<int> TargetRequiredIds,
    bool CanDraw,
    int PendingPenalty,
    bool CanPass,
    IReadOnlyList<AbilityInfo> AbilityChoices,
    bool ChoosingAbility,
    IReadOnlyList<AugmentInfo> AugmentChoices,
    bool ChoosingAugment,
    bool[] DraftPending,
    int DebtPlayer,
    int DebtRemaining,
    CardColor DebtSuit,
    int DebtDrawn,
    string DebtReason,
    bool MustDraw,
    IReadOnlyList<CardJob> Jobs,
    bool FrenzyActive,
    CardColor FrenzyColor,
    int FrenzyNumber,
    bool[] SkipNext,
    CardKind LastEffect,
    CardColor LordSuit,
    int DrawPileCount,
    int TurnCount,
    int Round,
    int[] Ranks,
    bool[] Eliminated,
    IReadOnlyList<Card> DrawChoices,
    bool[] DrawChoicePlayable,
    GameOptions Options,
    int AugmentCountdown,
    int? Winner)
{
    /// <summary>지금 진행 중인 미니게임입니다. 모두에게 보입니다.</summary>
    public MinigameInfo? Minigame { get; init; }

    /// <summary>자리마다 승리 조건 증강의 진행도입니다. 모두에게 보입니다.</summary>
    public IReadOnlyList<WinGoal>[] WinGoals { get; init; } = Array.Empty<IReadOnlyList<WinGoal>>();

    /// <summary>내가 지금 해야 하는 미니게임입니다.</summary>
    public MinigameInfo? MyMinigame => Minigame != null && Minigame.Player == PlayerId && !GameOver ? Minigame : null;

    public int PlayerCount => HandCounts.Length;

    public bool IsActive(int seat) => Ranks[seat] == 0;

    public bool GameOver => Winner.HasValue;

    /// <summary>내 다음 차례인 플레이어 자리입니다.</summary>
    public int NextSeat
    {
        get
        {
            int seat = PlayerId;
            for (int guard = 0; guard < PlayerCount; guard++)
            {
                seat = ((seat + Direction) % PlayerCount + PlayerCount) % PlayerCount;
                if (IsActive(seat))
                {
                    break;
                }
            }

            return seat;
        }
    }

    public bool PayingDebt => DebtPlayer >= 0;
}
