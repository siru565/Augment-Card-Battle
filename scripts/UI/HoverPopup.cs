using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 마우스를 올린 카드나 증강의 설명을 마우스 옆에 띄우는 팝업입니다.
/// 기본 툴팁과 달리 지연 없이 바로 나오고, 마우스를 따라다닙니다.
/// </summary>
public partial class HoverPopup : PanelContainer
{
    public static HoverPopup? Instance { get; private set; }

    private Label _title = null!;
    private Label _subtitle = null!;
    private Label _body = null!;
    private Label _footer = null!;
    private Control? _owner;

    /// <summary>개발용 스크린샷에서 실제 마우스 대신 쓸 위치입니다. 평소에는 null입니다.</summary>
    public Vector2? DebugMouseOverride { get; set; }

    public override void _Ready()
    {
        Instance = this;
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 100;
        CustomMinimumSize = new Vector2(320, 0);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        AddChild(box);

        _title = UiTheme.MakeLabel("", 20, Colors.White, bold: true);
        _subtitle = UiTheme.MakeLabel("", 13, UiTheme.TextDim);
        _body = UiTheme.MakeLabel("", 15, Colors.White);
        _body.AutowrapMode = TextServer.AutowrapMode.Word;
        _body.CustomMinimumSize = new Vector2(300, 0);
        _footer = UiTheme.MakeLabel("", 13, UiTheme.Gold);
        _footer.AutowrapMode = TextServer.AutowrapMode.Word;
        _footer.CustomMinimumSize = new Vector2(300, 0);

        box.AddChild(_title);
        box.AddChild(_subtitle);
        box.AddChild(new HSeparator { MouseFilter = MouseFilterEnum.Ignore });
        box.AddChild(_body);
        box.AddChild(_footer);
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        // 띄운 컨트롤이 사라졌으면 팝업도 닫습니다.
        if (_owner == null || !IsInstanceValid(_owner) || !_owner.IsVisibleInTree())
        {
            Hide();
            return;
        }

        // 마우스를 올린 대상의 바로 위에 띄웁니다. 위쪽 공간이 부족하면 아래에 띄웁니다.
        var viewport = GetViewportRect().Size;
        var size = Size;
        var target = _owner.GetGlobalRect();
        var pos = new Vector2(target.GetCenter().X - size.X / 2, target.Position.Y - size.Y - 12);
        if (pos.Y < 8)
        {
            pos.Y = target.End.Y + 12;
        }

        pos.X = Mathf.Clamp(pos.X, 8, Mathf.Max(8, viewport.X - size.X - 8));
        pos.Y = Mathf.Clamp(pos.Y, 8, Mathf.Max(8, viewport.Y - size.Y - 8));
        GlobalPosition = pos;
    }

    /// <summary>팝업 내용을 채우고 보여 줍니다.</summary>
    public void ShowFor(Control owner, string title, string subtitle, string body, string footer, Color accent)
    {
        _owner = owner;
        _title.Text = title;
        _title.AddThemeColorOverride("font_color", accent);
        _subtitle.Text = subtitle;
        _body.Text = body;
        _footer.Text = footer;
        _footer.Visible = !string.IsNullOrEmpty(footer);
        AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.055f, 0.063f, 0.082f, 0.97f), new Color(accent, 0.5f), 1, 6, 14));
        ResetSize();
        Visible = true;
        _Process(0);
    }

    public void HideFor(Control owner)
    {
        if (_owner == owner)
        {
            Hide();
        }
    }

    // ───────────── 내용 만들기 헬퍼 ─────────────

    public static void ShowCard(Control owner, Card card, string extra = "")
    {
        string subtitle = card.Kind switch
        {
            CardKind.Number => $"숫자 카드 · {Card.ColorName(card.Color)}",
            CardKind.Wild or CardKind.WildDrawFour => "와일드 카드",
            _ when card.IsSpecial => $"특수 카드 · {Card.ColorName(card.Color)}",
            _ => $"액션 카드 · {Card.ColorName(card.Color)}",
        };

        var accent = card.Color == CardColor.Wild ? UiTheme.Gold : UiTheme.CardColor(card.Color).Lightened(0.25f);
        Instance?.ShowFor(owner, card.ToString(), subtitle, Card.Describe(card.Kind), extra, accent);
    }

    public static void ShowAugment(Control owner, AugmentInfo augment)
    {
        string cooldown = augment.Cooldown > 0 ? $"쿨타임: 내 턴 {augment.Cooldown}번 남음" : "";
        Instance?.ShowFor(owner, augment.Name, $"증강 · {Augment.TierName(augment.Tier)}", augment.Description,
            cooldown, UiTheme.TierColor(augment.Tier));
    }

    public static void HideOwner(Control owner) => Instance?.HideFor(owner);
}
