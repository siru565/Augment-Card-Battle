using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 화면 전체에서 함께 쓰는 색, 폰트, 스타일입니다. 디자인을 바꿀 때는 여기만 고치면 됩니다.
/// </summary>
public static class UiTheme
{
    // 배경과 패널 색입니다. 채도를 낮춘 어두운 회청색 위에 강조색 하나만 씁니다.
    public static readonly Color TableCenter = Color.FromHtml("#1c212b");
    public static readonly Color TableEdge = Color.FromHtml("#0a0c10");
    public static readonly Color Panel = new(0.075f, 0.086f, 0.11f, 0.92f);
    public static readonly Color PanelRaised = Color.FromHtml("#1b1f28");
    public static readonly Color PanelBorder = new(1f, 1f, 1f, 0.07f);
    public static readonly Color Text = Color.FromHtml("#eceef2");

    /// <summary>강조색입니다. 내 차례, 선택 가능, 중요한 숫자에만 씁니다.</summary>
    public static readonly Color Gold = Color.FromHtml("#f0b849");
    public static readonly Color TextDim = Color.FromHtml("#8b93a1");
    public static readonly Color Danger = Color.FromHtml("#e5534b");

    // 등급 색입니다.
    public static readonly Color Silver = Color.FromHtml("#b9c3d1");
    public static readonly Color GoldTier = Color.FromHtml("#e9b44c");
    public static readonly Color Prism = Color.FromHtml("#c39bff");

    /// <summary>버튼의 역할입니다. 화면마다 색을 따로 고르지 않고 역할로만 구분합니다.</summary>
    public enum ButtonKind
    {
        /// <summary>화면에서 가장 중요한 행동 하나 (강조색 채움)</summary>
        Primary,

        /// <summary>일반 행동 (어두운 채움)</summary>
        Secondary,

        /// <summary>부가 행동 (배경 없음)</summary>
        Ghost,

        /// <summary>나가기, 강퇴 같은 위험한 행동 (빨간 글자)</summary>
        Danger,
    }

    private static Font? _regular;
    private static Font? _bold;

    /// <summary>한글이 확실히 나오도록 시스템 폰트(맑은 고딕 등)를 씁니다.</summary>
    public static Font Regular => _regular ??= MakeFont(500);

    public static Font Bold => _bold ??= MakeFont(700);

    private static Font MakeFont(int weight) => new SystemFont
    {
        FontNames = new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "NanumGothic", "sans-serif" },
        FontWeight = weight,
        Antialiasing = TextServer.FontAntialiasing.Gray,
    };

    public static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFont = Regular, DefaultFontSize = 15 };
        theme.SetColor("font_color", "Label", Text);
        return theme;
    }

    public static Color CardColor(CardColor color) => color switch
    {
        Core.CardColor.Red => Color.FromHtml("#ff6a3d"),
        Core.CardColor.Yellow => Color.FromHtml("#a070ff"),
        Core.CardColor.Green => Color.FromHtml("#36c47c"),
        Core.CardColor.Blue => Color.FromHtml("#22b4dc"),
        _ => Color.FromHtml("#e8c35a"),
    };

    public static Color TierColor(AugmentTier tier) => tier switch
    {
        AugmentTier.Silver => Silver,
        AugmentTier.Gold => GoldTier,
        _ => Prism,
    };

    public static StyleBoxFlat Box(Color bg, Color border, int borderWidth = 1, int radius = 6, int margin = 10)
    {
        var style = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        style.SetBorderWidthAll(borderWidth);
        style.SetCornerRadiusAll(radius);
        style.SetContentMarginAll(margin);
        return style;
    }

    /// <summary>역할에 맞는 버튼 스타일을 적용합니다.</summary>
    public static void StyleButton(Button button, ButtonKind kind, int fontSize = 15)
    {
        Color bg, hover, border, hoverBorder, fg, hoverFg;
        switch (kind)
        {
            case ButtonKind.Primary:
                bg = Gold;
                hover = Gold.Lightened(0.12f);
                border = hoverBorder = Colors.Transparent;
                fg = hoverFg = Color.FromHtml("#15171c");
                break;
            case ButtonKind.Ghost:
                bg = Colors.Transparent;
                hover = new Color(1, 1, 1, 0.06f);
                border = hoverBorder = Colors.Transparent;
                fg = TextDim;
                hoverFg = Text;
                break;
            case ButtonKind.Danger:
                bg = Colors.Transparent;
                hover = new Color(Danger, 0.14f);
                border = new Color(Danger, 0.45f);
                hoverBorder = Danger;
                fg = hoverFg = Danger.Lightened(0.15f);
                break;
            default:
                bg = PanelRaised;
                hover = PanelRaised.Lightened(0.08f);
                border = new Color(1, 1, 1, 0.08f);
                hoverBorder = new Color(1, 1, 1, 0.2f);
                fg = hoverFg = Text;
                break;
        }

        ApplyButton(button, bg, hover, border, hoverBorder, fg, hoverFg, fontSize);
    }

    /// <summary>색을 직접 정하는 버튼입니다. (문양 고르기 버튼처럼 색 자체가 정보인 곳에서만 씁니다.)</summary>
    public static void StyleButton(Button button, Color bg, Color fg, int fontSize = 16) =>
        ApplyButton(button, bg, bg.Lightened(0.1f), new Color(1, 1, 1, 0.06f), new Color(1, 1, 1, 0.25f), fg, fg, fontSize);

    private static void ApplyButton(Button button, Color bg, Color hover, Color border, Color hoverBorder, Color fg, Color hoverFg, int fontSize)
    {
        button.AddThemeStyleboxOverride("normal", ButtonBox(bg, border));
        button.AddThemeStyleboxOverride("hover", ButtonBox(hover, hoverBorder));
        button.AddThemeStyleboxOverride("pressed", ButtonBox(bg.Darkened(0.12f), hoverBorder));
        button.AddThemeStyleboxOverride("disabled", ButtonBox(new Color(bg, bg.A * 0.4f), new Color(border, border.A * 0.5f)));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeColorOverride("font_color", fg);
        button.AddThemeColorOverride("font_hover_color", hoverFg);
        button.AddThemeColorOverride("font_pressed_color", hoverFg);
        button.AddThemeColorOverride("font_hover_pressed_color", hoverFg);
        button.AddThemeColorOverride("font_disabled_color", new Color(fg, 0.35f));
        button.AddThemeFontOverride("font", Bold);
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.FocusMode = Control.FocusModeEnum.None;
        button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
    }

    private static StyleBoxFlat ButtonBox(Color bg, Color border)
    {
        var box = Box(bg, border, 1, 4, 8);
        box.ContentMarginLeft = box.ContentMarginRight = 16;
        return box;
    }

    /// <summary>모달 창(설정, 결과 등)의 패널 스타일입니다.</summary>
    public static StyleBoxFlat Modal(int margin = 28) => Box(Color.FromHtml("#12151b"), new Color(1, 1, 1, 0.08f), 1, 8, margin);

    /// <summary>작은 제목(구역 이름)입니다. 흐린 색 굵은 글씨로 씁니다.</summary>
    public static Label Caption(string text) => MakeLabel(text, 12, TextDim, bold: true);

    public static Label MakeLabel(string text, int size, Color color, bool bold = false)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        if (bold)
        {
            label.AddThemeFontOverride("font", Bold);
        }

        return label;
    }
}
