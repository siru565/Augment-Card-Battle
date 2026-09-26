using System;

namespace SpCardgame.Core;

/// <summary>
/// 룰 판정만 담당하는 정적 클래스입니다. 상태를 바꾸지 않습니다.
/// </summary>
public static class Rules
{
    public const int StartingHandSize = 7;

    /// <summary>무한 루프를 막기 위한 턴 제한입니다. 넘으면 무승부로 처리합니다.</summary>
    public const int TurnLimit = 3000;

    /// <summary>손패가 이 장수 이상이 되면 탈락합니다. (블랙홀 증강은 예외)</summary>
    public const int EliminationLimit = 30;

    /// <summary>
    /// 해당 카드를 현재 상태에서 낼 수 있는지 판정합니다.
    /// </summary>
    public static bool CanPlay(Card card, GameState state, int playerId)
    {
        var player = state.Players[playerId];

        if (card.Kind == CardKind.Augment)
        {
            return false;
        }

        // 연속 내기 중에는 그 문양의 숫자 카드만(쌍둥이는 같은 숫자만) 낼 수 있습니다.
        if (state.FrenzyActive)
        {
            return card.Kind == CardKind.Number &&
                   (state.FrenzyNumber >= 0 ? card.Number == state.FrenzyNumber : card.Color == state.FrenzyColor);
        }

        // +카드 공격이 쌓여 있으면 공격 카드로만 받아칠 수 있습니다.
        if (state.PendingPenalty > 0)
        {
            return !player.Sealed && CanStack(card, state);
        }

        // 봉인 중에는 숫자 카드만 낼 수 있습니다.
        if (player.Sealed && card.Kind != CardKind.Number)
        {
            return false;
        }

        if (card.IsWild)
        {
            return true;
        }

        // 행운의 7: 숫자 7은 언제든 낼 수 있습니다.
        if (card.Kind == CardKind.Number && card.Number == 7 && player.Has(SpecialAugmentId.LuckySeven))
        {
            return true;
        }

        // 문양 군주의 문양 숫자 카드는 언제든 낼 수 있습니다.
        if (card.Kind == CardKind.Number && card.Color == player.LordSuit && player.Has(SpecialAugmentId.SuitLord))
        {
            return true;
        }

        if (card.Color == state.CurrentColor)
        {
            return true;
        }

        var top = state.TopCard;

        if (card.Kind == CardKind.Number && top.Kind == CardKind.Number)
        {
            if (card.Number == top.Number)
            {
                return true;
            }

            // 연금술사는 숫자 1 차이도 이어서 낼 수 있습니다.
            // 짝수 애호가: 짝수 위에 짝수는 문양에 상관없이 낼 수 있습니다.
            if (player.Has(SpecialAugmentId.EvenLover) && card.Number % 2 == 0 && top.Number % 2 == 0)
            {
                return true;
            }

            return player.Has(SpecialAugmentId.Alchemist) && Math.Abs(card.Number - top.Number) == 1;
        }

        // 스킵 위에 스킵처럼, 같은 종류의 액션 카드는 문양이 달라도 낼 수 있습니다.
        return card.Kind != CardKind.Number && card.Kind == top.Kind;
    }

    /// <summary>
    /// 쌓인 공격 위에 더 얹을 수 있는지 확인합니다.
    /// 같은 + 숫자끼리는 문양에 상관없이 얹을 수 있고, 프리즘 +4는 어떤 조건이든 얹을 수 있습니다.
    /// </summary>
    public static bool CanStack(Card card, GameState state)
    {
        var kind = card.Kind == CardKind.Copy ? state.LastEffect : card.Kind;
        if (!Card.IsDrawAttack(kind))
        {
            return false;
        }

        // 프리즘 +4는 어떤 +카드 위에도 얹을 수 있고, +4 위에는 어떤 +카드든 얹을 수 있습니다.
        var topKind = state.TopCard.Kind == CardKind.Copy ? state.LastEffect : state.TopCard.Kind;
        if (kind == CardKind.WildDrawFour || topKind == CardKind.WildDrawFour)
        {
            return true;
        }

        // 나머지는 문양에 상관없이 같은 + 숫자끼리만 합산됩니다. (+1 위에 +1, +3 위에 +3)
        return kind == topKind;
    }

    public static bool IsAttack(CardKind kind) => kind == CardKind.Skip || Card.IsDrawAttack(kind);

    /// <summary>카드를 낼 때 문양을 골라야 하는지 확인합니다. (프리즘 카드)</summary>
    public static bool NeedsColor(Card card, GameState state, int playerId) => card.IsWild;

    /// <summary>카드를 낼 때 대상 플레이어를 골라야 하는지 확인합니다.</summary>
    public static bool NeedsTarget(Card card, GameState state, int playerId)
    {
        var kind = card.Kind == CardKind.Copy ? state.LastEffect : card.Kind;

        // 저격수는 스킵할 사람을 직접 고릅니다.
        if (kind == CardKind.Skip && state.Players[playerId].Has(SpecialAugmentId.Sniper))
        {
            return true;
        }

        return IsTargetedEffect(kind);
    }

    public static bool IsTargetedEffect(CardKind kind) =>
        kind is CardKind.Swap or CardKind.Seal or CardKind.StealAugment;
}
