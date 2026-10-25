using System;
using System.Collections.Generic;
using System.Windows;

namespace SmoothFrame.UI.Localization;

/// <summary>Describes a UI language supported by the application.</summary>
public sealed class AppLanguage
{
    public AppLanguage(string code, string nativeName, string englishName)
    {
        Code = code;
        NativeName = nativeName;
        EnglishName = englishName;
    }

    public string Code { get; }
    public string NativeName { get; }
    public string EnglishName { get; }

    public override string ToString() => NativeName;
}

/// <summary>A "welcome" greeting used by the first-run splash cycle.</summary>
public sealed class WelcomePhrase
{
    public WelcomePhrase(string code, string nativeName, string text)
    {
        Code = code;
        NativeName = nativeName;
        Text = text;
    }

    public string Code { get; }
    public string NativeName { get; }
    public string Text { get; }
}

/// <summary>
/// Runtime language manager. Strings live in per-language ResourceDictionaries
/// (Localization/Strings.{code}.xaml) merged into Application.Resources, so the whole
/// UI reacts instantly through {DynamicResource Str.*} bindings.
/// </summary>
public static class LocalizationManager
{
    public static readonly IReadOnlyList<AppLanguage> Languages = new[]
    {
        new AppLanguage("ru", "Русский", "Russian"),
        new AppLanguage("en", "English", "English"),
        new AppLanguage("de", "Deutsch", "German"),
        new AppLanguage("es", "Español", "Spanish"),
        new AppLanguage("fr", "Français", "French"),
        new AppLanguage("zh", "中文", "Chinese"),
    };

    /// <summary>Greetings cycled on the first-run splash (fade in, 3 s hold, fade out).</summary>
    public static readonly IReadOnlyList<WelcomePhrase> WelcomePhrases = new[]
    {
        new WelcomePhrase("ru", "Русский", "Добро пожаловать"),
        new WelcomePhrase("en", "English", "Welcome"),
        new WelcomePhrase("de", "Deutsch", "Willkommen"),
        new WelcomePhrase("fr", "Français", "Bienvenue"),
        new WelcomePhrase("es", "Español", "Bienvenido"),
        new WelcomePhrase("it", "Italiano", "Benvenuto"),
        new WelcomePhrase("pt", "Português", "Bem-vindo"),
        new WelcomePhrase("tr", "Türkçe", "Hoş geldiniz"),
        new WelcomePhrase("pl", "Polski", "Witamy"),
        new WelcomePhrase("uk", "Українська", "Ласкаво просимо"),
        new WelcomePhrase("zh", "中文", "欢迎"),
        new WelcomePhrase("ja", "日本語", "ようこそ"),
        new WelcomePhrase("ko", "한국어", "환영합니다"),
        new WelcomePhrase("hi", "हिन्दी", "स्वागत है"),
    };

    private static ResourceDictionary? _current;
    private static string _currentCode = "ru";

    /// <summary>Raised on the UI thread after the active dictionary has been swapped.</summary>
    public static event Action? LanguageChanged;

    public static string CurrentCode => _currentCode;

    /// <summary>True once a string dictionary has been loaded (see <see cref="Initialize"/>).</summary>
    public static bool IsInitialized => _current != null;

    public static AppLanguage Current
    {
        get
        {
            foreach (var lang in Languages)
            {
                if (lang.Code == _currentCode) return lang;
            }

            return Languages[0];
        }
    }

    /// <summary>Loads the dictionary for <paramref name="code"/> (no event, for startup).</summary>
    public static void Initialize(string? code)
    {
        var resolved = Normalize(code);
        Apply(resolved, raiseEvent: false);
    }

    public static void ApplyLanguage(string? code)
    {
        var resolved = Normalize(code);
        if (resolved == _currentCode && _current != null)
        {
            return;
        }

        Apply(resolved, raiseEvent: true);
    }

    /// <summary>Indexer-style access for ViewModel strings: Loc.T("VM.Status.SelectGame").</summary>
    public static string T(string key)
    {
        if (Application.Current?.Resources != null && Application.Current.Resources[key] is string s)
        {
            return s;
        }

        return _current?[key] as string ?? key;
    }

    public static string Format(string key, params object?[] args)
    {
        try
        {
            return string.Format(T(key), args);
        }
        catch (FormatException)
        {
            return T(key);
        }
    }

    private static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "ru";

        var clean = code.Trim().ToLowerInvariant();
        if (clean.Length > 2) clean = clean[..2];

        foreach (var lang in Languages)
        {
            if (lang.Code == clean) return clean;
        }

        return "ru";
    }

    private static void Apply(string code, bool raiseEvent)
    {
        var uri = new Uri($"/SmoothFrame.UI;component/Localization/Strings.{code}.xaml", UriKind.Relative);
        var dict = (ResourceDictionary)Application.LoadComponent(uri);

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current != null && merged.Contains(_current))
        {
            merged.Remove(_current);
        }

        merged.Add(dict);
        _current = dict;
        _currentCode = code;

        if (raiseEvent)
        {
            LanguageChanged?.Invoke();
        }
    }
}
