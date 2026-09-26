using System.Linq;
using System.Threading.Tasks;
using Godot;
using SpCardgame.Core;
using SpCardgame.Net;
using SpCardgame.UI;

namespace SpCardgame.DevTools;

/// <summary>
/// 개발일지에 넣을 게임 플레이 스크린샷을 자동으로 찍는 개발 도구입니다.
/// 실제로 혼자 하기 한 판을 진행하면서 장면마다 tools/shots/devlog 폴더에 저장합니다.
/// </summary>
public partial class DevlogShots : Node
{
    private const string Folder = "res://tools/shots/devlog";
    private GameController _game = null!;

    private HostSession Host => (HostSession)_game.Session!;

    private PlayerView View => _game.Session!.View!;

    public override async void _Ready()
    {
        try
        {
            await Run();
        }
        catch (System.Exception e)
        {
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath($"{Folder}/error.txt"), e.ToString());
        }

        GetTree().Quit();
    }

    private async Task Run()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Folder));
        _game = GD.Load<PackedScene>("res://scenes/Game.tscn").Instantiate<GameController>();
        AddChild(_game);

        // 1. 메인 화면과 설정 창
        await Wait(1.5);
        await Capture("01_main_menu.png");
        _game.DebugToggleSettings();
        await Wait(0.4);
        await Capture("02_settings.png");
        _game.DebugToggleSettings();

        // 2. 게임 시작 — 모두 동시에 첫 특수 증강 고르기
        _game.BotDelay = 0.35f;
        _game.DebugStartSolo(specialAugments: true);
        await Wait(1.3);
        await Capture("03_augment_draft.png");
        _game.Session!.Submit(PlayerAction.ChooseAugment(0));

        // 3. 몇 바퀴 돌린 뒤 내 차례의 보통 게임 화면
        await WaitMyTurn(minTurn: 6);
        _game.BotDelay = 999f;
        await Wait(2.2);
        await Capture("04_table.png");

        // 4. +카드 공격이 쌓여서 넘어온 순간과 누적 상태
        Host.DebugPenalty(5);
        _game.DebugFx(new GameEvent(GameEventType.Attack, View.NextSeat, View.PlayerId, Amount: 5));
        await Wait(0.18);
        await Capture("05_attack_fx.png");
        await Wait(1.4);
        await Capture("06_stacked_penalty.png");

        // 5. 받기를 누르면 직접 한 장씩 뽑아야 합니다.
        _game.Session.Submit(PlayerAction.Draw());
        await Wait(0.3);
        _game.Session.Submit(PlayerAction.ForcedDraw());
        await Wait(0.25);
        _game.Session.Submit(PlayerAction.ForcedDraw());
        await Wait(0.7);
        await Capture("07_forced_draw.png");
        for (int i = 0; i < 40 && View.MustDraw; i++)
        {
            _game.Session.Submit(PlayerAction.ForcedDraw());
            await Wait(0.12);
        }

        // 6. 각성 연출과 능력 선택
        _game.BotDelay = 0.35f;
        await WaitMyTurn(minTurn: 0);
        _game.BotDelay = 999f;
        _game.DebugFx(new GameEvent(GameEventType.Awaken, View.PlayerId));
        await Wait(0.2);
        await Capture("08_awaken_fx.png");
        await Wait(1.3);
        Host.DebugForceAbilityChoices();
        await Wait(1.0);
        await Capture("09_ability_choice.png");
        int target = Enumerable.Range(0, View.PlayerCount).First(i => i != View.PlayerId && View.IsActive(i));
        _game.Session.Submit(PlayerAction.ChooseAbility(0, target, CardColor.Red));
        await Wait(0.3);
        for (int i = 0; i < 40 && View.MustDraw; i++)
        {
            _game.Session.Submit(PlayerAction.ForcedDraw());
            await Wait(0.12);
        }

        // 7. 도박사: 2장 중 1장 고르기
        Host.DebugGiveAugment(SpecialAugmentId.Gambler);
        _game.BotDelay = 0.35f;
        for (int i = 0; i < 400 && !(_game.IsHumanTurn && View.CanDraw && View.PendingPenalty == 0); i++)
        {
            if (View.MustDraw)
            {
                _game.Session.Submit(PlayerAction.ForcedDraw());
            }

            await Wait(0.05);
        }

        _game.BotDelay = 999f;
        await Wait(2.2);
        _game.Session.Submit(PlayerAction.Draw());
        await Wait(0.8);
        await Capture("10_gamble.png");
        _game.Session.Submit(PlayerAction.ChooseDraw(0));

        // 8. 끝까지 자동으로 둬서 최종 순위표
        _game.BotDelay = 0.03f;
        _game.DebugAutoPlay = true;
        for (int i = 0; i < 3000 && View.Winner == null; i++)
        {
            await Wait(0.05);
        }

        await Wait(2.0);
        await Capture("11_ranking.png");

        // 9. 대기방
        _game.DebugAutoPlay = false;
        _game.DebugHost(24791);
        await Wait(0.8);
        await Capture("12_room.png");
    }

    /// <summary>봇이 두는 동안 기다렸다가 내 차례가 오면 돌아옵니다. (억지 뽑기는 대신 눌러 줍니다)</summary>
    private async Task WaitMyTurn(int minTurn)
    {
        for (int i = 0; i < 600; i++)
        {
            var view = _game.Session?.View;
            if (view != null && view.MustDraw)
            {
                _game.Session!.Submit(PlayerAction.ForcedDraw());
            }
            else if (view != null && view.AugmentChoices.Count > 0)
            {
                _game.Session!.Submit(PlayerAction.ChooseAugment(0));
            }
            else if (_game.IsHumanTurn && view!.TurnCount >= minTurn && view.PendingPenalty == 0 && !view.ChoosingAbility)
            {
                return;
            }
            else if (_game.IsHumanTurn)
            {
                // 조건이 안 맞는 내 차례는 봇 두뇌로 대신 둡니다.
                var action = new AI.RuleBasedBot().Decide(view!, new System.Random(i));
                _game.Session!.Submit(action);
            }

            await Wait(0.1);
        }
    }

    private async Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        image.SavePng(ProjectSettings.GlobalizePath($"{Folder}/{name}"));
        GD.Print($"[개발일지 스샷] {name}");
    }
}
