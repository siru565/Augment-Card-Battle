using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 증강 하나를 보여 주는 작은 칩입니다. 마우스를 올리면 설명 팝업이 나옵니다.
/// 모양은 scenes/ui/AugmentChip.tscn에 있고, 등급 색(배경·테두리·글자)만 코드에서 칠합니다.
/// </summary>
public partial class AugmentChip : PanelContainer
{
    private AugmentInfo _info = new("?", AugmentTier.Silver, "", 0);

    /// <summary>AugmentChip.tscn으로 칩을 만듭니다.</summary>
    public static AugmentChip Create(AugmentInfo info, int fontSize = 14)
    {
        var chip = Scenes.Create<AugmentChip>(Scenes.AugmentChip);
        chip.Setup(info, fontSize);
        return chip;
    }

    public void Setup(AugmentInfo info, int fontSize)
    {
        _info = info;
        var tier = UiTheme.TierColor(info.Tier);
        var style = UiTheme.Box(new Color(tier, 0.12f), new Color(tier, 0.35f), 1, 4, 4);
        style.ContentMarginLeft = style.ContentMarginRight = 8;
        AddThemeStyleboxOverride("panel", style);

        var label = GetNode<Label>("%Text");
        string cooldown = info.Cooldown > 0 ? $" ({info.Cooldown})" : "";
        label.Text = $"{info.Name}{cooldown}";
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", tier);
    }

    public override void _Ready()
    {
        MouseEntered += () => HoverPopup.ShowAugment(this, _info);
        MouseExited += () => HoverPopup.HideOwner(this);
    }
}
