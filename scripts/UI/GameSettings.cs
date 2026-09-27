using System;
using Godot;
using SpCardgame.Audio;

namespace SpCardgame.UI;

/// <summary>
/// 화면, 소리, 언어 설정입니다. user://settings.cfg에 저장하고, 게임을 켤 때 다시 불러와 적용합니다.
/// </summary>
public static class GameSettings
{
    private const string FilePath = "user://settings.cfg";

    public static readonly Vector2I[] Resolutions =
    {
        new(1280, 720), new(1600, 900), new(1920, 1080), new(2560, 1440),
    };

    /// <summary>화면 모드입니다. 0: 창 모드, 1: 전체 화면(테두리 없음), 2: 전체 화면(독점)</summary>
    public static int WindowMode { get; set; }

    public static int ResolutionIndex { get; set; }

    /// <summary>전체 볼륨(0~1)입니다.</summary>
    public static float MasterVolume { get; set; } = 0.8f;

    /// <summary>효과음 볼륨(0~1)입니다.</summary>
    public static float SfxVolume { get; set; } = 0.8f;

    /// <summary>배경음악 볼륨(0~1)입니다.</summary>
    public static float MusicVolume { get; set; } = 0.6f;

    public static bool Muted { get; set; }

    /// <summary>혼자 하기 상대 봇 수(1~3명)입니다.</summary>
    public static int SoloBots { get; set; } = 3;

    /// <summary>봇 난이도입니다. 0 쉬움, 1 보통, 2 어려움</summary>
    public static int BotLevel { get; set; } = 1;

    /// <summary>게임 언어입니다. "ko" 또는 "en"입니다.</summary>
    public static string Language { get; set; } = Localization.DefaultLanguage();

    private static bool _loaded;

    public static void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        var config = new ConfigFile();
        if (config.Load(FilePath) != Error.Ok)
        {
            return;
        }

        WindowMode = Math.Clamp((int)config.GetValue("display", "window_mode", 0), 0, 2);
        ResolutionIndex = Math.Clamp((int)config.GetValue("display", "resolution", 0), 0, Resolutions.Length - 1);
        MasterVolume = Math.Clamp((float)config.GetValue("audio", "master", 0.8f), 0f, 1f);
        SfxVolume = Math.Clamp((float)config.GetValue("audio", "sfx", 0.8f), 0f, 1f);
        MusicVolume = Math.Clamp((float)config.GetValue("audio", "music", 0.6f), 0f, 1f);
        Muted = (bool)config.GetValue("audio", "muted", false);
        SoloBots = Math.Clamp((int)config.GetValue("general", "solo_bots", 3), 1, 3);
        BotLevel = Math.Clamp((int)config.GetValue("general", "bot_level", 1), 0, 2);
        string language = (string)config.GetValue("general", "language", Language);
        Language = Array.IndexOf(Localization.Languages, language) >= 0 ? language : Language;
    }

    public static void Save()
    {
        var config = new ConfigFile();
        config.SetValue("display", "window_mode", WindowMode);
        config.SetValue("display", "resolution", ResolutionIndex);
        config.SetValue("audio", "master", MasterVolume);
        config.SetValue("audio", "sfx", SfxVolume);
        config.SetValue("audio", "music", MusicVolume);
        config.SetValue("audio", "muted", Muted);
        config.SetValue("general", "language", Language);
        config.SetValue("general", "solo_bots", SoloBots);
        config.SetValue("general", "bot_level", BotLevel);
        config.Save(FilePath);
    }

    /// <summary>볼륨만 적용합니다. (슬라이더를 움직일 때마다 부릅니다)</summary>
    public static void ApplyAudio()
    {
        Sfx.EnsureBus();
        int master = AudioServer.GetBusIndex("Master");
        AudioServer.SetBusVolumeDb(master, Mathf.LinearToDb(Mathf.Max(MasterVolume, 0.0001f)));
        AudioServer.SetBusMute(master, Muted || MasterVolume <= 0.001f);

        int sfx = AudioServer.GetBusIndex(Sfx.BusName);
        AudioServer.SetBusVolumeDb(sfx, Mathf.LinearToDb(Mathf.Max(SfxVolume, 0.0001f)));
        AudioServer.SetBusMute(sfx, SfxVolume <= 0.001f);

        Music.EnsureBus();
        int music = AudioServer.GetBusIndex(Music.BusName);
        AudioServer.SetBusVolumeDb(music, Mathf.LinearToDb(Mathf.Max(MusicVolume, 0.0001f)));
        AudioServer.SetBusMute(music, MusicVolume <= 0.001f);
    }

    /// <summary>화면 모드와 해상도를 적용합니다. 창 모드에서는 창을 모니터 가운데로 옮깁니다.</summary>
    public static void ApplyDisplay()
    {
        switch (WindowMode)
        {
            case 1:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                return;
            case 2:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
                return;
        }

        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        var size = Resolutions[ResolutionIndex];
        int screen = DisplayServer.WindowGetCurrentScreen();
        var usable = DisplayServer.ScreenGetUsableRect(screen);

        // 모니터보다 큰 해상도는 모니터에 맞게 줄입니다.
        size = new Vector2I(Math.Min(size.X, usable.Size.X), Math.Min(size.Y, usable.Size.Y));
        DisplayServer.WindowSetSize(size);
        DisplayServer.WindowSetPosition(usable.Position + (usable.Size - size) / 2);
    }

    public static string ResolutionName(int index) => $"{Resolutions[index].X} × {Resolutions[index].Y}";
}
