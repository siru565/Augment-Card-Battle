using System;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 각성 능력이나 특수 증강 선택지 한 장입니다. 등급 색 테두리, 이름, 설명, 필요한 선택(대상/문양)을 보여 줍니다.
/// 마우스를 올리면 살짝 커지고, 클릭하면 선택됩니다.
/// </summary>
public partial class AbilityCardView : PanelContainer
{
    private readonly AugmentTier _tier;
    private bool _hovered;

    public event Action? Picked;

    /// <summary>Godot가 스크립트를 다시 불러올 때 필요한 기본 생성자입니다.</summary>
    public AbilityCardView() : this(AbilityPool.All[0]) { }

    public AbilityCardView(AbilityInfo ability)
        : this(ability.Name, ability.Tier, ability.Description, NeedsText(ability))
    {
    }

    /// <summary>특수 증강 선택지로 만듭니다. 승리 조건을 바꾸는 증강은 아래에 따로 표시합니다.</summary>
    public AbilityCardView(AugmentInfo augment)
        : this(augment.Name, augment.Tier, augment.Description, augment.AltWin ? "승리 조건 추가" : "영구 효과")
    {
    }

    private static string NeedsText(AbilityInfo ability) =>
        ability.NeedsTarget && ability.NeedsSuit ? "대상 + 문양 선택"
        : ability.NeedsTarget ? "대상 선택"
        : ability.NeedsSuit ? "문양 선택"
        : "바로 발동";

    public AbilityCardView(string title, AugmentTier tierValue, string text, string needs)
    {
        _tier = tierValue;
        CustomMinimumSize = new Vector2(250, 330);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;

        var tier = UiTheme.TierColor(tierValue);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 10);
        AddChild(box);

        var tierLabel = UiTheme.MakeLabel(Augment.TierName(tierValue), 13, tier, bold: true);
        tierLabel.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(tierLabel);

        box.AddChild(new AbilityEmblem(tierValue) { CustomMinimumSize = new Vector2(0, 90) });

        var name = UiTheme.MakeLabel(title, 22, UiTheme.Text, bold: true);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(name);

        var description = UiTheme.MakeLabel(text, 15, Color.FromHtml("#b9bfca"));
        description.AutowrapMode = TextServer.AutowrapMode.Word;
        description.HorizontalAlignment = HorizontalAlignment.Center;
        description.CustomMinimumSize = new Vector2(220, 0);
        description.SizeFlagsVertical = SizeFlags.ExpandFill;
        box.AddChild(description);

        var footer = UiTheme.MakeLabel(needs, 12, UiTheme.TextDim, bold: true);
        footer.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(footer);

        ApplyStyle();
    }

    public override void _Ready()
    {
        PivotOffset = CustomMinimumSize / 2;
        MouseEntered += () => SetHovered(true);
        MouseExited += () => SetHovered(false);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Picked?.Invoke();
            AcceptEvent();
        }
    }

    private void SetHovered(bool hovered)
    {
        _hovered = hovered;
        ApplyStyle();
        PivotOffset = Size / 2;
        var tween = CreateTween();
        tween.TweenProperty(this, "scale", hovered ? new Vector2(1.03f, 1.03f) : Vector2.One, 0.1);
    }

    private void ApplyStyle()
    {
        var tier = UiTheme.TierColor(_tier);
        var bg = _hovered ? Color.FromHtml("#1d212a") : Color.FromHtml("#14171d");
        // 등급 색 장식 테두리입니다. 마우스를 올리면 테두리가 더 밝아집니다.
        AddThemeStyleboxOverride("panel", UiTheme.Ornate(bg, _hovered ? tier : new Color(tier, 0.6f), 18, 10));
    }
}
