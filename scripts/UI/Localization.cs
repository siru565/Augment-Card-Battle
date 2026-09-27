using System;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 게임 언어(한국어/영어)를 바꿉니다.
/// 영어일 때만 번역 서버에 영어 번역을 넣고, 한국어일 때는 빼서 원문 그대로 보여 줍니다.
/// </summary>
public static class Localization
{
    public const string TablePath = "res://assets/i18n/en.tsv";

    public static readonly string[] Languages = { "ko", "en" };

    private static LocTranslation? _translation;

    /// <summary>언어가 바뀌면 알려 줍니다. (그려서 보여 주는 글자와 기록을 다시 만듭니다)</summary>
    public static event Action? Changed;

    public static string WindowTitle => Loc.IsEnglish ? "Augment Card Battle" : "증강 카드 배틀";

    /// <summary>OS 언어가 한국어면 한국어, 아니면 영어를 기본값으로 씁니다.</summary>
    public static string DefaultLanguage() => OS.GetLocaleLanguage() == "ko" ? "ko" : "en";

    public static void Apply(string language)
    {
        if (Loc.Count == 0)
        {
            using var file = FileAccess.Open(TablePath, FileAccess.ModeFlags.Read);
            if (file != null)
            {
                Loc.Load(file.GetAsText());
            }
            else
            {
                GD.PushWarning($"[Loc] 번역표를 열지 못했습니다: {TablePath}");
            }
        }

        _translation ??= new LocTranslation { Locale = "en" };
        bool english = language == "en";
        Loc.Language = english ? "en" : "ko";

        TranslationServer.RemoveTranslation(_translation);
        if (english)
        {
            TranslationServer.AddTranslation(_translation);
        }

        TranslationServer.SetLocale(english ? "en" : "ko");

        if (Engine.GetMainLoop() is SceneTree tree)
        {
            tree.Root.Title = WindowTitle;
            tree.Root.PropagateNotification((int)Node.NotificationTranslationChanged);
        }

        Changed?.Invoke();
    }
}
