using System.Linq;
using System.Threading.Tasks;
using Godot;
using SpCardgame.UI;

namespace SpCardgame.DevTools;

/// <summary>
/// 멀티플레이 자동 테스트입니다. 한 프로세스 안에 호스트 화면과 참가자 화면을 나란히 띄우고,
/// 각각 다른 MultiplayerAPI를 써서 실제 ENet으로 접속시킨 뒤 봇 두뇌로 한 판을 끝까지 둡니다.
/// 결과는 tools/shots/net_result.txt와 net_test.png로 저장됩니다.
/// </summary>
public partial class NetTest : Node
{
    private const int Port = 24699;

    public override async void _Ready()
    {
        string result;
        try
        {
            result = await Run();
        }
        catch (System.Exception e)
        {
            result = "예외: " + e;
        }

        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://tools/shots"));
        System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("res://tools/shots/net_result.txt"), result);
        GD.Print(result);
        GetTree().Quit();
    }

    private async Task<string> Run()
    {
        var row = new HBoxContainer();
        row.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(row);

        var host = AddGame(row, "HostView");
        var client = AddGame(row, "ClientView");

        await Wait(0.5);
        host.DebugHost(Port);
        await Wait(0.3);
        client.DebugJoin("127.0.0.1", Port);

        for (int i = 0; i < 50 && host.Session?.LobbyNames.Length < 2; i++)
        {
            await Wait(0.1);
        }

        if (host.Session?.LobbyNames.Length < 2)
        {
            return "실패: 참가자가 로비에 들어오지 않았어요.";
        }

        string lobby = string.Join(", ", host.Session!.LobbyNames);
        host.BotDelay = 0.05f;
        host.DebugAutoPlay = true;
        client.DebugAutoPlay = true;

        // 1판은 봇을 모두 빼서 사람 둘만(2인) 합니다.
        var room = (SpCardgame.Net.HostSession)host.Session!;
        room.SetBotCount(0);
        await Wait(0.3);
        bool botsSynced = client.Session!.LobbyBots == 0;
        host.DebugStartGame();

        await Wait(1.0);
        int firstPlayers = client.Session?.View?.PlayerCount ?? -1;
        await Capture("net_test.png");

        var report = new System.Text.StringBuilder();
        report.AppendLine($"로비: {lobby}");
        report.AppendLine($"봇 빼기: 참가자 화면 봇 수 0={botsSynced}, 1판 인원={firstPlayers}");
        string first = await PlayToEnd(host, client);
        report.AppendLine($"1판 (특수 증강 OFF): {first}");
        if (!first.StartsWith("끝") || !botsSynced || firstPlayers != 2)
        {
            return "실패\n" + report;
        }

        // 대기방으로 돌아가서 설정을 바꾸고 다시 시작합니다. 방은 그대로여야 합니다.
        host.Session!.ReturnToRoom();
        await Wait(0.5);
        bool clientInRoom = client.Session != null && !client.Session.Playing && client.Session.LobbyNames.Length == 2;
        report.AppendLine($"대기방 복귀: 참가자 대기방={clientInRoom}");
        await Capture("net_room.png");

        ((SpCardgame.Net.HostSession)host.Session).SetOptions(new SpCardgame.Core.GameOptions(SpecialAugments: true));
        await Wait(0.3);
        report.AppendLine($"설정 전달: 참가자 특수 증강={client.Session?.RoomOptions.SpecialAugments}");

        // 2판은 봇 1명(어려움)을 넣어 3인으로 합니다.
        room.AddBot();
        room.SetBotLevel(SpCardgame.AI.BotLevel.Hard);
        await Wait(0.3);
        report.AppendLine($"봇 난이도 전달: 참가자 화면={client.Session?.LobbyBotLevel}");
        host.DebugStartGame();
        await Wait(2.0);
        int secondPlayers = client.Session?.View?.PlayerCount ?? -1;
        report.AppendLine($"봇 넣기: 참가자 화면 봇 수={client.Session?.LobbyBots}, 2판 인원={secondPlayers}");

        // 2판: 참가자가 중간에 나가기 → 대기방으로, 자리는 봇이 이어받고 게임은 계속됩니다.
        client.Session!.LeaveGame();
        await Wait(0.5);
        bool clientLeft = !client.Session.Playing && client.Session.GameRunning && host.Session!.LobbyBusy.Length == 2 && !host.Session.LobbyBusy[1];
        string second = await WaitWinner(host);
        report.AppendLine($"2판 (특수 증강 ON, 참가자 중간 퇴장): 참가자 대기방={clientLeft}, {second}");

        // 3판: 바로 한 판 더 → 대기방에 있던 참가자도 불려 옵니다. 이번엔 방장이 중간에 나갑니다.
        host.Session!.Restart();
        await Wait(1.0);
        bool pulled = client.Session.Playing;

        // 참가자에게 정밀 사수를 줘서, 참가자 화면에서 미니게임이 뜨고 결과가 방장에게 전달되는지 봅니다.
        ((SpCardgame.Net.HostSession)host.Session!).DebugGiveAugmentTo(client.Session.MySeat, SpCardgame.Core.SpecialAugmentId.Marksman);
        _sawClientMinigame = false;
        host.Session.LeaveGame();
        await Wait(0.5);
        bool hostLeft = !host.Session.Playing && client.Session.GameRunning;
        string third = await WaitWinner(client);
        report.AppendLine($"3판 (방장 중간 퇴장): 참가자 합류={pulled}, 방장 대기방={hostLeft}, 참가자 미니게임={_sawClientMinigame}, {third}");
        await Wait(0.5);
        second = _sawClientMinigame && secondPlayers == 3 && client.Session?.LobbyBotLevel == SpCardgame.AI.BotLevel.Hard && clientLeft && pulled && hostLeft && third.StartsWith("끝") ? second : "실패";

        // 다시 대기방으로 돌아간 뒤 참가자를 강퇴합니다.
        host.Session!.ReturnToRoom();
        await Wait(0.5);
        ((SpCardgame.Net.HostSession)host.Session).Kick(1);
        await Wait(1.5);
        bool kicked = client.Session == null && host.Session!.LobbyNames.Length == 1;
        report.AppendLine($"강퇴: 참가자 퇴장={client.Session == null}, 방 인원={host.Session!.LobbyNames.Length}");

        // 다시 들어온 참가자가 스스로 '방 나가기'를 누르면, 방장 화면에서도 바로 빠져야 합니다.
        client.DebugJoin("127.0.0.1", Port);
        for (int i = 0; i < 50 && host.Session!.LobbyNames.Length < 2; i++)
        {
            await Wait(0.1);
        }

        bool rejoined = host.Session!.LobbyNames.Length == 2;
        client.DebugLeaveRoom();
        int waitedMs = 0;
        while (waitedMs < 5000 && host.Session!.LobbyNames.Length > 1)
        {
            await Wait(0.05);
            waitedMs += 50;
        }

        bool leftSeen = host.Session!.LobbyNames.Length == 1;
        report.AppendLine($"스스로 나가기: 재입장={rejoined}, 방장 화면에서 빠짐={leftSeen} ({waitedMs}ms)");

        // 강퇴 뒤에 방장은 계속 방을 유지해야 합니다.
        bool ok = clientInRoom && second.StartsWith("끝") && kicked && rejoined && leftSeen && waitedMs <= 1000;
        return (ok ? "성공\n" : "실패\n") + report;
    }

    private bool _sawClientMinigame;

    private async Task<string> WaitWinner(GameController who)
    {
        for (int i = 0; i < 2400; i++)
        {
            await Wait(0.05);
            var v = who.Session?.View;
            if (v?.MyMinigame != null)
            {
                _sawClientMinigame = true;
            }

            if (v?.Winner != null)
            {
                return $"끝 · 승자 {who.Session!.NameOf(v.Winner.Value)}, 턴 {v.TurnCount}";
            }
        }

        return "2분 안에 끝나지 않음";
    }

    private async Task<string> PlayToEnd(GameController host, GameController client)
    {
        int mismatches = 0;
        for (int i = 0; i < 2400; i++)
        {
            await Wait(0.05);
            var hv = host.Session?.View;
            var cv = client.Session?.View;
            if (hv == null || cv == null)
            {
                continue;
            }

            if (hv.Winner.HasValue && cv.Winner.HasValue)
            {
                await Wait(0.3);
                await Capture("net_end.png");
                return $"끝 · 승자 {host.Session!.NameOf(hv.Winner.Value)} (참가자 화면: {client.Session!.NameOf(cv.Winner.Value)}), " +
                       $"턴 {hv.TurnCount}, 증강 {string.Join("/", hv.Augments.Select(a => a.Count))}, 장수 불일치 관측 {mismatches}";
            }

            if (!hv.HandCounts.SequenceEqual(cv.HandCounts))
            {
                mismatches++;
            }
        }

        return "2분 안에 끝나지 않음";
    }

    private GameController AddGame(Control parent, string name)
    {
        var container = new SubViewportContainer { Stretch = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        parent.AddChild(container);
        var viewport = new SubViewport { Name = name, Size = new Vector2I(1280, 720) };
        container.AddChild(viewport);

        // 화면마다 따로 MultiplayerAPI를 붙여서 한 프로세스 안에서 호스트와 참가자를 흉내 냅니다.
        GetTree().SetMultiplayer(new SceneMultiplayer(), viewport.GetPath());

        var game = GD.Load<PackedScene>("res://scenes/Game.tscn").Instantiate<GameController>();
        Collection.TestMode = true;
        viewport.AddChild(game);
        return game;
    }

    private async Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://tools/shots/{name}"));
    }
}
