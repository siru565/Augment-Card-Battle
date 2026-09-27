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
    private AugmentTier _tier;
    private bool _hovered;
    private bool _codex;
    private bool _owned;

    public event Action? Picked;

    /// <summary>각성 능력 선택지로 만듭니다. (모양은 scenes/ui/AbilityCard.tscn)</summary>
    public static AbilityCardView Create(AbilityInfo ability) =>
        Create(ability.Name, ability.Tier, ability.Description, NeedsText(ability));

    /// <summary>특수 증강 선택지로 만듭니다. 승리 조건을 바꾸는 증강은 아래에 따로 표시합니다.</summary>
    public static AbilityCardView Create(AugmentInfo augment) =>
        Create(augment.Name, augment.Tier, augment.Description, augment.AltWin ? "승리 조건 추가" : "영구 효과");

    public static AbilityCardView Create(string title, AugmentTier tierValue, string text, string needs)
    {
        var card = Scenes.Create<AbilityCardView>(Scenes.AbilityCard);
        card.Setup(title, tierValue, text, needs);
        return card;
    }

    private static string NeedsText(AbilityInfo ability) =>
        ability.NeedsTarget && ability.NeedsSuit ? "대상 + 문양 선택"
        : ability.NeedsTarget ? "대상 선택"
        : ability.NeedsSuit ? "문양 선택"
        : "바로 발동";

    /// <summary>씬의 라벨과 문장에 값을 채우고 등급 색을 칠합니다.</summary>
    public void Setup(string title, AugmentTier tierValue, string text, string needs)
    {
        _tier = tierValue;
        var tier = UiTheme.TierColor(tierValue);

        var tierLabel = GetNode<Label>("%Tier");
        tierLabel.Text = Augment.TierName(tierValue);
        tierLabel.AddThemeColorOverride("font_color", tier);
        GetNode<AbilityEmblem>("%Emblem").Tier = tierValue;
        GetNode<Label>("%Title").Text = title;
        GetNode<Label>("%Description").Text = text;
        GetNode<Label>("%Footer").Text = needs;
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
        if (!_codex && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Picked?.Invoke();
            AcceptEvent();
        }
    }

    /// <summary>
    /// 도감용 모양으로 바꿉니다. 고를 수 없으니 손가락 커서와 확대를 끄고,
    /// 얻어 본 카드는 테두리를 밝게, 못 얻은 카드는 흐리게 만듭니다.
    /// </summary>
    public void SetCodex(bool owned)
    {
        _codex = true;
        _owned = owned;
        MouseDefaultCursorShape = CursorShape.Arrow;
        CustomMinimumSize = new Vector2(214, 292);
        GetNode<Control>("%Emblem").CustomMinimumSize = new Vector2(0, 70);
        GetNode<Label>("%Title").AddThemeFontSizeOverride("font_size", 19);
        var description = GetNode<Label>("%Description");
        description.CustomMinimumSize = new Vector2(184, 0);
        description.AddThemeFontSizeOverride("font_size", 13);

        var footer = GetNode<Label>("%Footer");
        footer.Text = owned ? "✦ 획득" : "미획득";
        if (owned)
        {
            footer.AddThemeColorOverride("font_color", UiTheme.TierColor(_tier).Lightened(0.2f));
        }

        Modulate = owned ? Colors.White : new Color(0.55f, 0.57f, 0.62f, 0.85f);
        ApplyStyle();
    }

    private void SetHovered(bool hovered)
    {
        _hovered = hovered;
        ApplyStyle();
        if (_codex)
        {
            return;
        }

        PivotOffset = Size / 2;
        var tween = CreateTween();
        tween.TweenProperty(this, "scale", hovered ? new Vector2(1.03f, 1.03f) : Vector2.One, 0.1);
    }

    private void ApplyStyle()
    {
        var tier = UiTheme.TierColor(_tier);
        var bg = _hovered ? Color.FromHtml("#1d212a") : Color.FromHtml("#14171d");
        // 등급 색 장식 테두리입니다. 마우스를 올리면 테두리가 더 밝아집니다.
        bool bright = _hovered || (_codex && _owned);
        AddThemeStyleboxOverride("panel", UiTheme.Ornate(bg, bright ? tier : new Color(tier, 0.6f), 18, 10));
    }
}
