using Godot;
using SpCardgame.UI;

namespace SpCardgame.Debug;

/// <summary>
/// 개발용: UiTheme.CreateThemeResource()로 만든 테마를 assets/ui/ui_theme.tres로 저장하고 종료합니다.
/// 한 번 저장한 뒤로는 에디터에서 ui_theme.tres를 열어 색·글꼴·스타일을 직접 고치면 됩니다.
/// (다시 실행하면 코드 기본값으로 덮어쓰므로, 에디터에서 고친 뒤에는 실행하지 마세요)
/// </summary>
public partial class ThemeGenerator : Node
{
    public override void _Ready()
    {
        var theme = UiTheme.CreateThemeResource();
        var error = ResourceSaver.Save(theme, UiTheme.ThemePath);
        using var log = FileAccess.Open("res://tools/gen_theme_log.txt", FileAccess.ModeFlags.Write);
        log?.StoreString($"save {UiTheme.ThemePath}: {error}");
        GetTree().Quit();
    }
}
