using System;
using System.Collections.Generic;
using System.Linq;

namespace SpCardgame.Core;

public enum AugmentTier
{
    Silver,
    Gold,
    Prism,
}

/// <summary>
/// 특수 증강 목록입니다. 시작할 때와 그 뒤 5턴마다 3개 중 하나를 골라 얻고, 게임 규칙 자체를 바꿉니다.
/// </summary>
public enum SpecialAugmentId
{
    Avengers,
    BlackHole,
    Collector,
    Alchemist,
    Pacifist,
    Berserker,
    SuitLord,
    Chronomancer,
    Vampire,
    Mirror,
    Phoenix,
    Bomber,
    Gambler,
    Sniper,
    Twins,
    TimeThief,
    LuckySeven,
    Janitor,
    Guardian,
    Headwind,
    FreeSpirit,
    EvenLover,
    Foresight,
    Dominator,
    Curling,
    Jackpot,
    Domino,
    Oracle,
    Marksman,
}

/// <summary>직업 카드입니다. 어벤져스 증강을 가진 플레이어의 카드에만 붙습니다.</summary>
public enum Job
{
    Warrior,
    Archer,
    Pirate,
    Thief,
    Mage,
    Fisher,
    Angler,
    Farmer,
    Knight,
    Priest,
}

/// <summary>뷰와 UI에 전달하는 증강 요약입니다.</summary>
public sealed record AugmentInfo(string Name, AugmentTier Tier, string Description, int Cooldown = 0, bool AltWin = false);

/// <summary>
/// 특수 증강의 정의와 뽑기 규칙을 담습니다.
/// </summary>
public static class SpecialAugments
{
    /// <summary>한 사람이 가질 수 있는 특수 증강 개수입니다.</summary>
    public const int MaxPerPlayer = 2;

    /// <summary>자기 차례가 이 횟수만큼 돌아올 때마다 특수 증강을 고릅니다.</summary>
    public const int OfferEveryTurns = 5;

    public const int ChoiceCount = 3;

    /// <summary>블랙홀 증강의 승리 장수입니다.</summary>
    public const int BlackHoleTarget = 28;

    /// <summary>불사조 증강이 한 번에 받는 최대 장수입니다.</summary>
    public const int PhoenixCap = 3;

    /// <summary>어벤져스에서 모아야 하는 직업 수입니다.</summary>
    public static int AvengersJobCount { get; set; } = 8;

    /// <summary>숫자 카드에만 직업이 붙는지 정합니다. (액션·특수·프리즘 카드는 직업이 없습니다)</summary>
    public static bool AvengersNumberOnly { get; set; } = true;

    /// <summary>켜면 완성한 순간이 아니라, 완성한 채로 내 차례가 다시 돌아와야 승리합니다.</summary>
    public static bool AvengersTurnStart { get; set; } = true;

    /// <summary>켜면 손패가 직업 수와 정확히 같아야(겹치는 직업 없이) 승리합니다.</summary>
    public static bool AvengersExact { get; set; }

    public static IReadOnlyList<Job> AllJobs => ((Job[])Enum.GetValues(typeof(Job))).Take(AvengersJobCount).ToList();

    /// <summary>이 카드에 직업이 붙는지 확인합니다.</summary>
    public static bool HasJob(Card card) => !AvengersNumberOnly || card.Kind == CardKind.Number;

    private static readonly Dictionary<SpecialAugmentId, (string Name, AugmentTier Tier, string Description)> Table = new()
    {
        [SpecialAugmentId.Avengers] = ("어벤져스", AugmentTier.Prism,
            "내 숫자 카드가 직업 카드(전사·궁수·해적·도적·마법사·어부·낚시꾼·농부)가 됩니다. 액션·특수·프리즘 카드는 직업이 없습니다. " +
            "8가지 직업을 모두 모은 채로 내 차례가 다시 돌아오면 승리! 모으는 순간 모두에게 들키니 한 바퀴를 버텨야 합니다. (손패를 다 내도 승리합니다)"),
        [SpecialAugmentId.BlackHole] = ("블랙홀", AugmentTier.Prism,
            $"손패가 {BlackHoleTarget}장이 되는 순간 승리! 카드를 뽑는 게 오히려 이득이 됩니다. (손패를 다 내도 승리합니다)"),
        [SpecialAugmentId.Collector] = ("수집가", AugmentTier.Gold,
            "같은 숫자를 네 문양(불꽃·달빛·숲·물결) 모두 손에 모으면 승리! 프리즘 카드 1장은 빠진 문양 하나를 대신합니다. (손패를 다 내도 승리합니다)"),
        [SpecialAugmentId.Alchemist] = ("연금술사", AugmentTier.Silver,
            "내 숫자 카드는 바닥 숫자와 1 차이만 나도 문양에 상관없이 낼 수 있습니다."),
        [SpecialAugmentId.Pacifist] = ("평화주의자", AugmentTier.Gold,
            "+카드 공격의 영향을 받지 않습니다. 대신 내가 내는 +카드는 뽑게 하는 효과가 사라집니다."),
        [SpecialAugmentId.Berserker] = ("광전사", AugmentTier.Gold,
            "내가 얹는 +카드는 두 배로 쌓입니다. 대신 내가 받는 공격도 두 배입니다."),
        [SpecialAugmentId.SuitLord] = ("문양 군주", AugmentTier.Silver,
            "고르는 순간 내 손에 가장 많은 문양이 '내 문양'이 됩니다. 내 문양의 숫자 카드는 바닥과 상관없이 언제든 낼 수 있습니다."),
        [SpecialAugmentId.Chronomancer] = ("시간술사", AugmentTier.Gold,
            "숫자 카드를 낸 뒤, 같은 문양 숫자 카드를 한 장 더 이어서 낼 수 있습니다."),
        [SpecialAugmentId.Vampire] = ("흡혈귀", AugmentTier.Silver,
            "내가 +카드를 얹을 때마다, 내 카드 1장(무작위)을 다음 사람에게 떠넘깁니다."),
        [SpecialAugmentId.Mirror] = ("반사의 거울", AugmentTier.Prism,
            "쌓인 공격을 받을 때, 절반(내림)을 마지막으로 얹은 사람에게 되돌려 보냅니다."),
        [SpecialAugmentId.Phoenix] = ("불사조", AugmentTier.Prism,
            $"쌓인 공격을 받을 때 아무리 많아도 최대 {PhoenixCap}장만 받습니다."),
        [SpecialAugmentId.Bomber] = ("폭탄마", AugmentTier.Silver,
            "내가 얹는 +카드는 1장 더 쌓입니다. (+1이 +2처럼, +4가 +5처럼)"),
        [SpecialAugmentId.Gambler] = ("도박사", AugmentTier.Silver,
            "카드를 뽑을 때 2장을 보고, 낼 수 있는 카드를 골라 가집니다. 나머지는 덱 맨 아래로 갑니다."),
        [SpecialAugmentId.Sniper] = ("저격수", AugmentTier.Gold,
            "스킵을 낼 때 건너뛸 사람을 직접 고르고, 저격당한 사람은 카드 2장을 뽑습니다. 멀리 있는 사람도 저격할 수 있습니다."),
        [SpecialAugmentId.Twins] = ("쌍둥이", AugmentTier.Gold,
            "숫자 카드를 낸 뒤, 같은 숫자 카드를 문양에 상관없이 원하는 만큼 이어서 낼 수 있습니다."),
        [SpecialAugmentId.TimeThief] = ("시간 도둑", AugmentTier.Gold,
            "리버스를 내면 방향이 바뀌고, 내 차례가 한 번 더 옵니다."),
        [SpecialAugmentId.LuckySeven] = ("행운의 7", AugmentTier.Silver,
            "내 숫자 7 카드는 바닥에 상관없이 언제든 낼 수 있습니다."),
        [SpecialAugmentId.Janitor] = ("청소부", AugmentTier.Silver,
            "숫자 0을 내면 내 카드 1장(무작위)을 함께 버립니다."),
        [SpecialAugmentId.Guardian] = ("수호천사", AugmentTier.Silver,
            "손패가 2장 이하일 때 쌓인 공격을 받으면 절반(올림)만 받습니다."),
        [SpecialAugmentId.Headwind] = ("역풍", AugmentTier.Silver,
            "리버스를 내면, 바뀐 방향의 다음 사람이 카드 1장을 뽑습니다."),
        [SpecialAugmentId.FreeSpirit] = ("자유로운 영혼", AugmentTier.Silver,
            "봉인과 저격(멀리서 건너뛰기)에 걸리지 않습니다."),
        [SpecialAugmentId.EvenLover] = ("짝수 애호가", AugmentTier.Silver,
            "바닥이 짝수 숫자면, 내 짝수 숫자 카드는 문양에 상관없이 낼 수 있습니다."),
        [SpecialAugmentId.Foresight] = ("선견지명", AugmentTier.Gold,
            "각성 카드를 내면 능력 선택지가 3개가 아니라 4개 나옵니다."),
        [SpecialAugmentId.Dominator] = ("지배자", AugmentTier.Prism,
            "각성 카드를 내면 능력을 하나 고른 뒤, 새 선택지에서 하나를 더 골라 연달아 발동합니다."),
        [SpecialAugmentId.Curling] = ("컬링", AugmentTier.Prism,
            $"카드를 {StreakRules.CurlingCharge}장 낼 때마다 내 차례에 카드 스톤을 한 번 튕길 수 있습니다. 하우스 정중앙(버튼)에 멈추면 승리! " +
            "하우스 안에만 들어가도 손패 1장을 버립니다. 얼음은 매번 다르게 휘니 잘 보고 던지세요. (손패를 다 내도 승리합니다)"),
        [SpecialAugmentId.Jackpot] = ("잭팟", AugmentTier.Gold,
            "내 차례가 올 때마다 슬롯머신이 돌아갑니다. ★★★이 나오면 승리! 같은 문양 3개가 나오면 그 문양 카드 1장을 버립니다. (손패를 다 내도 승리합니다)"),
        [SpecialAugmentId.Domino] = ("도미노", AugmentTier.Gold,
            $"내가 낸 숫자 카드가 1씩 차이 나게 {StreakRules.DominoTarget}번 연속으로 이어지면 승리! 올라가도 내려가도 됩니다. (예: 3 → 4 → 5 → 4, 문양 상관없음) " +
            "숫자가 아닌 카드를 내거나 숫자가 끊기면 처음부터입니다. (손패를 다 내도 승리합니다)"),
        [SpecialAugmentId.Oracle] = ("예언자", AugmentTier.Gold,
            $"내 차례마다 다음 내 차례가 올 때의 바닥 문양을 예언합니다. {StreakRules.OracleTarget}번 연속으로 맞히면 승리! (손패를 다 내도 승리합니다)"),
        [SpecialAugmentId.Marksman] = ("정밀 사수", AugmentTier.Prism,
            $"내 차례마다 엄청 빠르게 왕복하는 바늘을 황금 구간에서 멈춥니다. {MarksmanRules.Target}번 연속 성공하면 승리! " +
            "명중할 때마다 손패 1장을 버리고, 성공할수록 더 빨라집니다. 한 번이라도 놓치면 처음부터입니다. (손패를 다 내도 승리합니다)"),
    };

    private static readonly Dictionary<AugmentTier, int> TierWeights = new()
    {
        [AugmentTier.Silver] = 40,
        [AugmentTier.Gold] = 40,
        [AugmentTier.Prism] = 20,
    };

    public static IReadOnlyList<SpecialAugmentId> All => Table.Keys.ToList();

    public static AugmentInfo Info(SpecialAugmentId id)
    {
        var (name, tier, description) = Table[id];
        return new AugmentInfo(name, tier, description, 0, IsAltWin(id));
    }

    public static string NameOf(SpecialAugmentId id) => Table[id].Name;

    public static AugmentTier TierOf(SpecialAugmentId id) => Table[id].Tier;

    /// <summary>직업마다 다른 색입니다. (카드 위 직업 표시용)</summary>
    public static string JobColorHex(Job job) => job switch
    {
        Job.Warrior => "#ff7a5c",
        Job.Archer => "#8fe36b",
        Job.Pirate => "#5cc8ff",
        Job.Thief => "#b58cff",
        Job.Mage => "#ff8fe0",
        Job.Fisher => "#4fe0c8",
        Job.Angler => "#ffd35c",
        Job.Farmer => "#c9a36b",
        Job.Knight => "#d7dcea",
        Job.Priest => "#fff3a8",
        _ => "#ffffff",
    };

    public static string JobName(Job job) => job switch
    {
        Job.Warrior => "전사",
        Job.Archer => "궁수",
        Job.Pirate => "해적",
        Job.Thief => "도적",
        Job.Mage => "마법사",
        Job.Fisher => "어부",
        Job.Angler => "낚시꾼",
        Job.Farmer => "농부",
        Job.Knight => "기사",
        Job.Priest => "사제",
        _ => "?",
    };

    /// <summary>
    /// 카드 한 장의 직업입니다. 판마다 다른 소금값(salt)을 섞어서 매 판 배정이 달라집니다.
    /// </summary>
    public static Job JobOf(int cardId, int salt) =>
        AllJobs[(int)((uint)(cardId * 2654435761u + (uint)salt) % (uint)AllJobs.Count)];

    /// <summary>
    /// 게임 중에 특수 증강을 고르는 차례인지 확인합니다. 첫 증강은 게임 시작 때 모두 동시에 고르고,
    /// 그 뒤로 5턴마다 1개씩 고릅니다. (6, 11번째 차례…)
    /// </summary>
    public static bool IsOfferTurn(int turnsStarted) => turnsStarted > 1 && (turnsStarted - 1) % OfferEveryTurns == 0;

    /// <summary>다음 증강까지 남은 내 차례 수입니다. (지금 차례가 1번째라면 3)</summary>
    public static int TurnsUntilOffer(int turnsStarted) => OfferEveryTurns - (turnsStarted - 1) % OfferEveryTurns;

    /// <summary>승리 조건을 바꾸는 증강인지 확인합니다. 한 사람은 이런 증강을 하나만 가질 수 있습니다.</summary>
    public static bool IsAltWin(SpecialAugmentId id) =>
        id is SpecialAugmentId.Avengers or SpecialAugmentId.BlackHole or SpecialAugmentId.Collector
            or SpecialAugmentId.Curling or SpecialAugmentId.Jackpot or SpecialAugmentId.Domino
            or SpecialAugmentId.Oracle or SpecialAugmentId.Marksman;

    /// <summary>
    /// 이미 가진 증강과 겹치거나 충돌하지 않는 후보 중에서 등급 가중치로 3개를 뽑습니다.
    /// </summary>
    public static List<SpecialAugmentId> RollChoices(IReadOnlyCollection<SpecialAugmentId> owned, Random rng)
    {
        var pool = Table.Keys.Where(id => !owned.Contains(id) && !Conflicts(id, owned)).ToList();
        var result = new List<SpecialAugmentId>(ChoiceCount);
        FillWeighted(pool, result, rng);
        return result;
    }

    private static void FillWeighted(List<SpecialAugmentId> pool, List<SpecialAugmentId> result, Random rng)
    {
        while (result.Count < ChoiceCount && pool.Count > 0)
        {
            int total = pool.Sum(id => TierWeights[TierOf(id)]);
            int roll = rng.Next(total);
            foreach (var id in pool)
            {
                roll -= TierWeights[TierOf(id)];
                if (roll < 0)
                {
                    result.Add(id);
                    pool.Remove(id);
                    break;
                }
            }
        }
    }

    private static bool Conflicts(SpecialAugmentId id, IReadOnlyCollection<SpecialAugmentId> owned)
    {
        if (IsAltWin(id) && owned.Any(IsAltWin))
        {
            return true;
        }

        // 평화주의자는 공격을 안 받고 못 거니, 공격 관련 증강과 함께 나오지 않게 합니다.
        var attackRelated = new[]
        {
            SpecialAugmentId.Berserker, SpecialAugmentId.Mirror, SpecialAugmentId.Phoenix,
            SpecialAugmentId.Bomber, SpecialAugmentId.Vampire,
        };
        return (id == SpecialAugmentId.Pacifist && owned.Any(attackRelated.Contains))
            || (attackRelated.Contains(id) && owned.Contains(SpecialAugmentId.Pacifist));
    }
}
