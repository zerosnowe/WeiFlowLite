using WeiFlowLite.Helpers;
using System;
using Windows.System.Profile;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WeiFlowLite
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isLoadingSelection;

        public SettingsPage()
        {
            this.InitializeComponent();
            ApplyMobileLayout();
            LoadThemeSelection();
        }

        private void ApplyMobileLayout()
        {
            if (string.Equals(
                AnalyticsInfo.VersionInfo.DeviceFamily,
                "Windows.Mobile",
                StringComparison.OrdinalIgnoreCase))
            {
                SettingsHeader.Padding = new Thickness(0, 16, 20, 16);
                SettingsBackButton.Visibility = Visibility.Visible;
            }
        }

        private void LoadThemeSelection()
        {
            _isLoadingSelection = true;
            var theme = ThemeHelper.CurrentTheme;
            switch (theme)
            {
                case ThemeType.System:
                    SystemThemeRadio.IsChecked = true;
                    break;
                case ThemeType.Light:
                    LightThemeRadio.IsChecked = true;
                    break;
                case ThemeType.Dark:
                    DarkThemeRadio.IsChecked = true;
                    break;
            }
            _isLoadingSelection = false;
        }

        private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSelection)
            {
                return;
            }

            if (!(sender is RadioButton radio) || radio.Tag == null)
            {
                return;
            }

            if (int.TryParse(radio.Tag.ToString(), out int themeValue))
            {
                var theme = (ThemeType)themeValue;
                ThemeHelper.SetTheme(theme);
                UpdateSidebarBackground();
            }
        }

        private static void UpdateSidebarBackground()
        {
            if (Window.Current.Content is Frame frame &&
                frame.Content is MainPage mainPage)
            {
                mainPage.ApplySidebarBackdrop();
            }
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (Window.Current.Content is Frame frame &&
                frame.Content is MainPage mainPage)
            {
                mainPage.UpdateSidebarOnThemeChange();
            }
        }

        private void SettingsBackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Window.Current.Content is Frame frame &&
                frame.Content is MainPage mainPage)
            {
                mainPage.NavigateBack();
            }
        }
    }
}
