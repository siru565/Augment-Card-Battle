using System;
using System.Collections.Generic;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 화면 연출 전용 레이어입니다. 화면 흔들림, 번쩍임, 파티클, 빛줄기, 충격파, 떠오르는 글자, 날아가는 카드를 담당합니다.
/// 입력은 받지 않으며(마우스 통과), 게임 규칙과는 전혀 상관이 없습니다.
/// </summary>
public partial class FxLayer : Control
{
    private struct Particle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public Color Color;
        public float Life;
        public float MaxLife;
        public float Size;
        public float Gravity;
        public bool Square;
        public float Spin;
    }

    private sealed class Timed
    {
        public Vector2 Position;
        public Color Color;
        public float Age;
        public float Duration;
        public float Radius;
        public bool Rays;
    }

    private readonly List<Particle> _particles = new();
    private readonly List<Timed> _effects = new();
    private readonly Random _rng = new();
    private ColorRect _flash = null!;

    private Control? _shakeTarget;
    private float _shakeStrength;
    private float _shakeTime;
    private float _shakeDuration;

    public FxLayer()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        _flash = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = MouseFilterEnum.Ignore };
        _flash.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_flash);
    }

    /// <summary>흔들 노드를 정합니다. (게임 화면 전체를 감싼 컨테이너)</summary>
    public void SetShakeTarget(Control target) => _shakeTarget = target;

    // ───────────── 기본 효과 ─────────────

    /// <summary>화면을 흔듭니다. 이미 흔들리는 중이면 더 센 쪽을 따릅니다.</summary>
    public void Shake(float strength, float duration)
    {
        if (strength < _shakeStrength * (1 - _shakeTime / Mathf.Max(_shakeDuration, 0.01f)))
        {
            return;
        }

        _shakeStrength = strength;
        _shakeDuration = duration;
        _shakeTime = 0f;
    }

    /// <summary>화면 전체를 한 번 번쩍입니다.</summary>
    public void Flash(Color color, float alpha, float duration)
    {
        _flash.Color = color with { A = alpha };
        var tween = CreateTween();
        tween.TweenProperty(_flash, "color:a", 0f, duration).SetEase(Tween.EaseType.Out);
    }

    /// <summary>한 점에서 파티클을 사방으로 터뜨립니다.</summary>
    public void Burst(Vector2 at, Color color, int count = 24, float speed = 320f, float size = 5f, float gravity = 380f)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = (float)(_rng.NextDouble() * Math.Tau);
            float v = speed * (0.35f + (float)_rng.NextDouble() * 0.8f);
            float life = 0.45f + (float)_rng.NextDouble() * 0.55f;
            _particles.Add(new Particle
            {
                Position = at,
                Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * v,
                Color = color.Lightened((float)_rng.NextDouble() * 0.4f),
                Life = life,
                MaxLife = life,
                Size = size * (0.6f + (float)_rng.NextDouble() * 0.8f),
                Gravity = gravity,
                Square = _rng.Next(3) == 0,
                Spin = (float)_rng.NextDouble() * 6f,
            });
        }
    }

    /// <summary>승리 연출용 색종이입니다. 화면 위에서 쏟아집니다.</summary>
    public void Confetti(int count = 160)
    {
        var colors = new[] { UiTheme.Gold, UiTheme.Prism, Color.FromHtml("#ff6a3d"), Color.FromHtml("#36c47c"), Color.FromHtml("#22b4dc"), Colors.White };
        for (int i = 0; i < count; i++)
        {
            float life = 2.2f + (float)_rng.NextDouble() * 1.8f;
            _particles.Add(new Particle
            {
                Position = new Vector2((float)_rng.NextDouble() * Size.X, -20 - (float)_rng.NextDouble() * 300),
                Velocity = new Vector2(((float)_rng.NextDouble() - 0.5f) * 160, 120 + (float)_rng.NextDouble() * 160),
                Color = colors[_rng.Next(colors.Length)],
                Life = life,
                MaxLife = life,
                Size = 5 + (float)_rng.NextDouble() * 5,
                Gravity = 60,
                Square = true,
                Spin = ((float)_rng.NextDouble() - 0.5f) * 14f,
            });
        }
    }

    /// <summary>한 점에서 퍼져 나가는 충격파 고리입니다.</summary>
    public void Ring(Vector2 at, Color color, float radius = 180f, float duration = 0.55f) =>
        _effects.Add(new Timed { Position = at, Color = color, Duration = duration, Radius = radius });

    /// <summary>한 점에서 빙글 도는 빛줄기입니다. (각성 연출)</summary>
    public void Rays(Vector2 at, Color color, float radius = 420f, float duration = 1.4f) =>
        _effects.Add(new Timed { Position = at, Color = color, Duration = duration, Radius = radius, Rays = true });

    /// <summary>글자가 톡 튀어나왔다가 떠오르며 사라집니다.</summary>
    public void FloatText(Vector2 at, string text, Color color, int fontSize = 34, float hold = 0.6f)
    {
        var label = UiTheme.MakeLabel(text, fontSize, color, bold: true);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("outline_size", Math.Max(6, fontSize / 5));
        label.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(label);
        label.Size = label.GetCombinedMinimumSize();
        label.PivotOffset = label.Size / 2;
        label.Position = at - label.Size / 2;
        label.Scale = new Vector2(0.3f, 0.3f);

        var tween = CreateTween();
        tween.TweenProperty(label, "scale", Vector2.One * 1.15f, 0.16).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(label, "scale", Vector2.One, 0.1);
        tween.TweenInterval(hold);
        tween.Parallel().TweenProperty(label, "position:y", label.Position.Y - 46, hold + 0.5).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(label, "modulate:a", 0f, 0.35);
        tween.TweenCallback(Callable.From(label.QueueFree));
    }

    /// <summary>카드 한 장을 from에서 to로 날립니다. card가 null이면 뒷면으로 날립니다.</summary>
    public void FlyCard(Vector2 from, Vector2 to, Card? card, float duration = 0.32f, float delay = 0f, Vector2? size = null)
    {
        var cardSize = size ?? new Vector2(78, 112);
        var view = new CardView
        {
            Card = card,
            FaceDown = card == null,
            MouseFilter = MouseFilterEnum.Ignore,
            Size = cardSize,
            PivotOffset = cardSize / 2,
            Position = from - cardSize / 2,
            Rotation = (float)(_rng.NextDouble() - 0.5) * 0.6f,
            Modulate = delay > 0 ? new Color(1, 1, 1, 0) : Colors.White,
        };
        view.CustomMinimumSize = Vector2.Zero;
        AddChild(view);

        var tween = CreateTween();
        if (delay > 0)
        {
            tween.TweenInterval(delay);
            tween.TweenProperty(view, "modulate:a", 1f, 0.01);
        }

        tween.TweenProperty(view, "position", to - cardSize / 2, duration).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(view, "rotation", (float)(_rng.NextDouble() - 0.5) * 0.3f, duration);
        tween.Parallel().TweenProperty(view, "scale", new Vector2(0.85f, 0.85f), duration);
        tween.TweenProperty(view, "modulate:a", 0f, 0.12);
        tween.TweenCallback(Callable.From(view.QueueFree));
    }

    // ───────────── 매 프레임 ─────────────

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        UpdateShake(dt);

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life -= dt;
            if (p.Life <= 0)
            {
                _particles.RemoveAt(i);
                continue;
            }

            p.Velocity += new Vector2(0, p.Gravity * dt);
            p.Velocity *= 1f - 1.2f * dt;
            p.Position += p.Velocity * dt;
            _particles[i] = p;
        }

        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            _effects[i].Age += dt;
            if (_effects[i].Age >= _effects[i].Duration)
            {
                _effects.RemoveAt(i);
            }
        }

        QueueRedraw();
    }

    private void UpdateShake(float dt)
    {
        if (_shakeTarget == null)
        {
            return;
        }

        if (_shakeTime >= _shakeDuration)
        {
            if (_shakeStrength > 0)
            {
                _shakeStrength = 0;
                _shakeTarget.Position = Vector2.Zero;
            }

            return;
        }

        _shakeTime += dt;
        float k = 1f - Mathf.Clamp(_shakeTime / _shakeDuration, 0f, 1f);
        float power = _shakeStrength * k * k;
        _shakeTarget.Position = new Vector2(
            ((float)_rng.NextDouble() * 2 - 1) * power,
            ((float)_rng.NextDouble() * 2 - 1) * power);
    }

    public override void _Draw()
    {
        foreach (var effect in _effects)
        {
            float t = effect.Age / effect.Duration;
            if (effect.Rays)
            {
                DrawRays(effect, t);
            }
            else
            {
                float r = effect.Radius * Mathf.Sqrt(t);
                DrawArc(effect.Position, r, 0, Mathf.Tau, 64, effect.Color with { A = (1 - t) * 0.9f }, 10f * (1 - t) + 2f, true);
            }
        }

        foreach (var p in _particles)
        {
            float a = Mathf.Clamp(p.Life / p.MaxLife, 0f, 1f);
            var color = p.Color with { A = p.Color.A * a };
            if (p.Square)
            {
                float angle = p.Spin * p.Life;
                var half = new Vector2(p.Size, p.Size * 0.6f);
                var corners = new[] { -half, new Vector2(half.X, -half.Y), half, new Vector2(-half.X, half.Y) };
                for (int i = 0; i < 4; i++)
                {
                    corners[i] = p.Position + corners[i].Rotated(angle);
                }

                DrawColoredPolygon(corners, color);
            }
            else
            {
                DrawCircle(p.Position, p.Size * (0.5f + 0.5f * a), color);
                DrawCircle(p.Position, p.Size * 2.2f * a, color with { A = color.A * 0.18f });
            }
        }
    }

    private void DrawRays(Timed effect, float t)
    {
        // 빠르게 켜졌다가 천천히 사라지는 밝기입니다.
        float intensity = t < 0.15f ? t / 0.15f : 1f - (t - 0.15f) / 0.85f;
        const int count = 14;
        float spin = effect.Age * 0.9f;
        float r = effect.Radius * (0.6f + 0.4f * Mathf.Min(1f, t * 3f));

        for (int i = 0; i < count; i++)
        {
            float a = spin + i * Mathf.Tau / count;
            float width = 0.07f;
            var points = new[]
            {
                effect.Position,
                effect.Position + new Vector2(Mathf.Cos(a - width), Mathf.Sin(a - width)) * r,
                effect.Position + new Vector2(Mathf.Cos(a + width), Mathf.Sin(a + width)) * r,
            };
            var colors = new[]
            {
                effect.Color with { A = 0.55f * intensity },
                effect.Color with { A = 0f },
                effect.Color with { A = 0f },
            };
            DrawPolygon(points, colors);
        }

        DrawCircle(effect.Position, 70 * intensity, Colors.White with { A = 0.35f * intensity });
        DrawCircle(effect.Position, 130 * intensity, effect.Color with { A = 0.18f * intensity });
    }
}
