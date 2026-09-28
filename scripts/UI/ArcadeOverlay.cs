using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 미니게임 대회 창입니다. (scenes/screens/Arcade.tscn)
/// 모두가 동시에 같은 게임을 하고, 끝나면 점수를 방장에게 보냅니다. 모두 내면 결과(별)를 보여 줍니다.
/// </summary>
public partial class ArcadeOverlay : ColorRect
{
    /// <summary>내 점수를 보낼 때 호출합니다.</summary>
    public event Action<int>? Submitted;

    private Label _title = null!;
    private Label _howTo = null!;
    private Label _time = null!;
    private Label _score = null!;
    private Label _status = null!;
    private VBoxContainer _results = null!;
    private ArcadeStage _stage = null!;
    private double _closeAt = -1;
    private bool _submitted;

    public int ShownId { get; private set; } = -1;

    /// <summary>결과를 보여 주는 중이면 true입니다. (그동안 게임 상태가 바뀌어도 닫지 않습니다)</summary>
    public bool ShowingResult => _closeAt > 0;

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _howTo = GetNode<Label>("%HowTo");
        _time = GetNode<Label>("%TimeLabel");
        _score = GetNode<Label>("%ScoreLabel");
        _status = GetNode<Label>("%Status");
        _results = GetNode<VBoxContainer>("%Results");
        _stage = GetNode<ArcadeStage>("%Stage");
        _stage.Finished += OnFinished;
    }

    /// <summary>대회를 엽니다. 내가 참가자면 바로 게임을 시작합니다. (이미 점수를 냈으면 기다림 화면)</summary>
    public void Open(ArcadeInfo arcade, int me)
    {
        ShownId = arcade.Id;
        _closeAt = -1;
        Visible = true;
        Clear(_results);
        _title.Text = ArcadeRules.Name(arcade.Game);
        _howTo.Text = ArcadeRules.HowTo(arcade.Game);
        bool playing = arcade.Players.Contains(me) && !arcade.Submitted.Contains(me);
        _submitted = !playing;
        _stage.Visible = true;
        if (playing)
        {
            _stage.Begin(arcade.Game, arcade.Seed, (arcade.Id - 1) / 3);
            _status.Text = "";
            Audio.Sfx.Play("augment_offer", 0f, 1f, 0f);
        }
        else
        {
            _stage.Stop();
            _status.Text = "다른 사람들이 미니게임을 하는 중…";
        }
    }

    /// <summary>다른 사람들이 얼마나 끝냈는지 보여 줍니다.</summary>
    public void UpdateProgress(ArcadeInfo arcade, int me)
    {
        // 다른 경로로 이미 점수가 들어갔으면(자동 진행 등) 게임을 멈추고 기다림 화면으로 바꿉니다.
        if (!_submitted && arcade.Submitted.Contains(me))
        {
            _submitted = true;
            _stage.Stop();
        }

        if (_submitted && _closeAt < 0)
        {
            _status.Text = $"다른 사람을 기다리는 중… ({arcade.Submitted.Length}/{arcade.Players.Length})";
        }
    }

    private void OnFinished(int score)
    {
        if (_submitted)
        {
            return;
        }

        _submitted = true;
        _status.Text = $"내 점수 {score}점! 다른 사람을 기다리는 중…";
        Submitted?.Invoke(score);
    }

    /// <summary>결과를 보여 주고 잠시 뒤 닫습니다.</summary>
    public void ShowResult(GameEvent e, Func<int, string> nameOf, int[] stars)
    {
        if (!Visible)
        {
            Visible = true;
        }

        _stage.Stop();
        _stage.Visible = false;
        _howTo.Text = "";
        Clear(_results);

        var scores = e.Text.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(':'))
            .Where(p => p.Length == 2)
            .Select(p => (Seat: int.Parse(p[0]), Score: int.Parse(p[1])))
            .OrderByDescending(p => p.Score)
            .ToList();
        int best = e.Amount;
        _status.Text = best > 0 ? "결과 발표!" : "우승자가 없습니다.";
        foreach (var (seat, score) in scores)
        {
            bool winner = best > 0 && score == best;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 14);
            var name = UiTheme.MakeLabel(nameOf(seat), 17, winner ? UiTheme.Gold : Colors.White);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(name);
            row.AddChild(UiTheme.MakeLabel($"{score}점", 17, winner ? UiTheme.Gold : UiTheme.TextDim));
            // 결과 사건이 화면 갱신보다 먼저 오므로, 이번에 받은 별을 더해서 보여 줍니다.
            int have = (seat < stars.Length ? stars[seat] : 0) + (winner ? 1 : 0);
            row.AddChild(UiTheme.MakeLabel(winner ? $"★ +1  ({have}/{ArcadeRules.StarsToWin})" : $"({have}/{ArcadeRules.StarsToWin})",
                16, winner ? UiTheme.Gold : UiTheme.TextDim));
            _results.AddChild(row);
        }

        _closeAt = Time.GetTicksMsec() / 1000.0 + 3.5;
    }

    public void Close()
    {
        Visible = false;
        _closeAt = -1;
        _stage.Stop();
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        if (_stage.Running)
        {
            _time.Text = _stage.Countdown > 0 ? "준비!" : $"{_stage.TimeLeft:0.0}초";
            _score.Text = $"점수 {_stage.Score}";
        }

        if (_closeAt > 0 && Time.GetTicksMsec() / 1000.0 >= _closeAt)
        {
            Close();
        }
    }

    private static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
