using WeiFlowLite.Helpers;
using WeiFlowLite.Services;
using System;
using Windows.System.Profile;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WeiFlowLite
{
    public sealed partial class SettingsPage : Page
    {
        private readonly LiveTileService _liveTileService = new LiveTileService();
        private bool _isLoadingSelection;

        public SettingsPage()
        {
            this.InitializeComponent();
            ApplyMobileLayout();
            LoadThemeSelection();
            RefreshLiveTileSelection();
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

        internal void RefreshLiveTileSelection()
        {
            _isLoadingSelection = true;

            var isAuthenticated = IsAuthenticated();
            _liveTileService.EnsureRecommendedWhenUnauthenticated(isAuthenticated);
            var source = _liveTileService.GetFeedSource();

            RecommendedTileRadio.IsChecked = source == LiveTileFeedSource.Recommended;
            FollowingTileRadio.IsChecked = source == LiveTileFeedSource.Following && isAuthenticated;
            FollowingTileRadio.IsEnabled = isAuthenticated;
            FollowingTileRadio.Content = isAuthenticated ? "关注" : "关注（登录后可用）";

            if (!isAuthenticated)
            {
                RecommendedTileRadio.IsChecked = true;
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

        private void TileFeedRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSelection)
            {
                return;
            }

            if (!(sender is RadioButton radio) || radio.Tag == null)
            {
                return;
            }

            if (!int.TryParse(radio.Tag.ToString(), out int sourceValue))
            {
                return;
            }

            var source = (LiveTileFeedSource)sourceValue;
            if (source == LiveTileFeedSource.Following && !IsAuthenticated())
            {
                RefreshLiveTileSelection();
                return;
            }

            _liveTileService.SetFeedSource(source);
            RefreshLiveTileAsync();
        }

        private static void UpdateSidebarBackground()
        {
            if (Window.Current.Content is Frame frame &&
                frame.Content is MainPage mainPage)
            {
                mainPage.ApplySidebarBackdrop();
            }
        }

        private static bool IsAuthenticated()
        {
            return Window.Current.Content is Frame frame &&
                frame.Content is MainPage mainPage &&
                mainPage.IsAuthenticatedForSettings;
        }

        private static void RefreshLiveTileAsync()
        {
            if (Window.Current.Content is Frame frame &&
                frame.Content is MainPage mainPage)
            {
                _ = mainPage.RefreshLiveTileAsync();
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
