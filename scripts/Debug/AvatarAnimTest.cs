using Godot;
using SpCardgame.Net;
using SpCardgame.UI;

namespace SpCardgame.Debug;

/// <summary>
/// 개발용: 내 Steam 아바타를 크게 띄우고, 움직이는 아바타가 도착하면 시간 간격을 두고 3장 캡처합니다.
/// 결과는 tools/shots/avatar_anim_*.png, 로그는 tools/shots/avatar_anim.txt에 남깁니다.
/// </summary>
public partial class AvatarAnimTest : Control
{
    private AvatarView? _avatar;
    private double _elapsed;
    private double _readyAt = -1;
    private int _shots;

    public override void _Ready()
    {
        SteamRuntime.EnsureInitialized(this);
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(new ColorRect { Color = new Color("#1b2233"), AnchorRight = 1, AnchorBottom = 1 });
        _avatar = new AvatarView("A") { Position = new Vector2(40, 40), Size = new Vector2(200, 200), CustomMinimumSize = new Vector2(200, 200) };
        AddChild(_avatar);
        _avatar.SetSteamId(SteamRuntime.MySteamId);
        SteamRuntime.AvatarLoaded += _ => _avatar.RefreshPicture();
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        var anim = SteamAnimatedAvatars.Get(SteamRuntime.MySteamId);
        if (anim != null && _readyAt < 0)
        {
            _readyAt = _elapsed;
            Log($"steam={SteamRuntime.IsReady} id={SteamRuntime.MySteamId} frames={anim.Frames.Length} total={anim.TotalMs}ms loaded_at={_elapsed:F1}s");
        }

        if (_readyAt >= 0 && _elapsed >= _readyAt + 0.5 + _shots * 0.37 && _shots < 3)
        {
            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://tools/shots/avatar_anim_{_shots}.png"));
            _shots++;
        }

        if (_shots >= 3 || _elapsed > 20)
        {
            if (_readyAt < 0)
            {
                Log($"timeout steam={SteamRuntime.IsReady} id={SteamRuntime.MySteamId} static={(SteamRuntime.GetAvatar(SteamRuntime.MySteamId) != null)}");
            }

            GetTree().Quit();
        }
    }

    private static void Log(string text)
    {
        using var f = FileAccess.Open("res://tools/shots/avatar_anim.txt", FileAccess.ModeFlags.Write);
        f?.StoreString(text);
    }
}
