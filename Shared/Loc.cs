using System;
using System.Collections.Generic;

namespace TGShared;

// Localization: the language follows the game's own language setting (Unity Localization),
// or a forced code from the mod config. Missing strings fall back to English.
internal static class Loc
{
    // column order of every string table
    internal static readonly string[] Codes = { "en", "ru", "de", "fr", "es", "it", "pl", "cs", "pt", "ja", "ko", "zh-Hans", "zh-Hant", "uk" };

    internal static Dictionary<string, string[]> Table = new();
    internal static Func<string> Forced = () => "auto";
    internal static Action<string> Log;

    private static int _lang;
    private static float _checkedAt = -999f;
    private static string _gameCode = "";
    private static bool _loggedLocales;

    internal static string Lang => Codes[_lang];
    internal static bool IsCjk => _lang is 9 or 10 or 11 or 12;

    // Call from Update(); cheap, re-checks the game language every couple of seconds.
    internal static void Tick(float now)
    {
        if (now - _checkedAt < 2f) return;
        _checkedAt = now;
        string forced = (Forced?.Invoke() ?? "auto").Trim();
        string code = forced;
        if (forced.Length == 0 || forced.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            code = GameCode();
            if (string.IsNullOrEmpty(code)) return; // localization not ready yet — keep current
        }
        int idx = IndexOf(code);
        if (idx != _lang) { _lang = idx; Log?.Invoke($"language: {Codes[_lang]} (game '{_gameCode}', config '{forced}')"); }
    }

    private static string GameCode()
    {
        try
        {
            var loc = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale;
            if (loc == null) return null;
            _gameCode = loc.Identifier.Code ?? "";
            if (!_loggedLocales)
            {
                _loggedLocales = true;
                try
                {
                    var list = UnityEngine.Localization.Settings.LocalizationSettings.AvailableLocales?.Locales;
                    var codes = new List<string>();
                    if (list != null) for (int i = 0; i < list.Count; i++) codes.Add(list[i]?.Identifier.Code);
                    Log?.Invoke("game locales: " + string.Join(", ", codes));
                }
                catch { }
            }
            return _gameCode;
        }
        catch { return null; }
    }

    internal static int IndexOf(string code)
    {
        if (string.IsNullOrEmpty(code)) return 0;
        string c = code.Trim().ToLowerInvariant().Replace('_', '-');
        if (c.StartsWith("zh"))
            return c.Contains("hant") || c.Contains("tw") || c.Contains("hk") || c.Contains("mo") ? 12 : 11;
        string two = c.Length >= 2 ? c.Substring(0, 2) : c;
        if (two == "ua") two = "uk";
        if (two == "cz") two = "cs";
        if (two == "jp") two = "ja";
        if (two == "kr") two = "ko";
        for (int i = 0; i < Codes.Length; i++) if (Codes[i] == two) return i;
        return 0;
    }

    internal static string T(string key)
    {
        if (!Table.TryGetValue(key, out var row) || row == null || row.Length == 0) return key;
        string s = _lang < row.Length ? row[_lang] : null;
        return string.IsNullOrEmpty(s) ? row[0] : s;
    }

    internal static string F(string key, params object[] args)
    {
        try { return string.Format(T(key), args); } catch { return T(key); }
    }

    // Plural form index: 0 = one, 1 = few, 2 = many (Slavic languages); others use one/many.
    internal static int PluralForm(long n)
    {
        n = Math.Abs(n);
        switch (Lang)
        {
            case "ru": case "uk":
                if (n % 10 == 1 && n % 100 != 11) return 0;
                if (n % 10 is >= 2 and <= 4 && !(n % 100 is >= 12 and <= 14)) return 1;
                return 2;
            case "pl":
                if (n == 1) return 0;
                if (n % 10 is >= 2 and <= 4 && !(n % 100 is >= 12 and <= 14)) return 1;
                return 2;
            case "cs":
                if (n == 1) return 0;
                if (n is >= 2 and <= 4) return 1;
                return 2;
            case "fr": case "pt":
                return n <= 1 ? 0 : 2;
            default:
                return n == 1 ? 0 : 2;
        }
    }

    // key holds "one|few|many" (or "one|many"; or a single form for languages without plurals)
    internal static string Plural(string key, long n)
    {
        var forms = T(key).Split('|');
        int f = PluralForm(n);
        if (forms.Length == 1) return forms[0];
        if (forms.Length == 2) return f == 0 ? forms[0] : forms[1];
        return forms[Math.Min(f, forms.Length - 1)];
    }
}
