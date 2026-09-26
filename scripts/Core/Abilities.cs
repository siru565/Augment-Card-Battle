using System;
using System.Collections.Generic;
using System.Linq;

namespace SpCardgame.Core;

/// <summary>
/// 각성 카드로 고를 수 있는 특수능력 목록입니다.
/// </summary>
public enum AbilityId
{
    // 기획 아이디어 4종입니다.
    AbyssDraw,
    GreatRotation,
    HandSwap,
    Brand,

    // 추가로 만든 능력입니다.
    Storm,
    Purge,
    TimeStop,
    MassSeal,
    Harvest,
    Equalize,
    Gift,
}

/// <summary>
/// 특수능력 한 가지의 표시 정보입니다. 대상(플레이어)과 문양을 골라야 하는지도 담습니다.
/// </summary>
public sealed record AbilityInfo(
    AbilityId Id,
    string Name,
    AugmentTier Tier,
    string Description,
    bool NeedsTarget,
    bool NeedsSuit);

/// <summary>
/// 특수능력 목록과 3지선다 뽑기를 담당합니다.
/// </summary>
public static class AbilityPool
{
    public const int ChoiceCount = 3;

    /// <summary>끝없는 드로우의 최대 장수입니다.</summary>
    public const int AbyssDrawLimit = 25;

    /// <summary>문양 숙청으로 한 번에 버릴 수 있는 최대 장수입니다.</summary>
    public const int PurgeLimit = 3;

    /// <summary>선물 상자로 떠넘기는 장수입니다.</summary>
    public const int GiftCount = 3;

    public static readonly IReadOnlyList<AbilityInfo> All = new[]
    {
        new AbilityInfo(AbilityId.AbyssDraw, "심연의 부름", AugmentTier.Gold,
            $"다음 차례 사람은 지금 문양의 카드가 나올 때까지 직접 계속 뽑아야 합니다. (최대 {AbyssDrawLimit}장)", false, false),
        new AbilityInfo(AbilityId.GreatRotation, "대회전", AugmentTier.Prism,
            "모든 플레이어의 손패를 통째로 시계 방향(다음 자리)으로 넘깁니다.", false, false),
        new AbilityInfo(AbilityId.HandSwap, "운명 교환", AugmentTier.Prism,
            "한 명을 골라 서로의 손패를 통째로 바꿉니다.", true, false),
        new AbilityInfo(AbilityId.Brand, "저주의 낙인", AugmentTier.Prism,
            "한 명과 문양 하나를 고릅니다. 그 사람은 그 문양의 카드가 나올 때까지 직접 끝없이 뽑아야 합니다.", true, true),
        new AbilityInfo(AbilityId.Storm, "카드 폭풍", AugmentTier.Silver,
            "나를 뺀 모두가 2장씩 뽑습니다.", false, false),
        new AbilityInfo(AbilityId.Purge, "문양 숙청", AugmentTier.Gold,
            $"문양 하나를 고릅니다. 내 손패에서 그 문양 숫자 카드를 최대 {PurgeLimit}장 버립니다. (마지막 1장은 남습니다)", false, true),
        new AbilityInfo(AbilityId.TimeStop, "시간 정지", AugmentTier.Gold,
            "모두의 차례를 건너뛰고 내 차례가 한 번 더 옵니다.", false, false),
        new AbilityInfo(AbilityId.MassSeal, "대봉인", AugmentTier.Silver,
            "모든 상대가 다음 차례에 숫자 카드만 낼 수 있습니다.", false, false),
        new AbilityInfo(AbilityId.Equalize, "균형의 저울", AugmentTier.Gold,
            "모든 플레이어의 손패를 모아 섞은 뒤 똑같이 나눠 가집니다.", false, false),
        new AbilityInfo(AbilityId.Gift, "선물 상자", AugmentTier.Silver,
            $"한 명을 골라 내 카드 무작위 {GiftCount}장을 떠넘깁니다. (마지막 1장은 남습니다)", true, false),
    };

    private static readonly Dictionary<AugmentTier, int> TierWeights = new()
    {
        [AugmentTier.Silver] = 45,
        [AugmentTier.Gold] = 35,
        [AugmentTier.Prism] = 20,
    };

    public static AbilityInfo Get(AbilityId id) => All.First(a => a.Id == id);

    /// <summary>
    /// 등급 가중치로 서로 다른 능력 3개를 뽑습니다.
    /// </summary>
    public static List<AbilityId> RollChoices(Random rng, int count = ChoiceCount)
    {
        var pool = All.ToList();
        var result = new List<AbilityId>(count);

        while (result.Count < count && pool.Count > 0)
        {
            int total = pool.Sum(a => TierWeights[a.Tier]);
            int roll = rng.Next(total);
            foreach (var ability in pool)
            {
                roll -= TierWeights[ability.Tier];
                if (roll < 0)
                {
                    result.Add(ability.Id);
                    pool.Remove(ability);
                    break;
                }
            }
        }

        return result;
    }
}
