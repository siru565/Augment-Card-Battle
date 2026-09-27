using System;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 잭팟 증강의 슬롯머신 연출입니다. 누구의 슬롯이든 모두의 화면에 잠깐 떴다가 사라집니다.
/// 릴 3개가 빠르게 돌다가 왼쪽부터 차례로 멈춥니다. (결과는 방장이 이미 정한 값입니다)
/// </summary>
public partial class SlotMachineView : Control
{
    private const float ReelSize = 64f;
    private static readonly float[] StopTimes = { 0.55f, 0.8f, 1.05f };
    private const float HoldUntil = 2.1f;

    private string _title = "";
    private int[] _reels = { 0, 0, 0 };
    private int _outcome;
    private float _t;
    private bool _celebrated;

    /// <summary>결과가 확정되는 순간(마지막 릴이 멈출 때) 호출합니다. (잭팟 연출용)</summary>
    public event Action<int>? Landed;

    public SlotMachineView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(ReelSize * 3 + 60, ReelSize + 64);
    }

    public void Play(string title, int[] reels, int outcome)
    {
        _title = title;
        _reels = reels;
        _outcome = outcome;
        _t = 0;
        _celebrated = false;
        Modulate = Colors.White;
        Audio.Sfx.Play("shuffle", -6f, 1.4f);
    }

    public override void _Process(double delta)
    {
        float before = _t;
        _t += (float)delta;
        foreach (float stop in StopTimes)
        {
            if (before < stop && _t >= stop)
            {
                Audio.Sfx.Play("play", -8f, 1.3f);
            }
        }

        if (!_celebrated && _t >= StopTimes[^1])
        {
            _celebrated = true;
            Landed?.Invoke(_outcome);
        }

        if (_t > HoldUntil)
        {
            Modulate = new Color(1, 1, 1, Math.Max(0f, 1f - (_t - HoldUntil) / 0.35f));
            if (_t > HoldUntil + 0.35f)
            {
                QueueFree();
            }
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        var border = _outcome == 2 && _celebrated ? UiTheme.Gold : new Color(UiTheme.Gold, 0.55f);
        DrawStyleBox(UiTheme.Box(new Color(0.05f, 0.06f, 0.09f, 0.95f), border, _outcome == 2 && _celebrated ? 3 : 2, 12, 0),
            new Rect2(Vector2.Zero, Size));
        DrawString(UiTheme.Bold, new Vector2(0, 24), Loc.Tr(_title), HorizontalAlignment.Center, Size.X, 15, UiTheme.Gold);

        for (int i = 0; i < 3; i++)
        {
            var rect = new Rect2(new Vector2(20 + i * (ReelSize + 10), 36), new Vector2(ReelSize, ReelSize));
            bool stopped = _t >= StopTimes[i];
            bool highlight = stopped && _celebrated && _outcome > 0;
            DrawRect(rect, highlight ? new Color(0.25f, 0.2f, 0.08f) : Color.FromHtml("#161a24"));
            DrawRect(rect, highlight ? UiTheme.Gold : new Color(1, 1, 1, 0.2f), false, 2f);

            // 도는 중에는 기호가 빠르게 바뀌고 살짝 아래로 흐릅니다.
            int symbol = stopped ? _reels[i] : (int)(_t * 16 + i * 2) % 5;
            float slide = stopped ? 0 : (_t * 16 % 1f) * 10f - 5f;
            DrawSymbol(symbol, rect.GetCenter() + new Vector2(0, slide), stopped ? 1f : 0.55f);
        }
    }

    private void DrawSymbol(int symbol, Vector2 center, float alpha)
    {
        if (symbol == JackpotRules.Star)
        {
            SuitIcons.DrawStar(this, center, 22f, new Color(UiTheme.Gold, alpha));
            return;
        }

        var suit = (CardColor)symbol;
        var color = new Color(UiTheme.CardColor(suit), alpha);
        SuitIcons.Draw(this, suit, center, 34f, color, new Color(color.Darkened(0.6f), alpha));
    }
}
