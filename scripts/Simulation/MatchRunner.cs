using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SpCardgame.AI;
using SpCardgame.Core;

namespace SpCardgame.Simulation;

public sealed record GameResult(int Winner, int Turns, string WinnerBot, string WinReason = "");

/// <summary>
/// 봇끼리 게임을 자동으로 진행합니다. 화면이 없어도 돌아가므로 밸런스 테스트에 사용합니다.
/// </summary>
public static class MatchRunner
{
    /// <summary>
    /// 한 판을 끝까지 진행합니다. 봇이 규칙에 어긋난 행동을 하면 예외를 던져서 버그를 바로 드러냅니다.
    /// </summary>
    public static GameResult RunGame(IReadOnlyList<IBot> bots, int seed, Action<string>? log = null, GameOptions? options = null)
    {
        var engine = new GameEngine(bots.Count, seed, options) { Log = log };
        var botRng = new Random(seed * 31 + 7);
        string reason = "";
        engine.Event += e =>
        {
            if (e.Type == GameEventType.Win)
            {
                reason = e.Text;
            }
        };
        engine.Start();

        while (!engine.State.IsFinished)
        {
            int current = engine.State.InputPlayer;
            var view = engine.State.ViewFor(current);
            var action = bots[current].Decide(view, botRng);
            var result = engine.Apply(current, action);

            if (!result.Ok)
            {
                throw new InvalidOperationException(
                    $"시드 {seed}, P{current}({bots[current].Name})의 잘못된 행동 {action}: {result.Error}");
            }

            AssertInvariants(engine, seed);
        }

        int winner = engine.State.Winner!.Value;
        return new GameResult(winner, engine.State.TurnCount, winner >= 0 ? bots[winner].Name : "무승부", reason);
    }

    /// <summary>
    /// 카드가 복제되거나 사라지지 않았는지, 증강이 최대 개수를 넘지 않는지 확인합니다.
    /// </summary>
    private static void AssertInvariants(GameEngine engine, int seed)
    {
        var state = engine.State;
        int total = state.DrawPile.Count + state.DiscardPile.Count + state.GambleCards.Count
            + state.Players.Sum(p => p.Hand.Count);
        if (total != engine.DeckSize)
        {
            throw new InvalidOperationException($"시드 {seed}: 카드 수가 {total}장입니다. {engine.DeckSize}장이어야 합니다.");
        }

        var ids = state.DrawPile.Concat(state.DiscardPile).Concat(state.GambleCards)
            .Concat(state.Players.SelectMany(p => p.Hand)).Select(c => c.Id);
        if (ids.Distinct().Count() != engine.DeckSize)
        {
            throw new InvalidOperationException($"시드 {seed}: 같은 카드가 두 곳에 있습니다.");
        }

        if (state.Players.Any(p => p.Augments.Count > SpecialAugments.MaxPerPlayer))
        {
            throw new InvalidOperationException($"시드 {seed}: 증강이 최대 개수를 넘었습니다.");
        }

        if (state.Players.Any(p => p.Augments.Count(SpecialAugments.IsAltWin) > 1))
        {
            throw new InvalidOperationException($"시드 {seed}: 승리 조건 증강이 2개입니다.");
        }
    }

    /// <summary>
    /// 여러 판을 돌려서 자리별·봇별 승률과 평균 턴 수를 요약합니다.
    /// </summary>
    public static string Simulate(Func<IReadOnlyList<IBot>> makeBots, int games, int baseSeed = 1, GameOptions? options = null)
    {
        var bots = makeBots();
        var seatWins = new int[bots.Count];
        var botWins = new Dictionary<string, int>();
        int draws = 0;
        long totalTurns = 0;

        for (int g = 0; g < games; g++)
        {
            var result = RunGame(makeBots(), baseSeed + g, null, options);
            totalTurns += result.Turns;

            if (result.Winner < 0)
            {
                draws++;
                continue;
            }

            seatWins[result.Winner]++;
            botWins[result.WinnerBot] = botWins.GetValueOrDefault(result.WinnerBot) + 1;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"=== 시뮬레이션 {games}판 ({string.Join(", ", bots.Select((b, i) => $"P{i}:{b.Name}"))}) ===");
        sb.AppendLine($"평균 턴 수: {(double)totalTurns / games:F1}");
        sb.AppendLine($"무승부: {draws}판");

        for (int i = 0; i < seatWins.Length; i++)
        {
            sb.AppendLine($"P{i} ({bots[i].Name}) 승리: {seatWins[i]}판 ({100.0 * seatWins[i] / games:F1}%)");
        }

        foreach (var (name, wins) in botWins.OrderByDescending(kv => kv.Value))
        {
            int count = bots.Count(b => b.Name == name);
            sb.AppendLine($"[{name}] 1명당 평균 승률: {100.0 * wins / games / count:F1}%");
        }

        return sb.ToString();
    }
}
