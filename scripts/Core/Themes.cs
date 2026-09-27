using System;
using System.Collections.Generic;
using System.Linq;

namespace SpCardgame.Core;

/// <summary>
/// 판 테마입니다. 게임을 시작할 때 하나가 무작위로 정해지고, 그 판에만 특별한 승리 조건이 붙습니다.
/// (손패를 다 내는 기본 승리는 언제나 그대로입니다)
/// </summary>
public enum ThemeId
{
    /// <summary>테마 없음 (테마를 끈 방)</summary>
    None = -1,

    /// <summary>미니게임 대회: 몇 턴마다 모두가 같은 미니게임을 동시에 하고, 1등이 별을 받습니다. 별을 모으면 승리.</summary>
    Arcade = 0,

    /// <summary>야추: 손패로 족보(풀하우스, 스트레이트…)를 만들어 등록합니다. 족보를 모으면 승리.</summary>
    Yacht = 1,

    /// <summary>빙고: 각자 공개된 3×3 빙고판. 바닥에 나온 카드로 칸이 찍히고, 줄을 완성하면 승리.</summary>
    Bingo = 2,

    /// <summary>카드 레이스: 낸 카드만큼 말이 전진, 공격을 받으면 뒤로. 결승선에 먼저 도착하면 승리.</summary>
    Race = 3,

    /// <summary>영토 전쟁: 문양 깃발 4개. 그 문양을 가장 많이 낸 사람이 점령. 3개를 쥔 채로 차례가 오면 승리.</summary>
    Territory = 4,

    /// <summary>비밀 임무: 각자 남모르는 임무. 먼저 완수하면 승리.</summary>
    Mission = 5,

    /// <summary>보스 레이드: 모두 함께 보스를 때리고, 마지막 일격을 넣은 사람이 승리.</summary>
    Boss = 6,

    /// <summary>시한폭탄: 카드를 낼 때마다 폭탄이 넘어갑니다. 터질 때 손패가 가장 적은 사람이 메달, 메달을 모으면 승리.</summary>
    Bomb = 7,
}

public sealed record ThemeInfo(ThemeId Id, string Name, string Summary);

public static class Themes
{
    /// <summary>지금 무작위로 뽑히는 테마들입니다. (만든 테마만 들어갑니다)</summary>
    public static readonly ThemeId[] Pool =
    {
        ThemeId.Arcade, ThemeId.Yacht, ThemeId.Bingo, ThemeId.Race,
        ThemeId.Territory, ThemeId.Mission, ThemeId.Boss, ThemeId.Bomb,
    };

    public static ThemeInfo Info(ThemeId id) => id switch
    {
        ThemeId.Arcade => new ThemeInfo(id, "미니게임 대회",
            $"{ArcadeRules.Every}턴마다 모두가 같은 미니게임을 동시에 합니다. 1등은 별 1개! 별 {ArcadeRules.StarsToWin}개를 모으면 승리"),
        ThemeId.Yacht => new ThemeInfo(id, "야추",
            $"원카드 규칙이 바뀝니다! 차례마다 카드를 1장 더 뽑고, 손패를 다 내도 이기지 않고 새 카드 {YachtRules.RerollCards}장을 받습니다. 손패로 족보를 등록해서 {YachtRules.CategoriesToWin}종류를 먼저 채우면 승리"),
        ThemeId.Bingo => new ThemeInfo(id, "빙고",
            $"각자 숫자가 적힌 3×3 빙고판을 받습니다. 내가 낸 숫자 카드로 내 칸이 찍히고, 프리즘은 아무 칸이나 찍습니다. {BingoRules.LinesToWin}줄을 먼저 완성하면 승리"),
        ThemeId.Race => new ThemeInfo(id, "카드 레이스",
            $"숫자 카드는 그 숫자만큼, 다른 카드는 {RaceRules.ActionStep}칸 전진! 누적 공격을 받으면 받은 장수 × {RaceRules.PenaltyKnockback}칸 뒤로 밀립니다. {RaceRules.Finish}칸에 먼저 도착하면 승리"),
        ThemeId.Territory => new ThemeInfo(id, "영토 전쟁",
            $"문양 깃발 4개는 그 문양 카드를 가장 많이 낸 사람이 차지합니다. (최소 {TerritoryRules.MinPlays}장) 깃발 {TerritoryRules.FlagsToWin}개(2인은 {TerritoryRules.FlagsToWinTwoPlayers}개)를 쥔 채로 내 차례가 돌아오면 승리"),
        ThemeId.Mission => new ThemeInfo(id, "비밀 임무",
            "각자 남모르는 임무를 받습니다. 먼저 완수하면 승리! 절반을 넘기면 다른 사람에게도 임무와 진행도가 보입니다"),
        ThemeId.Boss => new ThemeInfo(id, "보스 레이드",
            $"모두 함께 보스를 공격합니다. 숫자 카드는 그 숫자만큼, +카드는 뽑게 하는 장수의 2배, 다른 카드는 {BossRules.ActionDamage}. 체력이 줄면 보스가 분노해서 모두 1장씩 뽑습니다. 마지막 일격을 넣은 사람이 승리"),
        ThemeId.Bomb => new ThemeInfo(id, "시한폭탄",
            $"카드가 나올 때마다 폭탄의 도화선이 타들어 갑니다. 다 탄 순간 카드를 낸 사람은 {BombRules.BlastCards}장을 받고, 그때 손패가 가장 적은 사람이 메달을 받습니다. 메달 {BombRules.MedalsToWin}개면 승리"),
        _ => new ThemeInfo(id, "", ""),
    };
}

// ───────────── 미니게임 대회 ─────────────

public enum ArcadeGame
{
    /// <summary>벽돌깨기: 패들로 공을 튕겨 벽돌을 깹니다. 점수 = 깬 벽돌 수</summary>
    Breakout,

    /// <summary>두더지 카드: 튀어나오는 카드를 재빨리 누릅니다. 폭탄 카드는 누르면 감점. 점수 = 맞힌 수</summary>
    Whack,

    /// <summary>미로 탈출: 방향키로 미로를 빠져나갑니다. 점수 = 빨리 나올수록 높음</summary>
    Maze,
}

/// <summary>
/// 지금 진행 중인 미니게임 대회 한 판입니다. 모두가 같은 게임, 같은 시드로 동시에 합니다.
/// Scores는 낸 사람만 들어 있습니다. (다 내면 결과가 나옵니다)
/// </summary>
public sealed record ArcadeRound(int Id, ArcadeGame Game, int Seed, int[] Players, Dictionary<int, int> Scores);

/// <summary>화면과 네트워크로 보내는 미니게임 대회 정보입니다. (점수는 결과가 나올 때 사건으로 따로 보냅니다)</summary>
public sealed record ArcadeInfo(int Id, ArcadeGame Game, int Seed, int[] Players, int[] Submitted);

public static class ArcadeRules
{
    /// <summary>이만큼의 턴(모든 사람의 차례 합)마다 대회가 열립니다.</summary>
    public static int Every { get; set; } = 12;

    /// <summary>별을 이만큼 모으면 승리합니다.</summary>
    public static int StarsToWin { get; set; } = 3;

    /// <summary>한 판 제한 시간(초)입니다.</summary>
    public const float TimeLimit = 20f;

    public static string Name(ArcadeGame game) => game switch
    {
        ArcadeGame.Breakout => "벽돌깨기",
        ArcadeGame.Whack => "두더지 카드",
        _ => "미로 탈출",
    };

    public static string HowTo(ArcadeGame game) => game switch
    {
        ArcadeGame.Breakout => "마우스로 패들을 움직여 공을 튕기세요. 벽돌을 많이 깰수록 점수가 높습니다. 공을 3번 놓치면 끝!",
        ArcadeGame.Whack => "튀어나오는 카드를 재빨리 누르세요. 빨간 폭탄 카드는 누르면 감점!",
        _ => "방향키(또는 WASD)로 움직여 금색 출구로 나가세요. 빨리 나올수록 점수가 높습니다.",
    };

    /// <summary>
    /// 봇의 점수입니다. 사람이 보통 받는 점수 범위에서 난이도(skill 0.5~1.2)에 맞춰 뽑습니다.
    /// </summary>
    public static int BotScore(ArcadeGame game, double skill, Random rng)
    {
        double noise = Gaussian(rng);
        double score = game switch
        {
            ArcadeGame.Breakout => 18 * skill + noise * 6,
            ArcadeGame.Whack => 16 * skill + noise * 4,
            _ => (skill > 0.6 || rng.NextDouble() < 0.7) ? 1000 + (8 * skill + noise * 3) * 10 : 300 + noise * 100,
        };
        return Math.Max(0, (int)Math.Round(score));
    }

    private static double Gaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    // ── 두더지 카드: 시드로 등장 순서를 정해서 모두가 같은 판을 합니다 ──

    public sealed record Mole(float Time, int Hole, bool Bomb);

    /// <summary>두더지 카드의 등장 순서입니다. (시각, 구멍 0~8, 폭탄 여부)</summary>
    public static List<Mole> Moles(int seed)
    {
        var rng = new Random(seed);
        var list = new List<Mole>();
        float t = 0.6f;
        while (t < TimeLimit - 0.5f)
        {
            list.Add(new Mole(t, rng.Next(9), rng.NextDouble() < 0.2));
            // 뒤로 갈수록 빨라집니다.
            t += 0.75f - 0.35f * (t / TimeLimit) + (float)rng.NextDouble() * 0.25f;
        }

        return list;
    }

    /// <summary>두더지 카드가 떠 있는 시간(초)입니다.</summary>
    public const float MoleLife = 0.85f;

    // ── 미로: 시드로 같은 미로를 만듭니다 (깊이 우선 탐색) ──

    public const int MazeSize = 11;

    /// <summary>미로 벽 정보입니다. walls[x, y]가 true면 벽입니다. (크기 2n+1 격자, 시작 (1,1), 출구 (2n-1, 2n-1))</summary>
    public static bool[,] Maze(int seed)
    {
        int n = MazeSize;
        int size = 2 * n + 1;
        var walls = new bool[size, size];
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                walls[x, y] = true;
            }
        }

        var rng = new Random(seed);
        var stack = new Stack<(int X, int Y)>();
        var visited = new bool[n, n];
        stack.Push((0, 0));
        visited[0, 0] = true;
        walls[1, 1] = false;
        var dirs = new (int X, int Y)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Peek();
            var options = dirs.Where(d => cx + d.X >= 0 && cx + d.X < n && cy + d.Y >= 0 && cy + d.Y < n && !visited[cx + d.X, cy + d.Y]).ToList();
            if (options.Count == 0)
            {
                stack.Pop();
                continue;
            }

            var (dx, dy) = options[rng.Next(options.Count)];
            int nx = cx + dx, ny = cy + dy;
            visited[nx, ny] = true;
            walls[2 * cx + 1 + dx, 2 * cy + 1 + dy] = false;
            walls[2 * nx + 1, 2 * ny + 1] = false;
            stack.Push((nx, ny));
        }

        return walls;
    }

    /// <summary>미로 점수: 나가면 1000 + 남은 시간(0.1초 단위), 못 나가면 출구에 얼마나 가까웠는지(0~500)입니다.</summary>
    public static int MazeScore(bool escaped, float timeLeft, float progress) =>
        escaped ? 1000 + (int)(timeLeft * 10) : (int)(Math.Clamp(progress, 0f, 1f) * 500);
}

// ───────────── 야추 ─────────────

public enum YachtCategory
{
    /// <summary>같은 숫자 3장</summary>
    Triple,

    /// <summary>같은 숫자 2장 + 같은 숫자 2장</summary>
    TwoPair,

    /// <summary>숫자 4장이 연속 (예: 3-4-5-6)</summary>
    Straight,

    /// <summary>같은 문양 숫자 4장</summary>
    Flush,

    /// <summary>같은 숫자 3장 + 같은 숫자 2장</summary>
    FullHouse,

    /// <summary>같은 숫자 4장 (야추!)</summary>
    Yacht,
}

public static class YachtRules
{
    /// <summary>족보를 이만큼 종류별로 등록하면 승리합니다.</summary>
    public static int CategoriesToWin { get; set; } = 3;

    /// <summary>야추 테마에서는 손패를 다 내도 이기지 않고, 이만큼 새로 받습니다. (주사위를 다시 굴리듯)</summary>
    public static int RerollCards { get; set; } = 5;

    /// <summary>야추 테마: 내 차례가 시작될 때 주사위를 굴리듯 카드를 이만큼 더 뽑습니다.</summary>
    public static int DrawPerTurn { get; set; } = 1;

    public static readonly YachtCategory[] All =
    {
        YachtCategory.Triple, YachtCategory.TwoPair, YachtCategory.Straight,
        YachtCategory.Flush, YachtCategory.FullHouse, YachtCategory.Yacht,
    };

    public static string Name(YachtCategory c) => c switch
    {
        YachtCategory.Triple => "트리플",
        YachtCategory.TwoPair => "투 페어",
        YachtCategory.Straight => "스트레이트",
        YachtCategory.Flush => "플러시",
        YachtCategory.FullHouse => "풀하우스",
        _ => "야추",
    };

    public static string Describe(YachtCategory c) => c switch
    {
        YachtCategory.Triple => "같은 숫자 3장",
        YachtCategory.TwoPair => "같은 숫자 2장 + 같은 숫자 2장",
        YachtCategory.Straight => "연속된 숫자 4장 (예: 3·4·5·6)",
        YachtCategory.Flush => "같은 문양 숫자 카드 4장",
        YachtCategory.FullHouse => "같은 숫자 3장 + 같은 숫자 2장",
        _ => "같은 숫자 4장",
    };

    /// <summary>
    /// 손패에서 족보에 쓸 카드를 찾습니다. 없으면 null입니다.
    /// 숫자 카드만 씁니다. 여러 조합이 되면 작은 숫자부터 씁니다.
    /// </summary>
    public static List<Card>? Find(IReadOnlyList<Card> hand, YachtCategory category)
    {
        var numbers = hand.Where(c => c.Kind == CardKind.Number).ToList();
        var byNumber = numbers.GroupBy(c => c.Number).OrderBy(g => g.Key).ToList();

        List<Card>? Same(int count, int exceptNumber = -1) =>
            byNumber.Where(g => g.Key != exceptNumber && g.Count() >= count).Select(g => g.Take(count).ToList()).FirstOrDefault();

        switch (category)
        {
            case YachtCategory.Triple:
                return Same(3);

            case YachtCategory.Yacht:
                return Same(4);

            case YachtCategory.TwoPair:
            {
                var first = Same(2);
                if (first == null)
                {
                    return null;
                }

                var second = Same(2, first[0].Number);
                return second == null ? null : first.Concat(second).ToList();
            }

            case YachtCategory.FullHouse:
            {
                foreach (var three in byNumber.Where(g => g.Count() >= 3))
                {
                    var two = Same(2, three.Key);
                    if (two != null)
                    {
                        return three.Take(3).Concat(two).ToList();
                    }
                }

                return null;
            }

            case YachtCategory.Straight:
            {
                var present = byNumber.Select(g => g.Key).ToHashSet();
                for (int start = 0; start <= 6; start++)
                {
                    if (Enumerable.Range(start, 4).All(present.Contains))
                    {
                        return Enumerable.Range(start, 4).Select(n => byNumber.First(g => g.Key == n).First()).ToList();
                    }
                }

                return null;
            }

            default:
            {
                var suit = numbers.GroupBy(c => c.Color).FirstOrDefault(g => g.Count() >= 4);
                return suit?.OrderBy(c => c.Number).Take(4).ToList();
            }
        }
    }
}


// ───────────── 빙고 ─────────────

/// <summary>빙고 칸입니다. Suit가 Wild가 아니면 그 문양 카드, Number가 0 이상이면 그 숫자 카드로 찍힙니다. 가운데는 무료 칸입니다.</summary>
public sealed record BingoCell(CardColor Suit, int Number, bool Marked)
{
    public bool Free => Suit == CardColor.Wild && Number < 0;

    public bool Matches(Card card) =>
        !Free && ((Suit != CardColor.Wild && card.Color == Suit) || (Number >= 0 && card.Kind == CardKind.Number && card.Number == Number));

    public string Label => Free ? "FREE" : Suit != CardColor.Wild ? Card.ColorName(Suit) : Number.ToString();
}

public static class BingoRules
{
    public static int LinesToWin { get; set; } = 3;

    /// <summary>true면 누가 내든 모두의 판이 찍히고, false면 내가 낸 카드로 내 판만 찍힙니다.</summary>
    public static bool Shared { get; set; } = false;

    /// <summary>문양 칸 수입니다. (나머지는 숫자 칸) 문양 칸이 많을수록 쉽게 찍힙니다.</summary>
    public static int SuitCells { get; set; } = 0;

    private static readonly int[][] Lines =
    {
        new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 },
        new[] { 0, 3, 6 }, new[] { 1, 4, 7 }, new[] { 2, 5, 8 },
        new[] { 0, 4, 8 }, new[] { 2, 4, 6 },
    };

    public static BingoCell[] MakeBoard(Random rng)
    {
        var cells = new List<BingoCell>();
        var suits = Deck.Colors.OrderBy(_ => rng.Next()).Take(SuitCells).ToList();
        cells.AddRange(suits.Select(s => new BingoCell(s, -1, false)));
        var numbers = Enumerable.Range(0, 10).OrderBy(_ => rng.Next()).Take(8 - SuitCells).ToList();
        cells.AddRange(numbers.Select(n => new BingoCell(CardColor.Wild, n, false)));
        cells = cells.OrderBy(_ => rng.Next()).ToList();
        cells.Insert(4, new BingoCell(CardColor.Wild, -1, true));
        return cells.ToArray();
    }

    public static int LinesDone(IReadOnlyList<BingoCell> board) => Lines.Count(line => line.All(i => board[i].Marked));
}

// ───────────── 카드 레이스 ─────────────

public static class RaceRules
{
    public static int Finish { get; set; } = 50;

    /// <summary>숫자가 아닌 카드를 냈을 때 전진 칸 수입니다.</summary>
    public static int ActionStep { get; set; } = 2;

    /// <summary>쌓인 공격을 받을 때 받은 장수 1장당 뒤로 밀리는 칸 수입니다.</summary>
    public static int PenaltyKnockback { get; set; } = 2;

    public static int Step(Card card) => card.Kind == CardKind.Number ? Math.Max(1, card.Number) : ActionStep;
}

// ───────────── 영토 전쟁 ─────────────

public static class TerritoryRules
{
    public static int FlagsToWin { get; set; } = 2;

    /// <summary>2인 게임에서 필요한 깃발 수입니다.</summary>
    public static int FlagsToWinTwoPlayers { get; set; } = 3;

    /// <summary>4인 게임에서 필요한 깃발 수입니다.</summary>
    public static int FlagsToWinFourPlayers { get; set; } = 2;

    /// <summary>깃발을 차지하려면 그 문양 카드를 적어도 이만큼 내야 합니다.</summary>
    public static int MinPlays { get; set; } = 3;

    public static int FlagsFor(int players) => players <= 2 ? FlagsToWinTwoPlayers : players >= 4 ? FlagsToWinFourPlayers : FlagsToWin;
}

// ───────────── 비밀 임무 ─────────────

public enum MissionKind
{
    /// <summary>정해진 문양 카드 N장 내기</summary>
    SuitCards,

    /// <summary>+카드로 N번 공격하기</summary>
    Attacks,

    /// <summary>액션 카드(스킵·리버스·교환·봉인·복사·폭주·각성) N장 내기</summary>
    Actions,

    /// <summary>짝수 숫자 카드 N장 내기</summary>
    EvenNumbers,

    /// <summary>프리즘 카드 N장 내기</summary>
    Prisms,

    /// <summary>숫자 카드를 1씩 커지게 N번 이어서 내기 (내 카드끼리, 사이에 다른 카드를 내면 끊김)</summary>
    Ladder,
}

/// <summary>비밀 임무입니다. Suit는 SuitCards일 때만 씁니다.</summary>
public sealed record Mission(MissionKind Kind, int Target, CardColor Suit = CardColor.Wild)
{
    public string Describe() => Kind switch
    {
        MissionKind.SuitCards => $"{Card.ColorName(Suit)} 카드 {Target}장 내기",
        MissionKind.Attacks => $"+카드로 {Target}번 공격하기",
        MissionKind.Actions => $"액션 카드(스킵·리버스·교환·봉인·복사·폭주·각성) {Target}장 내기",
        MissionKind.EvenNumbers => $"짝수 숫자 카드 {Target}장 내기",
        MissionKind.Prisms => $"프리즘 카드 {Target}장 내기",
        _ => $"숫자 카드를 1씩 커지게 {Target}번 이어서 내기",
    };
}

public static class MissionRules
{
    /// <summary>임무 목표치입니다. (종류 순서: 문양, 공격, 액션, 짝수, 프리즘, 연속 숫자)</summary>
    public static int[] Targets { get; set; } = { 7, 3, 5, 6, 2, 3 };

    public static Mission Roll(Random rng)
    {
        int kind = rng.Next(6);
        return new Mission((MissionKind)kind, Targets[kind], kind == 0 ? Deck.Colors[rng.Next(4)] : CardColor.Wild);
    }

    /// <summary>이 카드가 임무 진행에 도움이 되는지 봅니다. 연속 임무는 마지막 숫자(last)를 기준으로 봅니다.</summary>
    public static bool Counts(Mission mission, Card card, int last) => mission.Kind switch
    {
        MissionKind.SuitCards => card.Color == mission.Suit,
        MissionKind.Attacks => Card.IsDrawAttack(card.Kind),
        MissionKind.Actions => card.IsAction && !card.IsWild && !Card.IsDrawAttack(card.Kind),
        MissionKind.EvenNumbers => card.Kind == CardKind.Number && card.Number % 2 == 0,
        MissionKind.Prisms => card.IsWild,
        _ => card.Kind == CardKind.Number && (last < 0 || card.Number == last + 1),
    };
}

// ───────────── 보스 레이드 ─────────────

public static class BossRules
{
    /// <summary>보스 체력 = 기본 + 사람 수 × 1인당 체력입니다. (여럿이 때릴수록 튼튼해집니다)</summary>
    public static int BaseHp { get; set; } = 60;

    public static int HpPerPlayer { get; set; } = 45;

    public static int MaxHpFor(int players) => BaseHp + HpPerPlayer * players;

    public static int ActionDamage { get; set; } = 3;

    public static int Damage(Card card) =>
        card.Kind == CardKind.Number ? card.Number
        : Card.IsDrawAttack(card.Kind) ? Card.DrawAmount(card.Kind) * 2
        : ActionDamage;
}

// ───────────── 시한폭탄 ─────────────

public static class BombRules
{
    public static int MedalsToWin { get; set; } = 3;

    /// <summary>폭탄이 터지면 들고 있던 사람이 받는 장수입니다.</summary>
    public static int BlastCards { get; set; } = 5;

    /// <summary>도화선 길이(카드 몇 장이 나오면 터지는지)입니다. 이 범위에서 무작위이고, 아무도 모릅니다.</summary>
    public static int FuseMin { get; set; } = 6;

    public static int FuseMax { get; set; } = 16;

    /// <summary>도화선이 이만큼 이하로 남으면 "곧 터짐" 경고를 보여 줍니다.</summary>
    public const int DangerFuse = 3;
}
