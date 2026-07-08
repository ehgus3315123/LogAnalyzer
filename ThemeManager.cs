using System.Windows;

namespace LogAnalyzer
{
    public static class ThemeManager
    {
        public static void Apply(string theme)
        {
            var app = Application.Current;
            if (app == null) return;

            var existing = app.Resources.MergedDictionaries;
            for (int i = existing.Count - 1; i >= 0; i--)
            {
                var src = existing[i].Source?.OriginalString ?? string.Empty;
                if (src.Contains("Theme"))
                    existing.RemoveAt(i);
            }

            string uri = theme == "Dark"
                ? "pack://application:,,,/Themes/DarkTheme.xaml"
                : "pack://application:,,,/Themes/LightTheme.xaml";

            existing.Add(new ResourceDictionary { Source = new System.Uri(uri) });
        }
    }
}
