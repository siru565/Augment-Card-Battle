using System;
using System.Collections.Generic;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 메인 화면(로비·대기방) 배경입니다. 어두운 그라데이션 위로 카드 몇 장이 흐리게 천천히 떠다닙니다.
/// 입력은 받지 않습니다.
/// </summary>
public partial class MenuBackground : Control
{
    private sealed class Floater
    {
        public CardView View = null!;
        public Vector2 Velocity;
        public float Spin;
    }

    private const int CardCount = 14;

    private readonly List<Floater> _floaters = new();
    private readonly Random _rng = new();
    private float _t;

    public MenuBackground()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    private bool _spawned;

    /// <summary>카드를 처음 크기가 정해졌을 때 한 번 뿌립니다.</summary>
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
                Modulate = new Color(1, 1, 1, 0.22f + 0.45f * (depth - 0.45f)),
            };
            AddChild(view);
            _floaters.Add(new Floater
            {
                View = view,
                Velocity = new Vector2(((float)_rng.NextDouble() - 0.5f) * 20f, -(8f + (float)_rng.NextDouble() * 18f)) * depth,
                Spin = ((float)_rng.NextDouble() - 0.5f) * 0.25f,
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
        QueueRedraw();

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

    }

    public override void _Draw()
    {
        var size = Size;

        // 바탕: 위는 푸른 회색, 아래로 갈수록 어두워지는 세로 그라데이션입니다.
        var top = Color.FromHtml("#1d2433");
        var bottom = Color.FromHtml("#0c0f16");
        const int bands = 24;
        for (int i = 0; i < bands; i++)
        {
            float t = i / (float)(bands - 1);
            DrawRect(new Rect2(0, size.Y * i / bands, size.X, size.Y / bands + 1), top.Lerp(bottom, t));
        }

        // 오른쪽 가운데의 큰 조명입니다. 메뉴(왼쪽) 뒤보다 밝게 해서 공간감을 줍니다.
        SoftGlow(new Vector2(size.X * 0.68f, size.Y * 0.42f), size.Y * 0.95f, Color.FromHtml("#4a5878"), 0.5f);

        // 문양 색 빛 번짐이 천천히 떠다닙니다. (카드 색과 같은 색입니다)
        var glows = new (Vector2 Pos, CardColor Suit, float Radius)[]
        {
            (new Vector2(0.55f, 0.18f), CardColor.Yellow, 0.42f),
            (new Vector2(0.92f, 0.3f), CardColor.Blue, 0.38f),
            (new Vector2(0.78f, 0.86f), CardColor.Red, 0.4f),
            (new Vector2(0.45f, 0.8f), CardColor.Green, 0.34f),
        };
        for (int i = 0; i < glows.Length; i++)
        {
            var (pos, suit, radius) = glows[i];
            var drift = new Vector2(Mathf.Sin(_t * 0.13f + i * 1.9f), Mathf.Cos(_t * 0.11f + i * 2.7f)) * size.Y * 0.05f;
            SoftGlow(pos * size + drift, size.Y * radius, UiTheme.CardColor(suit), 0.22f);
        }
    }

    /// <summary>가운데가 진하고 바깥으로 갈수록 사라지는 둥근 빛을 그립니다.</summary>
    private void SoftGlow(Vector2 center, float radius, Color color, float strength)
    {
        const int steps = 40;
        for (int i = 0; i < steps; i++)
        {
            float t = i / (float)steps;
            DrawCircle(center, radius * (1f - t), new Color(color, strength / steps * 1.6f));
        }
    }
}
