using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;

namespace WeiFlowLite.Helpers
{
    public enum ThemeType
    {
        System = 0,
        Light = 1,
        Dark = 2
    }

    public static class ThemeHelper
    {
        private const string SettingsKey = "AppTheme";

        public static ThemeType CurrentTheme { get; private set; } = ThemeType.System;

        public static void Initialize()
        {
            var settings = GetSettings();
            if (settings.TryGetValue(SettingsKey, out object value))
            {
                CurrentTheme = CoerceTheme(value);
            }
            ApplyApplicationTheme(CurrentTheme);
        }

        public static void SetTheme(ThemeType theme)
        {
            CurrentTheme = theme;
            var settings = GetSettings();
            settings[SettingsKey] = (int)theme;
            ApplyThemeToCurrentRoot(theme);
        }

        public static bool IsDarkTheme()
        {
            return ResolveElementTheme(CurrentTheme) == ElementTheme.Dark;
        }

        private static void ApplyApplicationTheme(ThemeType theme)
        {
            try
            {
                Application.Current.RequestedTheme = ResolveApplicationTheme(theme);
            }
            catch (System.NotSupportedException)
            {
                ApplyThemeToCurrentRoot(theme);
            }
        }

        private static void ApplyThemeToCurrentRoot(ThemeType theme)
        {
            var elementTheme = ResolveElementTheme(theme);
            if (Window.Current?.Content is FrameworkElement root)
            {
                root.RequestedTheme = elementTheme;
            }
        }

        private static ApplicationTheme ResolveApplicationTheme(ThemeType theme)
        {
            return ResolveElementTheme(theme) == ElementTheme.Dark
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light;
        }

        private static ElementTheme ResolveElementTheme(ThemeType theme)
        {
            switch (theme)
            {
                case ThemeType.Light:
                    return ElementTheme.Light;
                case ThemeType.Dark:
                    return ElementTheme.Dark;
                case ThemeType.System:
                default:
                    return IsSystemDarkTheme() ? ElementTheme.Dark : ElementTheme.Light;
            }
        }

        private static bool IsSystemDarkTheme()
        {
            try
            {
                var color = new UISettings().GetColorValue(UIColorType.Background);
                return color == Colors.Black;
            }
            catch
            {
                return false;
            }
        }

        private static ThemeType CoerceTheme(object value)
        {
            if (value is int intValue && intValue >= 0 && intValue <= 2)
            {
                return (ThemeType)intValue;
            }

            if (value is string stringValue && int.TryParse(stringValue, out int parsedValue) && parsedValue >= 0 && parsedValue <= 2)
            {
                return (ThemeType)parsedValue;
            }

            return ThemeType.System;
        }

        private static IPropertySet GetSettings()
        {
            return ApplicationData.Current.LocalSettings.Values;
        }
    }
}
