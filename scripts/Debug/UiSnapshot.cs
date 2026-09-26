using System.Linq;
using System.Threading.Tasks;
using Godot;
using SpCardgame.Core;
using SpCardgame.UI;

namespace SpCardgame.DevTools;

/// <summary>
/// UI 확인용 개발 도구입니다. 게임 화면을 띄운 뒤 내 차례가 오면 스크린샷을 찍고,
/// 카드에 마우스를 올린 상태(설명 팝업)도 찍은 뒤 종료합니다. 결과는 tools/shots 폴더에 저장됩니다.
/// </summary>
public partial class UiSnapshot : Node
{
    public override async void _Ready()
    {
        try
        {
            await Run();
        }
        catch (System.Exception e)
        {
            GD.PrintErr(e.ToString());
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("res://tools/shots/error.txt"), e.ToString());
        }

        GetTree().Quit();
    }

    private async Task Run()
    {
        var game = GD.Load<PackedScene>("res://scenes/Game.tscn").Instantiate<GameController>();
        game.BotDelay = 0.05f;
        AddChild(game);

        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://tools/shots"));

        // 로비 화면을 먼저 찍고 혼자 하기를 시작합니다.
        await Wait(0.5);
        await Capture("shot_lobby.png");
        game.DebugToggleSettings();
        await Wait(0.3);
        await Capture("shot_settings.png");
        game.DebugToggleSettings();
        game.DebugStartSolo(specialAugments: true);

        // 내 차례가 올 때까지 기다립니다.
        for (int i = 0; i < 200 && !game.IsHumanTurn; i++)
        {
            await Wait(0.05);
        }

        game.BotDelay = 999f;
        var host = (SpCardgame.Net.HostSession)game.Session!;
        host.DebugGiveAugment(SpecialAugmentId.Avengers);
        host.DebugGiveAugment(SpecialAugmentId.Pacifist);
        await Wait(0.8);
        await Capture("shot_table.png");

        // 손패에서 가운데 카드에 마우스를 올린 것처럼 팝업을 띄웁니다.
        var cards = AllChildren(game).OfType<CardView>().Where(c => c.LiftOnHover).ToList();
        if (cards.Count > 0)
        {
            var target = cards.FirstOrDefault(c => c.Card?.IsSpecial == true) ?? cards[cards.Count / 2];
            // 사용자의 실제 마우스를 움직이지 않도록 팝업을 직접 띄웁니다.
            var center = target.GetGlobalRect().GetCenter();
            target.Selected = true;
            if (HoverPopup.Instance != null && target.Card != null)
            {
                HoverPopup.Instance.DebugMouseOverride = center;
                HoverPopup.ShowCard(target, target.Card, target.HoverExtra);
            }

            await Wait(0.6);
            await Capture("shot_hover.png");
        }

        // 각성 능력 3지선다 화면을 띄워서 찍습니다.
        if (HoverPopup.Instance != null)
        {
            HoverPopup.Instance.Hide();
        }

        host.DebugForceAbilityChoices();
        await Wait(1.0);
        await Capture("shot_ability.png");
        game.Session!.Submit(PlayerAction.ChooseAbility(0, 1, CardColor.Red));
        await Wait(0.3);

        // 각성 연출(빛줄기 + 번쩍임 + 지진)을 찍습니다.
        game.DebugFx(new GameEvent(GameEventType.Awaken, 0));
        await Wait(0.18);
        await Capture("shot_awaken_fx.png");
        await Wait(1.4);

        // 특수 증강 3지선다입니다.
        for (int i = 0; i < 200 && !game.IsHumanTurn; i++)
        {
            game.BotDelay = 0.05f;
            await Wait(0.05);
        }

        game.BotDelay = 999f;
        host.DebugForceAugmentChoices();
        await Wait(1.0);
        await Capture("shot_augment.png");
        game.Session!.Submit(PlayerAction.ChooseAugment(0));
        await Wait(0.3);

        // 공격을 맞고 직접 뽑아야 하는 상황입니다.
        host.DebugForceDebt(4);
        game.DebugFx(new GameEvent(GameEventType.Attack, 1, 0, Amount: 4));
        game.DebugFx(new GameEvent(GameEventType.ForcedDrawStarted, 1, 0, Amount: 4, Text: "+4 공격"));
        await Wait(0.2);
        await Capture("shot_attack_fx.png");
        await Wait(1.5);
        await Capture("shot_debt.png");

        // +2·+4가 쌓인 상황입니다.
        for (int i = 0; i < 200 && !game.IsHumanTurn; i++)
        {
            game.BotDelay = 0.05f;
            await Wait(0.05);
        }

        game.BotDelay = 999f;
        host.DebugPenalty(6);
        await Wait(0.6);
        await Capture("shot_stack.png");

        // 도박사: 2장 중 1장 고르기 창입니다.
        host.DebugPenalty(0);
        host.DebugGiveAugment(SpecialAugmentId.Gambler);
        for (int i = 0; i < 60 && game.Session!.View!.MustDraw; i++)
        {
            game.Session.Submit(PlayerAction.ForcedDraw());
            await Wait(0.05);
        }

        for (int i = 0; i < 200 && !(game.IsHumanTurn && game.Session!.View!.CanDraw); i++)
        {
            game.BotDelay = 0.05f;
            await Wait(0.05);
        }

        game.BotDelay = 999f;
        game.Session!.Submit(PlayerAction.Draw());
        await Wait(0.6);
        await Capture("shot_gamble.png");

        // 대기방 화면입니다. (IP 방으로 확인합니다)
        game.DebugHost(24777);
        await Wait(0.6);
        await Capture("shot_room.png");

        System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("res://tools/shots/info.txt"),
            $"cards={cards.Count} popup={(HoverPopup.Instance != null)} visible={HoverPopup.Instance?.Visible}");
    }

    private static System.Collections.Generic.IEnumerable<Node> AllChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var grandChild in AllChildren(child))
            {
                yield return grandChild;
            }
        }
    }

    private async Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        string path = ProjectSettings.GlobalizePath($"res://tools/shots/{name}");
        image.SavePng(path);
        GD.Print($"[스냅샷] {path}");
    }
}
