using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 원형 아바타입니다. 차례인 플레이어는 금색 테두리가 됩니다.
/// </summary>
public partial class AvatarView : Control
{
    private string _letter;
    private bool _active;
    private float _t;

    public string Letter
    {
        get => _letter;
        set { _letter = value; QueueRedraw(); }
    }

    public bool Active
    {
        get => _active;
        set { _active = value; QueueRedraw(); }
    }

    private Texture2D? _picture;

    /// <summary>프로필 사진입니다. (Steam 프로필) 있으면 글자 대신 원형으로 잘라 그립니다.</summary>
    public Texture2D? Picture
    {
        get => _picture;
        set { _picture = value; QueueRedraw(); }
    }

    /// <summary>이 아바타에 보여 줄 Steam ID입니다. 0이면 글자만 보여 줍니다.</summary>
    public ulong SteamId { get; private set; }

    /// <summary>Steam ID로 프로필 사진을 불러옵니다. 아직 도착하지 않았으면 도착했을 때 RefreshPicture로 다시 불러옵니다.</summary>
    public void SetSteamId(ulong steamId)
    {
        SteamId = steamId;
        RefreshPicture();
    }

    /// <summary>움직이는 아바타(GIF)입니다. 있으면 정지 사진 대신 이것을 재생합니다.</summary>
    private Net.SteamAnimatedAvatars.Animation? _animation;

    private int _frame = -1;

    public void RefreshPicture()
    {
        Picture = SteamId == 0 ? null : Net.SteamRuntime.GetAvatar(SteamId);
        _animation = SteamId == 0 ? null : Net.SteamAnimatedAvatars.Get(SteamId);
        _frame = -1;
        QueueRedraw();
    }

    /// <summary>지금 그릴 사진입니다. 움직이는 아바타가 있으면 현재 프레임, 없으면 정지 사진입니다.</summary>
    private Texture2D? CurrentPicture =>
        _animation != null && _frame >= 0 ? _animation.Frames[_frame] : _picture;

    /// <summary>Godot가 스크립트를 다시 불러올 때 필요한 기본 생성자입니다.</summary>
    public AvatarView() : this("?") { }

    public AvatarView(string letter)
    {
        _letter = letter;
        CustomMinimumSize = new Vector2(56, 56);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        if (_active)
        {
            _t += (float)delta * 4f;
            QueueRedraw();
        }

        // 움직이는 아바타는 모든 화면이 같은 시계를 써서, 같은 사람의 아바타가 여러 곳에서 똑같이 움직입니다.
        if (_animation != null && IsVisibleInTree())
        {
            int frame = _animation.FrameAt(Time.GetTicksMsec());
            if (frame != _frame)
            {
                _frame = frame;
                QueueRedraw();
            }
        }
    }

    public override void _Draw()
    {
        var center = Size / 2;
        float r = Mathf.Min(Size.X, Size.Y) / 2 - 3;
        DrawCircle(center, r, Color.FromHtml("#34405a"));

        var picture = CurrentPicture;
        if (picture != null)
        {
            // 원을 여러 개의 점으로 만들고, 각 점에 사진 좌표(UV)를 붙여서 원형으로 잘라 그립니다.
            const int segments = 48;
            var points = new Vector2[segments];
            var uvs = new Vector2[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.Tau / segments;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                points[i] = center + dir * (r - 1);
                uvs[i] = new Vector2(0.5f, 0.5f) + dir * 0.5f;
            }

            DrawColoredPolygon(points, Colors.White, uvs, picture);
        }

        var ring = _active ? UiTheme.Gold with { A = 0.7f + 0.3f * Mathf.Sin(_t) } : new Color(1, 1, 1, 0.25f);
        DrawArc(center, r, 0, Mathf.Tau, 48, ring, _active ? 4f : 2f, true);

        if (picture != null)
        {
            return;
        }

        var font = UiTheme.Bold;
        const int size = 24;
        var pos = new Vector2(0, center.Y + font.GetAscent(size) / 2 - 3);
        DrawString(font, pos, _letter, HorizontalAlignment.Center, Size.X, size, Colors.White);
    }
}
