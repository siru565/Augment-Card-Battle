using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SpCardgame.Core;

/// <summary>
/// 한국어 ↔ 영어 번역기입니다. (Godot 의존 없음)
/// 게임 규칙과 네트워크 메시지는 모두 한국어 원문으로 만들고, 화면에 보여 줄 때 이 번역기가 영어로 바꿉니다.
/// 그래서 방장은 한국어, 참가자는 영어처럼 서로 다른 언어로 같이 플레이할 수 있습니다.
///
/// 번역표(assets/i18n/en.tsv)의 한 줄은 "한국어 원문 TAB 영어"이고, 원문에 {0} 같은 자리가 있으면 틀(템플릿)로 씁니다.
/// 번역 순서: ① 똑같은 문장 ② 쉼표 목록 나누기 ③ 틀에 맞추기(자리에 들어간 값도 다시 번역) ④ 단어별 번역.
/// 영어 쪽 자리 표기: {0} 그대로, {0:ord} 서수(1st, 2nd), {0|card|cards} 숫자에 따라 단수/복수.
/// </summary>
public static class Loc
{
    private static readonly Regex Hangul = new("[가-힣]", RegexOptions.Compiled);
    private static readonly Regex Hole = new(@"\{(\d+)\}", RegexOptions.Compiled);
    private static readonly Regex EnHole = new(@"\{(\d+)(?::(ord)|\|([^|}]*)\|([^}]*))?\}", RegexOptions.Compiled);
    private static readonly Regex HangulPhrase = new("[가-힣]+(?: [가-힣]+)*", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> Exact = new();
    private static readonly List<(Regex Pattern, string English)> Templates = new();
    private static readonly Dictionary<string, string> Cache = new();

    /// <summary>현재 언어입니다. "ko"(기본) 또는 "en"입니다.</summary>
    public static string Language { get; set; } = "ko";

    public static bool IsEnglish => Language == "en";

    public static int Count => Exact.Count;

    /// <summary>번역표(TSV) 내용을 불러옵니다. # 으로 시작하는 줄은 설명입니다.</summary>
    public static void Load(string tsv)
    {
        Exact.Clear();
        Templates.Clear();
        Cache.Clear();
        var templates = new List<(Regex Pattern, string English, int Weight)>();
        foreach (string raw in tsv.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int tab = line.IndexOf('\t');
            if (tab <= 0)
            {
                continue;
            }

            string ko = line[..tab].Trim();
            string en = line[(tab + 1)..];
            Exact[ko] = en;

            // 자리({0})가 있고, 자리 밖 글자에 한글이 있는 틀만 씁니다. ("{0}!"처럼 너무 넓은 틀은 제외)
            string literal = Hole.Replace(ko, "");
            if (ko.Contains('{') && Hangul.IsMatch(literal))
            {
                templates.Add((BuildPattern(ko, en), en, literal.Length));
            }
        }

        // 글자가 많은(구체적인) 틀부터 맞춰 봅니다.
        foreach (var t in templates.OrderByDescending(t => t.Weight))
        {
            Templates.Add((t.Pattern, t.English));
        }
    }

    /// <summary>현재 언어로 보여 줄 문장을 돌려줍니다. 한국어면 그대로입니다.</summary>
    public static string Tr(string text) => IsEnglish ? ToEnglish(text) : text;

    /// <summary>한국어 문장을 영어로 바꿉니다. 모르는 문장은 그대로 돌려줍니다.</summary>
    public static string ToEnglish(string text)
    {
        if (string.IsNullOrEmpty(text) || !Hangul.IsMatch(text))
        {
            return text;
        }

        if (Cache.TryGetValue(text, out var cached))
        {
            return cached;
        }

        string result = Translate(text, 0);
        if (Cache.Count > 4000)
        {
            Cache.Clear();
        }

        Cache[text] = result;
        return result;
    }

    private static string Translate(string text, int depth)
    {
        if (depth > 5 || !Hangul.IsMatch(text))
        {
            return text;
        }

        // 여러 줄은 줄마다 따로 번역합니다.
        if (text.Contains('\n'))
        {
            return string.Join("\n", text.Split('\n').Select(line => Translate(line, depth)));
        }

        // 앞뒤 공백은 그대로 두고 가운데만 번역합니다.
        string core = text.Trim();
        int lead = text.IndexOf(core, StringComparison.Ordinal);
        string head = text[..lead];
        string tail = text[(lead + core.Length)..];

        return head + TranslateCore(core, depth) + tail;
    }

    private static string TranslateCore(string core, int depth)
    {
        // ① 똑같은 문장
        if (Exact.TryGetValue(core, out var exact) && !exact.Contains('{'))
        {
            return exact;
        }

        // ② 쉼표 목록("1등 봇 B, 2등 Ariki")은 항목마다 번역합니다. 항목이 모두 번역될 때만 씁니다.
        if (core.Contains(", "))
        {
            var parts = core.Split(", ").Select(p => Translate(p, depth + 1)).ToList();
            if (parts.All(p => !Hangul.IsMatch(p)))
            {
                return string.Join(", ", parts);
            }
        }

        // ③ 틀에 맞추기
        foreach (var (pattern, english) in Templates)
        {
            var match = pattern.Match(core);
            if (!match.Success)
            {
                continue;
            }

            var args = new List<string>();
            for (int i = 1; i < match.Groups.Count; i++)
            {
                args.Add(Translate(match.Groups[i].Value, depth + 1));
            }

            return FormatEnglish(english, args);
        }

        // ④ 단어별 번역 ("불꽃 7" → "Flame 7"). 한글 단어가 하나라도 모르는 것이면 원문을 그대로 둡니다.
        bool unknown = false;
        string words = HangulPhrase.Replace(core, m =>
        {
            if (Exact.TryGetValue(m.Value, out var phrase) && !phrase.Contains('{'))
            {
                return phrase;
            }

            var pieces = m.Value.Split(' ');
            var translated = new List<string>();
            foreach (string piece in pieces)
            {
                if (Exact.TryGetValue(piece, out var word) && !word.Contains('{'))
                {
                    translated.Add(word);
                }
                else
                {
                    unknown = true;
                    translated.Add(piece);
                }
            }

            return string.Join(" ", translated);
        });

        return unknown ? core : words;
    }

    /// <summary>
    /// "{0}의 {1}번째 차례" 같은 틀을 정규식으로 바꿉니다. 자리는 무엇이든(빈 값 포함) 받습니다.
    /// 다만 영어에서 서수·복수로 쓰는 자리는 숫자만 받고, 바로 뒤에 공백 하나를 두고 다른 자리가 오면
    /// 앞 자리가 최대한 많이 가져갑니다. ("봇 A 2등"에서 이름이 "봇 A", 순위가 "2"가 되도록)
    /// </summary>
    private static Regex BuildPattern(string template, string english)
    {
        var numeric = new HashSet<string>();
        foreach (Match m in EnHole.Matches(english))
        {
            if (m.Groups[2].Success || m.Groups[3].Success)
            {
                numeric.Add(m.Groups[1].Value);
            }
        }

        var holes = Hole.Matches(template);
        var sb = new StringBuilder("^");
        int last = 0;
        for (int i = 0; i < holes.Count; i++)
        {
            var m = holes[i];
            sb.Append(Regex.Escape(template[last..m.Index]));
            int end = m.Index + m.Length;
            bool spaceThenHole = i + 1 < holes.Count && template[end..holes[i + 1].Index] == " ";
            sb.Append(numeric.Contains(m.Groups[1].Value) ? @"(\d+)" : spaceThenHole ? "(.*)" : "(.*?)");
            last = end;
        }

        sb.Append(Regex.Escape(template[last..]));
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.Singleline);
    }

    private static string FormatEnglish(string english, IReadOnlyList<string> args) =>
        EnHole.Replace(english, m =>
        {
            int index = int.Parse(m.Groups[1].Value);
            string value = index < args.Count ? args[index] : "";
            if (m.Groups[2].Success)
            {
                return Ordinal(value);
            }

            if (m.Groups[3].Success)
            {
                bool one = value.Trim() == "1";
                return $"{value} {(one ? m.Groups[3].Value : m.Groups[4].Value)}";
            }

            return value;
        });

    /// <summary>숫자를 영어 서수로 바꿉니다. (1 → 1st, 2 → 2nd, 11 → 11th)</summary>
    public static string Ordinal(string value)
    {
        if (!int.TryParse(value.Trim(), out int n))
        {
            return value;
        }

        string suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
        return $"{n}{suffix}";
    }
}
