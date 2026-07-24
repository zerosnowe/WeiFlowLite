using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using WeiFlowLite.Helpers;

namespace WeiFlowLite
{
    sealed partial class App : Application
    {
        private bool _windowActivated;
        private bool _themeInitialized;

        public App()
        {
            // Keep the constructor limited to XAML/runtime registration.
            // Applying RequestedTheme here can fail on ARM/mobile before a window exists.
            this.InitializeComponent();
            this.Suspending += OnSuspending;
            this.UnhandledException += App_UnhandledException;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            if (!_themeInitialized)
            {
                try
                {
                    ThemeHelper.Initialize();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Theme initialization skipped: " + ex);
                }
                _themeInitialized = true;
            }

            Frame rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                }

                Window.Current.Content = rootFrame;
            }

            if (e.PrelaunchActivated == false)
            {
                try
                {
                    if (rootFrame.Content == null && !rootFrame.Navigate(typeof(MainPage), e.Arguments))
                    {
                        ShowStartupFailure(rootFrame, new InvalidOperationException("MainPage 导航没有成功完成。"));
                    }
                }
                catch (Exception ex)
                {
                    ShowStartupFailure(rootFrame, ex);
                }
                finally
                {
                    Window.Current.Activate();
                    _windowActivated = true;
                    _ = ShowDisclaimerIfNeededAsync();
                }
            }
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            e.Handled = true;
            ShowStartupFailure(sender as Frame, e.Exception ??
                new InvalidOperationException("无法加载页面 " + e.SourcePageType?.FullName));
        }

        private static void ShowStartupFailure(Frame rootFrame, Exception exception)
        {
            var details = exception?.ToString() ?? "未知启动错误";
            Debug.WriteLine("StartupFailure: " + details);
            try
            {
                ApplicationData.Current.LocalSettings.Values["LastStartupError"] = details;
            }
            catch
            {
            }

            var message = new TextBlock
            {
                Text = "WeiFlowLite 启动失败\n\n" + details,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 14
            };
            var scrollViewer = new ScrollViewer
            {
                Padding = new Thickness(20, 48, 20, 20),
                Content = message
            };
            var host = new Grid
            {
                Background = new SolidColorBrush(Colors.Black)
            };
            host.Children.Add(scrollViewer);

            if (rootFrame != null)
            {
                rootFrame.Content = host;
            }
            else
            {
                Window.Current.Content = host;
            }
        }

        private static void ShowFatalError(Exception ex)
        {
            try
            {
                var message = new TextBlock
                {
                    Text = "WeiFlowLite 启动失败\n\n" + (ex?.Message ?? "未知错误"),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Colors.White),
                    FontSize = 14
                };
                var scrollViewer = new ScrollViewer
                {
                    Padding = new Thickness(20, 48, 20, 20),
                    Content = message
                };
                var host = new Grid
                {
                    Background = new SolidColorBrush(Colors.Black)
                };
                host.Children.Add(scrollViewer);
                Window.Current.Content = host;
                Window.Current.Activate();
            }
            catch
            {
                // 彻底失败，无法显示任何内容
            }
        }

        private static async Task ShowDisclaimerIfNeededAsync()
        {
            const string key = "DisclaimerAccepted";
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out var value) &&
                    value is bool accepted && accepted)
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "免责声明",
                Content = "本项目仅供学习研究使用，请在24小时内删除。\n\n禁止用于任何商业用途或非法目的。",
                CloseButtonText = "我知道了",
                DefaultButton = ContentDialogButton.Close
            };

            try
            {
                await dialog.ShowAsync();
            }
            catch
            {
            }

            try
            {
                ApplicationData.Current.LocalSettings.Values[key] = true;
            }
            catch
            {
            }
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            deferral.Complete();
        }

        private void App_UnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            var msg = $"UnhandledException: {e.Message} (0x{e.Exception.HResult:X})";
            Debug.WriteLine(msg);
            Debug.WriteLine(e.Exception.ToString());
            try
            {
                ApplicationData.Current.LocalSettings.Values["LastError"] = msg + "\n" + e.Exception;
            }
            catch { }
            if (!_windowActivated)
            {
                ShowStartupFailure(Window.Current.Content as Frame, e.Exception);
                Window.Current.Activate();
                _windowActivated = true;
            }
            e.Handled = true;
        }
    }
}
