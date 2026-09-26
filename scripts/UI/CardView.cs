using System;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 카드 한 장을 직접 그리는 컨트롤입니다.
/// 문양 색 프레임 + 가운데 마름모 문장 + 옅은 문양 배경으로 그립니다. (이미지 없이 벡터로만 그립니다.)
/// </summary>
public partial class CardView : Control
{
    private Card? _card;
    private bool _faceDown;
    private bool _playable;
    private bool _dimmed;
    private bool _selected;
    private bool _justDrawn;
    private bool _hovered;
    private float _lift;
    private float _pulse;

    /// <summary>프리즘 홀로그램, 특수 카드 광택 애니메이션 시간입니다.</summary>
    private float _shine = (float)GD.RandRange(0.0, 10.0);

    private string _badge = "";
    private Color _badgeColor = Colors.White;
    private bool _alert;

    /// <summary>카드 위쪽에 붙는 작은 표시입니다. (어벤져스 증강의 직업 이름)</summary>
    public string Badge
    {
        get => _badge;
        set { _badge = value; QueueRedraw(); }
    }

    public Color BadgeColor
    {
        get => _badgeColor;
        set { _badgeColor = value; QueueRedraw(); }
    }

    /// <summary>빨갛게 깜빡이며 눌러 달라고 알립니다. (억지로 뽑아야 할 때 덱에 씁니다)</summary>
    public bool Alert
    {
        get => _alert;
        set { _alert = value; QueueRedraw(); }
    }

    /// <summary>카드를 클릭했을 때 호출합니다.</summary>
    public event Action<CardView>? Clicked;

    /// <summary>팝업 아래쪽에 덧붙일 설명입니다. (복사 대상 같은 상황 정보)</summary>
    public string HoverExtra { get; set; } = "";

    /// <summary>마우스를 올리면 떠오르는지 정합니다. 손패에서만 켭니다.</summary>
    public bool LiftOnHover { get; set; }

    /// <summary>현재 떠오른 높이입니다. HandView가 위치를 잡을 때 사용합니다.</summary>
    public float Lift => _lift;

    public bool IsHovered => _hovered;

    public Card? Card
    {
        get => _card;
        set { _card = value; QueueRedraw(); }
    }

    public bool FaceDown
    {
        get => _faceDown;
        set { _faceDown = value; QueueRedraw(); }
    }

    public bool Playable
    {
        get => _playable;
        set
        {
            _playable = value;
            MouseDefaultCursorShape = value ? CursorShape.PointingHand : CursorShape.Arrow;
            QueueRedraw();
        }
    }

    public bool Dimmed
    {
        get => _dimmed;
        set { _dimmed = value; QueueRedraw(); }
    }

    public bool Selected
    {
        get => _selected;
        set { _selected = value; QueueRedraw(); }
    }

    public bool JustDrawn
    {
        get => _justDrawn;
        set { _justDrawn = value; QueueRedraw(); }
    }

    public CardView()
    {
        CustomMinimumSize = new Vector2(92, 132);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready()
    {
        MouseEntered += OnMouseEntered;
        MouseExited += OnMouseExited;
    }

    public override void _Process(double delta)
    {
        float target = LiftOnHover && (_hovered || _selected) ? 26f : 0f;
        _lift = Mathf.Lerp(_lift, target, (float)Math.Min(1.0, delta * 14));

        if (_playable || _selected || _justDrawn || _alert)
        {
            _pulse += (float)delta * 3.2f;
            QueueRedraw();
        }

        // 프리즘·특수 카드는 앞면일 때 계속 빛이 움직입니다.
        if (IsShiny && IsVisibleInTree())
        {
            _shine += (float)delta;
            QueueRedraw();
        }
    }

    /// <summary>떠오른 만큼 아래쪽 판정 영역을 늘려서, 떠오를 때 마우스가 빠져 깜빡이는 현상을 막습니다.</summary>
    public override bool _HasPoint(Vector2 point) =>
        new Rect2(Vector2.Zero, Size + new Vector2(0, _lift)).HasPoint(point);

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Clicked?.Invoke(this);
            AcceptEvent();
        }
    }

    private void OnMouseEntered()
    {
        _hovered = true;
        if (LiftOnHover)
        {
            Audio.Sfx.Play("hover", -14f, 1f, 0.1f, 40);
        }

        if (_card != null && !_faceDown)
        {
            HoverPopup.ShowCard(this, _card, HoverExtra);
        }

        QueueRedraw();
    }

    private void OnMouseExited()
    {
        _hovered = false;
        HoverPopup.HideOwner(this);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        var rect = new Rect2(Vector2.Zero, size);
        float radius = size.X * 0.11f;

        // 그림자입니다.
        var shadow = UiTheme.Box(new Color(0, 0, 0, 0.35f), new Color(0, 0, 0, 0), 0, (int)radius, 0);
        DrawStyleBox(shadow, new Rect2(new Vector2(3, 5), size));

        if (_faceDown || _card == null)
        {
            DrawBack(rect, radius);
            DrawHighlight(rect, radius);
            return;
        }

        if (_card.IsSpecial)
        {
            DrawSpecialGlow(rect, radius);
        }

        DrawFace(rect, radius, _card);
        if (_card.Color == CardColor.Wild)
        {
            DrawHolo(rect, radius);
        }
        else if (_card.IsSpecial)
        {
            DrawGloss(rect, 3.4f, 0.22f);
        }

        DrawBadge(rect);
        DrawHighlight(rect, radius);

        if (_dimmed)
        {
            var dim = UiTheme.Box(new Color(0.02f, 0.03f, 0.05f, 0.45f), new Color(0, 0, 0, 0), 0, (int)radius, 0);
            DrawStyleBox(dim, rect);
        }
    }

    private bool IsShiny => _card != null && !_faceDown && (_card.Color == CardColor.Wild || _card.IsSpecial);

    /// <summary>특수 카드 둘레에 은은하게 숨 쉬는 금빛을 그립니다.</summary>
    private void DrawSpecialGlow(Rect2 rect, float radius)
    {
        float breathe = 0.6f + 0.4f * Mathf.Sin(_shine * 2.4f);
        for (int i = 1; i <= 4; i++)
        {
            float a = 0.3f * breathe * (1f - i / 5f);
            DrawStyleBox(UiTheme.Box(new Color(0, 0, 0, 0), new Color(UiTheme.Gold, a), 2, (int)radius + i * 2, 0), rect.Grow(i * 2));
        }
    }

    /// <summary>카드 앞면 안쪽 판의 네 꼭짓점입니다. 빛 효과를 이 안으로 잘라 그립니다.</summary>
    private Vector2[] FacePolygon(Rect2 rect)
    {
        var inner = rect.Grow(-rect.Size.X * 0.06f);
        return new[] { inner.Position, new Vector2(inner.End.X, inner.Position.Y), inner.End, new Vector2(inner.Position.X, inner.End.Y) };
    }

    /// <summary>
    /// 대각선 띠 하나를 카드 안쪽으로 잘라 그립니다. from~to는 대각선 방향으로 잰 위치(0~1)입니다.
    /// </summary>
    private void DrawDiagonalBand(Vector2[] face, Rect2 rect, Vector2 dir, float from, float to, Color color)
    {
        if (color.A <= 0.004f)
        {
            return;
        }

        // 대각선 방향으로 카드가 차지하는 길이입니다.
        float length = Mathf.Abs(rect.Size.X * dir.X) + Mathf.Abs(rect.Size.Y * dir.Y);
        var start = rect.GetCenter() - dir * length / 2;
        var normal = new Vector2(-dir.Y, dir.X) * rect.Size.Length();
        var a = start + dir * (from * length);
        var b = start + dir * (to * length);
        var band = new[] { a - normal, b - normal, b + normal, a + normal };
        foreach (var piece in Geometry2D.IntersectPolygons(band, face))
        {
            // 잘린 조각이 너무 얇으면(넓이가 거의 0) 삼각형으로 나눌 수 없어서 오류가 나므로 건너뜁니다.
            if (piece.Length >= 3 && PolygonArea(piece) > 0.5f)
            {
                DrawColoredPolygon(piece, color);
            }
        }
    }

    private static float PolygonArea(Vector2[] points)
    {
        float sum = 0f;
        for (int i = 0; i < points.Length; i++)
        {
            var p = points[i];
            var q = points[(i + 1) % points.Length];
            sum += p.X * q.Y - q.X * p.Y;
        }

        return Mathf.Abs(sum) / 2f;
    }

    /// <summary>주기적으로 카드 위를 한 번 쓸고 지나가는 흰 광택입니다.</summary>
    private void DrawGloss(Rect2 rect, float period, float strength)
    {
        float phase = _shine % period / 1.1f;
        if (phase > 1f)
        {
            return;
        }

        var face = FacePolygon(rect);
        var dir = new Vector2(1f, 0.7f).Normalized();
        float center = -0.2f + phase * 1.4f;
        for (int i = -3; i <= 3; i++)
        {
            float a = strength * Mathf.Exp(-i * i / 3f);
            DrawDiagonalBand(face, rect, dir, center + i * 0.025f, center + (i + 1) * 0.025f, new Color(1, 1, 1, a));
        }
    }

    /// <summary>
    /// 프리즘 카드의 홀로그램입니다. 무지개 띠가 천천히 흐르고, 마우스를 올리면 마우스 위치를 따라 색이 움직이며,
    /// 밝은 빛줄기가 지나가고 작은 별빛이 반짝입니다. (레어 카드 느낌)
    /// </summary>
    private void DrawHolo(Rect2 rect, float radius)
    {
        var face = FacePolygon(rect);
        var dir = new Vector2(1f, 0.55f).Normalized();

        float mouse = 0f;
        if (_hovered)
        {
            var m = GetLocalMousePosition() / Size;
            mouse = (m.X + m.Y) * 0.6f;
        }

        // 무지개 띠
        const int strips = 16;
        for (int i = 0; i < strips; i++)
        {
            float hue = Mathf.PosMod(i / (float)strips * 1.4f + _shine * 0.12f + mouse, 1f);
            DrawDiagonalBand(face, rect, dir, i / (float)strips, (i + 1) / (float)strips + 0.002f,
                Color.FromHsv(hue, 0.6f, 1f, _hovered ? 0.34f : 0.25f));
        }

        // 지나가는 빛줄기
        float sweep = Mathf.PosMod(_shine * 0.35f + mouse, 1.6f) - 0.3f;
        for (int i = -4; i <= 4; i++)
        {
            float a = 0.42f * Mathf.Exp(-i * i / 5f);
            DrawDiagonalBand(face, rect, dir, sweep + i * 0.02f, sweep + (i + 1) * 0.02f, new Color(1, 1, 1, a));
        }

        // 반짝이는 별빛 (카드마다 위치가 다릅니다)
        var rng = new RandomNumberGenerator { Seed = (ulong)(Math.Abs(_card?.Id ?? 1) * 7919 + 13) };
        var inner = rect.Grow(-rect.Size.X * 0.12f);
        for (int i = 0; i < 7; i++)
        {
            var p = inner.Position + new Vector2(rng.Randf() * inner.Size.X, rng.Randf() * inner.Size.Y);
            float phase = rng.Randf() * Mathf.Tau;
            float twinkle = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(_shine * 2.2f + phase)), 4f);
            if (twinkle < 0.02f)
            {
                continue;
            }

            float r = rect.Size.X * 0.05f * (0.6f + 0.4f * twinkle);
            var c = new Color(1, 1, 1, 0.9f * twinkle);
            DrawColoredPolygon(new[] { p + new Vector2(0, -r), p + new Vector2(r * 0.22f, 0), p + new Vector2(0, r), p + new Vector2(-r * 0.22f, 0) }, c);
            DrawColoredPolygon(new[] { p + new Vector2(-r, 0), p + new Vector2(0, -r * 0.22f), p + new Vector2(r, 0), p + new Vector2(0, r * 0.22f) }, c);
        }

        // 테두리에도 무지개빛이 돕니다.
        var edge = Color.FromHsv(Mathf.PosMod(_shine * 0.2f + mouse, 1f), 0.5f, 1f, 0.8f);
        DrawStyleBox(UiTheme.Box(new Color(0, 0, 0, 0), edge, 2, (int)radius, 0), rect);
    }

    /// <summary>강조 테두리입니다. 낼 수 있는 카드는 은은하게, 억지 뽑기 덱은 빨갛게 깜빡입니다.</summary>
    private void DrawHighlight(Rect2 rect, float radius)
    {
        if (_alert)
        {
            float a = 0.55f + 0.45f * Mathf.Sin(_pulse * 2.2f);
            DrawStyleBox(UiTheme.Box(new Color(0, 0, 0, 0), new Color(UiTheme.Danger, a), 3, (int)radius + 4, 0), rect.Grow(4));
            DrawStyleBox(UiTheme.Box(new Color(1f, 0.2f, 0.1f, 0.12f * a), new Color(0, 0, 0, 0), 0, (int)radius, 0), rect);
            return;
        }

        Color? glow = _selected ? UiTheme.Gold
            : _justDrawn ? Color.FromHtml("#8ec5ff")
            : _playable ? new Color(UiTheme.Gold, 0.55f + 0.25f * Mathf.Sin(_pulse))
            : null;

        if (glow.HasValue)
        {
            var glowBox = UiTheme.Box(new Color(0, 0, 0, 0), glow.Value, _selected ? 3 : 2, (int)radius + 3, 0);
            DrawStyleBox(glowBox, rect.Grow(3));
        }
    }

    /// <summary>카드 오른쪽 위에 직업 같은 작은 표시를 그립니다.</summary>
    private void DrawBadge(Rect2 rect)
    {
        if (string.IsNullOrEmpty(_badge))
        {
            return;
        }

        var font = UiTheme.Bold;
        int fontSize = (int)(rect.Size.X * 0.13f);
        var textSize = font.GetStringSize(_badge, HorizontalAlignment.Left, -1, fontSize);
        var box = new Rect2(rect.End.X - textSize.X - 14, rect.Position.Y + 4, textSize.X + 10, fontSize + 8);
        DrawStyleBox(UiTheme.Box(new Color(0.04f, 0.045f, 0.06f, 0.92f), _badgeColor, 1, 4, 0), box);
        DrawString(font, new Vector2(box.Position.X + 5, box.Position.Y + 4 + font.GetAscent(fontSize)), _badge,
            HorizontalAlignment.Left, -1, fontSize, _badgeColor);
    }

    /// <summary>
    /// 카드 앞면입니다. 문양 색 프레임 + 짙은 안쪽 판 + 가운데 마름모 문장(紋章) 구조입니다.
    /// </summary>
    private void DrawFace(Rect2 rect, float radius, Card card)
    {
        var size = rect.Size;
        bool isPrism = card.Color == CardColor.Wild;
        var suitColor = isPrism ? UiTheme.Gold : UiTheme.CardColor(card.Color);
        var deep = isPrism ? Color.FromHtml("#1b1532") : suitColor.Darkened(0.68f);
        var font = UiTheme.Title;

        // 프레임과 안쪽 판입니다. 특수 카드는 프레임이 금색입니다.
        var frame = card.IsSpecial ? UiTheme.Gold : suitColor;
        DrawStyleBox(UiTheme.Box(frame, frame, 0, (int)radius, 0), rect);
        var inner = rect.Grow(-size.X * 0.055f);
        DrawStyleBox(UiTheme.Box(deep, card.IsSpecial ? suitColor : deep.Lightened(0.15f), 2, (int)(radius * 0.7f), 0), inner);

        // 위쪽을 살짝 밝게 해서 입체감을 줍니다.
        var shine = new Rect2(inner.Position, new Vector2(inner.Size.X, inner.Size.Y * 0.45f));
        DrawStyleBox(UiTheme.Box(new Color(1, 1, 1, 0.05f), new Color(0, 0, 0, 0), 0, (int)(radius * 0.7f), 0), shine);

        var center = size / 2;

        // 배경에 큰 문양을 옅게 깝니다.
        if (isPrism)
        {
            var suits = new[] { CardColor.Red, CardColor.Yellow, CardColor.Green, CardColor.Blue };
            var offsets = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) };
            for (int i = 0; i < 4; i++)
            {
                SuitIcons.Draw(this, suits[i], center + offsets[i] * size.X * 0.22f, size.X * 0.3f,
                    UiTheme.CardColor(suits[i]) with { A = 0.35f }, deep);
            }
        }
        else
        {
            SuitIcons.Draw(this, card.Color, center, size.X * 0.72f, suitColor with { A = 0.13f }, deep);
        }

        // 가운데 마름모 문장입니다.
        float d = size.X * 0.34f;
        var diamond = new[]
        {
            center + new Vector2(0, -d * 1.18f), center + new Vector2(d, 0),
            center + new Vector2(0, d * 1.18f), center + new Vector2(-d, 0),
        };
        if (isPrism)
        {
            var suits = new[] { CardColor.Red, CardColor.Yellow, CardColor.Blue, CardColor.Green };
            for (int i = 0; i < 4; i++)
            {
                DrawColoredPolygon(new[] { center, diamond[i], diamond[(i + 1) % 4] }, UiTheme.CardColor(suits[i]).Darkened(0.1f));
            }
        }
        else
        {
            DrawColoredPolygon(diamond, suitColor.Darkened(0.25f));
        }

        DrawPolyline(new[] { diamond[0], diamond[1], diamond[2], diamond[3], diamond[0] },
            isPrism ? UiTheme.Gold : suitColor.Lightened(0.45f), 2.5f, true);

        // 가운데 값입니다. 글자가 길면 마름모 안에 들어가도록 줄입니다.
        string label = card.ShortLabel;
        bool isNumber = card.Kind == CardKind.Number;
        int bigSize = (int)(size.X * (isNumber ? 0.42f : 0.24f));
        float maxWidth = d * 1.55f;
        while (bigSize > 8 && font.GetStringSize(label, HorizontalAlignment.Left, -1, bigSize).X > maxWidth)
        {
            bigSize--;
        }

        float ascent = font.GetAscent(bigSize);
        float descent = font.GetDescent(bigSize);
        var textPos = new Vector2(0, center.Y + (ascent - descent) / 2);
        DrawStringOutline(font, textPos, label, HorizontalAlignment.Center, size.X, bigSize, (int)(bigSize * 0.14f) + 3,
            new Color(0, 0, 0, 0.75f));
        DrawString(font, textPos, label, HorizontalAlignment.Center, size.X, bigSize, Colors.White);

        // 왼쪽 위에는 작은 문양과 값을 둡니다. 손패가 겹쳐도 이 부분은 보입니다.
        int cornerSize = (int)(size.X * (isNumber ? 0.19f : 0.13f));
        var cornerPos = new Vector2(size.X * 0.1f, size.Y * 0.05f + font.GetAscent(cornerSize));
        DrawString(font, cornerPos, label, HorizontalAlignment.Left, -1, cornerSize, isPrism ? UiTheme.Gold : suitColor.Lightened(0.35f));
        var iconCenter = new Vector2(size.X * 0.1f + size.X * 0.08f, cornerPos.Y + size.X * 0.13f);
        if (isPrism)
        {
            SuitIcons.DrawStar(this, iconCenter, size.X * 0.16f, UiTheme.Gold);
        }
        else
        {
            SuitIcons.Draw(this, card.Color, iconCenter, size.X * 0.16f, suitColor.Lightened(0.2f), deep);
        }
    }

    /// <summary>
    /// 카드 뒷면입니다. 금색 테두리 + 남색 판 + 가장자리 마름모 띠 + 육각 엠블럼입니다.
    /// </summary>
    private void DrawBack(Rect2 rect, float radius)
    {
        var size = rect.Size;
        var navy = Color.FromHtml("#161633");
        var gold = Color.FromHtml("#c9a24a");
        DrawStyleBox(UiTheme.Box(gold, gold, 0, (int)radius, 0), rect);
        var inner = rect.Grow(-size.X * 0.055f);
        DrawStyleBox(UiTheme.Box(navy, navy, 0, (int)(radius * 0.7f), 0), inner);

        // 테두리를 따라 한 줄로 도는 작은 마름모 띠입니다. 가로 5칸에 맞춰 칸 크기를 정하고, 세로는 들어가는 만큼만 넣은 뒤
        // 남는 공간을 위아래로 똑같이 나눠서 좌우·상하 대칭이 되게 합니다. 가운데 엠블럼과는 겹치지 않습니다.
        var center = size / 2;
        float r = size.X * 0.26f;
        var line = new Color(gold, 0.28f);
        var area = inner.Grow(-size.X * 0.05f);
        const int cols = 5;
        float step = area.Size.X / cols;
        int rows = Mathf.FloorToInt(area.Size.Y / step);
        float top = area.Position.Y + (area.Size.Y - rows * step) / 2;
        float half = step * 0.34f;
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                bool edge = row == 0 || row == rows - 1 || col == 0 || col == cols - 1;
                if (!edge)
                {
                    continue;
                }

                var c = new Vector2(area.Position.X + (col + 0.5f) * step, top + (row + 0.5f) * step);
                DrawPolyline(new[] { c + new Vector2(0, -half), c + new Vector2(half, 0), c + new Vector2(0, half),
                    c + new Vector2(-half, 0), c + new Vector2(0, -half) }, line, 1f, true);
            }
        }

        DrawStyleBox(UiTheme.Box(new Color(0, 0, 0, 0), gold, 2, (int)(radius * 0.7f), 0), inner);

        // 가운데 육각 엠블럼입니다.
        var hex = new Vector2[7];
        for (int i = 0; i < 6; i++)
        {
            float a = -Mathf.Pi / 2 + i * Mathf.Tau / 6;
            hex[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        hex[6] = hex[0];
        DrawColoredPolygon(hex[..6], navy.Lightened(0.08f));
        DrawPolyline(hex, gold, 2.5f, true);
        SuitIcons.DrawStar(this, center, r * 1.1f, new Color(gold, 0.9f));
    }
}
