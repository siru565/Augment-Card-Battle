using System;
using System.Collections.Generic;
using System.Linq;

namespace SpCardgame.Core;

public enum ActionType
{
    Play,
    Draw,
    Pass,
    ChooseAbility,
    ChooseAugment,
    ForcedDraw,
    ChooseDraw,
}

/// <summary>
/// 플레이어가 하려는 행동입니다. 클라이언트는 이 값만 서버에 보내고, 판정은 서버의 GameEngine이 합니다.
/// </summary>
public readonly record struct PlayerAction(
    ActionType Type,
    int CardId = -1,
    CardColor ChosenColor = CardColor.Wild,
    int Target = -1)
{
    public static PlayerAction Play(int cardId, CardColor chosenColor = CardColor.Wild, int target = -1) =>
        new(ActionType.Play, cardId, chosenColor, target);

    public static PlayerAction Draw() => new(ActionType.Draw);
    public static PlayerAction Pass() => new(ActionType.Pass);

    /// <summary>억지로 뽑아야 하는 카드를 한 장 뽑습니다.</summary>
    public static PlayerAction ForcedDraw() => new(ActionType.ForcedDraw);

    /// <summary>각성 카드 선택지 중 index번째 능력을 고릅니다. 필요하면 대상과 문양도 함께 보냅니다.</summary>
    public static PlayerAction ChooseAbility(int index, int target = -1, CardColor suit = CardColor.Wild) =>
        new(ActionType.ChooseAbility, index, suit, target);

    /// <summary>도박사: 뽑은 2장 중 index번째를 가집니다.</summary>
    public static PlayerAction ChooseDraw(int index) => new(ActionType.ChooseDraw, index);

    /// <summary>특수 증강 선택지 중 index번째를 고릅니다.</summary>
    public static PlayerAction ChooseAugment(int index) => new(ActionType.ChooseAugment, index);
}

public readonly record struct ActionResult(bool Ok, string? Error = null)
{
    public static ActionResult Success() => new(true);
    public static ActionResult Fail(string error) => new(false, error);
}

/// <summary>
/// 룰을 적용해서 GameState를 바꾸는 유일한 진입점입니다.
/// Godot에 의존하지 않으므로 콘솔 테스트, 봇 시뮬레이션, 멀티 서버에서 똑같이 사용합니다.
/// </summary>
public sealed class GameEngine
{
    public GameState State { get; } = new();

    /// <summary>엔진 전용 난수입니다. 효과에서도 이 값을 써야 시드 재현이 유지됩니다.</summary>
    public Random Rng { get; }

    /// <summary>전체 카드 장수입니다. 카드가 복제되거나 사라지지 않았는지 검사할 때 씁니다.</summary>
    public int DeckSize { get; private set; }

    /// <summary>진행 로그를 받을 콜백입니다. null이면 로그를 남기지 않습니다.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>화면 연출용 사건을 받을 콜백입니다.</summary>
    public event Action<GameEvent>? Event;

    /// <summary>플레이어 표시 이름입니다. 비어 있으면 P0, P1처럼 표시합니다.</summary>
    public string[] Names { get; set; } = Array.Empty<string>();

    public GameEngine(int playerCount, int seed, GameOptions? options = null)
    {
        if (playerCount is < 2 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount), "플레이어는 2~8명이어야 합니다.");
        }

        Rng = new Random(seed);
        State.Options = options ?? new GameOptions();

        for (int i = 0; i < playerCount; i++)
        {
            State.Players.Add(new PlayerState(i));
        }
    }

    public string NameOf(int playerId) =>
        playerId >= 0 && playerId < Names.Length ? Names[playerId] : $"P{playerId}";

    public void Emit(string message) => Log?.Invoke(message);

    private void Raise(GameEvent gameEvent) => Event?.Invoke(gameEvent);

    /// <summary>
    /// 덱을 만들고 섞은 뒤 7장씩 나눠 주고, 첫 카드를 펼칩니다.
    /// 첫 카드는 숫자 카드가 나올 때까지 다시 뽑습니다.
    /// </summary>
    public void Start()
    {
        var deck = Deck.BuildPrototype();
        DeckSize = deck.Count;
        Deck.Shuffle(deck, Rng);
        State.DrawPile.AddRange(deck);
        State.JobSalt = Rng.Next();

        string mode = State.Options.SpecialAugments ? $" · 특수 증강 켜짐 (시작할 때 + {SpecialAugments.OfferEveryTurns}턴마다)" : "";
        Emit($"게임 시작 · 덱 {DeckSize}장{mode}");

        for (int round = 0; round < Rules.StartingHandSize; round++)
        {
            foreach (var player in State.Players)
            {
                DrawCards(player.Id, 1);
            }
        }

        Card first;
        while (true)
        {
            first = PopRaw()!;
            if (first.Kind == CardKind.Number)
            {
                break;
            }

            // 숫자 카드가 아니면 덱 맨 아래로 돌려보냅니다.
            State.DrawPile.Insert(0, first);
        }

        State.DiscardPile.Add(first);
        State.CurrentColor = first.Color;
        State.CurrentPlayer = Rng.Next(State.PlayerCount);
        State.Players[State.CurrentPlayer].TurnsStarted = 1;

        Emit($"첫 카드 [{first}], 선 플레이어 {NameOf(State.CurrentPlayer)}");

        // 특수 증강을 켰다면 모두가 동시에 첫 증강을 고릅니다. 다 고를 때까지 첫 차례는 시작되지 않습니다.
        if (State.Options.SpecialAugments)
        {
            foreach (var player in State.Players)
            {
                player.DraftChoices.AddRange(SpecialAugments.RollChoices(player.Augments, Rng));
                Raise(new GameEvent(GameEventType.AugmentOffered, player.Id));
            }

            Emit("  ✦ 모두 첫 특수 증강을 고르는 중입니다.");
        }
    }

    /// <summary>
    /// 플레이어의 행동을 검증하고 적용합니다. 규칙에 맞지 않으면 상태를 바꾸지 않고 실패를 반환합니다.
    /// </summary>
    public ActionResult Apply(int playerId, PlayerAction action)
    {
        if (State.IsFinished)
        {
            return ActionResult.Fail("이미 끝난 게임입니다.");
        }

        ActionResult result;

        if (State.Drafting)
        {
            if (action.Type != ActionType.ChooseAugment || State.Players[playerId].DraftChoices.Count == 0)
            {
                return ActionResult.Fail("모두 첫 특수 증강을 고르는 중입니다.");
            }

            result = TryDraftAugment(playerId, action.CardId);
        }
        else if (State.PayingDebt)
        {
            if (playerId != State.InputPlayer)
            {
                return ActionResult.Fail($"{NameOf(State.InputPlayer)}가 카드를 뽑는 중입니다.");
            }

            if (action.Type != ActionType.ForcedDraw)
            {
                return ActionResult.Fail("먼저 카드를 뽑아야 합니다.");
            }

            result = TryForcedDraw(playerId);
        }
        else if (playerId != State.CurrentPlayer)
        {
            return ActionResult.Fail("자기 차례가 아닙니다.");
        }
        else if (State.GambleCards.Count > 0)
        {
            result = action.Type == ActionType.ChooseDraw
                ? TryChooseDraw(playerId, action.CardId)
                : ActionResult.Fail("먼저 뽑은 2장 중 한 장을 골라야 합니다.");
        }
        else if (State.ChoosingAugment)
        {
            result = action.Type == ActionType.ChooseAugment
                ? TryChooseAugment(playerId, action.CardId)
                : ActionResult.Fail("먼저 특수 증강을 하나 골라야 합니다.");
        }
        else if (State.ChoosingAbility)
        {
            result = action.Type == ActionType.ChooseAbility
                ? TryChooseAbility(playerId, action)
                : ActionResult.Fail("먼저 각성 능력을 하나 골라야 합니다.");
        }
        else
        {
            result = action.Type switch
            {
                ActionType.Play => TryPlay(playerId, action),
                ActionType.Draw => TryDraw(playerId),
                ActionType.Pass => TryPass(playerId),
                _ => ActionResult.Fail("지금은 할 수 없는 행동입니다."),
            };
        }

        if (result.Ok)
        {
            CheckAltWins();
            CheckEliminations();
        }

        return result;
    }

    // ───────────── 행동 처리 ─────────────

    private ActionResult TryPlay(int playerId, PlayerAction action)
    {
        var player = State.Players[playerId];
        var card = player.Hand.FirstOrDefault(c => c.Id == action.CardId);

        if (card == null)
        {
            return ActionResult.Fail("손에 없는 카드입니다.");
        }

        if (State.HasDrawnThisTurn && !State.FrenzyActive && State.DrawnCard?.Id != card.Id)
        {
            return ActionResult.Fail("카드를 뽑은 뒤에는 뽑은 카드만 낼 수 있습니다.");
        }

        if (!Rules.CanPlay(card, State, playerId))
        {
            return ActionResult.Fail($"[{card}]는 지금 낼 수 없습니다.");
        }

        bool needsColor = Rules.NeedsColor(card, State, playerId);
        if (needsColor && action.ChosenColor == CardColor.Wild)
        {
            return ActionResult.Fail("이 카드는 문양을 골라야 합니다.");
        }

        bool needsTarget = Rules.NeedsTarget(card, State, playerId);
        if (needsTarget && (!State.IsActive(action.Target) || action.Target == playerId))
        {
            return ActionResult.Fail("대상 플레이어를 골라야 합니다.");
        }

        player.Hand.Remove(card);
        State.DiscardPile.Add(card);
        State.CurrentColor = needsColor ? action.ChosenColor : card.Color;

        string colorNote = needsColor ? $" → {Card.ColorName(action.ChosenColor)}" : "";
        string targetNote = needsTarget ? $" (대상: {NameOf(action.Target)})" : "";
        Emit($"  {NameOf(playerId)}: [{card}]{colorNote}{targetNote} (남은 {player.Hand.Count}장)");
        Raise(new GameEvent(GameEventType.CardPlayed, playerId, action.Target, card));

        // 손패를 다 내면 순위가 정해집니다. 게임은 남은 사람끼리 계속되고, 마지막 카드의 효과도 발동합니다.
        bool wentOut = CheckWin(playerId);
        if (State.IsFinished)
        {
            return ActionResult.Success();
        }

        if (wentOut)
        {
            int outSteps = 1;
            if (!State.FrenzyActive && card.Kind is not (CardKind.Awaken or CardKind.Frenzy))
            {
                outSteps = ResolveEffect(card.Kind, card.Color, action.Target) ?? 1;
                if (card.Kind != CardKind.Copy)
                {
                    State.LastEffect = card.Kind;
                }
            }

            State.PendingAbilities.Clear();
            State.FrenzyActive = false;
            FinishTurn(outSteps);
            return ActionResult.Success();
        }

        // 연속 내기 중에는 효과 없이 이어서 냅니다. 남은 장수를 다 쓰면 자동으로 끝납니다.
        if (State.FrenzyActive)
        {
            State.FrenzyRemaining--;

            // 다 썼거나, 더 이어서 낼 카드가 없으면 자동으로 끝냅니다.
            if (State.FrenzyRemaining <= 0 || !player.Hand.Any(c => Rules.CanPlay(c, State, playerId)))
            {
                EndTurn(1);
            }

            return ActionResult.Success();
        }

        int? steps = ResolveEffect(card.Kind, card.Color, action.Target);
        if (card.Kind != CardKind.Copy)
        {
            State.LastEffect = card.Kind;
        }

        if (State.IsFinished || !steps.HasValue)
        {
            return ActionResult.Success();
        }

        // 청소부: 0을 내면 내 카드 1장(무작위)을 더 버립니다.
        if (card.Kind == CardKind.Number && card.Number == 0 && player.Has(SpecialAugmentId.Janitor) && player.Hand.Count > 1)
        {
            var junk = player.Hand[Rng.Next(player.Hand.Count)];
            player.Hand.Remove(junk);
            State.DiscardPile.Insert(0, junk);
            Emit($"    → [청소부] [{junk}]도 함께 버립니다.");
        }

        // 쌍둥이: 숫자 카드를 낸 뒤 같은 숫자 카드를 문양에 상관없이 원하는 만큼 이어서 낼 수 있습니다.
        if (card.Kind == CardKind.Number && player.Has(SpecialAugmentId.Twins) &&
            player.Hand.Any(c => c.Kind == CardKind.Number && c.Number == card.Number))
        {
            State.FrenzyActive = true;
            State.FrenzyNumber = card.Number;
            State.FrenzyRemaining = int.MaxValue;
            Emit($"    → [쌍둥이] 숫자 {card.Number} 카드를 이어서 낼 수 있습니다.");
            return ActionResult.Success();
        }

        // 시간술사: 숫자 카드를 낸 뒤 같은 문양 숫자를 한 장 더 낼 수 있습니다.
        if (card.Kind == CardKind.Number && player.Has(SpecialAugmentId.Chronomancer) &&
            player.Hand.Any(c => c.Kind == CardKind.Number && c.Color == card.Color))
        {
            State.FrenzyActive = true;
            State.FrenzyColor = card.Color;
            State.FrenzyRemaining = 1;
            Emit($"    → [시간술사] {Card.ColorName(card.Color)} 숫자 카드를 한 장 더 낼 수 있습니다.");
            return ActionResult.Success();
        }

        FinishTurn(steps.Value);
        return ActionResult.Success();
    }

    private ActionResult TryDraw(int playerId)
    {
        if (State.HasDrawnThisTurn)
        {
            return ActionResult.Fail("이번 턴에는 이미 카드를 뽑았습니다.");
        }

        if (State.FrenzyActive)
        {
            return ActionResult.Fail("연속 내기 중에는 카드를 뽑을 수 없습니다. 턴을 넘기세요.");
        }

        if (State.PendingPenalty > 0)
        {
            TakePenalty(playerId);
            return ActionResult.Success();
        }

        // 도박사: 2장을 펼쳐 놓고 직접 한 장을 고릅니다.
        if (State.Players[playerId].Has(SpecialAugmentId.Gambler) && StartGamble())
        {
            Emit($"  {NameOf(playerId)}: [도박사] 2장 중 1장을 고르는 중");
            return ActionResult.Success();
        }

        var drawn = DrawOne(playerId);
        Emit($"  {NameOf(playerId)}: 1장 뽑음");

        State.HasDrawnThisTurn = true;
        State.DrawnCard = drawn;

        // 뽑을 카드가 없거나, 뽑은 카드를 낼 수 없으면 바로 턴을 넘깁니다.
        if (drawn == null || !Rules.CanPlay(drawn, State, playerId))
        {
            EndTurn(1);
        }

        return ActionResult.Success();
    }

    private ActionResult TryPass(int playerId)
    {
        if (!State.HasDrawnThisTurn && !State.FrenzyActive)
        {
            return ActionResult.Fail("카드를 뽑기 전에는 턴을 넘길 수 없습니다.");
        }

        Emit(State.FrenzyActive ? $"  {NameOf(playerId)}: 연속 내기 종료" : $"  {NameOf(playerId)}: 턴 넘김");
        EndTurn(1);
        return ActionResult.Success();
    }

    /// <summary>
    /// 도박사: 덱에서 2장을 꺼내 펼칩니다. 2장이 안 되면 false를 돌려주고 보통 뽑기를 합니다.
    /// </summary>
    private bool StartGamble()
    {
        var first = PopRaw();
        var second = PopRaw();
        if (first == null || second == null)
        {
            // 한 장뿐이면 도로 넣고 보통 뽑기로 처리합니다.
            if (first != null)
            {
                State.DrawPile.Add(first);
            }

            if (second != null)
            {
                State.DrawPile.Add(second);
            }

            return false;
        }

        State.GambleCards.Add(first);
        State.GambleCards.Add(second);
        return true;
    }

    /// <summary>도박사: 펼친 2장 중 하나를 가지고, 나머지는 덱 맨 아래로 돌려보냅니다.</summary>
    private ActionResult TryChooseDraw(int playerId, int index)
    {
        if (index < 0 || index >= State.GambleCards.Count)
        {
            return ActionResult.Fail("잘못된 선택입니다.");
        }

        var keep = State.GambleCards[index];
        foreach (var other in State.GambleCards.Where(c => c != keep))
        {
            State.DrawPile.Insert(0, other);
        }

        State.GambleCards.Clear();
        State.Players[playerId].Hand.Add(keep);
        Raise(new GameEvent(GameEventType.CardDrawn, playerId));
        Emit($"    → [도박사] 한 장을 골라 가졌습니다.");

        State.HasDrawnThisTurn = true;
        State.DrawnCard = keep;
        if (!Rules.CanPlay(keep, State, playerId))
        {
            EndTurn(1);
        }

        return ActionResult.Success();
    }

    /// <summary>
    /// 쌓인 공격을 모두 받습니다. 합산된 장수를 직접 뽑고, 이번 차례는 그걸로 끝납니다.
    /// </summary>
    private void TakePenalty(int playerId)
    {
        int amount = State.PendingPenalty;
        if (State.Players[playerId].Has(SpecialAugmentId.Berserker))
        {
            amount *= 2;
        }

        State.PendingPenalty = 0;
        var me = State.Players[playerId];

        // 반사의 거울: 절반(내림)을 마지막으로 얹은 사람에게 되돌려 보냅니다.
        int attacker = State.LastAttacker;
        State.LastAttacker = -1;
        if (me.Has(SpecialAugmentId.Mirror) && attacker >= 0 && attacker != playerId && amount >= 2)
        {
            int back = amount / 2;
            amount -= back;
            AddDebt(attacker, back, null, $"반사된 +{back}");
            Emit($"    → [반사의 거울] {back}장을 {NameOf(attacker)}에게 되돌려 보냅니다.");
            Raise(new GameEvent(GameEventType.Attack, playerId, attacker, Amount: back));
        }

        // 수호천사: 손패가 2장 이하면 절반만 받습니다.
        if (me.Has(SpecialAugmentId.Guardian) && me.Hand.Count <= 2 && amount >= 2)
        {
            Emit($"    → [수호천사] {amount}장 중 절반만 받습니다.");
            amount = (amount + 1) / 2;
        }

        // 불사조: 한 번에 최대 장수까지만 받습니다.
        if (me.Has(SpecialAugmentId.Phoenix) && amount > SpecialAugments.PhoenixCap)
        {
            Emit($"    → [불사조] {amount}장 중 {SpecialAugments.PhoenixCap}장만 받습니다.");
            amount = SpecialAugments.PhoenixCap;
        }

        // 받는 사람의 빚이 먼저 오도록 맨 앞에 넣습니다.
        State.DrawQueue.Insert(0, new DrawDebt(playerId, amount, null, $"누적 +{amount} 공격"));
        Emit($"  {NameOf(playerId)}: 누적 공격을 받아 {amount}장을 뽑습니다.");
        FinishTurn(1);
    }

    /// <summary>
    /// 억지로 뽑아야 하는 카드를 한 장 뽑습니다. 빚을 다 갚으면 미뤄 둔 턴 진행을 이어갑니다.
    /// </summary>
    private ActionResult TryForcedDraw(int playerId)
    {
        var debt = State.DrawQueue[0];
        var card = DrawOne(playerId);
        if (card != null)
        {
            State.DebtProgress++;
        }

        bool done = card == null
            || (debt.UntilSuit.HasValue && card.Color == debt.UntilSuit.Value)
            || State.DebtProgress >= debt.MaxCards;

        if (!done)
        {
            return ActionResult.Success();
        }

        string suitNote = debt.UntilSuit.HasValue ? $", {Card.ColorName(debt.UntilSuit.Value)} 카드를 찾을 때까지" : "";
        Emit($"    → {NameOf(playerId)}가 {State.DebtProgress}장을 뽑았습니다. ({debt.Reason}{suitNote})");
        State.DrawQueue.RemoveAt(0);
        State.DebtProgress = 0;

        if (State.PayingDebt)
        {
            RaiseDebtStarted(State.DrawQueue[0]);
            return ActionResult.Success();
        }

        if (State.DeferredSteps.HasValue)
        {
            int steps = State.DeferredSteps.Value;
            State.DeferredSteps = null;
            // 다른 승리 조건으로 차례인 사람이 빠지면 AfterRemoval이 이미 차례를 넘겼으므로 여기서는 넘기지 않습니다.
            // (두 번 넘기면 다음 사람의 특수 증강 선택지가 그 다음 사람에게 잘못 남습니다)
            int current = State.CurrentPlayer;
            CheckAltWins();
            if (!State.IsFinished && State.CurrentPlayer == current)
            {
                EndTurn(steps);
            }
        }

        return ActionResult.Success();
    }

    /// <summary>
    /// 턴을 끝냅니다. 누군가 카드를 뽑아야 하면, 다 뽑을 때까지 미뤘다가 끝냅니다.
    /// </summary>
    private void FinishTurn(int steps)
    {
        if (State.PayingDebt)
        {
            State.DeferredSteps = steps;
            RaiseDebtStarted(State.DrawQueue[0]);
            return;
        }

        EndTurn(steps);
    }

    /// <summary>억지 뽑기가 시작됐음을 알립니다. 문양이 나올 때까지 뽑는 경우 Amount는 -1입니다.</summary>
    private void RaiseDebtStarted(DrawDebt debt) =>
        Raise(new GameEvent(GameEventType.ForcedDrawStarted, State.CurrentPlayer, debt.Player,
            Amount: debt.UntilSuit.HasValue ? -1 : debt.MaxCards, Text: debt.Reason));

    /// <summary>카드 빚을 추가합니다. 당한 사람이 직접 뽑기를 눌러서 갚습니다.</summary>
    private void AddDebt(int playerId, int maxCards, CardColor? untilSuit, string reason)
    {
        State.DrawQueue.Add(new DrawDebt(playerId, maxCards, untilSuit, reason));
    }

    // ───────────── 카드 효과 ─────────────

    /// <summary>
    /// 카드 효과를 처리하고, 다음 차례까지 몇 자리 이동할지 반환합니다. null이면 턴이 계속됩니다(폭주, 각성).
    /// </summary>
    private int? ResolveEffect(CardKind kind, CardColor color, int target)
    {
        int current = State.CurrentPlayer;

        switch (kind)
        {
            case CardKind.Skip:
            {
                int next = State.SeatAfter(current);
                bool sniper = State.Players[current].Has(SpecialAugmentId.Sniper);
                int victim = sniper && target >= 0 && target != current ? target : next;

                // 저격수: 저격당한 사람은 카드 2장도 뽑습니다.
                if (sniper)
                {
                    AddDebt(victim, 2, null, "저격");
                }

                // 저격수: 멀리 있는 사람을 고르면 그 사람의 다음 차례를 건너뜁니다. (자유로운 영혼은 무시)
                if (victim != next && State.Players[victim].Has(SpecialAugmentId.FreeSpirit))
                {
                    Emit($"    → [자유로운 영혼] {NameOf(victim)}는 저격당하지 않습니다.");
                    Raise(new GameEvent(GameEventType.Immune, current, victim, Text: "자유로운 영혼"));
                    return 1;
                }

                if (victim != next)
                {
                    State.Players[victim].SkipNext = true;
                    Emit($"    → [저격수] {NameOf(victim)}의 다음 차례를 건너뛰고 2장을 뽑게 합니다.");
                    Raise(new GameEvent(GameEventType.Skip, current, victim));
                    return 1;
                }

                Emit($"    → {NameOf(next)}의 차례를 건너뜁니다.");
                Raise(new GameEvent(GameEventType.Skip, current, next));
                return 2;
            }

            case CardKind.Reverse:
                State.Direction *= -1;
                Emit("    → 방향이 바뀝니다.");
                Raise(new GameEvent(GameEventType.Reverse, current));

                // 역풍: 리버스를 내면 바뀐 방향의 다음 사람이 1장을 뽑습니다.
                if (State.Players[current].Has(SpecialAugmentId.Headwind))
                {
                    int blown = State.SeatAfter(current);
                    AddDebt(blown, 1, null, "역풍");
                    Emit($"    → [역풍] {NameOf(blown)}가 1장을 뽑습니다.");
                }

                // 시간 도둑: 방향을 바꾸고 내 차례가 한 번 더 옵니다.
                if (State.Players[current].Has(SpecialAugmentId.TimeThief))
                {
                    Emit($"    → [시간 도둑] {NameOf(current)}의 차례가 한 번 더 옵니다.");
                    return 0;
                }

                return State.ActiveCount == 2 ? 2 : 1;

            case CardKind.DrawOne:
            case CardKind.DrawTwo:
            case CardKind.DrawThree:
            case CardKind.WildDrawFour:
                return Punish(Card.DrawAmount(kind), kind);

            case CardKind.Swap:
                SwapRandomCards(current, target);
                return 1;

            case CardKind.Seal:
                if (State.Players[target].Has(SpecialAugmentId.FreeSpirit))
                {
                    Emit($"    → [자유로운 영혼] {NameOf(target)}는 봉인되지 않습니다.");
                    Raise(new GameEvent(GameEventType.Immune, current, target, Text: "자유로운 영혼"));
                    return 1;
                }

                State.Players[target].Sealed = true;
                Emit($"    → {NameOf(target)}는 다음 차례에 숫자 카드만 낼 수 있습니다.");
                return 1;

            case CardKind.Copy:
                if (State.LastEffect is CardKind.Number or CardKind.Wild or CardKind.Copy)
                {
                    Emit("    → 복사할 효과가 없습니다.");
                    return 1;
                }

                Emit($"    → [{Card.KindName(State.LastEffect)}] 효과를 복사합니다.");
                return ResolveEffect(State.LastEffect, color, target);

            case CardKind.Frenzy:
                State.FrenzyActive = true;
                State.FrenzyColor = color;
                State.FrenzyRemaining = int.MaxValue;
                Emit($"    → [폭주] {Card.ColorName(color)} 숫자 카드를 연달아 낼 수 있습니다.");
                return null;

            case CardKind.StealAugment:
                StealAugment(current, target);
                return 1;

            case CardKind.Purify:
                Purify();
                return 1;

            case CardKind.Awaken:
                State.PendingAbilities.Clear();
                State.AwakenSecondPick = false;
                int abilityChoices = State.Players[current].Has(SpecialAugmentId.Foresight) ? 4 : AbilityPool.ChoiceCount;
                State.PendingAbilities.AddRange(AbilityPool.RollChoices(Rng, abilityChoices));
                Emit($"    → [각성] {NameOf(current)}가 능력 {abilityChoices}가지 중 하나를 고르는 중입니다.");
                Raise(new GameEvent(GameEventType.Awaken, current));
                return null;

            default:
                return 1;
        }
    }

    /// <summary>
    /// 다음 플레이어에게 카드를 뽑게 합니다. 평화주의자는 무시하고, 광전사는 두 배가 됩니다.
    /// 뽑기는 당한 사람이 직접 누르며, 그 사람의 차례는 건너뜁니다.
    /// </summary>
    private int Punish(int amount, CardKind kind)
    {
        int attacker = State.CurrentPlayer;
        int target = State.SeatAfter(attacker);
        var victim = State.Players[target];

        if (State.Players[attacker].Has(SpecialAugmentId.Pacifist))
        {
            Emit($"    → [평화주의자] {NameOf(attacker)}의 +{amount}는 뽑게 하지 않습니다.");
            return 1;
        }

        if (State.Players[attacker].Has(SpecialAugmentId.Berserker))
        {
            amount *= 2;
        }

        // 폭탄마: 내가 얹는 +카드는 1장 더 쌓입니다.
        if (State.Players[attacker].Has(SpecialAugmentId.Bomber))
        {
            amount += 1;
        }

        State.LastAttacker = attacker;

        // 바로 뽑게 하지 않고 쌓아 둡니다. 다음 사람은 +카드로 넘기거나, 자기 차례에 합산해서 받습니다.
        State.PendingPenalty += amount;

        var attackerHand = State.Players[attacker].Hand;
        if (State.Players[attacker].Has(SpecialAugmentId.Vampire) && attackerHand.Count > 1)
        {
            var bitten = attackerHand[Rng.Next(attackerHand.Count)];
            attackerHand.Remove(bitten);
            victim.Hand.Add(bitten);
            Emit($"    → [흡혈귀] {NameOf(attacker)}가 카드 1장을 {NameOf(target)}에게 떠넘깁니다.");
        }

        int total = State.PendingPenalty;
        Emit(total > amount
            ? $"    → 공격 합산! 누적 +{total} → {NameOf(target)}에게 넘어갑니다."
            : $"    → {NameOf(target)}는 +카드로 넘기거나 {total}장을 받아야 합니다.");
        Raise(new GameEvent(GameEventType.Attack, attacker, target, Amount: total));
        return 1;
    }

    private void SwapRandomCards(int a, int b)
    {
        var handA = State.Players[a].Hand;
        var handB = State.Players[b].Hand;
        if (handA.Count == 0 || handB.Count == 0)
        {
            Emit("    → 바꿀 카드가 없습니다.");
            return;
        }

        var cardA = handA[Rng.Next(handA.Count)];
        var cardB = handB[Rng.Next(handB.Count)];
        handA.Remove(cardA);
        handB.Remove(cardB);
        handA.Add(cardB);
        handB.Add(cardA);
        Emit($"    → {NameOf(a)}와 {NameOf(b)}가 카드 1장을 맞바꿨습니다.");
    }

    /// <summary>상대의 특수 증강 1개를 빼앗습니다. (현재 덱에는 없는 카드입니다)</summary>
    private void StealAugment(int thief, int target)
    {
        var victim = State.Players[target];
        var candidates = victim.Augments
            .Where(id => !State.Players[thief].Has(id))
            .ToList();
        if (candidates.Count == 0 || State.Players[thief].Augments.Count >= SpecialAugments.MaxPerPlayer)
        {
            Emit($"    → {NameOf(target)}에게서 빼앗을 수 있는 증강이 없습니다.");
            return;
        }

        var stolen = candidates[Rng.Next(candidates.Count)];
        victim.Augments.Remove(stolen);
        GainAugment(thief, stolen);
        Emit($"    → {NameOf(thief)}가 {NameOf(target)}의 [{SpecialAugments.NameOf(stolen)}]을 빼앗았습니다.");
    }

    /// <summary>모두의 특수 증강을 1개씩 없앱니다. (현재 덱에는 없는 카드입니다)</summary>
    private void Purify()
    {
        Emit("    → [정화] 모두의 증강이 1개씩 사라집니다.");
        foreach (var player in State.Players.Where(p => p.Augments.Count > 0))
        {
            var removed = player.Augments[Rng.Next(player.Augments.Count)];
            player.Augments.Remove(removed);
            Emit($"      {NameOf(player.Id)}: [{SpecialAugments.NameOf(removed)}] 제거");
        }
    }

    // ───────────── 특수 증강 ─────────────

    /// <summary>
    /// 차례가 시작될 때, 특수 증강을 고를 차례인지 확인하고 선택지를 띄웁니다.
    /// </summary>
    private void OfferAugmentIfDue(int playerId)
    {
        // 새 차례가 시작되면 앞사람에게 떴던 선택지는 항상 지웁니다.
        State.PendingAugments.Clear();
        var player = State.Players[playerId];
        if (!State.Options.SpecialAugments ||
            player.Augments.Count >= SpecialAugments.MaxPerPlayer ||
            !SpecialAugments.IsOfferTurn(player.TurnsStarted))
        {
            return;
        }

        var choices = SpecialAugments.RollChoices(player.Augments, Rng);
        if (choices.Count == 0)
        {
            return;
        }

        State.PendingAugments.Clear();
        State.PendingAugments.AddRange(choices);
        Emit($"  ✦ {NameOf(playerId)}의 {player.TurnsStarted}번째 차례 · 특수 증강을 고르는 중입니다.");
        Raise(new GameEvent(GameEventType.AugmentOffered, playerId));
    }

    private ActionResult TryChooseAugment(int playerId, int index)
    {
        if (index < 0 || index >= State.PendingAugments.Count)
        {
            return ActionResult.Fail("잘못된 증강 선택입니다.");
        }

        var id = State.PendingAugments[index];
        State.PendingAugments.Clear();
        var player = State.Players[playerId];
        if (player.Augments.Count >= SpecialAugments.MaxPerPlayer || player.Has(id))
        {
            // 안전장치: 이미 가득 찼거나 같은 증강이면 받지 않고 넘어갑니다.
            return ActionResult.Success();
        }

        GainAugment(playerId, id);

        var tier = Augment.TierName(SpecialAugments.TierOf(id));
        Emit($"  ★ {NameOf(playerId)}: 특수 증강 [{tier}] {SpecialAugments.NameOf(id)} 획득");
        return ActionResult.Success();
    }

    /// <summary>게임 시작 때 첫 특수 증강을 고릅니다. 모두 고르면 첫 차례가 시작됩니다.</summary>
    private ActionResult TryDraftAugment(int playerId, int index)
    {
        var choices = State.Players[playerId].DraftChoices;
        if (index < 0 || index >= choices.Count)
        {
            return ActionResult.Fail("잘못된 증강 선택입니다.");
        }

        var id = choices[index];
        choices.Clear();
        GainAugment(playerId, id);

        var tier = Augment.TierName(SpecialAugments.TierOf(id));
        Emit($"  ★ {NameOf(playerId)}: 특수 증강 [{tier}] {SpecialAugments.NameOf(id)} 획득");

        if (!State.Drafting)
        {
            Emit($"  모두 골랐습니다. {NameOf(State.CurrentPlayer)}부터 시작합니다.");
        }

        return ActionResult.Success();
    }

    private void GainAugment(int playerId, SpecialAugmentId id)
    {
        var player = State.Players[playerId];
        player.Augments.Add(id);

        if (id == SpecialAugmentId.SuitLord)
        {
            player.LordSuit = player.Hand.Where(c => c.Color != CardColor.Wild)
                .GroupBy(c => c.Color).OrderByDescending(g => g.Count())
                .Select(g => g.Key).FirstOrDefault(CardColor.Red);
            Emit($"    → [문양 군주] {NameOf(playerId)}의 문양은 {Card.ColorName(player.LordSuit)}입니다.");
        }

        Raise(new GameEvent(GameEventType.AugmentGained, playerId, Text: SpecialAugments.NameOf(id)));
    }

    // ───────────── 각성 능력 ─────────────

    private ActionResult TryChooseAbility(int playerId, PlayerAction action)
    {
        int index = action.CardId;
        if (index < 0 || index >= State.PendingAbilities.Count)
        {
            return ActionResult.Fail("잘못된 능력 선택입니다.");
        }

        var ability = AbilityPool.Get(State.PendingAbilities[index]);

        if (ability.NeedsTarget && (!State.IsActive(action.Target) || action.Target == playerId))
        {
            return ActionResult.Fail("능력의 대상을 골라야 합니다.");
        }

        if (ability.NeedsSuit && action.ChosenColor == CardColor.Wild)
        {
            return ActionResult.Fail("능력에 쓸 문양을 골라야 합니다.");
        }

        State.PendingAbilities.Clear();
        string targetNote = ability.NeedsTarget ? $" → {NameOf(action.Target)}" : "";
        string suitNote = ability.NeedsSuit ? $" [{Card.ColorName(action.ChosenColor)}]" : "";
        Emit($"  ★ {NameOf(playerId)}: 각성 능력 [{ability.Name}]{targetNote}{suitNote}");
        Raise(new GameEvent(GameEventType.AbilityUsed, playerId, action.Target, Text: ability.Name));

        int steps = ResolveAbility(playerId, ability.Id, action.Target, action.ChosenColor);
        bool wentOut = CheckWin(playerId);
        if (State.IsFinished)
        {
            return ActionResult.Success();
        }

        // 지배자: 각성 능력을 한 번 더 고릅니다.
        if (!wentOut && !State.AwakenSecondPick && State.Players[playerId].Has(SpecialAugmentId.Dominator) && !State.PayingDebt)
        {
            State.AwakenSecondPick = true;
            State.PendingAbilities.AddRange(AbilityPool.RollChoices(Rng)
                .Where(a => a != ability.Id));
            Emit($"    → [지배자] {NameOf(playerId)}가 능력을 하나 더 고릅니다.");
            return ActionResult.Success();
        }

        FinishTurn(steps);
        return ActionResult.Success();
    }

    /// <summary>
    /// 능력 효과를 처리하고, 다음 차례까지 몇 자리 이동할지 반환합니다.
    /// </summary>
    private int ResolveAbility(int caster, AbilityId id, int target, CardColor suit)
    {
        var players = State.Players;

        switch (id)
        {
            case AbilityId.AbyssDraw:
            {
                int next = State.SeatAfter(caster);
                AddDebt(next, AbilityPool.AbyssDrawLimit, State.CurrentColor, "심연의 부름");
                Emit($"    → {NameOf(next)}는 {Card.ColorName(State.CurrentColor)} 카드가 나올 때까지 직접 뽑아야 합니다.");
                return 1;
            }

            case AbilityId.GreatRotation:
            {
                // 게임 중인 사람끼리만 돌립니다.
                var active = players.Where(p => p.Active).ToList();
                var hands = active.Select(p => p.Hand.ToList()).ToList();
                for (int i = 0; i < active.Count; i++)
                {
                    var receiver = active[(i + 1) % active.Count].Hand;
                    receiver.Clear();
                    receiver.AddRange(hands[i]);
                }

                Emit("    → 모든 손패가 다음 자리로 넘어갔습니다.");
                Raise(new GameEvent(GameEventType.HandsShuffled, caster, Text: "대회전"));
                return 1;
            }

            case AbilityId.HandSwap:
            {
                var mine = players[caster].Hand.ToList();
                players[caster].Hand.Clear();
                players[caster].Hand.AddRange(players[target].Hand);
                players[target].Hand.Clear();
                players[target].Hand.AddRange(mine);
                Emit($"    → {NameOf(caster)}와 {NameOf(target)}가 손패를 통째로 바꿨습니다.");
                Raise(new GameEvent(GameEventType.HandsShuffled, caster, target, Text: "운명 교환"));
                return 1;
            }

            case AbilityId.Brand:
                AddDebt(target, int.MaxValue, suit, "저주의 낙인");
                Emit($"    → {NameOf(target)}는 {Card.ColorName(suit)} 카드가 나올 때까지 직접 뽑아야 합니다.");
                return 1;

            case AbilityId.Storm:
                foreach (var other in players.Where(p => p.Active && p.Id != caster))
                {
                    AddDebt(other.Id, 2, null, "카드 폭풍");
                }

                Emit("    → [카드 폭풍] 나를 뺀 모두가 2장씩 직접 뽑아야 합니다.");
                return 1;

            case AbilityId.Purge:
            {
                var hand = players[caster].Hand;
                var victims = hand.Where(c => c.Kind == CardKind.Number && c.Color == suit)
                    .Take(Math.Min(AbilityPool.PurgeLimit, hand.Count - 1)).ToList();
                foreach (var card in victims)
                {
                    hand.Remove(card);

                    // 맨 위 카드와 현재 문양이 바뀌지 않도록 버린 더미 맨 아래에 넣습니다.
                    State.DiscardPile.Insert(0, card);
                }

                Emit($"    → {Card.ColorName(suit)} 숫자 카드 {victims.Count}장을 버렸습니다.");
                return 1;
            }

            case AbilityId.TimeStop:
                Emit($"    → [시간 정지] {NameOf(caster)}의 차례가 한 번 더 옵니다.");
                return 0;

            case AbilityId.MassSeal:
                foreach (var player in players.Where(p => p.Id != caster && p.Active && !p.Has(SpecialAugmentId.FreeSpirit)))
                {
                    player.Sealed = true;
                }

                Emit("    → [대봉인] 모든 상대가 다음 차례에 숫자 카드만 낼 수 있습니다.");
                return 1;

            case AbilityId.Equalize:
            {
                var active = players.Where(p => p.Active).ToList();
                var pool = active.SelectMany(p => p.Hand).ToList();
                foreach (var player in active)
                {
                    player.Hand.Clear();
                }

                Deck.Shuffle(pool, Rng);
                int seat = State.SeatAfter(caster);
                foreach (var card in pool)
                {
                    players[seat].Hand.Add(card);
                    seat = State.SeatAfter(seat);
                }

                Emit($"    → [균형의 저울] 카드 {pool.Count}장을 모두에게 똑같이 나눴습니다.");
                Raise(new GameEvent(GameEventType.HandsShuffled, caster, Text: "균형의 저울"));
                return 1;
            }

            case AbilityId.Gift:
            {
                var hand = players[caster].Hand;
                int count = Math.Min(AbilityPool.GiftCount, hand.Count - 1);
                for (int i = 0; i < count; i++)
                {
                    var card = hand[Rng.Next(hand.Count)];
                    hand.Remove(card);
                    players[target].Hand.Add(card);
                }

                Emit($"    → {NameOf(target)}에게 카드 {count}장을 떠넘겼습니다.");
                return 1;
            }

            default:
                return 1;
        }
    }

    // ───────────── 승리 판정 ─────────────

    private bool CheckWin(int playerId)
    {
        if (State.IsFinished || !State.IsActive(playerId) || State.Players[playerId].Hand.Count > 0)
        {
            return false;
        }

        Place(playerId, "손패를 모두 냈습니다", eliminated: false);
        return true;
    }

    /// <summary>
    /// 특수 증강의 다른 승리 조건(어벤져스, 블랙홀, 수집가)을 현재 차례인 사람부터 확인합니다.
    /// </summary>
    private void CheckAltWins()
    {
        if (State.IsFinished)
        {
            return;
        }

        for (int i = 0; i < State.PlayerCount && !State.IsFinished; i++)
        {
            int seat = (State.CurrentPlayer + i) % State.PlayerCount;
            var player = State.Players[seat];
            if (!player.Active)
            {
                continue;
            }

            string? reason = null;
            if (player.Has(SpecialAugmentId.Avengers) && !SpecialAugments.AvengersTurnStart && State.AvengersComplete(seat))
            {
                reason = $"어벤져스 — {SpecialAugments.AvengersJobCount}가지 직업을 모두 모았습니다";
            }
            else if (player.Has(SpecialAugmentId.BlackHole) && player.Hand.Count >= SpecialAugments.BlackHoleTarget)
            {
                reason = $"블랙홀 — 손패 {SpecialAugments.BlackHoleTarget}장을 모았습니다";
            }
            else if (player.Has(SpecialAugmentId.Collector) && HasFullSuitSet(player))
            {
                reason = "수집가 — 같은 숫자를 네 문양 모두 모았습니다";
            }

            if (reason != null)
            {
                Place(seat, reason, eliminated: false);
                AfterRemoval(seat);
            }
        }
    }

    /// <summary>
    /// 손패가 너무 많아진 사람을 탈락시킵니다. 탈락한 사람은 남은 순위 중 가장 낮은 순위가 됩니다.
    /// (블랙홀 증강을 가진 사람은 손패를 모으는 게 목표라서 탈락하지 않습니다)
    /// </summary>
    private void CheckEliminations()
    {
        for (int i = 0; i < State.PlayerCount && !State.IsFinished; i++)
        {
            int seat = (State.CurrentPlayer + i) % State.PlayerCount;
            var player = State.Players[seat];
            if (!player.Active || player.Has(SpecialAugmentId.BlackHole) || player.Hand.Count < Rules.EliminationLimit)
            {
                continue;
            }

            Place(seat, $"손패가 {Rules.EliminationLimit}장을 넘어 탈락했습니다", eliminated: true);
            AfterRemoval(seat);
        }
    }

    /// <summary>수집가 진행도입니다. 같은 숫자로 모은 문양 수(프리즘 1장 포함, 최대 4)를 돌려줍니다.</summary>
    public static int CollectorProgress(IEnumerable<Card> hand)
    {
        var cards = hand.ToList();
        int joker = cards.Any(c => c.IsWild) ? 1 : 0;
        int best = cards.Where(c => c.Kind == CardKind.Number)
            .GroupBy(c => c.Number)
            .Select(g => g.Select(c => c.Color).Distinct().Count())
            .DefaultIfEmpty(0).Max();
        return Math.Min(4, best + (best > 0 ? joker : 0));
    }

    private static bool HasFullSuitSet(PlayerState player) => CollectorProgress(player.Hand) >= 4;

    /// <summary>
    /// 순위를 정하고 그 사람을 게임에서 뺍니다. 이기면 남은 순위 중 가장 높은 순위, 탈락하면 가장 낮은 순위입니다.
    /// 한 명만 남으면 그 사람이 남은 순위를 받고 게임이 끝납니다.
    /// </summary>
    private void Place(int playerId, string reason, bool eliminated)
    {
        var player = State.Players[playerId];
        if (!player.Active || State.IsFinished)
        {
            return;
        }

        var taken = State.Players.Where(p => !p.Active).Select(p => p.Rank).ToHashSet();
        var free = Enumerable.Range(1, State.PlayerCount).Where(r => !taken.Contains(r)).ToList();
        player.Rank = eliminated ? free.Max() : free.Min();
        player.Eliminated = eliminated;
        player.SkipNext = false;
        player.Sealed = false;
        player.DraftChoices.Clear();

        // 탈락한 사람의 손패는 버린 더미 아래로 보내서 다시 쓰이게 합니다.
        if (eliminated)
        {
            foreach (var card in player.Hand)
            {
                State.DiscardPile.Insert(0, card);
            }

            player.Hand.Clear();
        }

        // 이 사람이 갚아야 할 억지 뽑기는 없앱니다.
        if (State.DrawQueue.Count > 0 && State.DrawQueue[0].Player == playerId)
        {
            State.DebtProgress = 0;
        }

        State.DrawQueue.RemoveAll(d => d.Player == playerId);

        string title = eliminated ? $"탈락 ({player.Rank}등)" : $"{player.Rank}등";
        Emit($"★★ {NameOf(playerId)} {title} ★★ ({reason}, {State.TurnCount + 1}턴)");
        Raise(new GameEvent(player.Rank == 1 ? GameEventType.Win : GameEventType.Placed, playerId,
            Amount: player.Rank, Text: eliminated ? $"탈락 — {reason}" : reason));

        if (State.ActiveCount <= 1)
        {
            var last = State.Players.FirstOrDefault(p => p.Active);
            if (last != null)
            {
                var lastTaken = State.Players.Where(p => !p.Active).Select(p => p.Rank).ToHashSet();
                last.Rank = Enumerable.Range(1, State.PlayerCount).First(r => !lastTaken.Contains(r));
                Emit($"  {NameOf(last.Id)} {last.Rank}등 (마지막까지 남았습니다)");
                Raise(new GameEvent(last.Rank == 1 ? GameEventType.Win : GameEventType.Placed, last.Id,
                    Amount: last.Rank, Text: "마지막까지 남았습니다"));
            }

            EndGame();
        }
    }

    /// <summary>모든 순위가 정해져서 게임을 끝냅니다. Winner는 1등입니다.</summary>
    private void EndGame()
    {
        State.Winner = State.Players.FirstOrDefault(p => p.Rank == 1)?.Id ?? -1;
        State.DrawQueue.Clear();
        State.PendingPenalty = 0;
        State.FrenzyActive = false;
        State.PendingAbilities.Clear();
        State.PendingAugments.Clear();
        State.GambleCards.Clear();
        Emit($"게임 종료 · 최종 순위: {string.Join(", ", State.Players.OrderBy(p => p.Rank).Select(p => $"{p.Rank}등 {NameOf(p.Id)}"))}");
    }

    /// <summary>
    /// 차례 도중에 누군가 빠졌을 때 게임이 멈추지 않도록 진행을 이어 줍니다.
    /// </summary>
    private void AfterRemoval(int playerId)
    {
        if (State.IsFinished || State.PayingDebt)
        {
            return;
        }

        if (State.DeferredSteps.HasValue)
        {
            int steps = State.DeferredSteps.Value;
            State.DeferredSteps = null;
            EndTurn(steps);
            return;
        }

        if (State.CurrentPlayer == playerId)
        {
            State.PendingAbilities.Clear();
            State.PendingAugments.Clear();
            State.GambleCards.Clear();
            EndTurn(1);
        }
    }

    // ───────────── 턴과 드로우 ─────────────

    private void EndTurn(int steps)
    {
        var current = State.Players[State.CurrentPlayer];
        current.Sealed = false;

        State.CurrentPlayer = State.SeatAfter(State.CurrentPlayer, steps);

        // 순위가 정해져 빠진 사람에게는 차례가 가지 않습니다. (한 번 더 하기 효과로 제자리에 머문 경우)
        if (!State.Players[State.CurrentPlayer].Active)
        {
            State.CurrentPlayer = State.SeatAfter(State.CurrentPlayer);
        }

        State.HasDrawnThisTurn = false;
        State.DrawnCard = null;
        State.FrenzyActive = false;
        State.FrenzyRemaining = 0;
        State.FrenzyNumber = -1;
        State.TurnCount++;

        // 저격수에게 찍힌 사람은 이번 차례를 건너뜁니다.
        for (int guard = 0; guard < State.PlayerCount && State.Players[State.CurrentPlayer].SkipNext; guard++)
        {
            var skipped = State.Players[State.CurrentPlayer];
            skipped.SkipNext = false;
            Emit($"    → {NameOf(skipped.Id)}는 저격당해서 차례를 건너뜁니다.");
            State.CurrentPlayer = State.SeatAfter(State.CurrentPlayer);
        }

        if (State.TurnCount >= Rules.TurnLimit)
        {
            // 턴 제한: 남은 사람은 손패가 적은 순서로 순위를 매깁니다.
            Emit("턴 제한을 넘어서 손패가 적은 순서로 순위를 정합니다.");
            foreach (var p in State.Players.Where(p => p.Active).OrderBy(p => p.Hand.Count).ToList())
            {
                Place(p.Id, "턴 제한 — 손패가 적은 순서", eliminated: false);
            }

            if (!State.IsFinished)
            {
                EndGame();
            }

            return;
        }

        var next = State.Players[State.CurrentPlayer];
        next.TurnsStarted++;

        // 어벤져스(차례 시작 판정): 완성한 채로 내 차례가 돌아오면 승리합니다.
        if (SpecialAugments.AvengersTurnStart && next.Has(SpecialAugmentId.Avengers) && State.AvengersComplete(next.Id))
        {
            Place(next.Id, $"어벤져스 — {SpecialAugments.AvengersJobCount}가지 직업을 모두 모았습니다", eliminated: false);
            if (!State.IsFinished)
            {
                EndTurn(1);
            }

            return;
        }

        // 평화주의자에게 넘어온 공격은 사라집니다.
        if (State.PendingPenalty > 0 && next.Has(SpecialAugmentId.Pacifist))
        {
            Emit($"    → [평화주의자] {NameOf(next.Id)}는 누적 +{State.PendingPenalty}의 영향을 받지 않습니다.");
            Raise(new GameEvent(GameEventType.Immune, -1, next.Id, Text: "평화주의자"));
            State.PendingPenalty = 0;
        }

        OfferAugmentIfDue(next.Id);
    }

    /// <summary>한 장을 뽑아서 손에 넣습니다. 덱이 비면 null입니다.</summary>
    private Card? DrawOne(int playerId)
    {
        var card = PopRaw();
        if (card != null)
        {
            State.Players[playerId].Hand.Add(card);
            Raise(new GameEvent(GameEventType.CardDrawn, playerId));
        }

        return card;
    }

    /// <summary>
    /// 카드를 count장 바로 뽑아 손에 넣습니다. (처음 나눠 줄 때 사용합니다)
    /// </summary>
    public List<Card> DrawCards(int playerId, int count)
    {
        var drawn = new List<Card>(count);
        for (int i = 0; i < count; i++)
        {
            var card = PopRaw();
            if (card == null)
            {
                break;
            }

            State.Players[playerId].Hand.Add(card);
            drawn.Add(card);
        }

        return drawn;
    }

    private Card? PopRaw()
    {
        if (State.DrawPile.Count == 0)
        {
            RecycleDiscardPile();
            if (State.DrawPile.Count == 0)
            {
                return null;
            }
        }

        var card = State.DrawPile[^1];
        State.DrawPile.RemoveAt(State.DrawPile.Count - 1);
        return card;
    }

    /// <summary>
    /// 맨 위 카드만 남기고 버린 더미를 섞어서 뽑을 더미로 옮깁니다.
    /// </summary>
    private void RecycleDiscardPile()
    {
        if (State.DiscardPile.Count <= 1)
        {
            return;
        }

        var top = State.DiscardPile[^1];
        State.DiscardPile.RemoveAt(State.DiscardPile.Count - 1);

        State.DrawPile.AddRange(State.DiscardPile);
        State.DiscardPile.Clear();
        State.DiscardPile.Add(top);

        Deck.Shuffle(State.DrawPile, Rng);
        Emit("  (덱을 다시 섞었습니다)");
    }
}

/// <summary>등급 이름 같은 공통 표시 헬퍼입니다.</summary>
public static class Augment
{
    public static string TierName(AugmentTier tier) => tier switch
    {
        AugmentTier.Silver => "실버",
        AugmentTier.Gold => "골드",
        _ => "프리즘",
    };
}
