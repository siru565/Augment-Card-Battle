using Godot;
using SpCardgame.AI;
using SpCardgame.Simulation;

namespace SpCardgame.DevTools;

/// <summary>
/// 봇끼리 대결시켜서 결과를 출력하는 테스트용 노드입니다.
/// scenes/Simulation.tscn을 실행하면 한 판을 자세히 보여 주고, 이어서 여러 판의 통계를 출력한 뒤 종료합니다.
/// </summary>
public partial class SimulationRunner : Node
{
    /// <summary>통계를 낼 판 수입니다. 인스펙터에서 바꿀 수 있습니다.</summary>
    [Export] public int Games { get; set; } = 1000;

    /// <summary>자세히 보여 줄 판의 시드입니다.</summary>
    [Export] public int DemoSeed { get; set; } = 42;

    /// <summary>끝나면 자동으로 종료할지 정합니다.</summary>
    [Export] public bool QuitWhenDone { get; set; } = true;

    public override void _Ready()
    {
        GD.Print("===== 데모 1판 (규칙봇 2 vs 랜덤봇 2) =====");
        MatchRunner.RunGame(MakeBots(), DemoSeed, msg => GD.Print(msg));

        GD.Print("");
        GD.Print(MatchRunner.Simulate(MakeBots, Games));

        if (QuitWhenDone)
        {
            GetTree().Quit();
        }
    }

    private static IBot[] MakeBots() => new IBot[]
    {
        new RuleBasedBot(), new RuleBasedBot(), new RandomBot(), new RandomBot(),
    };
}
