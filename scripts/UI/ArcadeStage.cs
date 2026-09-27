using System;
using System.Collections.Generic;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 미니게임 대회의 게임 화면입니다. (ArcadeOverlay가 씁니다)
/// 모두가 같은 시드로 같은 판을 하고, 끝나면 점수를 알려 줍니다.
/// - 벽돌깨기: 마우스로 패들, 공 3개, 점수 = 깬 벽돌 수
/// - 두더지 카드: 튀어나오는 카드를 클릭 (폭탄은 -2)
/// - 미로 탈출: 방향키/WASD, 빨리 나올수록 높은 점수
/// 그림은 전부 코드로 그려서 따로 에셋이 필요 없습니다.
/// </summary>
public partial class ArcadeStage : Control
{
    /// <summary>게임이 끝났을 때(시간 종료, 공 소진, 탈출) 점수와 함께 호출합니다.</summary>
    public event Action<int>? Finished;

    private ArcadeGame _game;
    private bool _running;
    private float _time;
    private float _countdown;

    public int Score { get; private set; }

    public float TimeLeft => Mathf.Max(0f, ArcadeRules.TimeLimit - _time);

    public bool Running => _running;

    /// <summary>시작 전 카운트다운(초)입니다. 0보다 크면 아직 시작 전입니다.</summary>
    public float Countdown => _countdown;

    public void Begin(ArcadeGame game, int seed)
    {
        _game = game;
        _time = 0;
        _countdown = 2.0f;
        Score = 0;
        _running = true;
        switch (game)
        {
            case ArcadeGame.Breakout:
                SetupBreakout();
                break;
            case ArcadeGame.Whack:
                _moles = ArcadeRules.Moles(seed);
                _moleHit = new bool[_moles.Count];
                break;
            default:
                SetupMaze(seed);
                break;
        }

        GrabFocus();
        QueueRedraw();
    }

    public void Stop() => _running = false;

    private void Finish()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        if (_game == ArcadeGame.Maze)
        {
            Score = ArcadeRules.MazeScore(_escaped, TimeLeft, MazeProgress());
        }

        Finished?.Invoke(Score);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree())
        {
            return;
        }

        float dt = (float)delta;
        if (_running && _countdown > 0)
        {
            _countdown -= dt;
            QueueRedraw();
            return;
        }

        if (_running)
        {
            _time += dt;
            switch (_game)
            {
                case ArcadeGame.Breakout:
                    UpdateBreakout(dt);
                    break;
                case ArcadeGame.Maze:
                    UpdateMaze(dt);
                    break;
            }

            if (_time >= ArcadeRules.TimeLimit)
            {
                Finish();
            }
        }

        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!_running || _countdown > 0)
        {
            return;
        }

        if (_game == ArcadeGame.Breakout && @event is InputEventMouseMotion motion)
        {
            _paddleX = Mathf.Clamp(motion.Position.X, PaddleWidth / 2, Size.X - PaddleWidth / 2);
        }

        if (_game == ArcadeGame.Whack && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
        {
            WhackAt(click.Position);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Color.FromHtml("#0c0f16"));
        switch (_game)
        {
            case ArcadeGame.Breakout:
                DrawBreakout();
                break;
            case ArcadeGame.Whack:
                DrawWhack();
                break;
            default:
                DrawMaze();
                break;
        }

        DrawRect(new Rect2(Vector2.Zero, Size), new Color(1, 1, 1, 0.15f), false, 2f);

        if (_running && _countdown > 0)
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.55f));
            string text = Mathf.CeilToInt(_countdown).ToString();
            DrawString(UiTheme.Title, new Vector2(0, Size.Y / 2 + 30), text, HorizontalAlignment.Center, Size.X, 84, UiTheme.Gold);
        }
    }

    // ───────────── 벽돌깨기 ─────────────

    private const int BrickCols = 10;
    private const int BrickRows = 5;
    private const float PaddleWidth = 96f;
    private const float BallRadius = 6f;
    private bool[,] _bricks = new bool[BrickCols, BrickRows];
    private float _paddleX;
    private Vector2 _ball;
    private Vector2 _velocity;
    private int _lives;
    private float _serveDelay;

    private float PaddleY => Size.Y - 24f;

    private Rect2 BrickRect(int c, int r)
    {
        float w = (Size.X - 20f) / BrickCols;
        return new Rect2(10 + c * w + 2, 30 + r * 22, w - 4, 16);
    }

    private void SetupBreakout()
    {
        _bricks = new bool[BrickCols, BrickRows];
        for (int c = 0; c < BrickCols; c++)
        {
            for (int r = 0; r < BrickRows; r++)
            {
                _bricks[c, r] = true;
            }
        }

        _paddleX = Size.X / 2;
        _lives = 3;
        Serve();
    }

    private void Serve()
    {
        _serveDelay = 0.5f;
        _ball = new Vector2(_paddleX, PaddleY - 20);
        _velocity = new Vector2(120f, -320f);
    }

    private void UpdateBreakout(float dt)
    {
        if (_paddleX <= 0)
        {
            _paddleX = Size.X / 2;
        }

        if (_serveDelay > 0)
        {
            _serveDelay -= dt;
            _ball = new Vector2(_paddleX, PaddleY - 20);
            return;
        }

        // 빠른 공이 벽돌을 뚫지 않도록 작은 걸음으로 나눠서 움직입니다.
        const int steps = 4;
        for (int i = 0; i < steps; i++)
        {
            _ball += _velocity * dt / steps;

            if (_ball.X < BallRadius || _ball.X > Size.X - BallRadius)
            {
                _velocity.X = -_velocity.X;
                _ball.X = Mathf.Clamp(_ball.X, BallRadius, Size.X - BallRadius);
            }

            if (_ball.Y < BallRadius)
            {
                _velocity.Y = Mathf.Abs(_velocity.Y);
            }

            // 패들: 맞은 위치에 따라 튕기는 각도가 달라집니다.
            if (_velocity.Y > 0 && _ball.Y + BallRadius >= PaddleY && _ball.Y < PaddleY + 10
                && Mathf.Abs(_ball.X - _paddleX) <= PaddleWidth / 2 + BallRadius)
            {
                float offset = (_ball.X - _paddleX) / (PaddleWidth / 2);
                float speed = _velocity.Length();
                _velocity = new Vector2(offset * 0.8f, -1f).Normalized() * speed;
                Audio.Sfx.Play("play", -12f, 1.4f);
            }

            if (_ball.Y > Size.Y + 10)
            {
                _lives--;
                if (_lives <= 0)
                {
                    Finish();
                    return;
                }

                Serve();
                return;
            }

            HitBricks();
        }
    }

    private void HitBricks()
    {
        for (int c = 0; c < BrickCols; c++)
        {
            for (int r = 0; r < BrickRows; r++)
            {
                if (!_bricks[c, r])
                {
                    continue;
                }

                var rect = BrickRect(c, r).Grow(BallRadius);
                if (!rect.HasPoint(_ball))
                {
                    continue;
                }

                _bricks[c, r] = false;
                Score++;
                Audio.Sfx.Play("draw", -10f, 1.2f + r * 0.08f);

                // 덜 파고든 쪽으로 튕깁니다.
                float dx = Mathf.Min(_ball.X - rect.Position.X, rect.End.X - _ball.X);
                float dy = Mathf.Min(_ball.Y - rect.Position.Y, rect.End.Y - _ball.Y);
                if (dx < dy)
                {
                    _velocity.X = -_velocity.X;
                }
                else
                {
                    _velocity.Y = -_velocity.Y;
                }

                _velocity *= 1.02f;
                if (Score >= BrickCols * BrickRows)
                {
                    // 다 깨면 남은 시간만큼 보너스를 주고 끝냅니다.
                    Score += Mathf.RoundToInt(TimeLeft);
                    Finish();
                }

                return;
            }
        }
    }

    private void DrawBreakout()
    {
        var colors = new[] { UiTheme.CardColor(CardColor.Red), UiTheme.CardColor(CardColor.Yellow), UiTheme.CardColor(CardColor.Green), UiTheme.CardColor(CardColor.Blue), UiTheme.Gold };
        for (int c = 0; c < BrickCols; c++)
        {
            for (int r = 0; r < BrickRows; r++)
            {
                if (_bricks[c, r])
                {
                    var rect = BrickRect(c, r);
                    DrawRect(rect, colors[r].Darkened(0.1f));
                    DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, 4)), colors[r].Lightened(0.25f));
                }
            }
        }

        var paddle = new Rect2(_paddleX - PaddleWidth / 2, PaddleY, PaddleWidth, 10);
        DrawRect(paddle, Colors.White);
        DrawCircle(_ball, BallRadius, UiTheme.Gold, true, -1f, true);

        for (int i = 0; i < 3; i++)
        {
            DrawCircle(new Vector2(Size.X - 16 - i * 16, 14), 5f, i < _lives ? UiTheme.Gold : new Color(1, 1, 1, 0.15f), true, -1f, true);
        }
    }

    // ───────────── 두더지 카드 ─────────────

    private List<ArcadeRules.Mole> _moles = new();
    private bool[] _moleHit = Array.Empty<bool>();

    private Rect2 HoleRect(int hole)
    {
        float cell = Mathf.Min(Size.X, Size.Y) / 3f;
        var origin = new Vector2((Size.X - cell * 3) / 2, 0);
        return new Rect2(origin + new Vector2(hole % 3 * cell, hole / 3 * cell), new Vector2(cell, cell));
    }

    private void WhackAt(Vector2 point)
    {
        for (int i = 0; i < _moles.Count; i++)
        {
            var mole = _moles[i];
            if (_moleHit[i] || _time < mole.Time || _time > mole.Time + ArcadeRules.MoleLife || !HoleRect(mole.Hole).HasPoint(point))
            {
                continue;
            }

            _moleHit[i] = true;
            Score = Math.Max(0, Score + (mole.Bomb ? -2 : 1));
            Audio.Sfx.Play(mole.Bomb ? "error" : "play_action", mole.Bomb ? -4f : -8f, 1.2f);
            return;
        }
    }

    private void DrawWhack()
    {
        for (int hole = 0; hole < 9; hole++)
        {
            var rect = HoleRect(hole);
            var center = rect.GetCenter() + new Vector2(0, rect.Size.Y * 0.28f);
            DrawSetTransform(center, 0, new Vector2(1f, 0.35f));
            DrawCircle(Vector2.Zero, rect.Size.X * 0.36f, Color.FromHtml("#1b2130"), true, -1f, true);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        for (int i = 0; i < _moles.Count; i++)
        {
            var mole = _moles[i];
            float age = _time - mole.Time;
            if (age < 0 || age > ArcadeRules.MoleLife || _countdown > 0)
            {
                continue;
            }

            // 튀어올랐다가 들어갑니다.
            float up = Mathf.Sin(Mathf.Pi * age / ArcadeRules.MoleLife);
            var rect = HoleRect(mole.Hole);
            var size = new Vector2(rect.Size.X * 0.42f, rect.Size.X * 0.58f);
            var bottom = rect.GetCenter() + new Vector2(0, rect.Size.Y * 0.28f);
            var card = new Rect2(bottom - new Vector2(size.X / 2, size.Y * up), new Vector2(size.X, size.Y * up));
            if (card.Size.Y < 2)
            {
                continue;
            }

            var bg = _moleHit[i] ? new Color(0.3f, 0.3f, 0.3f) : mole.Bomb ? Color.FromHtml("#b3261e") : Color.FromHtml("#1d2340");
            DrawRect(card, bg);
            DrawRect(card, mole.Bomb ? Colors.White : UiTheme.Gold, false, 2f);
            var mid = card.GetCenter();
            if (mole.Bomb)
            {
                DrawString(UiTheme.Title, new Vector2(card.Position.X, mid.Y + 14), "!", HorizontalAlignment.Center, card.Size.X, 36, Colors.White);
            }
            else if (card.Size.Y > 20)
            {
                SuitIcons.DrawStar(this, mid, Mathf.Min(14f, card.Size.Y * 0.25f), UiTheme.Gold);
            }
        }
    }

    // ───────────── 미로 탈출 ─────────────

    private bool[,] _maze = new bool[1, 1];
    private int[,] _distance = new int[1, 1];
    private Vector2I _pos;
    private bool _escaped;
    private float _moveCooldown;

    private int MazeCells => ArcadeRules.MazeSize * 2 + 1;

    private Vector2I Exit => new(MazeCells - 2, MazeCells - 2);

    private void SetupMaze(int seed)
    {
        _maze = ArcadeRules.Maze(seed);
        _pos = new Vector2I(1, 1);
        _escaped = false;
        _moveCooldown = 0;

        // 출구까지 남은 거리(칸)를 미리 구해서 진행도를 셉니다.
        int n = MazeCells;
        _distance = new int[n, n];
        for (int x = 0; x < n; x++)
        {
            for (int y = 0; y < n; y++)
            {
                _distance[x, y] = -1;
            }
        }

        var queue = new Queue<Vector2I>();
        queue.Enqueue(Exit);
        _distance[Exit.X, Exit.Y] = 0;
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var d in new[] { Vector2I.Right, Vector2I.Left, Vector2I.Up, Vector2I.Down })
            {
                var q = p + d;
                if (q.X >= 0 && q.Y >= 0 && q.X < n && q.Y < n && !_maze[q.X, q.Y] && _distance[q.X, q.Y] < 0)
                {
                    _distance[q.X, q.Y] = _distance[p.X, p.Y] + 1;
                    queue.Enqueue(q);
                }
            }
        }
    }

    private float MazeProgress()
    {
        int start = _distance[1, 1];
        int now = _distance[_pos.X, _pos.Y];
        return start <= 0 || now < 0 ? 0f : 1f - (float)now / start;
    }

    private void UpdateMaze(float dt)
    {
        _moveCooldown -= dt;
        if (_moveCooldown > 0)
        {
            return;
        }

        var dir = Vector2I.Zero;
        if (Input.IsKeyPressed(Key.Right) || Input.IsKeyPressed(Key.D))
        {
            dir = Vector2I.Right;
        }
        else if (Input.IsKeyPressed(Key.Left) || Input.IsKeyPressed(Key.A))
        {
            dir = Vector2I.Left;
        }
        else if (Input.IsKeyPressed(Key.Up) || Input.IsKeyPressed(Key.W))
        {
            dir = Vector2I.Up;
        }
        else if (Input.IsKeyPressed(Key.Down) || Input.IsKeyPressed(Key.S))
        {
            dir = Vector2I.Down;
        }

        if (dir == Vector2I.Zero)
        {
            return;
        }

        var next = _pos + dir;
        if (next.X < 0 || next.Y < 0 || next.X >= MazeCells || next.Y >= MazeCells || _maze[next.X, next.Y])
        {
            return;
        }

        _pos = next;
        _moveCooldown = 0.075f;
        if (_pos == Exit)
        {
            _escaped = true;
            Audio.Sfx.Play("augment_gain", -4f);
            Finish();
        }
    }

    private void DrawMaze()
    {
        int n = MazeCells;
        float cell = Mathf.Floor(Mathf.Min(Size.X, Size.Y) / n);
        var origin = ((Size - new Vector2(cell * n, cell * n)) / 2).Floor();
        for (int x = 0; x < n; x++)
        {
            for (int y = 0; y < n; y++)
            {
                if (_maze[x, y])
                {
                    DrawRect(new Rect2(origin + new Vector2(x, y) * cell, new Vector2(cell, cell)), Color.FromHtml("#2b3550"));
                }
            }
        }

        var exit = new Rect2(origin + new Vector2(Exit.X, Exit.Y) * cell, new Vector2(cell, cell));
        float pulse = 0.6f + 0.4f * Mathf.Sin(_time * 6f);
        DrawRect(exit.Grow(-1), new Color(UiTheme.Gold, pulse));
        var me = origin + (new Vector2(_pos.X, _pos.Y) + new Vector2(0.5f, 0.5f)) * cell;
        DrawCircle(me, cell * 0.38f, Color.FromHtml("#7dd3fc"), true, -1f, true);
    }
}
