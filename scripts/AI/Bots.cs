using System;
using System.Linq;
using SpCardgame.Core;

namespace SpCardgame.AI;

/// <summary>
/// 모든 봇이 구현하는 인터페이스입니다. 봇은 PlayerView만 보고 판단하므로, 다른 사람의 손패를 훔쳐볼 수 없습니다.
/// </summary>
public interface IBot
{
    string Name { get; }

    PlayerAction Decide(PlayerView view, Random rng);
}

/// <summary>
/// 봇들이 함께 쓰는 헬퍼입니다.
/// </summary>
public static class BotUtil
{
    /// <summary>
    /// 손패에서 가장 많은 색을 고릅니다. 와일드만 남았다면 무작위 색을 고릅니다.
    /// </summary>
    public static CardColor MostCommonColor(PlayerView view, int excludeCardId, Random rng)
    {
        var best = view.Hand
            .Where(c => c.Id != excludeCardId && c.Color != CardColor.Wild)
            .GroupBy(c => c.Color)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault(CardColor.Wild);

        return best != CardColor.Wild ? best : (CardColor)rng.Next(4);
    }

    /// <summary>
    /// 카드 효과에 맞는 대상을 고릅니다.
    /// 증강 강탈은 증강이 가장 많은 상대, 나머지는 손패가 가장 적은 상대를 노립니다.
    /// </summary>
    public static int PickTarget(PlayerView view, CardKind effect)
    {
        var opponents = Enumerable.Range(0, view.PlayerCount).Where(i => i != view.PlayerId && view.IsActive(i));

        return effect == CardKind.StealAugment
            ? opponents.OrderByDescending(i => view.Augments[i].Count).ThenBy(i => view.HandCounts[i]).First()
            : opponents.OrderByDescending(i => view.JobProgress[i] >= SpecialAugments.AllJobs.Count)
                .ThenBy(i => view.HandCounts[i]).First();
    }

    /// <summary>
    /// 이 카드를 냈을 때 실제로 발동할 효과입니다. 복사 카드는 직전 효과를 따라갑니다.
    /// </summary>
    public static CardKind EffectiveKind(Card card, PlayerView view) =>
        card.Kind == CardKind.Copy ? view.LastEffect : card.Kind;

    /// <summary>
    /// 선택한 카드에 필요한 색과 대상을 채워서 행동을 만듭니다.
    /// </summary>
    public static PlayerAction MakePlay(Card card, PlayerView view, Random rng, bool randomChoices)
    {
        var color = CardColor.Wild;
        if (view.ColorRequiredIds.Contains(card.Id))
        {
            color = randomChoices ? (CardColor)rng.Next(4) : MostCommonColor(view, card.Id, rng);
        }

        int target = -1;
        if (view.TargetRequiredIds.Contains(card.Id))
        {
            if (randomChoices)
            {
                var opponents = Enumerable.Range(0, view.PlayerCount).Where(i => i != view.PlayerId && view.IsActive(i)).ToList();
                target = opponents[rng.Next(opponents.Count)];
            }
            else
            {
                target = PickTarget(view, EffectiveKind(card, view));
            }
        }

        return PlayerAction.Play(card.Id, color, target);
    }

    /// <summary>
    /// 능력 선택 행동을 만듭니다. 대상은 손패가 가장 적은 상대, 문양은 상황에 맞게 고릅니다.
    /// </summary>
    public static PlayerAction MakeAbilityChoice(PlayerView view, int index, Random rng, bool randomChoices)
    {
        var ability = view.AbilityChoices[index];
        var opponents = Enumerable.Range(0, view.PlayerCount).Where(i => i != view.PlayerId && view.IsActive(i)).ToList();

        int target = -1;
        if (ability.NeedsTarget)
        {
            target = randomChoices ? opponents[rng.Next(opponents.Count)]
                : opponents.OrderByDescending(i => view.JobProgress[i] >= SpecialAugments.AllJobs.Count)
                .ThenBy(i => view.HandCounts[i]).First();
        }

        var suit = CardColor.Wild;
        if (ability.NeedsSuit)
        {
            if (randomChoices)
            {
                suit = (CardColor)rng.Next(4);
            }
            else if (ability.Id == AbilityId.Purge)
            {
                // 내 숫자 카드가 가장 많은 문양을 버립니다.
                suit = view.Hand.Where(c => c.Kind == CardKind.Number)
                    .GroupBy(c => c.Color).OrderByDescending(g => g.Count())
                    .Select(g => g.Key).FirstOrDefault(CardColor.Red);
            }
            else
            {
                // 저주의 낙인은 내가 가장 적게 가진 문양을 고릅니다. (덱에 많이 남아 있을 확률이 낮은 쪽이 오래 걸립니다.)
                suit = Deck.Colors.OrderBy(c => view.Hand.Count(h => h.Color == c)).First();
            }
        }

        return PlayerAction.ChooseAbility(index, target, suit);
    }

    /// <summary>
    /// 능력이 지금 나에게 얼마나 이득인지 대략 점수를 매깁니다.
    /// </summary>
    public static int AbilityScore(PlayerView view, AbilityInfo ability)
    {
        int me = view.Hand.Count;
        var opponents = Enumerable.Range(0, view.PlayerCount).Where(i => i != view.PlayerId && view.IsActive(i)).ToList();
        int fewest = opponents.Min(i => view.HandCounts[i]);
        int previous = view.HandCounts[(view.PlayerId - 1 + view.PlayerCount) % view.PlayerCount];
        double average = view.HandCounts.Average();

        return ability.Id switch
        {
            AbilityId.HandSwap => (me - fewest) * 10,
            AbilityId.GreatRotation => (me - previous) * 10,
            AbilityId.Equalize => (int)((me - average) * 10),
            AbilityId.Brand => 45,
            AbilityId.AbyssDraw => 40,
            AbilityId.Storm => 35,
            AbilityId.Purge => Math.Min(AbilityPool.PurgeLimit, me - 1) * 12,
            AbilityId.Gift => Math.Min(AbilityPool.GiftCount, me - 1) * 12,
            AbilityId.TimeStop => 20,
            AbilityId.MassSeal => fewest <= 2 ? 30 : 10,
            _ => 0,
        };
    }

    public static PlayerAction NoPlayableAction(PlayerView view) =>
        view.CanPass ? PlayerAction.Pass() : PlayerAction.Draw();

    /// <summary>
    /// 차례와 상관없이 먼저 처리해야 하는 일(억지 뽑기, 특수 증강 선택)을 처리합니다. 없으면 null입니다.
    /// </summary>
    public static PlayerAction? Mandatory(PlayerView view, Random rng, bool randomChoices)
    {
        if (view.MustDraw)
        {
            return PlayerAction.ForcedDraw();
        }

        // 도박사: 낼 수 있는 카드를 고르고, 둘 다 되면 무작위로 고릅니다.
        if (view.DrawChoices.Count > 0)
        {
            var playable = Enumerable.Range(0, view.DrawChoices.Count).Where(i => view.DrawChoicePlayable[i]).ToList();
            return PlayerAction.ChooseDraw(playable.Count > 0 ? playable[rng.Next(playable.Count)] : rng.Next(view.DrawChoices.Count));
        }

        if (view.AugmentChoices.Count > 0)
        {
            int index = randomChoices
                ? rng.Next(view.AugmentChoices.Count)
                : Enumerable.Range(0, view.AugmentChoices.Count)
                    .OrderByDescending(i => AugmentScore(view, view.AugmentChoices[i]))
                    .First();
            return PlayerAction.ChooseAugment(index);
        }

        return null;
    }

    /// <summary>특수 증강이 지금 나에게 얼마나 좋은지 대략 점수를 매깁니다.</summary>
    public static int AugmentScore(PlayerView view, AugmentInfo augment)
    {
        int me = view.Hand.Count;
        return augment.Name switch
        {
            "블랙홀" => me >= 10 ? 90 : 40,
            "어벤져스" => 42,
            "수집가" => 55,
            "평화주의자" => 50,
            "시간술사" => 50,
            "연금술사" => 45,
            "광전사" => 40,
            "흡혈귀" => 45,
            "반사의 거울" => 50,
            "불사조" => 45,
            "폭탄마" => 40,
            "도박사" => 45,
            "저격수" => 40,
            "쌍둥이" => 50,
            "시간 도둑" => 45,
            "행운의 7" => 40,
            "청소부" => 38,
            "수호천사" => 38,
            "역풍" => 36,
            "자유로운 영혼" => 34,
            "짝수 애호가" => 42,
            "선견지명" => 40,
            "지배자" => 48,
            "문양 군주" => 45,
            _ => 30,
        };
    }

    public static bool Has(PlayerView view, string augmentName) =>
        view.Augments[view.PlayerId].Any(a => a.Name == augmentName);

    /// <summary>
    /// 다른 승리 조건 증강에 맞춰 이 카드를 낼 때의 가산점입니다. 목표에 필요한 카드는 아끼고, 겹치는 카드는 먼저 냅니다.
    /// </summary>
    public static int AltWinBonus(Card card, PlayerView view)
    {
        if (view.Jobs.Count > 0)
        {
            var job = view.Jobs.FirstOrDefault(j => j.CardId == card.Id)?.Job;
            if (job == null)
            {
                // 직업이 없는 카드(액션·특수)는 어벤져스에 쓸모가 없으니 먼저 냅니다.
                return 30;
            }

            int same = view.Jobs.Count(j => j.Job == job);
            return same > 1 ? 40 : -60;
        }

        if (Has(view, "수집가") && card.Kind == CardKind.Number)
        {
            int suits = view.Hand.Where(c => c.Kind == CardKind.Number && c.Number == card.Number)
                .Select(c => c.Color).Distinct().Count();
            int copies = view.Hand.Count(c => c.Kind == CardKind.Number && c.Number == card.Number && c.Color == card.Color);
            return suits >= 2 && copies == 1 ? -30 * suits : 10;
        }

        return 0;
    }

    /// <summary>블랙홀 증강이 있으면 손패를 불리기 위해 일부러 뽑습니다.</summary>
    public static bool WantsToHoard(PlayerView view) =>
        Has(view, "블랙홀") && view.Hand.Count >= 9 && view.DrawPileCount > 5;
}

/// <summary>
/// 낼 수 있는 카드 중 아무거나 내는 봇입니다. 버그 찾기와 비교 기준용입니다.
/// </summary>
public sealed class RandomBot : IBot
{
    public string Name => "랜덤봇";

    public PlayerAction Decide(PlayerView view, Random rng)
    {
        if (BotUtil.Mandatory(view, rng, randomChoices: true) is { } mandatory)
        {
            return mandatory;
        }

        if (view.AbilityChoices.Count > 0)
        {
            int index = rng.Next(view.AbilityChoices.Count);
            return BotUtil.MakeAbilityChoice(view, index, rng, randomChoices: true);
        }

        if (view.PlayableCardIds.Count == 0)
        {
            return BotUtil.NoPlayableAction(view);
        }

        // 폭주 중이거나 뽑은 뒤에는 가끔 그냥 넘겨서 넘기기 경로도 테스트합니다.
        if (view.CanPass && rng.Next(4) == 0)
        {
            return PlayerAction.Pass();
        }

        int cardId = view.PlayableCardIds[rng.Next(view.PlayableCardIds.Count)];
        var card = view.Hand.First(c => c.Id == cardId);
        return BotUtil.MakePlay(card, view, rng, randomChoices: true);
    }
}

/// <summary>
/// 간단한 우선순위로 판단하는 봇입니다.
/// 1. 다음 사람의 손패가 2장 이하면 공격·방해 카드를 먼저 냅니다.
/// 2. 같은 색 숫자가 많으면 폭주를 씁니다.
/// 3. 평소에는 많이 가진 색의 카드를 내서 흐름을 유지합니다.
/// 4. 와일드는 다른 수가 없을 때 마지막에 씁니다.
/// </summary>
public sealed class RuleBasedBot : IBot
{
    public string Name => "규칙봇";

    /// <summary>켜면 특수 증강을 무작위로 고릅니다. 증강 자체의 강함을 재는 밸런스 테스트용입니다.</summary>
    public bool RandomAugments { get; init; }

    public PlayerAction Decide(PlayerView view, Random rng)
    {
        if (BotUtil.Mandatory(view, rng, randomChoices: RandomAugments) is { } mandatory)
        {
            return mandatory;
        }

        if (view.AbilityChoices.Count > 0)
        {
            int best = Enumerable.Range(0, view.AbilityChoices.Count)
                .OrderByDescending(i => BotUtil.AbilityScore(view, view.AbilityChoices[i]))
                .First();
            return BotUtil.MakeAbilityChoice(view, best, rng, randomChoices: false);
        }

        if (view.PlayableCardIds.Count == 0)
        {
            return BotUtil.NoPlayableAction(view);
        }

        // 블랙홀: 손패를 불리는 중이면 내지 않고 뽑습니다.
        if (BotUtil.WantsToHoard(view))
        {
            if (view.CanDraw)
            {
                return PlayerAction.Draw();
            }

            if (view.CanPass)
            {
                return PlayerAction.Pass();
            }
        }

        var playable = view.Hand.Where(c => view.PlayableCardIds.Contains(c.Id)).ToList();
        bool nextIsDanger = view.HandCounts[view.NextSeat] <= 2;

        var chosen = playable
            .OrderByDescending(c => Score(c, view, nextIsDanger) + BotUtil.AltWinBonus(c, view))
            .First();

        return BotUtil.MakePlay(chosen, view, rng, randomChoices: false);
    }

    /// <summary>
    /// 카드의 우선순위 점수를 계산합니다. 점수가 높을수록 먼저 냅니다.
    /// </summary>
    private static int Score(Card card, PlayerView view, bool nextIsDanger)
    {
        var effect = BotUtil.EffectiveKind(card, view);

        if (nextIsDanger)
        {
            int attack = effect switch
            {
                CardKind.WildDrawFour => 400,
                CardKind.DrawThree => 320,
                CardKind.DrawTwo => 300,
                CardKind.DrawOne => 280,
                CardKind.Seal => 250,
                CardKind.Skip => 200,
                CardKind.Reverse => 100,
                _ => 0,
            };

            if (attack > 0)
            {
                return 1000 + attack;
            }
        }

        int sameColorCount = view.Hand.Count(c => c.Color == card.Color && c.Id != card.Id);
        int sameColorNumbers = view.Hand.Count(c => c.Kind == CardKind.Number && c.Color == card.Color);

        switch (effect)
        {
            case CardKind.Frenzy when sameColorNumbers >= 2:
                return 500 + sameColorNumbers * 10;

            case CardKind.StealAugment:
                bool anyAugment = Enumerable.Range(0, view.PlayerCount)
                    .Any(i => i != view.PlayerId && view.Augments[i].Count > 0);
                return anyAugment ? 150 : -50;

            case CardKind.Purify:
                int mine = view.Augments[view.PlayerId].Count;
                int others = Enumerable.Range(0, view.PlayerCount)
                    .Where(i => i != view.PlayerId).Sum(i => view.Augments[i].Count);
                return others > mine ? 120 : -50;

            case CardKind.Awaken:
                return 90;

            case CardKind.WildDrawFour:
                return -200;

            case CardKind.Wild:
                return -100;
        }

        int actionBonus = card.IsAction ? 5 : 0;
        return sameColorCount * 10 + actionBonus + Math.Max(card.Number, 0);
    }
}
