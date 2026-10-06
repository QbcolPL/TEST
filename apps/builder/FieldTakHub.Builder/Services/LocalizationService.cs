using System.Windows;

namespace FieldTakHub.Builder.Services;

public static class LocalizationService
{
    public static string CurrentLanguage { get; private set; } = "en";

    public static void Apply(string language)
    {
        language = language.Equals("pl", StringComparison.OrdinalIgnoreCase) ? "pl" : "en";
        var app = Application.Current;
        var dictionaries = app.Resources.MergedDictionaries;
        var old = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Strings.", StringComparison.OrdinalIgnoreCase) == true);
        if (old != null) dictionaries.Remove(old);
        dictionaries.Insert(0, new ResourceDictionary { Source = new Uri($"Resources/Strings.{language}.xaml", UriKind.Relative) });
        CurrentLanguage = language;
    }

    public static string Text(string key, string fallback = "") => Application.Current.TryFindResource(key) as string ?? fallback;
}
