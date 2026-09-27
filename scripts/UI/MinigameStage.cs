using System;
using System.Collections.Generic;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 미니게임을 그리는 판입니다. (MinigameOverlay가 씁니다)
/// - 컬링: 위에서 내려다본 얼음판. 내 차례면 드래그로 당겼다 놓아서 카드 스톤을 튕깁니다. (새총처럼 당긴 반대 방향으로 나갑니다)
/// - 정밀 사수: 좌우로 아주 빠르게 왕복하는 바늘. 클릭이나 Space로 멈춥니다.
/// 다른 사람의 미니게임도 똑같이 그려서 구경할 수 있습니다. (조작만 막힙니다)
/// </summary>
public partial class MinigameStage : Control
{
    /// <summary>던지기(컬링: 조준, 힘)나 멈추기(정밀 사수: 멈춘 시각)를 했을 때 호출합니다.</summary>
    public event Action<float, float>? Committed;

    private MinigameInfo? _game;
    private bool _interactive;
    private bool _committed;

    // 컬링
    private const float MaxPull = 230f;
    private bool _dragging;
    private Vector2 _dragStart;
    private Vector2 _dragNow;
    private readonly List<(float X, float Y)> _path = new();
    private float _pathTime = -1f;
    private CurlingSim.Outcome? _curlingOutcome;

    // 정밀 사수
    private ulong _startUsec;
    private float? _stoppedAt;
    private bool? _hit;

    private float _t;

    /// <summary>얼음판은 셰이더로 그려서 원과 선의 가장자리가 계단 없이 매끄럽습니다. (부모 그림 뒤에 깔립니다)</summary>
    private ColorRect _ice = null!;
    private ShaderMaterial _iceMaterial = null!;

    public MinigameInfo? Game => _game;

    public override void _Ready()
    {
        _iceMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/curling_ice.gdshader") };
        _ice = new ColorRect
        {
            Material = _iceMaterial,
            MouseFilter = MouseFilterEnum.Ignore,
            ShowBehindParent = true,
            Visible = false,
        };
        AddChild(_ice);
    }

    /// <summary>얼음판 위치와 하우스 위치를 셰이더에 알려 줍니다.</summary>
    private void UpdateIce()
    {
        bool curling = _game?.Kind == MinigameKind.Curling;
        _ice.Visible = curling;
        if (!curling)
        {
            return;
        }

        var sheet = SheetRect;
        _ice.Position = sheet.Position;
        _ice.Size = sheet.Size;
        float s = SheetScale;
        _iceMaterial.SetShaderParameter("rect_size", sheet.Size);
        _iceMaterial.SetShaderParameter("house_center", ToScreen(_game!.A, CurlingSim.HouseY) - sheet.Position);
        _iceMaterial.SetShaderParameter("house_radius", CurlingSim.HouseRadius * s);
        _iceMaterial.SetShaderParameter("button_radius", CurlingSim.ButtonRadius * s);
        _iceMaterial.SetShaderParameter("back_line_y", ToScreen(0, CurlingSim.BackLine).Y - sheet.Position.Y);
    }

    /// <summary>결과 연출이 끝났는지 알려 줍니다. (컬링 스톤이 멈췄는지)</summary>
    public bool AnimationDone => _game?.Kind != MinigameKind.Curling || _pathTime < 0 || _pathTime * 30f >= _path.Count;

    public void Begin(MinigameInfo game, bool interactive)
    {
        _game = game;
        _interactive = interactive;
        _committed = false;
        _dragging = false;
        _path.Clear();
        _pathTime = -1f;
        _curlingOutcome = null;
        _stoppedAt = null;
        _hit = null;
        _startUsec = Time.GetTicksUsec();
        MouseDefaultCursorShape = interactive ? CursorShape.PointingHand : CursorShape.Arrow;
        UpdateIce();
        QueueRedraw();
    }

    /// <summary>방장이 계산한 컬링 결과를 받아서 스톤을 굴립니다. (모두의 화면에서 같은 궤적)</summary>
    public void ShowThrow(float aim, float power, CurlingSim.Outcome outcome)
    {
        if (_game == null)
        {
            return;
        }

        _committed = true;
        _dragging = false;
        _path.Clear();
        CurlingSim.Simulate(_game.A, _game.B, aim, power, _path);
        _pathTime = 0f;
        _curlingOutcome = outcome;
    }

    /// <summary>정밀 사수 결과를 받아서 바늘을 멈춘 자리에 세웁니다.</summary>
    public void ShowStop(float time, bool hit)
    {
        _committed = true;
        _stoppedAt = time;
        _hit = hit;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_game == null || !IsVisibleInTree())
        {
            return;
        }

        _t += (float)delta;
        UpdateIce();
        if (_pathTime >= 0)
        {
            _pathTime += (float)delta;
        }

        // 정밀 사수: 제한 시간이 지나면 저절로 멈춥니다. (실패)
        if (_game.Kind == MinigameKind.Marksman && _interactive && !_committed && Elapsed() >= MarksmanRules.TimeLimit)
        {
            Commit(MarksmanRules.TimeLimit, 0);
        }

        QueueRedraw();
    }

    private float Elapsed() => (Time.GetTicksUsec() - _startUsec) / 1_000_000f;

    private void Commit(float a, float b)
    {
        if (_committed)
        {
            return;
        }

        _committed = true;
        if (_game?.Kind == MinigameKind.Marksman)
        {
            _stoppedAt = a;
        }

        Committed?.Invoke(a, b);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_game == null || !_interactive || _committed)
        {
            return;
        }

        if (_game.Kind == MinigameKind.Marksman)
        {
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                Commit(Elapsed(), 0);
                AcceptEvent();
            }

            return;
        }

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
                _dragging = true;
                _dragStart = press.Position;
                _dragNow = press.Position;
                AcceptEvent();
                break;

            case InputEventMouseMotion motion when _dragging:
                _dragNow = motion.Position;
                AcceptEvent();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } when _dragging:
                _dragging = false;
                var (aim, power, ok) = CurrentShot();
                if (ok)
                {
                    Commit(aim, power);
                }

                AcceptEvent();
                break;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 정밀 사수는 Space로도 멈출 수 있습니다.
        if (_game?.Kind == MinigameKind.Marksman && _interactive && !_committed && IsVisibleInTree()
            && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space })
        {
            Commit(Elapsed(), 0);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>지금 당긴 만큼의 조준(-1~1)과 힘(0~1)입니다. 아래로 당겨야(위로 쏘아야) 유효합니다.</summary>
    private (float Aim, float Power, bool Ok) CurrentShot()
    {
        var pull = _dragStart - _dragNow;
        if (pull.Y > -8f)
        {
            return (0, 0, false);
        }

        float angle = Mathf.Atan2(pull.X, -pull.Y);
        float aim = Mathf.Clamp(angle / 0.45f, -1f, 1f);
        float power = Mathf.Clamp(pull.Length() / MaxPull, 0f, 1f);
        return (aim, power, true);
    }

    public override void _Draw()
    {
        if (_game == null)
        {
            return;
        }

        if (_game.Kind == MinigameKind.Curling)
        {
            DrawCurling(_game);
        }
        else if (_game.Kind == MinigameKind.Marksman)
        {
            DrawMarksman(_game);
        }
    }

    // ───────────── 컬링 ─────────────

    private float SheetScale => Mathf.Min((Size.X - 160f) / 1f, (Size.Y - 20f) / 1.3f);

    private Vector2 SheetOrigin => new(Size.X / 2f, Size.Y - 18f);

    private Vector2 ToScreen(float x, float y) => SheetOrigin + new Vector2(x * SheetScale, -y * SheetScale);

    /// <summary>얼음판 사각형입니다. 픽셀 경계에 맞춰서 테두리가 흐려지지 않게 합니다.</summary>
    private Rect2 SheetRect
    {
        get
        {
            float s = SheetScale;
            var rect = new Rect2(ToScreen(-0.5f, 1.25f), new Vector2(s, 1.3f * s));
            return new Rect2(rect.Position.Round(), rect.Size.Round());
        }
    }

    private void DrawCurling(MinigameInfo game)
    {
        // 얼음판·하우스·선은 셰이더(curling_ice.gdshader)가 부드럽게 그립니다. 여기서는 그 위에 올라가는 것만 그립니다.
        var sheet = SheetRect;

        // 얼음이 휘는 방향 표시 (시트 옆에 화살표 + 세기)
        DrawCurlHint(game.B, sheet);

        // 조준선 (내가 당기는 중일 때만)
        var stoneStart = ToScreen(0, 0);
        if (_dragging && _interactive && !_committed)
        {
            var (aim, power, ok) = CurrentShot();
            if (ok)
            {
                float angle = aim * 0.45f;
                var dir = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
                float length = 40f + power * 110f;
                for (int i = 0; i < 10; i++)
                {
                    float a = length * i / 10f;
                    float b = length * (i + 0.55f) / 10f;
                    DrawLine(stoneStart + dir * a, stoneStart + dir * b, new Color(1, 0.85f, 0.3f, 0.95f), 3f, true);
                }

                DrawPowerBar(sheet, power);
            }
        }

        // 스톤이 지나간 자국과 지금 위치
        var stone = stoneStart;
        if (_pathTime >= 0 && _path.Count > 0)
        {
            int index = Math.Min(_path.Count - 1, (int)(_pathTime * 30f));
            if (index >= 1)
            {
                // 스톤이 지나간 자국: 한 줄(폴리라인)로 이어서 부드럽게 그립니다.
                var trail = new Vector2[index + 1];
                for (int i = 0; i <= index; i++)
                {
                    trail[i] = ToScreen(_path[i].X, _path[i].Y);
                }

                DrawPolyline(trail, new Color(0.25f, 0.35f, 0.5f, 0.3f), 4f, true);
            }

            stone = ToScreen(_path[index].X, _path[index].Y);
        }

        DrawStone(stone);

        if (_interactive && !_committed && !_dragging)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(_t * 4f);
            DrawArc(stoneStart, 22f + pulse * 4f, 0, Mathf.Tau, 32, new Color(1, 0.85f, 0.3f, 0.5f + 0.4f * pulse), 2f, true);
        }
    }

    /// <summary>카드 모양 스톤입니다. (별 무늬 카드 뒷면)</summary>
    private void DrawStone(Vector2 at)
    {
        var size = new Vector2(18, 24);
        var rect = new Rect2(at - size / 2, size);
        _stoneBox ??= MakeStoneBox();
        DrawStyleBox(_stoneBox, rect);
        SuitIcons.DrawStar(this, at, 6.5f, UiTheme.Gold);
    }

    private StyleBoxFlat? _stoneBox;

    /// <summary>둥근 모서리 + 금테 + 그림자 카드입니다. (StyleBox는 모서리를 안티앨리어싱해서 그립니다)</summary>
    private static StyleBoxFlat MakeStoneBox()
    {
        var box = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#1d2340"),
            BorderColor = UiTheme.Gold,
            ShadowColor = new Color(0, 0, 0, 0.35f),
            ShadowSize = 4,
            ShadowOffset = new Vector2(1, 2),
            AntiAliasing = true,
        };
        box.SetBorderWidthAll(2);
        box.SetCornerRadiusAll(3);
        return box;
    }

    private void DrawCurlHint(float curl, Rect2 sheet)
    {
        var font = UiTheme.Bold;
        bool right = curl > 0;
        float strength = Mathf.Abs(curl);
        var at = new Vector2(right ? sheet.End.X + 16 : sheet.Position.X - 64, sheet.Position.Y + 40);
        DrawString(font, at, Loc.Tr("얼음 휨"), HorizontalAlignment.Left, 60, 13, UiTheme.TextDim);
        var arrowFrom = at + new Vector2(right ? 4 : 44, 22);
        var arrowTo = arrowFrom + new Vector2(right ? 40 : -40, 0);
        DrawLine(arrowFrom, arrowTo, UiTheme.Gold, 3f, true);
        DrawPolyline(new[] { arrowTo + new Vector2(right ? -9 : 9, -7), arrowTo, arrowTo + new Vector2(right ? -9 : 9, 7) }, UiTheme.Gold, 3f, true);

        // 세기 막대 (5칸)
        int bars = Math.Clamp((int)Mathf.Round(strength / 0.35f * 5f), 1, 5);
        for (int i = 0; i < 5; i++)
        {
            var bar = new Rect2(at + new Vector2(i * 9, 36), new Vector2(6, 10));
            DrawRect(bar, i < bars ? UiTheme.Gold : new Color(1, 1, 1, 0.15f));
        }
    }

    private void DrawPowerBar(Rect2 sheet, float power)
    {
        var bar = new Rect2(new Vector2(sheet.End.X + 22, sheet.End.Y - 170), new Vector2(14, 160));
        DrawRect(bar, new Color(0, 0, 0, 0.4f));
        float filled = bar.Size.Y * power;
        var color = new Color(0.3f, 0.8f, 0.4f).Lerp(new Color(0.95f, 0.3f, 0.25f), power);
        DrawRect(new Rect2(bar.Position.X, bar.End.Y - filled, bar.Size.X, filled), color);
        DrawRect(bar, new Color(1, 1, 1, 0.4f), false, 1f);
        DrawString(UiTheme.Bold, bar.Position + new Vector2(-6, -8), $"{Mathf.RoundToInt(power * 100)}%",
            HorizontalAlignment.Left, 60, 14, Colors.White);
    }

    // ───────────── 정밀 사수 ─────────────

    private void DrawMarksman(MinigameInfo game)
    {
        var bar = new Rect2(new Vector2(30, Size.Y / 2 - 30), new Vector2(Size.X - 60, 60));
        DrawRect(bar, Color.FromHtml("#10131a"));

        float zone = MarksmanRules.ZoneWidth(game.Level);
        var zoneRect = new Rect2(bar.Position.X + bar.Size.X * (game.C - zone / 2), bar.Position.Y, bar.Size.X * zone, bar.Size.Y);
        float glow = 0.6f + 0.4f * Mathf.Sin(_t * 10f);
        DrawRect(zoneRect.Grow(3), new Color(UiTheme.Gold, 0.25f * glow));
        DrawRect(zoneRect, UiTheme.Gold);
        DrawRect(bar, new Color(1, 1, 1, 0.35f), false, 2f);

        float time = _stoppedAt ?? Elapsed();
        float position = MarksmanRules.Position(game.A, game.B, time);
        float x = bar.Position.X + bar.Size.X * position;
        var needleColor = _hit == true ? Color.FromHtml("#7dff9a") : _hit == false ? Color.FromHtml("#ff5c5c") : Colors.White;
        DrawLine(new Vector2(x, bar.Position.Y - 14), new Vector2(x, bar.End.Y + 14), needleColor, 4f, true);
        DrawCircle(new Vector2(x, bar.Position.Y - 16), 6f, needleColor, true, -1f, true);

        // 단계 표시: 연속 성공 수만큼 금색 점
        for (int i = 0; i < MarksmanRules.Target; i++)
        {
            var dot = new Vector2(Size.X / 2 + (i - (MarksmanRules.Target - 1) / 2f) * 30f, bar.End.Y + 50);
            bool done = i < game.Level || (i == game.Level && _hit == true);
            DrawCircle(dot, 9f, done ? UiTheme.Gold : new Color(1, 1, 1, 0.15f), true, -1f, true);
            DrawArc(dot, 9f, 0, Mathf.Tau, 24, new Color(1, 1, 1, 0.4f), 1.5f, true);
        }

        string speed = $"{MarksmanRules.Speed(game.Level):0.0}";
        DrawString(UiTheme.Bold, new Vector2(0, bar.Position.Y - 40), Loc.Tr($"단계 {game.Level + 1}/{MarksmanRules.Target} · 초당 {speed}번 왕복"),
            HorizontalAlignment.Center, Size.X, 15, UiTheme.TextDim);
    }
}
