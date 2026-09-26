using System.Threading.Tasks;
using Godot;
using SpCardgame.Net;

namespace SpCardgame.DevTools;

/// <summary>
/// Steam 연동 확인용 개발 도구입니다. Steam 초기화와 로비 생성까지 해 보고
/// 결과를 tools/shots/steam_result.txt에 저장한 뒤 종료합니다.
/// </summary>
public partial class SteamTest : Node
{
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
        System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("res://tools/shots/steam_result.txt"), result);
        GetTree().Quit();
    }

    private async Task<string> Run()
    {
        SteamRuntime.EnsureInitialized(this);
        if (!SteamRuntime.IsReady)
        {
            return $"초기화 실패: {SteamRuntime.InitError}";
        }

        string code = "";
        string error = "";
        SteamRuntime.CreateLobby(4, lobby => code = SteamRuntime.ToRoomCode(lobby), e => error = e);

        for (int i = 0; i < 100 && code == "" && error == ""; i++)
        {
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        }

        using var server = new SteamServerTransport();
        bool parsed = SteamRuntime.TryParseRoomCode(code, out var back);
        return $"초기화 성공\n계정: {SteamRuntime.PersonaName}\n로비 코드: {code}\n오류: {error}\n" +
               $"코드 되돌리기: {parsed && back == SteamRuntime.CurrentLobby}";
    }
}
