using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpCardgame.UI;

/// <summary>
/// 도감 기록입니다. 내가 한 번이라도 얻은 특수 증강과 발동한 각성 능력을 user://collection.cfg에 저장합니다.
/// 이름은 한국어 원문(엔진이 쓰는 이름)으로 저장하므로 언어를 바꿔도 기록이 그대로입니다.
/// </summary>
public static class Collection
{
    private const string FilePath = "user://collection.cfg";

    private static readonly HashSet<string> Augments = new();
    private static readonly HashSet<string> Abilities = new();
    private static bool _loaded;

    /// <summary>
    /// 개발 도구(스크린샷, 멀티 테스트)가 켭니다. 켜면 파일을 읽거나 쓰지 않고 메모리에만 기록해서,
    /// 봇이 대신 둔 판이 실제 플레이어의 도감에 섞이지 않게 합니다.
    /// </summary>
    public static bool TestMode
    {
        get => _testMode;
        set
        {
            _testMode = value;
            _loaded = value;
            Augments.Clear();
            Abilities.Clear();
        }
    }

    private static bool _testMode;

    /// <summary>새로 등록된 것이 생기면 알려 줍니다. (종류, 이름)</summary>
    public static event Action<string, string>? Unlocked;

    public static bool HasAugment(string name)
    {
        Load();
        return Augments.Contains(name);
    }

    public static bool HasAbility(string name)
    {
        Load();
        return Abilities.Contains(name);
    }

    public static int AugmentCount
    {
        get
        {
            Load();
            return Augments.Count;
        }
    }

    public static int AbilityCount
    {
        get
        {
            Load();
            return Abilities.Count;
        }
    }

    /// <summary>특수 증강을 도감에 등록합니다. 처음 얻은 것이면 true입니다.</summary>
    public static bool AddAugment(string name) => Add(Augments, "augment", name);

    /// <summary>각성 능력을 도감에 등록합니다. 처음 발동한 것이면 true입니다.</summary>
    public static bool AddAbility(string name) => Add(Abilities, "ability", name);

    private static bool Add(HashSet<string> set, string kind, string name)
    {
        Load();
        if (string.IsNullOrEmpty(name) || !set.Add(name))
        {
            return false;
        }

        Save();
        Unlocked?.Invoke(kind, name);
        return true;
    }

    private static void Load()
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

        foreach (string name in (string[])config.GetValue("codex", "augments", Array.Empty<string>()))
        {
            Augments.Add(name);
        }

        foreach (string name in (string[])config.GetValue("codex", "abilities", Array.Empty<string>()))
        {
            Abilities.Add(name);
        }
    }

    private static void Save()
    {
        if (_testMode)
        {
            return;
        }

        var config = new ConfigFile();
        config.SetValue("codex", "augments", Augments.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        config.SetValue("codex", "abilities", Abilities.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        config.Save(FilePath);
    }
}
