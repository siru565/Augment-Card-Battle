namespace SpCardgame.Core;

/// <summary>
/// 카드 문양(색)입니다. 화면에는 불꽃·달빛·숲·물결로 표시합니다.
/// 내부 이름은 저장 호환을 위해 그대로 두고, 표시 이름만 ColorName에서 바꿉니다.
/// Wild는 아직 문양이 정해지지 않은 카드(프리즘, 증강 카드)를 뜻합니다.
/// </summary>
public enum CardColor
{
    Red,
    Yellow,
    Green,
    Blue,
    Wild,
}

/// <summary>
/// 카드 종류입니다.
/// </summary>
public enum CardKind
{
    // 기본 우노 카드입니다.
    Number,
    Skip,
    Reverse,
    DrawOne,
    DrawTwo,
    DrawThree,
    Wild,
    WildDrawFour,

    // 새 특수 카드입니다.
    Swap,
    Seal,
    Copy,
    Frenzy,
    StealAugment,
    Purify,

    // 내면 3가지 특수능력 중 하나를 골라 발동하는 각성 카드입니다.
    Awaken,

    // 뽑는 순간 발동하는 증강 카드입니다. 손패에 들어가지 않습니다.
    Augment,
}

/// <summary>
/// 카드 한 장의 불변 데이터입니다. Id는 한 판 안에서 고유하며, 네트워크로 카드를 지정할 때 사용합니다.
/// </summary>
public sealed record Card(int Id, CardColor Color, CardKind Kind, int Number = -1)
{
    public bool IsWild => Kind is CardKind.Wild or CardKind.WildDrawFour;

    public bool IsAction => Kind is not CardKind.Number;

    /// <summary>+카드(뽑게 하는 공격 카드)의 장수입니다. 공격 카드가 아니면 0입니다.</summary>
    public static int DrawAmount(CardKind kind) => kind switch
    {
        CardKind.DrawOne => 1,
        CardKind.DrawTwo => 2,
        CardKind.DrawThree => 3,
        CardKind.WildDrawFour => 4,
        _ => 0,
    };

    public static bool IsDrawAttack(CardKind kind) => DrawAmount(kind) > 0;

    public bool IsSpecial => Kind is CardKind.Swap or CardKind.Seal or CardKind.Copy
        or CardKind.Frenzy or CardKind.StealAugment or CardKind.Purify or CardKind.Awaken;

    public override string ToString() => Kind switch
    {
        CardKind.Number => $"{ColorName(Color)} {Number}",
        CardKind.Wild => "프리즘",
        CardKind.WildDrawFour => "프리즘 +4",
        CardKind.Augment => "증강 카드",
        _ => $"{ColorName(Color)} {KindName(Kind)}",
    };

    /// <summary>카드 버튼에 크게 표시할 짧은 이름입니다.</summary>
    public string ShortLabel => Kind == CardKind.Number ? Number.ToString() : KindName(Kind);

    public static string KindName(CardKind kind) => kind switch
    {
        CardKind.Number => "숫자",
        CardKind.Skip => "스킵",
        CardKind.Reverse => "리버스",
        CardKind.DrawOne => "+1",
        CardKind.DrawTwo => "+2",
        CardKind.DrawThree => "+3",
        CardKind.Wild => "프리즘",
        CardKind.WildDrawFour => "+4",
        CardKind.Swap => "교환",
        CardKind.Seal => "봉인",
        CardKind.Copy => "복사",
        CardKind.Frenzy => "폭주",
        CardKind.StealAugment => "강탈",
        CardKind.Purify => "정화",
        CardKind.Awaken => "각성",
        CardKind.Augment => "증강",
        _ => "?",
    };

    /// <summary>카드 효과 설명입니다. UI 툴팁에 사용합니다.</summary>
    public static string Describe(CardKind kind) => kind switch
    {
        CardKind.Number => "같은 문양이나 같은 숫자 위에 낼 수 있습니다.",
        CardKind.Skip => "다음 사람의 차례를 건너뜁니다.",
        CardKind.Reverse => "진행 방향을 바꿉니다.",
        CardKind.DrawOne or CardKind.DrawTwo or CardKind.DrawThree =>
            $"다음 사람에게 +{DrawAmount(kind)} 공격! 받은 사람은 문양에 상관없이 같은 +{DrawAmount(kind)}이나 프리즘 +4를 얹어 합산해서 넘길 수 있고, 못 넘기면 자기 차례에 모두 받습니다.",
        CardKind.Wild => "아무 때나 낼 수 있고, 원하는 문양으로 바꿉니다.",
        CardKind.WildDrawFour => "원하는 문양으로 바꾸고 다음 사람에게 +4 공격! 어떤 +카드 위에도 얹을 수 있고, 받은 사람은 +1·+2·+3·+4 무엇이든 얹어 넘길 수 있습니다.",
        CardKind.Swap => "지정한 상대와 무작위 카드를 1장씩 맞바꿉니다.",
        CardKind.Seal => "지정한 상대는 다음 차례에 숫자 카드만 낼 수 있습니다.",
        CardKind.Copy => "직전에 발동한 카드 효과를 한 번 더 발동합니다.",
        CardKind.Frenzy => "이번 턴에 같은 문양 숫자 카드를 원하는 만큼 연달아 냅니다. 다 내면 '폭주 끝내기'를 누릅니다.",
        CardKind.StealAugment => "지정한 상대의 증강 1개를 빼앗습니다.",
        CardKind.Purify => "모든 플레이어의 증강을 1개씩 무작위로 제거합니다.",
        CardKind.Awaken => "내면 특수능력 3가지가 나오고, 그중 하나를 골라 즉시 발동합니다.",
        CardKind.Augment => "뽑는 순간 공개되고 무작위 증강을 얻습니다. 대신 카드를 1장 더 뽑습니다.",
        _ => "",
    };

    public static string ColorName(CardColor color) => color switch
    {
        CardColor.Red => "불꽃",
        CardColor.Yellow => "달빛",
        CardColor.Green => "숲",
        CardColor.Blue => "물결",
        _ => "프리즘",
    };
}
