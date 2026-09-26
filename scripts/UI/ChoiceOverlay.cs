using Godot;

namespace SpCardgame.UI;

/// <summary>
/// 가운데에 선택지 카드를 늘어놓는 반투명 창입니다. (scenes/ui/ChoiceOverlay.tscn)
/// 각성 능력, 특수 증강, 도박사 선택에 같이 쓰고, 제목·색·설명은 Game.tscn에서 인스턴스마다 인스펙터로 정합니다.
/// </summary>
public partial class ChoiceOverlay : ColorRect
{
    [Export]
    public string Title { get; set; } = "";

    [Export]
    public Color TitleColor { get; set; } = Colors.White;

    [Export]
    public string Subtitle { get; set; } = "";

    /// <summary>선택지 카드 사이 간격입니다.</summary>
    [Export]
    public int RowSeparation { get; set; } = 24;

    /// <summary>선택지 카드를 넣을 줄입니다.</summary>
    public HBoxContainer Row => GetNode<HBoxContainer>("%Row");

    public Label SubtitleLabel => GetNode<Label>("%Subtitle");

    public override void _Ready()
    {
        var title = GetNode<Label>("%Title");
        title.Text = Title;
        title.AddThemeColorOverride("font_color", TitleColor);
        SubtitleLabel.Text = Subtitle;
        Row.AddThemeConstantOverride("separation", RowSeparation);
    }
}
