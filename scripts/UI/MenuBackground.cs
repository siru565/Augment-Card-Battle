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

    private const int CardCount = 10;

    private readonly List<Floater> _floaters = new();
    private readonly Random _rng = new();

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
                Modulate = new Color(0.8f, 0.8f, 0.85f, 0.07f + 0.2f * (depth - 0.45f)),
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
        var center = new Vector2(size.X * 0.62f, size.Y * 0.45f);

        // 오른쪽이 살짝 밝은 원형 그라데이션만 깝니다. (메뉴는 왼쪽에 있어서 오른쪽을 밝게 둡니다)
        DrawRect(new Rect2(Vector2.Zero, size), UiTheme.TableEdge);
        float maxR = size.Length() * 0.55f;
        for (int i = 32; i >= 0; i--)
        {
            float t = i / 32f;
            DrawCircle(center, maxR * t, UiTheme.TableEdge.Lerp(UiTheme.TableCenter, (1f - t) * 0.9f));
        }
    }
}
