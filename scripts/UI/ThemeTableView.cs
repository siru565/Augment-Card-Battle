using System;
using System.Linq;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 테이블 오른쪽에 놓이는 테마 판입니다. (빙고판, 레이스 트랙, 영토 깃발, 비밀 임무, 보스, 시한폭탄)
/// 전부 _Draw로 직접 그려서 노드를 늘리지 않습니다. 글자는 Loc.Tr로 영어 전환을 따릅니다.
/// </summary>
public partial class ThemeTableView : Control
{
    private static readonly Color[] SeatColors =
    {
        Color.FromHtml("#f0b849"), Color.FromHtml("#5ec8f0"), Color.FromHtml("#e86a8f"), Color.FromHtml("#8fdc6a"),
    };

    private PlayerView? _view;
    private Func<int, string> _nameOf = seat => $"P{seat}";

    // 애니메이션 상태
    private float[] _raceShown = Array.Empty<float>();
    private float _bossHpShown = -1;
    private float _bossShake;
    private float _bossFlash;
    private float _blastFlash;
    private int _bingoPulseCell = -1;
    private float _bingoPulse;
    private float _time;

    public ThemeTableView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public static bool Supports(ThemeId theme) =>
        theme is ThemeId.Arcade or ThemeId.Bingo or ThemeId.Race or ThemeId.Territory or ThemeId.Mission or ThemeId.Boss or ThemeId.Bomb;

    public void SetView(PlayerView view, Func<int, string> nameOf)
    {
        _view = view;
        _nameOf = nameOf;
        Visible = (view.Board != null || view.Theme == ThemeId.Arcade) && Supports(view.Theme);
        if (view.Board is { } board && _raceShown.Length != board.RacePos.Length)
        {
            _raceShown = board.RacePos.Select(p => (float)p).ToArray();
        }

        if (_bossHpShown < 0 && view.Board != null)
        {
            _bossHpShown = view.Board.BossHp;
        }
    }

    /// <summary>게임 이벤트에 맞춰 판을 흔들거나 반짝입니다. 판 안에서 효과를 띄울 위치(전역 좌표)를 돌려줍니다.</summary>
    public Vector2 OnEvent(GameEvent e)
    {
        switch (e.Type)
        {
            case GameEventType.BossHit:
                _bossShake = 0.3f;
                _bossFlash = 1f;
                return GetGlobalRect().Position + BossCenter();
            case GameEventType.BombExploded:
                _blastFlash = 1f;
                return GetGlobalRect().Position + BombCenter();
            case GameEventType.BingoMarked when _view != null && e.Player == _view.PlayerId:
                _bingoPulseCell = e.Amount;
                _bingoPulse = 1f;
                break;
        }

        return GetGlobalRect().GetCenter();
    }

    public override void _Process(double delta)
    {
        if (!Visible || _view == null)
        {
            return;
        }

        if (_view.Board == null)
        {
            _time += (float)delta;
            QueueRedraw();
            return;
        }

        float dt = (float)delta;
        _time += dt;
        var board = _view.Board;
        for (int i = 0; i < _raceShown.Length && i < board.RacePos.Length; i++)
        {
            _raceShown[i] = Mathf.MoveToward(_raceShown[i], board.RacePos[i], dt * 22f);
        }

        _bossHpShown = Mathf.MoveToward(_bossHpShown, board.BossHp, dt * 90f);
        _bossShake = Mathf.Max(0, _bossShake - dt);
        _bossFlash = Mathf.Max(0, _bossFlash - dt * 3f);
        _ragePop = Mathf.Max(0, _ragePop - dt * 1.8f);
        _blastFlash = Mathf.Max(0, _blastFlash - dt * 1.2f);
        _bingoPulse = Mathf.Max(0, _bingoPulse - dt * 1.5f);
        QueueRedraw();
    }

    // ───────────── 그리기 ─────────────

    private const float Pad = 10f;

    public override void _Draw()
    {
        if (_view == null || !Visible)
        {
            return;
        }

        var board = _view.Board;

        var rect = new Rect2(Vector2.Zero, Size);
        DrawStyleBox(UiTheme.Box(new Color(0.07f, 0.09f, 0.12f, 0.82f), new Color(UiTheme.Gold, 0.35f), 1, 8, 0), rect);
        var info = Themes.Info(_view.Theme);
        Text(UiTheme.Title, new Vector2(Pad, Pad + 17), Loc.Tr(info.Name), 18, UiTheme.Gold);

        float top = Pad + 30;
        if (board == null)
        {
            if (_view.Theme == ThemeId.Arcade)
            {
                DrawArcade(top);
            }

            return;
        }

        switch (_view.Theme)
        {
            case ThemeId.Bingo:
                DrawBingo(board, top);
                break;
            case ThemeId.Race:
                DrawRace(board, top);
                break;
            case ThemeId.Territory:
                DrawTerritory(board, top);
                break;
            case ThemeId.Mission:
                DrawMission(board, top);
                break;
            case ThemeId.Boss:
                DrawBoss(board, top);
                break;
            case ThemeId.Bomb:
                DrawBomb(board, top);
                break;
        }
    }

    private void Text(Font font, Vector2 at, string text, int size, Color color, float width = -1, HorizontalAlignment align = HorizontalAlignment.Left) =>
        DrawString(font, at, text, align, width, size, color);

    private void Wrapped(Vector2 at, string text, int size, Color color, float width, int maxLines = 4) =>
        DrawMultilineString(UiTheme.Bold, at, text, HorizontalAlignment.Left, width, size, maxLines, color);

    private string ShortName(int seat)
    {
        string name = Loc.Tr(_nameOf(seat));
        return name.Length > 7 ? name[..7] + "…" : name;
    }

    private bool Active(int seat) => _view!.IsActive(seat);

    // 미니게임 대회: 다음 대회까지 남은 턴과 별 현황
    private void DrawArcade(float top)
    {
        var view = _view!;
        int every = ArcadeRules.Every;
        int left = every - (view.TurnCount % every);
        if (left <= 0)
        {
            left = every;
        }

        Text(UiTheme.Bold, new Vector2(Pad, top + 12), Loc.Tr($"다음 대회까지 {left}턴"), 13, left <= 2 ? UiTheme.Danger : UiTheme.Text);
        Bar(new Rect2(Pad, top + 22, Size.X - Pad * 2, 8), 1f - (left - 1) / (float)every, Color.FromHtml("#ff4fd8"));
        Text(UiTheme.Regular, new Vector2(Pad, top + 48), Loc.Tr($"1등은 별 1개 · 별 {ArcadeRules.StarsToWin}개면 승리"), 11, UiTheme.TextDim, Size.X - Pad * 2);

        float y = top + 74;
        for (int s = 0; s < view.PlayerCount; s++)
        {
            Text(UiTheme.Bold, new Vector2(Pad, y), ShortName(s), 12, Active(s) ? UiTheme.Text : new Color(UiTheme.TextDim, 0.4f));
            int stars = s < view.Stars.Length ? view.Stars[s] : 0;
            for (int k = 0; k < ArcadeRules.StarsToWin; k++)
            {
                var at = new Vector2(Size.X - Pad - 8 - (ArcadeRules.StarsToWin - 1 - k) * 20, y - 5);
                float pulse = k < stars ? 1f + 0.08f * Mathf.Sin(_time * 3 + k) : 1f;
                SuitIcons.DrawStar(this, at, 7.5f * pulse, k < stars ? UiTheme.Gold : new Color(1, 1, 1, 0.14f));
            }

            y += 22;
        }

        // 종목 순서 안내
        string next = ArcadeRules.Name((ArcadeGame)(view.TurnCount / every % 3));
        Text(UiTheme.Regular, new Vector2(Pad, y + 8), Loc.Tr($"다음 종목: {next}"), 11, UiTheme.TextDim, Size.X - Pad * 2);
    }

    // 빙고: 내 판을 크게, 다른 사람은 줄 수만 아래에 적습니다.
    private void DrawBingo(ThemeBoard board, float top)
    {
        var mine = board.Bingo[_view!.PlayerId];
        if (mine.Length != 9)
        {
            return;
        }

        int lines = BingoRules.LinesDone(mine);
        Text(UiTheme.Bold, new Vector2(Pad, top + 12), Loc.Tr($"내 빙고 {lines}/{BingoRules.LinesToWin}줄"), 13, UiTheme.Text);
        float others = 18f * Enumerable.Range(0, _view.PlayerCount).Count(s => s != _view.PlayerId);
        float cell = Mathf.Min((Size.X - Pad * 2 - 8) / 3f, (Size.Y - top - 26 - others - Pad) / 3f);
        cell = Mathf.Max(cell, 20);
        var origin = new Vector2((Size.X - cell * 3 - 8) / 2, top + 22);
        for (int i = 0; i < 9; i++)
        {
            var c = mine[i];
            var r = new Rect2(origin + new Vector2(i % 3 * (cell + 4), i / 3 * (cell + 4)), new Vector2(cell, cell));
            float pulse = i == _bingoPulseCell ? _bingoPulse : 0;
            if (pulse > 0)
            {
                r = r.Grow(pulse * 4);
            }

            var fill = c.Marked ? new Color(UiTheme.Gold, 0.85f) : new Color(1, 1, 1, 0.07f);
            DrawStyleBox(UiTheme.Box(fill, c.Marked ? UiTheme.Gold : new Color(1, 1, 1, 0.25f), 1, 6, 0), r);
            string label = c.Free ? "FREE" : c.Suit != CardColor.Wild ? Loc.Tr(Card.ColorName(c.Suit)) : c.Number.ToString();
            int size = c.Free ? (int)(cell * 0.26f) : (int)(cell * 0.5f);
            var color = c.Marked ? new Color("#1b1406") : UiTheme.Text;
            Text(UiTheme.Title, new Vector2(r.Position.X, r.Position.Y + r.Size.Y / 2 + size * 0.36f), label, size, color, r.Size.X, HorizontalAlignment.Center);
        }

        float y = origin.Y + cell * 3 + 8 + 16;
        for (int s = 0; s < _view.PlayerCount; s++)
        {
            if (s == _view.PlayerId || board.Bingo[s].Length != 9)
            {
                continue;
            }

            int l = BingoRules.LinesDone(board.Bingo[s]);
            int marks = board.Bingo[s].Count(c => c.Marked && !c.Free);
            Text(UiTheme.Bold, new Vector2(Pad, y), Loc.Tr($"{ShortName(s)}: {l}/{BingoRules.LinesToWin}줄 · {marks}칸"), 12,
                Active(s) ? UiTheme.TextDim : new Color(UiTheme.TextDim, 0.4f));
            y += 18;
        }
    }

    // 레이스: 자리마다 트랙 한 줄, 결승선은 체크무늬입니다.
    private void DrawRace(ThemeBoard board, float top)
    {
        Text(UiTheme.Bold, new Vector2(Pad, top + 12), Loc.Tr($"결승선 {RaceRules.Finish}칸"), 13, UiTheme.TextDim);
        int n = board.RacePos.Length;
        float rowH = Mathf.Min(44, (Size.Y - top - 24 - Pad) / n);
        float trackX = Pad;
        float trackW = Size.X - Pad * 2 - 12;
        for (int s = 0; s < n; s++)
        {
            float y = top + 24 + s * rowH;
            var color = SeatColors[s % SeatColors.Length];
            if (!Active(s))
            {
                color = new Color(color, 0.35f);
            }

            Text(UiTheme.Bold, new Vector2(trackX, y + 12), $"{ShortName(s)}  {board.RacePos[s]}", 12, s == _view!.PlayerId ? UiTheme.Text : UiTheme.TextDim);
            float lineY = y + rowH * 0.66f;
            DrawLine(new Vector2(trackX, lineY), new Vector2(trackX + trackW, lineY), new Color(1, 1, 1, 0.15f), 6, true);
            for (int t = 10; t < RaceRules.Finish; t += 10)
            {
                float tx = trackX + trackW * t / RaceRules.Finish;
                DrawLine(new Vector2(tx, lineY - 4), new Vector2(tx, lineY + 4), new Color(1, 1, 1, 0.2f), 1);
            }

            // 결승선
            float fx = trackX + trackW;
            for (int k = 0; k < 4; k++)
            {
                DrawRect(new Rect2(fx + (k % 2) * 5, lineY - 10 + k * 5, 5, 5), Colors.White);
                DrawRect(new Rect2(fx + ((k + 1) % 2) * 5, lineY - 10 + k * 5, 5, 5), Colors.Black);
            }

            float shown = s < _raceShown.Length ? _raceShown[s] : board.RacePos[s];
            float px = trackX + trackW * Mathf.Clamp(shown / RaceRules.Finish, 0, 1);
            DrawLine(new Vector2(trackX, lineY), new Vector2(px, lineY), new Color(color, 0.7f), 6, true);
            float bob = Mathf.Abs(shown - board.RacePos[s]) > 0.1f ? Mathf.Sin(_time * 30) * 2 : 0;
            DrawCircle(new Vector2(px, lineY + bob), 8, color);
            DrawArc(new Vector2(px, lineY + bob), 8, 0, Mathf.Tau, 20, new Color(0, 0, 0, 0.6f), 2, true);
        }
    }

    // 영토: 문양 깃발 네 개와 주인, 문양별 최다 기록
    private void DrawTerritory(ThemeBoard board, float top)
    {
        int mineCount = board.Flags.Count(f => f == _view!.PlayerId);
        Text(UiTheme.Bold, new Vector2(Pad, top + 12), Loc.Tr($"내 깃발 {mineCount}/{board.FlagsNeeded}"), 12, UiTheme.TextDim, Size.X - Pad * 2);
        float rowH = Mathf.Min(46, (Size.Y - top - 24 - Pad) / 4);
        for (int suit = 0; suit < 4; suit++)
        {
            float y = top + 24 + suit * rowH;
            var suitColor = UiTheme.CardColor((CardColor)suit);
            // 깃대와 깃발
            var pole = new Vector2(Pad + 6, y + 4);
            DrawLine(pole, pole + new Vector2(0, rowH - 10), UiTheme.Silver, 2);
            int owner = board.Flags[suit];
            float wave = Mathf.Sin(_time * 4 + suit) * 2;
            var flag = new[]
            {
                pole, pole + new Vector2(22, 3 + wave), pole + new Vector2(22, 15 + wave), pole + new Vector2(0, 13),
            };
            DrawColoredPolygon(flag, owner >= 0 ? suitColor : new Color(suitColor, 0.25f));
            if (owner >= 0)
            {
                DrawCircle(pole + new Vector2(11, 8 + wave / 2), 3.5f, SeatColors[owner % SeatColors.Length]);
            }

            string ownerText = owner >= 0 ? ShortName(owner) : Loc.Tr("주인 없음");
            Text(UiTheme.Bold, new Vector2(Pad + 36, y + 14), Loc.Tr(Card.ColorName((CardColor)suit)), 13, suitColor);
            Text(UiTheme.Bold, new Vector2(Pad + 36, y + 14), ownerText, 13, owner == _view!.PlayerId ? UiTheme.Gold : UiTheme.Text,
                Size.X - Pad * 2 - 36, HorizontalAlignment.Right);
            int best = board.SuitPlays.Max(p => p[suit]);
            int mine = board.SuitPlays[_view.PlayerId][suit];
            Text(UiTheme.Regular, new Vector2(Pad + 36, y + 30), Loc.Tr($"나 {mine}장 · 최다 {best}장"), 11, UiTheme.TextDim);
        }
    }

    // 비밀 임무: 내 임무는 크게, 남의 임무는 절반을 넘겨야 보입니다.
    private void DrawMission(ThemeBoard board, float top)
    {
        int me = _view!.PlayerId;
        float width = Size.X - Pad * 2;
        Text(UiTheme.Bold, new Vector2(Pad, top + 12), Loc.Tr("내 임무"), 12, UiTheme.TextDim);
        string mission = Loc.Tr(board.Missions[me]);
        Wrapped(new Vector2(Pad, top + 30), mission, 13, UiTheme.Text, width, 3);
        int lines = Math.Clamp((int)Math.Ceiling(UiTheme.Bold.GetMultilineStringSize(mission, HorizontalAlignment.Left, width, 13).Y / 16f), 1, 3);
        float barY = top + 30 + lines * 16 - 4;
        int target = Math.Max(1, board.MissionTarget[me]);
        int progress = Math.Max(0, board.MissionProgress[me]);
        Bar(new Rect2(Pad, barY, width, 10), progress / (float)target, UiTheme.Gold);
        Text(UiTheme.Bold, new Vector2(Pad, barY + 26), $"{progress}/{target}", 12, UiTheme.Gold);

        float y = barY + 48;
        for (int s = 0; s < _view.PlayerCount; s++)
        {
            if (s == me)
            {
                continue;
            }

            bool hidden = board.MissionProgress[s] < 0;
            string text = hidden ? Loc.Tr($"{ShortName(s)}: ??? (비밀)") : $"{ShortName(s)}: {Loc.Tr(board.Missions[s])} {board.MissionProgress[s]}/{board.MissionTarget[s]}";
            Wrapped(new Vector2(Pad, y), text, 11, hidden ? UiTheme.TextDim : UiTheme.Danger, width, 2);
            y += hidden ? 17 : 30;
        }
    }

    private void Bar(Rect2 r, float ratio, Color color)
    {
        DrawStyleBox(UiTheme.Box(new Color(1, 1, 1, 0.08f), new Color(1, 1, 1, 0.15f), 1, 4, 0), r);
        if (ratio > 0)
        {
            DrawStyleBox(UiTheme.Box(color, color, 0, 4, 0), new Rect2(r.Position, new Vector2(r.Size.X * Mathf.Clamp(ratio, 0, 1), r.Size.Y)));
        }
    }

    private static Texture2D[]? _bossSprites;

    /// <summary>보스 그림을 한 번만 찾아 둡니다. 없으면 코드로 그린 보스를 씁니다.</summary>
    private static Texture2D[] BossSprites()
    {
        if (_bossSprites != null)
        {
            return _bossSprites;
        }

        var list = new System.Collections.Generic.List<Texture2D>();
        using var dir = DirAccess.Open("res://assets/textures/boss");
        if (dir != null)
        {
            // 내보낸 게임에서는 .png 대신 .png.import만 보이므로 둘 다 봅니다.
            var names = dir.GetFiles().Select(f => f.EndsWith(".import") ? f[..^7] : f)
                .Where(f => f.EndsWith(".png") || f.EndsWith(".webp") || f.EndsWith(".jpg"))
                .Distinct().OrderBy(f => f, StringComparer.Ordinal);
            foreach (var name in names)
            {
                string path = $"res://assets/textures/boss/{name}";
                // 에디터가 아직 가져오기(import)를 안 한 새 그림이면 파일을 직접 읽습니다.
                var tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
                if (tex == null && Image.LoadFromFile(path) is { } image)
                {
                    tex = ImageTexture.CreateFromImage(image);
                }

                if (tex != null)
                {
                    list.Add(tex);
                }
            }
        }

        _bossSprites = list.ToArray();
        return _bossSprites;
    }

    /// <summary>
    /// 보스 그림을 움직여서 그립니다. (그림 한 장을 코드로 움직입니다)
    /// - 평소: 위아래로 둥실둥실 + 숨쉬듯 커졌다 작아짐 + 살짝 좌우로 기울기
    /// - 분노할수록: 더 빠르게 움직이고, 뒤쪽 오라가 붉어지고, 2단계는 부르르 떱니다
    /// - 맞으면: 찌그러졌다 돌아오며 하얗게 번쩍, 분노 단계가 오르면: 크게 부풀었다 돌아옴
    /// </summary>
    private void DrawBossSprite(Texture2D[] sprites, int rage, Vector2 c, float r)
    {
        if (rage != _lastRage)
        {
            if (_lastRage >= 0 && rage > _lastRage)
            {
                _ragePop = 1f;
                _bossShake = 0.3f;
            }

            _lastRage = rage;
        }

        var tex = sprites[Math.Min(rage, sprites.Length - 1)];
        float speed = 1.6f + rage * 1.3f;
        float bob = Mathf.Sin(_time * speed) * (3f + rage);
        float breathe = 1f + 0.035f * Mathf.Sin(_time * speed * 2f);
        float sway = Mathf.Sin(_time * speed * 0.5f) * (0.04f + rage * 0.02f);
        var jitter = rage >= 2 ? new Vector2(Mathf.Sin(_time * 53f), Mathf.Cos(_time * 47f)) * 1.6f : Vector2.Zero;
        float hit = Mathf.Clamp(_bossShake / 0.3f, 0f, 1f);
        float pop = _ragePop * _ragePop;
        var scale = new Vector2(breathe * (1f + 0.14f * hit), breathe * (1f - 0.12f * hit)) * (1f + 0.3f * pop);
        var center = c + new Vector2(0, -r * 0.25f + bob) + jitter;

        // 뒤쪽 오라
        var aura = rage switch
        {
            0 => Color.FromHtml("#7b4dff"),
            1 => Color.FromHtml("#ff3fa4"),
            _ => Color.FromHtml("#ff6a1f"),
        };
        float auraPulse = 1f + 0.08f * Mathf.Sin(_time * speed * 1.5f);
        for (int k = 3; k >= 1; k--)
        {
            DrawCircle(center, r * (0.7f + 0.22f * k) * auraPulse, new Color(aura, (0.05f + 0.03f * rage) * (1f + pop)));
        }

        float h = r * 2.7f;
        float w = h * tex.GetWidth() / Math.Max(1, tex.GetHeight());
        // 맞았을 때와 분노할 때는 하얗게 번쩍입니다. (1보다 큰 색은 더 밝게 그려집니다)
        float glow = 1f + 1.4f * Mathf.Max(_bossFlash, pop);
        DrawSetTransform(center, sway, scale);
        DrawTextureRect(tex, new Rect2(-w / 2, -h / 2, w, h), false, new Color(glow, glow, glow));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private int _lastRage = -1;
    private float _ragePop;

    private Vector2 BossCenter() => new(Size.X / 2, Mathf.Min(Size.Y * 0.45f, 110));

    // 보스: 뿔 달린 머리, 분노할수록 붉어지고 눈이 커집니다.
    private void DrawBoss(ThemeBoard board, float top)
    {
        var c = BossCenter() + (_bossShake > 0 ? new Vector2(Mathf.Sin(_time * 90) * 5 * _bossShake / 0.3f, 0) : Vector2.Zero);
        float r = Mathf.Min(BossSprites().Length > 0 ? 52 : 46, (Size.Y - top - 70) / 2);
        var skin = board.BossRage switch
        {
            0 => Color.FromHtml("#6a4c93"),
            1 => Color.FromHtml("#9a3f6a"),
            _ => Color.FromHtml("#c0392b"),
        };
        skin = skin.Lerp(Colors.White, _bossFlash * 0.7f);

        // assets/textures/boss/ 에 그림이 있으면 그걸 씁니다. (이름순: 평상시 → 분노 1 → 분노 2, 모자라면 마지막 그림)
        var sprites = BossSprites();
        if (sprites.Length > 0)
        {
            DrawBossSprite(sprites, board.BossRage, c, r);
            DrawBossHp(board, c.Y + r + 14);
            return;
        }

        // 뿔
        DrawColoredPolygon(new[] { c + new Vector2(-r * 0.8f, -r * 0.4f), c + new Vector2(-r * 1.1f, -r * 1.3f), c + new Vector2(-r * 0.35f, -r * 0.8f) }, UiTheme.Silver);
        DrawColoredPolygon(new[] { c + new Vector2(r * 0.8f, -r * 0.4f), c + new Vector2(r * 1.1f, -r * 1.3f), c + new Vector2(r * 0.35f, -r * 0.8f) }, UiTheme.Silver);
        DrawCircle(c, r, skin);
        DrawArc(c, r, 0, Mathf.Tau, 40, new Color(0, 0, 0, 0.5f), 3, true);
        // 눈 (분노하면 눈썹이 기울어집니다)
        float eye = r * (0.16f + board.BossRage * 0.03f);
        var eyeColor = board.BossRage >= 2 ? Color.FromHtml("#ffd166") : Colors.White;
        foreach (int side in new[] { -1, 1 })
        {
            var e = c + new Vector2(side * r * 0.36f, -r * 0.12f);
            DrawCircle(e, eye, eyeColor);
            DrawCircle(e + new Vector2(0, eye * 0.2f), eye * 0.45f, Colors.Black);
            float tilt = board.BossRage * 0.12f * r;
            DrawLine(e + new Vector2(-side * eye * 1.4f, -eye * 1.5f - tilt), e + new Vector2(side * eye * 1.2f, -eye * 1.2f), Colors.Black, 3, true);
        }

        // 입
        var mouth = c + new Vector2(0, r * 0.45f);
        DrawLine(mouth + new Vector2(-r * 0.4f, 0), mouth + new Vector2(r * 0.4f, 0), Colors.Black, 3, true);
        for (int k = -1; k <= 1; k += 2)
        {
            DrawColoredPolygon(new[] { mouth + new Vector2(k * r * 0.25f - 4, 0), mouth + new Vector2(k * r * 0.25f + 4, 0), mouth + new Vector2(k * r * 0.25f, 7) }, Colors.White);
        }

        DrawBossHp(board, c.Y + r + 14);
    }

    private void DrawBossHp(ThemeBoard board, float barY)
    {
        float max = Math.Max(1, board.BossMaxHp);
        var hpRect = new Rect2(Pad, barY, Size.X - Pad * 2, 12);
        Bar(hpRect, _bossHpShown / max, Color.FromHtml("#e5534b"));
        // 분노 구간 표시
        foreach (float mark in new[] { 1f / 3, 2f / 3 })
        {
            float x = hpRect.Position.X + hpRect.Size.X * mark;
            DrawLine(new Vector2(x, barY - 2), new Vector2(x, barY + 14), new Color(1, 1, 1, 0.5f), 1);
        }

        Text(UiTheme.Bold, new Vector2(Pad, barY + 30), $"HP {board.BossHp}/{board.BossMaxHp}", 13, UiTheme.Text);
        string rage = board.BossRage == 0 ? Loc.Tr("평온") : Loc.Tr($"분노 {board.BossRage}단계");
        Text(UiTheme.Bold, new Vector2(Pad, barY + 30), rage, 13, board.BossRage > 0 ? UiTheme.Danger : UiTheme.TextDim, Size.X - Pad * 2, HorizontalAlignment.Right);
        Text(UiTheme.Regular, new Vector2(Pad, barY + 48), Loc.Tr("마지막 일격을 넣으면 승리"), 11, UiTheme.TextDim);
    }

    private Vector2 BombCenter() => new(Size.X / 2, Mathf.Min(Size.Y * 0.42f, 100));

    // 시한폭탄: 지금 차례인 사람이 폭탄을 들고 있습니다. 도화선이 짧아지면 붉게 깜빡입니다.
    private void DrawBomb(ThemeBoard board, float top)
    {
        var c = BombCenter();
        float r = Mathf.Min(34, (Size.Y - top - 90) / 2);
        bool danger = board.BombDanger;
        float beat = danger ? 1 + 0.08f * Mathf.Sin(_time * 14) : 1 + 0.02f * Mathf.Sin(_time * 3);
        r *= beat;
        var body = danger ? Color.FromHtml("#3a1414").Lerp(Color.FromHtml("#8a1f1f"), 0.5f + 0.5f * Mathf.Sin(_time * 14)) : Color.FromHtml("#23262e");
        body = body.Lerp(Colors.White, _blastFlash * 0.8f);
        DrawCircle(c, r, body);
        DrawArc(c, r, 0, Mathf.Tau, 40, new Color(0, 0, 0, 0.7f), 3, true);
        DrawCircle(c + new Vector2(-r * 0.35f, -r * 0.35f), r * 0.18f, new Color(1, 1, 1, 0.25f));
        // 꼭지와 도화선, 불꽃
        var cap = c + new Vector2(r * 0.55f, -r * 0.75f);
        DrawRect(new Rect2(cap - new Vector2(6, 6), new Vector2(12, 10)), UiTheme.Silver);
        var fuseEnd = cap + new Vector2(14, -16);
        DrawPolyline(new[] { cap + new Vector2(0, -6), cap + new Vector2(6, -14), fuseEnd }, Color.FromHtml("#c8a26a"), 3, true);
        float spark = 5 + 3 * Mathf.Sin(_time * 25);
        DrawCircle(fuseEnd, spark, Color.FromHtml("#ffb347"));
        DrawCircle(fuseEnd, spark * 0.5f, Colors.White);

        float y = c.Y + r + 22;
        int holder = _view!.CurrentPlayer;
        Text(UiTheme.Bold, new Vector2(Pad, y), Loc.Tr($"폭탄: {ShortName(holder)}"), 13, holder == _view.PlayerId ? UiTheme.Danger : UiTheme.Text,
            Size.X - Pad * 2, HorizontalAlignment.Center);
        if (danger)
        {
            Text(UiTheme.Title, new Vector2(Pad, y + 20), Loc.Tr("곧 터짐!"), 16, UiTheme.Danger.Lerp(Colors.White, 0.5f + 0.5f * Mathf.Sin(_time * 14)),
                Size.X - Pad * 2, HorizontalAlignment.Center);
        }

        // 메달 현황
        float my = y + 42;
        for (int s = 0; s < _view.PlayerCount; s++)
        {
            Text(UiTheme.Bold, new Vector2(Pad, my), ShortName(s), 12, Active(s) ? UiTheme.TextDim : new Color(UiTheme.TextDim, 0.4f));
            for (int m = 0; m < BombRules.MedalsToWin; m++)
            {
                var at = new Vector2(Size.X - Pad - 8 - (BombRules.MedalsToWin - 1 - m) * 18, my - 4);
                DrawCircle(at, 6.5f, m < board.Medals[s] ? UiTheme.Gold : new Color(1, 1, 1, 0.12f));
            }

            my += 17;
        }
    }
}
