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

    public MinigameInfo? Game => _game;

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

    private void DrawCurling(MinigameInfo game)
    {
        float s = SheetScale;
        var sheet = new Rect2(ToScreen(-0.5f, 1.25f), new Vector2(s, 1.3f * s));
        DrawRect(sheet, Color.FromHtml("#dfe9f2"));
        DrawRect(sheet, new Color(0.3f, 0.45f, 0.6f, 0.9f), false, 3f);

        // 얼음 결 (흐린 가로줄)
        for (int i = 1; i < 13; i++)
        {
            float y = sheet.Position.Y + sheet.Size.Y * i / 13f;
            DrawLine(new Vector2(sheet.Position.X, y), new Vector2(sheet.End.X, y), new Color(0.6f, 0.72f, 0.82f, 0.35f), 1f);
        }

        // 하우스 (파랑 · 흰색 · 빨강 · 금색 버튼)
        var house = ToScreen(game.A, CurlingSim.HouseY);
        DrawCircle(house, CurlingSim.HouseRadius * s, Color.FromHtml("#2f7fd6"));
        DrawCircle(house, CurlingSim.HouseRadius * s * 0.68f, Colors.White);
        DrawCircle(house, CurlingSim.HouseRadius * s * 0.4f, Color.FromHtml("#e0463c"));
        DrawCircle(house, CurlingSim.ButtonRadius * s, UiTheme.Gold);
        DrawArc(house, CurlingSim.ButtonRadius * s, 0, Mathf.Tau, 32, Colors.White, 2f, true);

        // 뒷선과 호그선
        var back = ToScreen(-0.5f, CurlingSim.BackLine);
        DrawLine(back, back + new Vector2(s, 0), new Color(0.2f, 0.3f, 0.4f, 0.7f), 2f);

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
                    DrawLine(stoneStart + dir * a, stoneStart + dir * b, new Color(1, 0.85f, 0.3f, 0.95f), 3f);
                }

                DrawPowerBar(sheet, power);
            }
        }

        // 스톤이 지나간 자국과 지금 위치
        var stone = stoneStart;
        if (_pathTime >= 0 && _path.Count > 0)
        {
            int index = Math.Min(_path.Count - 1, (int)(_pathTime * 30f));
            for (int i = 1; i <= index; i++)
            {
                DrawLine(ToScreen(_path[i - 1].X, _path[i - 1].Y), ToScreen(_path[i].X, _path[i].Y), new Color(0.25f, 0.35f, 0.5f, 0.35f), 3f);
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
        var size = new Vector2(16, 22);
        var rect = new Rect2(at - size / 2, size);
        DrawRect(new Rect2(rect.Position + new Vector2(2, 3), size), new Color(0, 0, 0, 0.3f));
        DrawRect(rect, Color.FromHtml("#1d2340"));
        DrawRect(rect, UiTheme.Gold, false, 2f);
        SuitIcons.DrawStar(this, at, 6f, UiTheme.Gold);
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
        DrawLine(arrowFrom, arrowTo, UiTheme.Gold, 3f);
        DrawLine(arrowTo, arrowTo + new Vector2(right ? -9 : 9, -7), UiTheme.Gold, 3f);
        DrawLine(arrowTo, arrowTo + new Vector2(right ? -9 : 9, 7), UiTheme.Gold, 3f);

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
        DrawLine(new Vector2(x, bar.Position.Y - 14), new Vector2(x, bar.End.Y + 14), needleColor, 4f);
        DrawCircle(new Vector2(x, bar.Position.Y - 16), 6f, needleColor);

        // 단계 표시: 연속 성공 수만큼 금색 점
        for (int i = 0; i < MarksmanRules.Target; i++)
        {
            var dot = new Vector2(Size.X / 2 + (i - (MarksmanRules.Target - 1) / 2f) * 30f, bar.End.Y + 50);
            bool done = i < game.Level || (i == game.Level && _hit == true);
            DrawCircle(dot, 9f, done ? UiTheme.Gold : new Color(1, 1, 1, 0.15f));
            DrawArc(dot, 9f, 0, Mathf.Tau, 24, new Color(1, 1, 1, 0.4f), 1.5f, true);
        }

        string speed = $"{MarksmanRules.Speed(game.Level):0.0}";
        DrawString(UiTheme.Bold, new Vector2(0, bar.Position.Y - 40), Loc.Tr($"단계 {game.Level + 1}/{MarksmanRules.Target} · 초당 {speed}번 왕복"),
            HorizontalAlignment.Center, Size.X, 15, UiTheme.TextDim);
    }
}
