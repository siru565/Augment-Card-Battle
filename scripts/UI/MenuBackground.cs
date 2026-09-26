using System;
using System.Collections.Generic;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 메인 화면(로비·대기방) 배경입니다. 남보라 그라데이션, 마름모 격자, 은은하게 도는 빛,
/// 천천히 떠다니는 카드와 반짝이는 빛 알갱이를 그립니다. 입력은 받지 않습니다.
/// </summary>
public partial class MenuBackground : Control
{
    private sealed class Floater
    {
        public CardView View = null!;
        public Vector2 Velocity;
        public float Spin;
    }

    private struct Mote
    {
        public Vector2 Position;
        public float Speed;
        public float Phase;
        public float Size;
    }

    private const int CardCount = 14;
    private const int MoteCount = 70;

    private readonly List<Floater> _floaters = new();
    private readonly List<Mote> _motes = new();
    private readonly Random _rng = new();
    private float _t;

    public MenuBackground()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    private bool _spawned;

    /// <summary>카드와 빛 알갱이를 처음 크기가 정해졌을 때 한 번 뿌립니다.</summary>
    private void Spawn(Vector2 size)
    {
        _spawned = true;

        // 떠다니는 카드입니다. 앞면과 뒷면을 섞고, 뒤쪽 카드일수록 작고 흐리게 둡니다.
        var kinds = new[] { CardKind.Number, CardKind.Number, CardKind.Skip, CardKind.DrawTwo, CardKind.DrawOne, CardKind.DrawThree, CardKind.Reverse, CardKind.Awaken, CardKind.Frenzy, CardKind.Swap };
        for (int i = 0; i < CardCount; i++)
        {
            float depth = 0.45f + (float)_rng.NextDouble() * 0.55f;
            var kind = kinds[_rng.Next(kinds.Length)];
            var color = i % 5 == 4 ? CardColor.Wild : (CardColor)_rng.Next(4);
            if (color == CardColor.Wild)
            {
                kind = _rng.Next(2) == 0 ? CardKind.Wild : CardKind.WildDrawFour;
            }

            var card = new Card(-100 - i, color, kind, kind == CardKind.Number ? _rng.Next(10) : -1);
            var cardSize = new Vector2(96, 138) * depth;
            var view = new CardView
            {
                Card = card,
                FaceDown = _rng.Next(3) == 0,
                MouseFilter = MouseFilterEnum.Ignore,
                CustomMinimumSize = Vector2.Zero,
                Size = cardSize,
                PivotOffset = cardSize / 2,
                Position = new Vector2((float)_rng.NextDouble() * size.X, (float)_rng.NextDouble() * size.Y),
                Rotation = (float)(_rng.NextDouble() * Math.Tau),
                Modulate = new Color(1, 1, 1, 0.18f + 0.4f * (depth - 0.45f)),
            };
            AddChild(view);
            _floaters.Add(new Floater
            {
                View = view,
                Velocity = new Vector2(((float)_rng.NextDouble() - 0.5f) * 20f, -(8f + (float)_rng.NextDouble() * 18f)) * depth,
                Spin = ((float)_rng.NextDouble() - 0.5f) * 0.25f,
            });
        }

        for (int i = 0; i < MoteCount; i++)
        {
            _motes.Add(new Mote
            {
                Position = new Vector2((float)_rng.NextDouble() * size.X, (float)_rng.NextDouble() * size.Y),
                Speed = 6f + (float)_rng.NextDouble() * 18f,
                Phase = (float)(_rng.NextDouble() * Math.Tau),
                Size = 1f + (float)_rng.NextDouble() * 2.2f,
            });
        }
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree())
        {
            return;
        }

        // 부모(메인 화면 오버레이)를 꽉 채웁니다.
        if (GetParent() is Control parent)
        {
            Position = Vector2.Zero;
            Size = parent.Size;
        }

        var size = Size;
        if (size.X < 10)
        {
            return;
        }

        if (!_spawned)
        {
            Spawn(size);
        }

        float dt = (float)delta;
        _t += dt;

        foreach (var floater in _floaters)
        {
            var view = floater.View;
            view.Position += floater.Velocity * dt;
            view.Rotation += floater.Spin * dt;

            // 화면 위로 빠져나가면 아래에서 다시 올라옵니다.
            if (view.Position.Y < -view.Size.Y * 1.5f)
            {
                view.Position = new Vector2((float)_rng.NextDouble() * size.X, size.Y + view.Size.Y);
            }

            if (view.Position.X < -view.Size.X * 1.5f)
            {
                view.Position = view.Position with { X = size.X + view.Size.X };
            }
            else if (view.Position.X > size.X + view.Size.X * 1.5f)
            {
                view.Position = view.Position with { X = -view.Size.X };
            }
        }

        for (int i = 0; i < _motes.Count; i++)
        {
            var mote = _motes[i];
            mote.Position.Y -= mote.Speed * dt;
            if (mote.Position.Y < -5)
            {
                mote.Position = new Vector2((float)_rng.NextDouble() * size.X, size.Y + 5);
            }

            _motes[i] = mote;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        var center = new Vector2(size.X / 2, size.Y * 0.5f);

        // 가운데가 밝은 원형 그라데이션입니다. 천천히 숨 쉬듯 밝아졌다 어두워집니다.
        DrawRect(new Rect2(Vector2.Zero, size), UiTheme.TableEdge);
        float breathe = 1f + 0.04f * Mathf.Sin(_t * 0.6f);
        float maxR = size.Length() * 0.6f * breathe;
        for (int i = 40; i >= 0; i--)
        {
            float t = i / 40f;
            DrawCircle(center, maxR * t, UiTheme.TableEdge.Lerp(UiTheme.TableCenter, 1f - t));
        }

        // 옅은 마름모 격자가 아주 천천히 흘러갑니다.
        var line = new Color(UiTheme.Gold, 0.05f);
        float step = size.X / 14f;
        float shift = _t * 6f % step;
        for (float x = -size.Y - step; x < size.X + size.Y; x += step)
        {
            DrawLine(new Vector2(x + shift, 0), new Vector2(x + shift + size.Y, size.Y), line, 2f, true);
            DrawLine(new Vector2(x - shift + size.Y, 0), new Vector2(x - shift, size.Y), line, 2f, true);
        }

        // 가운데 뒤쪽에서 천천히 도는 빛줄기입니다.
        const int rays = 12;
        for (int i = 0; i < rays; i++)
        {
            float a = _t * 0.05f + i * Mathf.Tau / rays;
            var points = new[]
            {
                center,
                center + new Vector2(Mathf.Cos(a - 0.06f), Mathf.Sin(a - 0.06f)) * size.X,
                center + new Vector2(Mathf.Cos(a + 0.06f), Mathf.Sin(a + 0.06f)) * size.X,
            };
            DrawPolygon(points, new[] { new Color(UiTheme.Gold, 0.06f), new Color(UiTheme.Gold, 0f), new Color(UiTheme.Gold, 0f) });
        }

        // 네 귀퉁이 근처의 큰 문양입니다.
        var suits = new[] { CardColor.Red, CardColor.Yellow, CardColor.Green, CardColor.Blue };
        var spots = new[] { new Vector2(0.09f, 0.22f), new Vector2(0.91f, 0.22f), new Vector2(0.1f, 0.84f), new Vector2(0.9f, 0.84f) };
        for (int i = 0; i < 4; i++)
        {
            float bob = Mathf.Sin(_t * 0.8f + i * 1.7f) * 6f;
            var color = UiTheme.CardColor(suits[i]);
            SuitIcons.Draw(this, suits[i], spots[i] * size + new Vector2(0, bob), size.Y * 0.16f, new Color(color, 0.22f), UiTheme.TableEdge);
        }

        // 떠오르는 빛 알갱이입니다.
        foreach (var mote in _motes)
        {
            float a = 0.25f + 0.25f * Mathf.Sin(_t * 2f + mote.Phase);
            DrawCircle(mote.Position, mote.Size, new Color(UiTheme.Gold, a));
            DrawCircle(mote.Position, mote.Size * 3f, new Color(UiTheme.Gold, a * 0.15f));
        }
    }
}
