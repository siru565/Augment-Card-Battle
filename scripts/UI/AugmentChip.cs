using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 증강 하나를 보여 주는 작은 칩입니다. 마우스를 올리면 설명 팝업이 나옵니다.
/// </summary>
public partial class AugmentChip : PanelContainer
{
    private readonly AugmentInfo _info;

    /// <summary>Godot가 스크립트를 다시 불러올 때 필요한 기본 생성자입니다.</summary>
    public AugmentChip() : this(new AugmentInfo("?", AugmentTier.Silver, "", 0)) { }

    public AugmentChip(AugmentInfo info, int fontSize = 14)
    {
        _info = info;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.Help;

        var tier = UiTheme.TierColor(info.Tier);
        var style = UiTheme.Box(new Color(tier, 0.12f), new Color(tier, 0.35f), 1, 4, 4);
        style.ContentMarginLeft = style.ContentMarginRight = 8;
        AddThemeStyleboxOverride("panel", style);

        string cooldown = info.Cooldown > 0 ? $" ({info.Cooldown})" : "";
        AddChild(UiTheme.MakeLabel($"{info.Name}{cooldown}", fontSize, tier, bold: true));
    }

    public override void _Ready()
    {
        MouseEntered += () => HoverPopup.ShowAugment(this, _info);
        MouseExited += () => HoverPopup.HideOwner(this);
    }
}
