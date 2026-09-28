using System;
using System.Collections.Generic;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 미니게임 대회의 게임 화면입니다. (ArcadeOverlay가 씁니다)
/// 모두가 같은 시드로 같은 판을 하고, 끝나면 점수를 알려 줍니다.
/// - 벽돌깨기: 마우스로 패들, 공 3개, 점수 = 깬 벽돌 수 (아이템: 멀티볼 · 넓은 패들 · 불공, 공은 점점 빨라짐)
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

    /// <param name="level">이번 판에서 같은 종목을 몇 번째 하는지(0부터)입니다. 벽돌깨기는 높을수록 공이 빨라집니다.</param>
    public void Begin(ArcadeGame game, int seed, int level = 0)
    {
        _level = level;
        _game = game;
        _time = 0;
        _countdown = 2.0f;
        Score = 0;
        _running = true;
        switch (game)
        {
            case ArcadeGame.Breakout:
                SetupBreakout(seed);
                break;
            case ArcadeGame.Whack:
                _moles = ArcadeRules.Moles(seed);
                _moleHitAt = new float[_moles.Count];
                Array.Fill(_moleHitAt, -1f);
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
    // 벽돌을 깨면 가끔 아이템이 떨어집니다. (멀티볼 · 넓은 패들 · 관통 불공)
    // 공은 시간이 갈수록 빨라지고, 같은 판에서 벽돌깨기를 다시 할 때마다 처음 속도도 빨라집니다.

    private const int BrickCols = 10;
    private const int BrickRows = 6;
    private const float BasePaddleWidth = 96f;
    private const float BallRadius = 6f;
    private const float WideTime = 7f;
    private const float FireTime = 5f;
    private const int MaxBalls = 6;

    private enum PowerUp
    {
        None,
        MultiBall,
        Wide,
        Fire,
    }

    private sealed class Ball
    {
        public Vector2 Pos;
        public Vector2 Vel;
    }

    private sealed class Drop
    {
        public Vector2 Pos;
        public PowerUp Kind;
    }

    /// <summary>벽돌 내구도입니다. 0이면 깨진 벽돌, 맨 윗줄은 2번 맞아야 깨집니다.</summary>
    private int[,] _bricks = new int[BrickCols, BrickRows];

    /// <summary>벽돌마다 들어 있는 아이템입니다. 시드로 정해서 모두 같은 자리에 같은 아이템이 있습니다.</summary>
    private PowerUp[,] _brickItems = new PowerUp[BrickCols, BrickRows];

    private readonly List<Ball> _balls = new();
    private readonly List<Drop> _drops = new();
    private float _paddleX;
    private int _lives;
    private float _serveDelay;
    private float _wideLeft;
    private float _fireLeft;
    private int _level;
    private float _flash;
    private string _powerText = "";
    private float _powerTextLeft;

    private float PaddleWidth => _wideLeft > 0 ? BasePaddleWidth * 1.6f : BasePaddleWidth;

    private float PaddleY => Size.Y - 24f;

    /// <summary>지금 공 속도입니다. 20초 동안 1.0배 → 1.9배까지 올라가고, 판을 거듭할수록 시작 속도도 올라갑니다.</summary>
    private float TargetSpeed => 330f * (1f + 0.15f * _level) * (1f + 0.9f * Mathf.Clamp(_time / ArcadeRules.TimeLimit, 0f, 1f));

    private Rect2 BrickRect(int c, int r)
    {
        float w = (Size.X - 20f) / BrickCols;
        return new Rect2(10 + c * w + 2, 30 + r * 20, w - 4, 15);
    }

    private void SetupBreakout(int seed)
    {
        var rng = new Random(seed);
        _bricks = new int[BrickCols, BrickRows];
        _brickItems = new PowerUp[BrickCols, BrickRows];
        for (int c = 0; c < BrickCols; c++)
        {
            for (int r = 0; r < BrickRows; r++)
            {
                _bricks[c, r] = r == 0 ? 2 : 1;
                double roll = rng.NextDouble();
                _brickItems[c, r] = roll < 0.07 ? PowerUp.MultiBall : roll < 0.12 ? PowerUp.Wide : roll < 0.16 ? PowerUp.Fire : PowerUp.None;
            }
        }

        _drops.Clear();
        _wideLeft = 0;
        _fireLeft = 0;
        _powerTextLeft = 0;
        _paddleX = Size.X / 2;
        _lives = 3;
        Serve();
    }

    private void Serve()
    {
        _serveDelay = 0.5f;
        _balls.Clear();
        _balls.Add(new Ball { Pos = new Vector2(_paddleX, PaddleY - 20), Vel = new Vector2(0.35f, -1f).Normalized() * TargetSpeed });
    }

    private void UpdateBreakout(float dt)
    {
        if (_paddleX <= 0)
        {
            _paddleX = Size.X / 2;
        }

        _wideLeft = Mathf.Max(0, _wideLeft - dt);
        _fireLeft = Mathf.Max(0, _fireLeft - dt);
        _flash = Mathf.Max(0, _flash - dt * 3);
        _powerTextLeft = Mathf.Max(0, _powerTextLeft - dt);
        _paddleX = Mathf.Clamp(_paddleX, PaddleWidth / 2, Size.X - PaddleWidth / 2);
        UpdateDrops(dt);

        if (_serveDelay > 0)
        {
            _serveDelay -= dt;
            _balls[0].Pos = new Vector2(_paddleX, PaddleY - 20);
            return;
        }

        float speed = TargetSpeed;
        for (int b = _balls.Count - 1; b >= 0; b--)
        {
            var ball = _balls[b];
            // 속도는 시간에 맞춰 조금씩 올립니다. (방향은 그대로)
            ball.Vel = ball.Vel.Normalized() * Mathf.MoveToward(ball.Vel.Length(), speed, 60f * dt);

            // 빠른 공이 벽돌을 뚫지 않도록 작은 걸음으로 나눠서 움직입니다.
            int steps = Mathf.CeilToInt(ball.Vel.Length() * dt / 4f);
            bool lost = false;
            for (int i = 0; i < steps && !lost && _running; i++)
            {
                ball.Pos += ball.Vel * dt / steps;
                lost = StepBall(ball);
            }

            if (lost)
            {
                _balls.RemoveAt(b);
            }

            if (!_running)
            {
                return;
            }
        }

        if (_balls.Count == 0)
        {
            _lives--;
            if (_lives <= 0)
            {
                Finish();
                return;
            }

            Serve();
        }
    }

    /// <summary>공 하나를 한 걸음 움직인 뒤 벽·패들·벽돌과 부딪히는지 봅니다. 바닥으로 빠지면 true</summary>
    private bool StepBall(Ball ball)
    {
        if (ball.Pos.X < BallRadius || ball.Pos.X > Size.X - BallRadius)
        {
            ball.Vel.X = -ball.Vel.X;
            ball.Pos.X = Mathf.Clamp(ball.Pos.X, BallRadius, Size.X - BallRadius);
        }

        if (ball.Pos.Y < BallRadius)
        {
            ball.Vel.Y = Mathf.Abs(ball.Vel.Y);
        }

        // 패들: 맞은 위치에 따라 튕기는 각도가 달라집니다.
        if (ball.Vel.Y > 0 && ball.Pos.Y + BallRadius >= PaddleY && ball.Pos.Y < PaddleY + 10
            && Mathf.Abs(ball.Pos.X - _paddleX) <= PaddleWidth / 2 + BallRadius)
        {
            float offset = (ball.Pos.X - _paddleX) / (PaddleWidth / 2);
            ball.Vel = new Vector2(offset * 0.85f, -1f).Normalized() * ball.Vel.Length();
            Audio.Sfx.Play("play", -12f, 1.4f);
        }

        if (ball.Pos.Y > Size.Y + 10)
        {
            return true;
        }

        HitBricks(ball);
        return false;
    }

    private void HitBricks(Ball ball)
    {
        for (int c = 0; c < BrickCols; c++)
        {
            for (int r = 0; r < BrickRows; r++)
            {
                if (_bricks[c, r] <= 0)
                {
                    continue;
                }

                var rect = BrickRect(c, r).Grow(BallRadius);
                if (!rect.HasPoint(ball.Pos))
                {
                    continue;
                }

                // 불공은 한 번에 깨고 튕기지 않고 뚫고 지나갑니다.
                bool fire = _fireLeft > 0;
                _bricks[c, r] = fire ? 0 : _bricks[c, r] - 1;
                Audio.Sfx.Play("draw", -10f, 1.2f + r * 0.08f);
                if (_bricks[c, r] == 0)
                {
                    Score++;
                    if (_brickItems[c, r] != PowerUp.None)
                    {
                        _drops.Add(new Drop { Pos = BrickRect(c, r).GetCenter(), Kind = _brickItems[c, r] });
                    }
                }

                if (!fire)
                {
                    // 덜 파고든 쪽으로 튕깁니다.
                    float dx = Mathf.Min(ball.Pos.X - rect.Position.X, rect.End.X - ball.Pos.X);
                    float dy = Mathf.Min(ball.Pos.Y - rect.Position.Y, rect.End.Y - ball.Pos.Y);
                    if (dx < dy)
                    {
                        ball.Vel.X = -ball.Vel.X;
                    }
                    else
                    {
                        ball.Vel.Y = -ball.Vel.Y;
                    }
                }

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

    /// <summary>떨어지는 아이템을 움직이고, 패들로 받으면 효과를 켭니다.</summary>
    private void UpdateDrops(float dt)
    {
        for (int i = _drops.Count - 1; i >= 0; i--)
        {
            var drop = _drops[i];
            drop.Pos.Y += 150f * dt;
            bool caught = drop.Pos.Y >= PaddleY - 6 && drop.Pos.Y <= PaddleY + 14 && Mathf.Abs(drop.Pos.X - _paddleX) <= PaddleWidth / 2 + 10;
            if (caught)
            {
                ApplyPowerUp(drop.Kind);
                _drops.RemoveAt(i);
            }
            else if (drop.Pos.Y > Size.Y + 12)
            {
                _drops.RemoveAt(i);
            }
        }
    }

    private void ApplyPowerUp(PowerUp kind)
    {
        _flash = 1f;
        _powerTextLeft = 1.2f;
        Audio.Sfx.Play("augment_gain", -8f, 1.3f);
        switch (kind)
        {
            case PowerUp.MultiBall:
            {
                // 공마다 좌우로 하나씩 더 만듭니다.
                _powerText = Loc.Tr("멀티볼!");
                foreach (var ball in _balls.ToArray())
                {
                    foreach (float angle in new[] { -0.45f, 0.45f })
                    {
                        if (_balls.Count >= MaxBalls)
                        {
                            break;
                        }

                        var vel = ball.Vel.Rotated(angle);
                        if (vel.Y > -60f)
                        {
                            vel = new Vector2(vel.X, -Mathf.Abs(vel.Y) - 60f);
                        }

                        _balls.Add(new Ball { Pos = ball.Pos, Vel = vel });
                    }
                }

                break;
            }

            case PowerUp.Wide:
                _powerText = Loc.Tr("패들 확장!");
                _wideLeft = WideTime;
                break;
            default:
                _powerText = Loc.Tr("불공! 벽돌을 뚫습니다");
                _fireLeft = FireTime;
                break;
        }
    }

    private static Color PowerColor(PowerUp kind) => kind switch
    {
        PowerUp.MultiBall => Color.FromHtml("#5ec8f0"),
        PowerUp.Wide => Color.FromHtml("#8fdc6a"),
        _ => Color.FromHtml("#ff8a3d"),
    };

    private static string PowerLetter(PowerUp kind) => kind switch
    {
        PowerUp.MultiBall => "M",
        PowerUp.Wide => "W",
        _ => "F",
    };

    private void DrawBreakout()
    {
        var colors = new[]
        {
            UiTheme.Silver, UiTheme.CardColor(CardColor.Red), UiTheme.CardColor(CardColor.Yellow),
            UiTheme.CardColor(CardColor.Green), UiTheme.CardColor(CardColor.Blue), UiTheme.Gold,
        };
        for (int c = 0; c < BrickCols; c++)
        {
            for (int r = 0; r < BrickRows; r++)
            {
                if (_bricks[c, r] <= 0)
                {
                    continue;
                }

                var rect = BrickRect(c, r);
                var color = colors[r];
                if (r == 0 && _bricks[c, r] == 1)
                {
                    color = color.Darkened(0.35f); // 한 번 맞은 단단한 벽돌
                }

                DrawRect(rect, color.Darkened(0.1f));
                DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, 4)), color.Lightened(0.25f));
                if (_brickItems[c, r] != PowerUp.None)
                {
                    // 아이템이 든 벽돌은 가운데에 작은 보석이 박혀 있습니다.
                    DrawCircle(rect.GetCenter(), 3.5f, PowerColor(_brickItems[c, r]).Lightened(0.3f), true, -1f, true);
                }
            }
        }

        foreach (var drop in _drops)
        {
            var box = new Rect2(drop.Pos - new Vector2(13, 8), new Vector2(26, 16));
            var color = PowerColor(drop.Kind);
            DrawStyleBox(UiTheme.Box(color.Darkened(0.2f), color.Lightened(0.4f), 1, 8, 0), box);
            DrawString(UiTheme.Title, new Vector2(box.Position.X, box.Position.Y + 13), PowerLetter(drop.Kind), HorizontalAlignment.Center, box.Size.X, 13, Colors.White);
        }

        var paddleColor = _wideLeft > 0 ? PowerColor(PowerUp.Wide).Lightened(0.3f) : Colors.White;
        var paddle = new Rect2(_paddleX - PaddleWidth / 2, PaddleY, PaddleWidth, 10);
        DrawStyleBox(UiTheme.Box(paddleColor, paddleColor, 0, 5, 0), paddle);

        foreach (var ball in _balls)
        {
            if (_fireLeft > 0)
            {
                DrawCircle(ball.Pos - ball.Vel.Normalized() * 6, BallRadius * 1.1f, new Color(1f, 0.45f, 0.1f, 0.35f), true, -1f, true);
                DrawCircle(ball.Pos, BallRadius + 1.5f, Color.FromHtml("#ff8a3d"), true, -1f, true);
                DrawCircle(ball.Pos, BallRadius * 0.55f, Color.FromHtml("#fff1c1"), true, -1f, true);
            }
            else
            {
                DrawCircle(ball.Pos, BallRadius, UiTheme.Gold, true, -1f, true);
            }
        }

        for (int i = 0; i < 3; i++)
        {
            DrawCircle(new Vector2(Size.X - 16 - i * 16, 14), 5f, i < _lives ? UiTheme.Gold : new Color(1, 1, 1, 0.15f), true, -1f, true);
        }

        // 속도계: 공이 얼마나 빨라졌는지 보여 줍니다.
        float ratio = _balls.Count > 0 ? _balls[0].Vel.Length() / 330f : 1f;
        DrawString(UiTheme.Bold, new Vector2(10, 20), Loc.Tr($"속도 x{ratio:0.0}"), HorizontalAlignment.Left, -1, 12,
            ratio >= 1.6f ? UiTheme.Danger : UiTheme.TextDim);

        if (_powerTextLeft > 0)
        {
            float a = Mathf.Clamp(_powerTextLeft / 0.4f, 0, 1);
            DrawString(UiTheme.Title, new Vector2(0, Size.Y * 0.55f), _powerText, HorizontalAlignment.Center, Size.X, 30, new Color(UiTheme.Gold, a));
        }

        if (_flash > 0)
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(1, 1, 1, 0.12f * _flash));
        }
    }

    // ───────────── 두더지 카드 ─────────────
    // 카드가 구멍 안에서 쑥 올라왔다가 다시 내려갑니다. (크기는 그대로, 구멍 앞쪽 흙이 카드 아랫부분을 가립니다)

    private List<ArcadeRules.Mole> _moles = new();
    private float[] _moleHitAt = Array.Empty<float>();

    private static readonly Color WhackGround = Color.FromHtml("#17261c");

    private Rect2 HoleRect(int hole)
    {
        float cell = Mathf.Min(Size.X, Size.Y) / 3f;
        var origin = new Vector2((Size.X - cell * 3) / 2, 0);
        return new Rect2(origin + new Vector2(hole % 3 * cell, hole / 3 * cell), new Vector2(cell, cell));
    }

    private Vector2 HoleCenter(Rect2 rect) => rect.GetCenter() + new Vector2(0, rect.Size.Y * 0.3f);

    private Vector2 CardSize(Rect2 rect) => new(rect.Size.X * 0.4f, rect.Size.X * 0.56f);

    /// <summary>카드가 구멍 위로 얼마나 올라왔는지(0~1)입니다. 빠르게 튀어 오르고, 잠깐 머문 뒤 내려갑니다. 맞으면 쑥 들어갑니다.</summary>
    private float MoleRise(int i)
    {
        var mole = _moles[i];
        float age = _time - mole.Time;
        float life = ArcadeRules.MoleLife;
        if (age < 0 || age > life)
        {
            return 0;
        }

        if (_moleHitAt[i] >= 0)
        {
            float since = _time - _moleHitAt[i];
            float atHit = RiseCurve(_moleHitAt[i] - mole.Time, life);
            return Mathf.Max(0, atHit * (1 - since / 0.12f));
        }

        return RiseCurve(age, life);
    }

    private static float RiseCurve(float age, float life)
    {
        const float up = 0.16f;
        const float down = 0.2f;
        if (age < up)
        {
            // 살짝 넘쳤다가 자리 잡는 튀어오름
            float t = age / up;
            float c = 1.7f;
            return 1 + (c + 1) * Mathf.Pow(t - 1, 3) + c * Mathf.Pow(t - 1, 2);
        }

        if (age > life - down)
        {
            float t = (life - age) / down;
            return t * t;
        }

        return 1;
    }

    private void WhackAt(Vector2 point)
    {
        for (int i = 0; i < _moles.Count; i++)
        {
            var mole = _moles[i];
            if (_moleHitAt[i] >= 0 || MoleRise(i) < 0.35f)
            {
                continue;
            }

            var rect = HoleRect(mole.Hole);
            var size = CardSize(rect);
            var bottom = HoleCenter(rect);
            var card = new Rect2(bottom - new Vector2(size.X / 2, size.Y * MoleRise(i)), size).Grow(8);
            if (!card.HasPoint(point))
            {
                continue;
            }

            _moleHitAt[i] = _time;
            Score = Math.Max(0, Score + (mole.Bomb ? -2 : 1));
            Audio.Sfx.Play(mole.Bomb ? "error" : "play_action", mole.Bomb ? -4f : -8f, 1.2f);
            return;
        }
    }

    private void DrawWhack()
    {
        // 0) 잔디 바닥
        DrawRect(new Rect2(Vector2.Zero, Size), WhackGround);

        // 1) 구멍 안쪽 (어두운 타원)
        for (int hole = 0; hole < 9; hole++)
        {
            var rect = HoleRect(hole);
            var center = HoleCenter(rect);
            float rx = rect.Size.X * 0.34f;
            DrawSetTransform(center, 0, new Vector2(1f, 0.32f));
            DrawCircle(Vector2.Zero, rx + 5, Color.FromHtml("#3a2c22"), true, -1f, true);
            DrawCircle(Vector2.Zero, rx, Color.FromHtml("#07080c"), true, -1f, true);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        // 2) 카드 (크기는 그대로, 올라온 만큼 위로)
        for (int i = 0; i < _moles.Count; i++)
        {
            if (_countdown > 0)
            {
                break;
            }

            float rise = MoleRise(i);
            if (rise <= 0.01f)
            {
                continue;
            }

            var mole = _moles[i];
            var rect = HoleRect(mole.Hole);
            var size = CardSize(rect);
            var bottom = HoleCenter(rect);
            var card = new Rect2(bottom - new Vector2(size.X / 2, size.Y * rise), size);
            bool hit = _moleHitAt[i] >= 0;
            var bg = hit ? new Color(0.35f, 0.35f, 0.38f) : mole.Bomb ? Color.FromHtml("#b3261e") : Color.FromHtml("#1d2340");
            var border = mole.Bomb ? Colors.White : UiTheme.Gold;
            DrawStyleBox(UiTheme.Box(bg, border, 2, 7, 0), card);
            DrawStyleBox(UiTheme.Box(new Color(0, 0, 0, 0), new Color(border, 0.35f), 1, 5, 0), card.Grow(-5));
            var mid = card.Position + new Vector2(size.X / 2, size.Y * 0.45f);
            if (mole.Bomb)
            {
                DrawCircle(mid + new Vector2(0, 3), size.X * 0.22f, Color.FromHtml("#1a1a1a"), true, -1f, true);
                DrawLine(mid + new Vector2(size.X * 0.12f, -size.X * 0.14f), mid + new Vector2(size.X * 0.22f, -size.X * 0.3f), Color.FromHtml("#e8c07a"), 2, true);
                DrawCircle(mid + new Vector2(size.X * 0.22f, -size.X * 0.3f), 3 + Mathf.Sin(_time * 40) * 1.2f, Color.FromHtml("#ffb347"), true, -1f, true);
            }
            else
            {
                SuitIcons.DrawStar(this, mid, size.X * 0.24f, hit ? UiTheme.TextDim : UiTheme.Gold);
            }

            if (hit)
            {
                float since = _time - _moleHitAt[i];
                DrawString(UiTheme.Title, new Vector2(card.Position.X - 20, card.Position.Y - 6 - since * 60), mole.Bomb ? "-2" : "+1",
                    HorizontalAlignment.Center, card.Size.X + 40, 26, mole.Bomb ? UiTheme.Danger : UiTheme.Gold);
            }
        }

        // 3) 구멍 앞쪽 흙: 구멍 가운데 줄 아래를 덮어서 카드가 구멍 속에서 올라오는 것처럼 보이게 합니다.
        for (int hole = 0; hole < 9; hole++)
        {
            var rect = HoleRect(hole);
            var center = HoleCenter(rect);
            float rx = rect.Size.X * 0.34f;
            DrawRect(new Rect2(rect.Position.X, center.Y, rect.Size.X, rect.End.Y - center.Y), WhackGround);
            // 앞쪽 테두리 (아래 반원)
            var points = new List<Vector2>();
            for (int k = 0; k <= 24; k++)
            {
                float angle = Mathf.Pi * k / 24f;
                points.Add(center + new Vector2(Mathf.Cos(angle) * (rx + 5), Mathf.Sin(angle) * (rx + 5) * 0.32f));
            }

            for (int k = 24; k >= 0; k--)
            {
                float angle = Mathf.Pi * k / 24f;
                points.Add(center + new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * rx * 0.32f));
            }

            DrawColoredPolygon(points.ToArray(), Color.FromHtml("#5a4432"));
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
