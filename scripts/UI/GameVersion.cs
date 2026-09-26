using Godot;

namespace SpCardgame.UI;

/// <summary>
/// 게임 버전입니다. project.godot의 application/config/version 값을 읽습니다.
/// tools/publish_itch.bat가 업로드할 때마다 마지막 숫자를 1씩 올립니다.
/// </summary>
public static class GameVersion
{
    public static string Current =>
        ProjectSettings.GetSetting("application/config/version", "0.0.0").AsString();
}
