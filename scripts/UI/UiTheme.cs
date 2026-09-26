using System;
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
    public static readonly Color Panel = Color.FromHtml("#161a22f0");
    public static readonly Color PanelRaised = Color.FromHtml("#1b1f28");
    /// <summary>
    /// 패널 가장자리 색입니다. 밝은 선은 이미지가 잘린 것처럼 보여서, 배경보다 어두운 선과 그림자로 경계를 만듭니다.
    /// </summary>
    public static readonly Color PanelBorder = new(0f, 0f, 0f, 0.45f);
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
    private static Font? _title;

    /// <summary>본문 글꼴입니다. (Pretendard, OFL) 없으면 시스템 글꼴을 씁니다.</summary>
    public static Font Regular => _regular ??= LoadFont("res://assets/fonts/Pretendard-Regular.otf", 500);

    public static Font Bold => _bold ??= LoadFont("res://assets/fonts/Pretendard-Bold.otf", 700);

    /// <summary>제목·큰 숫자용 글꼴입니다. (Black Han Sans, OFL)</summary>
    public static Font Title => _title ??= LoadFont("res://assets/fonts/BlackHanSans-Regular.ttf", 800);

    /// <summary>
    /// 글꼴 파일을 불러옵니다. 글꼴에 없는 기호(화살표 등)는 시스템 글꼴로 이어서 그리도록 대체 글꼴을 붙입니다.
    /// </summary>
    private static Font LoadFont(string path, int fallbackWeight)
    {
        var system = MakeFont(fallbackWeight);
        if (ResourceLoader.Exists(path) && GD.Load<FontFile>(path) is { } file)
        {
            file.Fallbacks = new Godot.Collections.Array<Font> { system };
            return file;
        }

        return system;
    }

    /// <summary>한글이 확실히 나오도록 시스템 폰트(맑은 고딕 등)를 씁니다.</summary>
    private static Font MakeFont(int weight) => new SystemFont
    {
        FontNames = new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "NanumGothic", "sans-serif" },
        FontWeight = weight,
        Antialiasing = TextServer.FontAntialiasing.Gray,
    };

    /// <summary>에디터에서 고칠 수 있는 테마 리소스 경로입니다. (색·글꼴·버튼·패널 스타일이 모두 여기 들어 있습니다)</summary>
    public const string ThemePath = "res://assets/ui/ui_theme.tres";

    private static Theme? _theme;

    /// <summary>
    /// 게임 전체 테마를 불러옵니다. ui_theme.tres가 있으면 그것을 쓰고(에디터에서 수정한 값이 반영됩니다),
    /// 없으면 코드로 같은 테마를 만듭니다. 글꼴 대체(기호용 시스템 글꼴)는 불러온 뒤에 붙입니다.
    /// </summary>
    public static Theme LoadTheme()
    {
        if (_theme != null)
        {
            return _theme;
        }

        _theme = ResourceLoader.Exists(ThemePath) ? GD.Load<Theme>(ThemePath) : CreateThemeResource();
        foreach (var font in new[] { _theme.DefaultFont, Regular, Bold, Title })
        {
            if (font is FontFile file && file.Fallbacks.Count == 0)
            {
                file.Fallbacks = new Godot.Collections.Array<Font> { MakeFont(600) };
            }
        }

        return _theme;
    }

    /// <summary>
    /// 테마 리소스를 코드로 만듭니다. tools/gen_theme.tscn이 이 결과를 ui_theme.tres로 저장합니다.
    /// 여기서 정한 타입 변형(PrimaryButton, CaptionLabel, ModalPanel 등)을 씬의 theme_type_variation에 적어서 씁니다.
    /// </summary>
    public static Theme CreateThemeResource()
    {
        var theme = new Theme { DefaultFont = Regular, DefaultFontSize = 15 };

        // 글자
        theme.SetColor("font_color", "Label", Text);
        AddVariation(theme, "CaptionLabel", "Label");
        theme.SetFont("font", "CaptionLabel", Bold);
        theme.SetFontSize("font_size", "CaptionLabel", 12);
        theme.SetColor("font_color", "CaptionLabel", TextDim);
        AddVariation(theme, "DimLabel", "Label");
        theme.SetColor("font_color", "DimLabel", TextDim);
        theme.SetFontSize("font_size", "DimLabel", 13);
        AddVariation(theme, "TitleLabel", "Label");
        theme.SetFont("font", "TitleLabel", Title);
        theme.SetFontSize("font_size", "TitleLabel", 30);
        AddVariation(theme, "BoldLabel", "Label");
        theme.SetFont("font", "BoldLabel", Bold);

        // 버튼 (역할별)
        foreach (ButtonKind kind in Enum.GetValues(typeof(ButtonKind)))
        {
            var (bg, hover, border, hoverBorder, fg, hoverFg) = ButtonColors(kind);
            string type = $"{kind}Button";
            AddVariation(theme, type, "Button");
            SetButtonStyle(theme, type, bg, hover, border, hoverBorder, fg, hoverFg);
        }

        // 드롭다운(OptionButton)은 일반 버튼과 같은 모양입니다.
        var (b0, h0, bo0, hb0, f0, hf0) = ButtonColors(ButtonKind.Secondary);
        SetButtonStyle(theme, "OptionButton", b0, h0, bo0, hb0, f0, hf0);

        // 패널
        AddVariation(theme, "CardPanel", "PanelContainer");
        theme.SetStylebox("panel", "CardPanel", PanelBox(8, 10));
        AddVariation(theme, "ModalPanel", "PanelContainer");
        theme.SetStylebox("panel", "ModalPanel", Modal(26));
        AddVariation(theme, "SidePanel", "PanelContainer");
        var side = Box(new Color(0.043f, 0.05f, 0.066f, 0.9f), new Color(0, 0, 0, 0.5f), 0, 0, 0);
        side.BorderWidthRight = 1;
        side.ContentMarginLeft = side.ContentMarginRight = 56;
        side.ContentMarginTop = side.ContentMarginBottom = 48;
        theme.SetStylebox("panel", "SidePanel", side);
        AddVariation(theme, "RowPanel", "PanelContainer");
        theme.SetStylebox("panel", "RowPanel", Box(new Color(1, 1, 1, 0.045f), PanelBorder, 1, 6, 8));

        // 입력칸
        theme.SetStylebox("normal", "LineEdit", Box(Color.FromHtml("#0e1015"), new Color(0, 0, 0, 0.5f), 1, 4, 10));
        theme.SetStylebox("focus", "LineEdit", Box(Color.FromHtml("#0e1015"), Gold, 1, 4, 10));
        theme.SetStylebox("read_only", "LineEdit", Box(Color.FromHtml("#0e101599"), new Color(0, 0, 0, 0.3f), 1, 4, 10));
        theme.SetFontSize("font_size", "LineEdit", 15);

        // 켜기/끄기
        theme.SetFont("font", "CheckButton", Bold);
        theme.SetFontSize("font_size", "CheckButton", 14);
        theme.SetColor("font_color", "CheckButton", TextDim);
        theme.SetColor("font_hover_color", "CheckButton", Text);
        theme.SetColor("font_pressed_color", "CheckButton", Text);
        theme.SetColor("font_hover_pressed_color", "CheckButton", Text);
        theme.SetStylebox("focus", "CheckButton", new StyleBoxEmpty());

        // 구분선
        var line = new StyleBoxLine { Color = new Color(0, 0, 0, 0.35f), Thickness = 1 };
        theme.SetStylebox("separator", "HSeparator", line);
        theme.SetConstant("separation", "HSeparator", 12);
        return theme;
    }

    private static void AddVariation(Theme theme, string name, string baseType) => theme.SetTypeVariation(name, baseType);

    private static (Color Bg, Color Hover, Color Border, Color HoverBorder, Color Fg, Color HoverFg) ButtonColors(ButtonKind kind) => kind switch
    {
        ButtonKind.Primary => (Gold, Gold.Lightened(0.12f), Colors.Transparent, Colors.Transparent, Color.FromHtml("#15171c"), Color.FromHtml("#15171c")),
        ButtonKind.Ghost => (Colors.Transparent, new Color(1, 1, 1, 0.06f), Colors.Transparent, Colors.Transparent, TextDim, Text),
        ButtonKind.Danger => (Colors.Transparent, new Color(Danger, 0.14f), new Color(Danger, 0.45f), Danger, Danger.Lightened(0.15f), Danger.Lightened(0.15f)),
        _ => (PanelRaised, PanelRaised.Lightened(0.08f), new Color(0, 0, 0, 0.4f), new Color(0, 0, 0, 0.5f), Text, Text),
    };

    private static void SetButtonStyle(Theme theme, string type, Color bg, Color hover, Color border, Color hoverBorder, Color fg, Color hoverFg)
    {
        theme.SetStylebox("normal", type, ButtonBox(bg, border));
        theme.SetStylebox("hover", type, ButtonBox(hover, hoverBorder));
        theme.SetStylebox("pressed", type, ButtonBox(bg.Darkened(0.12f), hoverBorder));
        theme.SetStylebox("hover_pressed", type, ButtonBox(hover.Darkened(0.06f), hoverBorder));
        theme.SetStylebox("disabled", type, ButtonBox(new Color(bg, bg.A * 0.4f), new Color(border, border.A * 0.5f)));
        theme.SetStylebox("focus", type, new StyleBoxEmpty());
        theme.SetColor("font_color", type, fg);
        theme.SetColor("font_hover_color", type, hoverFg);
        theme.SetColor("font_pressed_color", type, hoverFg);
        theme.SetColor("font_hover_pressed_color", type, hoverFg);
        theme.SetColor("font_focus_color", type, fg);
        theme.SetColor("font_disabled_color", type, new Color(fg, 0.35f));
        theme.SetFont("font", type, Bold);
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

    /// <summary>
    /// 역할에 맞는 버튼 스타일을 적용합니다. 색은 테마(ui_theme.tres)의 타입 변형에서 오고, 여기서는 변형 이름과 글자 크기만 정합니다.
    /// </summary>
    public static void StyleButton(Button button, ButtonKind kind, int fontSize = 15)
    {
        button.ThemeTypeVariation = $"{kind}Button";
        if (fontSize != 15)
        {
            button.AddThemeFontSizeOverride("font_size", fontSize);
        }

        button.FocusMode = Control.FocusModeEnum.None;
        button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
    }

    /// <summary>색을 직접 정하는 버튼입니다. (문양 고르기 버튼처럼 색 자체가 정보인 곳에서만 씁니다.)</summary>
    public static void StyleButton(Button button, Color bg, Color fg, int fontSize = 16) =>
        ApplyButton(button, bg, bg.Lightened(0.1f), new Color(0, 0, 0, 0.4f), new Color(0, 0, 0, 0.5f), fg, fg, fontSize);

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
    /// <summary>모달 창(설정, 결과 등)의 패널입니다. 장식 테두리를 씁니다.</summary>
    public static StyleBox Modal(int margin = 28) => Ornate(Color.FromHtml("#171b23f5"), new Color(Gold, 0.6f), margin);

    /// <summary>패널 스타일입니다. 어두운 가장자리 + 부드러운 그림자로 배경과 구분합니다.</summary>
    public static StyleBoxFlat PanelBox(int radius = 8, int margin = 10) => Shadowed(Box(Panel, PanelBorder, 1, radius, margin), 10);

    /// <summary>스타일 박스 아래에 부드러운 그림자를 깝니다.</summary>
    public static StyleBoxFlat Shadowed(StyleBoxFlat box, int size)
    {
        box.ShadowColor = new Color(0, 0, 0, 0.35f);
        box.ShadowSize = size;
        box.ShadowOffset = new Vector2(0, size / 4f);
        return box;
    }

    /// <summary>제목 글꼴(Black Han Sans)로 된 라벨입니다.</summary>
    public static Label MakeTitle(string text, int size, Color color)
    {
        var label = MakeLabel(text, size, color);
        label.ThemeTypeVariation = "TitleLabel";
        return label;
    }

    private static readonly System.Collections.Generic.Dictionary<string, Texture2D?> OrnateCache = new();

    /// <summary>
    /// 장식 테두리 패널입니다. (Kenney Fantasy UI Borders, CC0) 안쪽은 bg 색으로 채우고 테두리 무늬는 accent 색으로 칠합니다.
    /// 모서리 16px은 늘리지 않고 그대로 그려서 무늬가 뭉개지지 않습니다. 이미지가 없으면 평범한 패널로 대신합니다.
    /// </summary>
    public static StyleBox Ornate(Color bg, Color accent, int margin = 24, int pattern = 7)
    {
        var texture = OrnateTexture(bg, accent, pattern);
        if (texture == null)
        {
            return Shadowed(Box(bg, accent, 1, 8, margin), 16);
        }

        var box = new StyleBoxTexture { Texture = texture };
        box.SetTextureMarginAll(16);
        box.SetContentMarginAll(margin);
        return box;
    }

    private static Texture2D? OrnateTexture(Color bg, Color accent, int pattern)
    {
        string key = $"{bg.ToHtml()}_{accent.ToHtml()}_{pattern}";
        if (OrnateCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        string borderPath = $"res://assets/ui/panel-border-{pattern:000}.png";
        string fillPath = $"res://assets/ui/panel-{pattern:000}.png";
        Texture2D? result = null;
        if (ResourceLoader.Exists(borderPath) && ResourceLoader.Exists(fillPath))
        {
            var border = ReadImage(borderPath);
            var fill = ReadImage(fillPath);
            int w = border.GetWidth();
            int h = border.GetHeight();
            var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // 채움 모양 안쪽은 배경색, 그 위에 테두리 무늬를 강조색으로 덮습니다.
                    float fa = fill.GetPixel(x, y).A;
                    float ba = border.GetPixel(x, y).A;
                    var color = new Color(bg, bg.A * fa);
                    color = color.Blend(new Color(accent, accent.A * ba));
                    image.SetPixel(x, y, color);
                }
            }

            result = ImageTexture.CreateFromImage(image);
        }

        OrnateCache[key] = result;
        return result;
    }

    private static Image ReadImage(string path)
    {
        var image = GD.Load<Texture2D>(path).GetImage();
        if (image.IsCompressed())
        {
            image.Decompress();
        }

        image.Convert(Image.Format.Rgba8);
        return image;
    }

    /// <summary>작은 제목(구역 이름)입니다. 흐린 색 굵은 글씨로 씁니다.</summary>
    public static Label Caption(string text) => new() { Text = text, ThemeTypeVariation = "CaptionLabel", MouseFilter = Control.MouseFilterEnum.Ignore };

    public static Label MakeLabel(string text, int size, Color color, bool bold = false)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        if (bold)
        {
            label.ThemeTypeVariation = "BoldLabel";
        }

        return label;
    }
}
