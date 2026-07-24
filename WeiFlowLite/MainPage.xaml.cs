using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using Windows.Storage.Streams;
using Windows.System.Profile;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.Web.Http.Filters;
using WeiFlowLite.Helpers;
using WeiFlowLite.Models;
using WeiFlowLite.Services;

namespace WeiFlowLite
{
    public sealed partial class MainPage : Page
    {
        private static readonly bool IsMobile = string.Equals(
            AnalyticsInfo.VersionInfo.DeviceFamily,
            "Windows.Mobile",
            StringComparison.OrdinalIgnoreCase);

        private const int BatchSize = 10;
        private const string DefaultContainerId = "102803";
        private const string FollowContainerId = "__friends_timeline__";
        private static readonly Uri WeiboLoginUri = new Uri("https://passport.weibo.com/");
        private static readonly Uri WeiboProfileUri = new Uri("https://m.weibo.cn/profile/");
        private const string MobileWeiboHost = "m.weibo.cn";
        private const string PopupScaleXPath = "(UIElement.RenderTransform).(CompositeTransform.ScaleX)";
        private const string PopupScaleYPath = "(UIElement.RenderTransform).(CompositeTransform.ScaleY)";
        private const string PopupTranslateYPath = "(UIElement.RenderTransform).(CompositeTransform.TranslateY)";
        private const string SlideTranslateXPath = "(UIElement.RenderTransform).(CompositeTransform.TranslateX)";
        private const string MessageWebViewDarkModeScript = @"
(function () {
    var styleId = 'weiflow-message-dark-style';
    var metaId = 'weiflow-message-color-scheme';
    var css =
        'html,body,#app,.m-container,.m-container-max,.m-main,.m-page,.m-panel,.card,.card-list,.m-card,.m-cell,.m-tab,.m-top-nav,.nav-main,.msg-main,.chat-main,.chat-list,.msg-list{background:#101010!important;color:#f2f2f2!important;}' +
        'body{color-scheme:dark!important;}' +
        'a,div,span,p,h1,h2,h3,h4,h5,h6,li,section,article,header,footer,main,label{color:#f2f2f2!important;}' +
        '.time,.sub,.sub-text,.txt-sub,.m-text-cut,.m-font-sub,.remark,.desc,.from,.date{color:#a8a8a8!important;}' +
        'input,textarea,select{background:#1b1b1b!important;color:#f2f2f2!important;border-color:#333!important;caret-color:#f2f2f2!important;}' +
        'button,.m-btn,.lite-page-tab,.m-bar-panel{background:#1b1b1b!important;color:#f2f2f2!important;border-color:#333!important;}' +
        '.bubble,.msg-bubble,[class*=bubble],[class*=Bubble]{background:#232323!important;color:#f2f2f2!important;}' +
        'img,video,svg,canvas{background:transparent!important;}';

    function ensureDarkStyle() {
        var head = document.head || document.getElementsByTagName('head')[0] || document.documentElement;
        var style = document.getElementById(styleId);
        if (!style) {
            style = document.createElement('style');
            style.id = styleId;
            head.appendChild(style);
        }
        style.type = 'text/css';
        style.textContent = css;

        var meta = document.getElementById(metaId);
        if (!meta) {
            meta = document.createElement('meta');
            meta.id = metaId;
            meta.name = 'color-scheme';
            head.appendChild(meta);
        }
        meta.content = 'dark';
        document.documentElement.style.backgroundColor = '#101010';
        document.body && (document.body.style.backgroundColor = '#101010');
    }

    ensureDarkStyle();
    if (window.__weiflowMessageDarkTimer) {
        clearInterval(window.__weiflowMessageDarkTimer);
    }
    window.__weiflowMessageDarkTimer = setInterval(ensureDarkStyle, 1200);
})();";
        private const string MessageWebViewClearDarkModeScript = @"
(function () {
    var style = document.getElementById('weiflow-message-dark-style');
    if (style && style.parentNode) {
        style.parentNode.removeChild(style);
    }
    var meta = document.getElementById('weiflow-message-color-scheme');
    if (meta && meta.parentNode) {
        meta.parentNode.removeChild(meta);
    }
    if (window.__weiflowMessageDarkTimer) {
        clearInterval(window.__weiflowMessageDarkTimer);
        window.__weiflowMessageDarkTimer = null;
    }
    document.documentElement.style.backgroundColor = '';
    document.body && (document.body.style.backgroundColor = '');
})();";

        private static readonly string[] ExpectedChannelNames =
        {
            "\u70ed\u95e8",
            "\u699c\u5355",
            "\u540c\u57ce",
            "\u793e\u4f1a",
            "\u79d1\u6280",
            "\u660e\u661f",
            "\u7535\u5f71",
            "\u97f3\u4e50",
            "\u60c5\u611f",
            "\u65f6\u5c1a",
            "\u7f8e\u5986"
        };

        private readonly WeiboApiService _apiService;
        private readonly WeiboAuthStorage _authStorage;
        private readonly WeiboImageLoader _imageLoader;
        private readonly LiveTileService _liveTileService;
        private readonly ObservableCollection<WeiboItemViewModel> _weiboItems;
        private readonly ObservableCollection<WeiboItemViewModel> _searchResults;
        private readonly ObservableCollection<WeiboCommentViewModel> _detailComments;
        private readonly ObservableCollection<WeiboHotSearchViewModel> _hotSearchItems;
        private readonly ObservableCollection<WeiboItemViewModel> _userTimelineItems;
        private readonly ObservableCollection<WeiboMessageCenterItemViewModel> _messageCenterItems;
        private readonly Queue<WeiboMblog> _pendingMblogs;
        private readonly Stack<string> _backStack;
        private readonly HashSet<string> _loadedMblogIds;
        private readonly HashSet<string> _searchLoadedMblogIds;
        private ScrollViewer _timelineScrollViewer;
        private ScrollViewer _searchScrollViewer;
        private ScrollViewer _userTimelineScrollViewer;
        private WebView _loginWebView;
        private WebView _messageWebView;
        private MediaElement _previewVideo;
        private bool _isLoadingChannels;
        private int _timelineLoadingVersion = -1;
        private bool _isResettingTimeline;
        private bool _isLoadingHotSearch;
        private bool _isLoadingMoreSearch;
        private bool _isLoadingMoreComments;
        private bool _authRestored;
        private bool _isAuthenticated;
        private bool _isSavingLogin;
        private bool _hasSavedLoginThisSession;
        private ContentDialog _activeErrorDialog;
        private CoreApplicationViewTitleBar _coreTitleBar;
        private bool _hasMore = true;
        private int _timelineRequestVersion;
        private long _nextSinceId;
        private long _nextSearchSinceId;
        private int _detailRequestVersion;
        private string _nextCursor;
        private string _currentSearchKeyword;
        private string _currentDetailId;
        private string _nextCommentMaxId;
        private string _activePreviewMediaUrl;
        private string _activePreviewLaunchUrl;
        private string _activePreviewShareUrl;
        private StorageFile _activePreviewImageFile;
        private bool _activePreviewIsVideo;
        private string _currentView = "home";
        private string _detailParentView = "home";
        private string _transitionSourceView;
        private string _selectedContainerId = DefaultContainerId;
        private double? _sameCityLatitude;
        private double? _sameCityLongitude;
        private DateTimeOffset? _sameCityLocationTimestamp;
        private WeiboItemViewModel _detailItem;
        private WeiboUserProfileViewModel _userProfile;
        private long _currentUserProfileUid;
        private long _currentAuthenticatedUserUid;
        private bool _isViewingAuthenticatedUserProfile;
        private long _nextUserTimelineSinceId;
        private bool _hasMoreSearch;
        private bool _hasMoreComments;
        private bool _hasMoreUserTimeline;
        private bool _isLoadingUserTimeline;
        private bool _isUpdatingFollowCategory;

        private bool _isNavigatingProgrammatically;

        public MainPage()
        {
            InitializeComponent();
            _apiService = new WeiboApiService();
            _authStorage = new WeiboAuthStorage();
            _imageLoader = new WeiboImageLoader();
            _liveTileService = new LiveTileService();
            _weiboItems = new ObservableCollection<WeiboItemViewModel>();
            _searchResults = new ObservableCollection<WeiboItemViewModel>();
            _detailComments = new ObservableCollection<WeiboCommentViewModel>();
            _hotSearchItems = new ObservableCollection<WeiboHotSearchViewModel>();
            _userTimelineItems = new ObservableCollection<WeiboItemViewModel>();
            _messageCenterItems = new ObservableCollection<WeiboMessageCenterItemViewModel>();
            _pendingMblogs = new Queue<WeiboMblog>();
            _backStack = new Stack<string>();
            _loadedMblogIds = new HashSet<string>();
            _searchLoadedMblogIds = new HashSet<string>();
            WeiboListView.ItemsSource = _weiboItems;
            SearchResultsList.ItemsSource = _searchResults;
            DetailCommentsList.ItemsSource = _detailComments;
            SearchDetailCommentsList.ItemsSource = _detailComments;
            HotSearchList.ItemsSource = _hotSearchItems;
            UserTimelineList.ItemsSource = _userTimelineItems;
            MessageCenterList.ItemsSource = _messageCenterItems;
            SizeChanged += MainPage_SizeChanged;
            if (!IsMobile)
            {
                ActualThemeChanged += MainPage_ActualThemeChanged;
            }
            DetailScrollViewer.ViewChanged += DetailScrollViewer_ViewChanged;
            SearchDetailScrollViewer.ViewChanged += DetailScrollViewer_ViewChanged;
            UserProfileScrollViewer.ViewChanged += UserTimelineScrollViewer_ViewChanged;
            if (!IsMobile)
            {
                ConfigureTitleBar();
                TitleBarDragRegion.Visibility = Visibility.Visible;
            }
            ApplyShellLayout();
            ApplySidebarBackdrop();
            ApplyMediaPreviewLayout();
            SetLoadingState(true, true);
            _isNavigatingProgrammatically = true;
            NavListView.SelectedItem = NavHomeItem;
            _isNavigatingProgrammatically = false;
            ShowPanel("home");
            if (!IsMobile)
            {
                UpdateBackButtonState();
            }
            SystemNavigationManager.GetForCurrentView().BackRequested += OnSystemBackRequested;
            DataTransferManager.GetForCurrentView().DataRequested += OnShareDataRequested;
        }

        private void MainPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyShellLayout();
            ApplySidebarBackdrop();
            ApplyMediaPreviewLayout();

            if ((string.Equals(_currentView, "detail", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(_currentView, "profile", StringComparison.OrdinalIgnoreCase)) &&
                !IsNarrowOrPortrait())
            {
                var parentView = string.IsNullOrWhiteSpace(_detailParentView) ? "home" : _detailParentView;
                _currentView = parentView;
                _backStack.Clear();
                SelectNavigationItem(parentView);
                ShowPanel(parentView);
                UpdateBackButtonState();
                return;
            }

            if (string.Equals(_currentView, "searchDetail", StringComparison.OrdinalIgnoreCase) &&
                !IsNarrowOrPortrait())
            {
                _currentView = "search";
                _backStack.Clear();
                SelectNavigationItem("search");
                ShowPanel("search");
                UpdateBackButtonState();
                return;
            }

            if (string.Equals(_currentView, "home", StringComparison.OrdinalIgnoreCase))
            {
                ApplyHomeListLayout();
            }
            else if (string.Equals(_currentView, "search", StringComparison.OrdinalIgnoreCase))
            {
                ApplySearchLayout();
            }
            else if (string.Equals(_currentView, "searchDetail", StringComparison.OrdinalIgnoreCase))
            {
                ApplySearchDetailSlideLayout(true);
            }
            else if (string.Equals(_currentView, "detail", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(_currentView, "profile", StringComparison.OrdinalIgnoreCase))
            {
                ApplyHomeDetailSlideLayout(true);
            }
        }

        private void HamburgerButton_Checked(object sender, RoutedEventArgs e)
        {
            RootSplitView.IsPaneOpen = true;
        }

        private void HamburgerButton_Unchecked(object sender, RoutedEventArgs e)
        {
            RootSplitView.IsPaneOpen = false;
        }

        private void NavListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isNavigatingProgrammatically)
            {
                return;
            }

            var item = (sender as ListView)?.SelectedItem as ListViewItem;
            if (item == null)
            {
                return;
            }

            NavigateTo(item.Tag as string, true);

            // 清空另一个列表的选中项，避免混淆
            if (sender == NavListView)
            {
                _isNavigatingProgrammatically = true;
                NavFooterListView.SelectedItem = null;
                _isNavigatingProgrammatically = false;
            }
        }

        private void NavFooterListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isNavigatingProgrammatically)
            {
                return;
            }

            var item = (sender as ListView)?.SelectedItem as ListViewItem;
            if (item == null)
            {
                return;
            }

            NavigateTo(item.Tag as string, true);

            // 清空另一个列表的选中项
            _isNavigatingProgrammatically = true;
            NavListView.SelectedItem = null;
            _isNavigatingProgrammatically = false;
        }

        private void OnSystemBackRequested(object sender, BackRequestedEventArgs e)
        {
            e.Handled = NavigateBack();
        }

        internal bool NavigateBack()
        {
            if (MediaPreviewOverlay.Visibility == Visibility.Visible)
            {
                CloseMediaPreview(true);
                if (string.Equals(_currentView, "media", StringComparison.OrdinalIgnoreCase))
                {
                    _transitionSourceView = _currentView;
                    _currentView = _backStack.Count > 0 ? _backStack.Pop() : (_detailParentView ?? "home");
                    ShowPanel(_currentView);
                    _transitionSourceView = null;
                    SelectNavigationItem(_currentView);
                    UpdateBackButtonState();
                }
                return true;
            }

            if (_backStack.Count == 0)
            {
                return false;
            }

            var previous = _backStack.Pop();
            _transitionSourceView = _currentView;
            ShowPanel(previous);
            _transitionSourceView = null;
            _currentView = previous;
            SelectNavigationItem(previous);
            UpdateBackButtonState();
            return true;
        }

        private void NavigateTo(string tag, bool addToBackStack)
        {
            tag = string.IsNullOrWhiteSpace(tag) ? "home" : tag;
            if (string.Equals(_currentView, tag, StringComparison.OrdinalIgnoreCase))
            {
                ShowPanel(tag);
                CloseMobilePane();
                return;
            }

            if (addToBackStack && !string.Equals(_currentView, "home", StringComparison.OrdinalIgnoreCase))
            {
                _backStack.Push(_currentView);
            }
            else if (addToBackStack && string.Equals(_currentView, "home", StringComparison.OrdinalIgnoreCase) && !string.Equals(tag, "home", StringComparison.OrdinalIgnoreCase))
            {
                _backStack.Push(_currentView);
            }

            _transitionSourceView = _currentView;
            ShowPanel(tag);
            _transitionSourceView = null;
            CloseMobilePane();
            _currentView = tag;
            UpdateBackButtonState();
        }

        private void ShowPanel(string tag)
        {
            var showLogin = string.Equals(tag, "login", StringComparison.OrdinalIgnoreCase);
            var showSearch = string.Equals(tag, "search", StringComparison.OrdinalIgnoreCase);
            var showSearchDetail = string.Equals(tag, "searchDetail", StringComparison.OrdinalIgnoreCase);
            var showMessages = string.Equals(tag, "messages", StringComparison.OrdinalIgnoreCase);
            var showDetail = string.Equals(tag, "detail", StringComparison.OrdinalIgnoreCase);
            var showSettings = string.Equals(tag, "settings", StringComparison.OrdinalIgnoreCase);
            var showMedia = string.Equals(tag, "media", StringComparison.OrdinalIgnoreCase);
            var showProfile = string.Equals(tag, "profile", StringComparison.OrdinalIgnoreCase);
            var isSecondary = showSearch || showSearchDetail || showDetail || showMessages || showSettings || showMedia || showProfile;
            if (!showMedia && MediaPreviewOverlay.Visibility == Visibility.Visible)
            {
                CloseMediaPreview(true);
            }

            HomePanel.Visibility = (showLogin || showSearch || showSearchDetail || showMessages || showSettings || showMedia) ? Visibility.Collapsed : Visibility.Visible;
            LoginPanel.Visibility = showLogin ? Visibility.Visible : Visibility.Collapsed;
            SearchPanel.Visibility = (showSearch || showSearchDetail) ? Visibility.Visible : Visibility.Collapsed;
            MessagePanel.Visibility = showMessages ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
            MediaPreviewOverlay.Visibility = showMedia ? Visibility.Visible : Visibility.Collapsed;

            if (showProfile)
            {
                UserProfilePanel.Visibility = Visibility.Visible;
                DetailScrollViewer.Visibility = Visibility.Collapsed;
                DetailBottomBar.Visibility = Visibility.Collapsed;
                DetailPlaceholder.Visibility = Visibility.Collapsed;

                // Keep profile navigation consistent with the mobile detail/search slide.
                // Set the start position before the panel can be rendered, then animate it in.
                var profileTransform = EnsurePanelTransform(UserProfilePanel);
                profileTransform.TranslateX = GetPageStackWidth();
                AnimatePanelTranslateX(UserProfilePanel, 0);
            }
            else
            {
                UserProfilePanel.Visibility = Visibility.Collapsed;
                UserProfileLoadingIndicator.Visibility = Visibility.Collapsed;
                EnsurePanelTransform(UserProfilePanel).TranslateX = 0;
            }

            if (IsMobile)
            {
                HamburgerButton.Visibility = isSecondary ? Visibility.Collapsed : Visibility.Visible;
            }

            if (showLogin)
            {
                _ = StartLoginAsync();
            }
            else if (showSearch)
            {
                ApplySearchLayout();
                _ = LoadHotSearchAsync();
            }
            else if (showSearchDetail)
            {
                ApplySearchDetailSlideLayout(true);
            }
            else if (showMessages)
            {
                _ = LoadMessageCenterAsync();
            }
            else if (showSettings)
            {
                if (SettingsFrame.Content == null)
                {
                    try
                    {
                        SettingsFrame.Navigate(typeof(SettingsPage));
                    }
                    catch (Exception ex)
                    {
                        _ = ShowErrorAsync($"设置页加载失败: {ex.Message}");
                    }
                }

                if (SettingsFrame.Content is SettingsPage settingsPage)
                {
                    settingsPage.RefreshLiveTileSelection();
                }
            }
            else if (showDetail)
            {
                ApplyHomeDetailSlideLayout(true);
            }
            else if (showProfile)
            {
                ApplyHomeDetailSlideLayout(true);
            }
            else if (showMedia)
            {
                ApplyMediaPreviewLayout();
            }
            else
            {
                ApplyHomeListLayout();
            }
        }

        private void ApplyStandardPageTransition(string tag)
        {
            var offset = GetPageTransitionOffset(tag);
            SetPageEntranceTransition(HomePanel, offset);
            SetPageEntranceTransition(SearchPanel, offset);
            SetPageEntranceTransition(MessagePanel, offset);
            SetPageEntranceTransition(LoginPanel, offset);
            SetPageEntranceTransition(SettingsPanel, offset);
            SetPageEntranceTransition(DetailPanel, offset);
            SetPageEntranceTransition(SearchDetailPanel, offset);
            SetPageEntranceTransition(UserProfilePanel, offset);
            SetPageEntranceTransition(MediaPreviewOverlay, offset);
        }

        private static double GetPageTransitionOffset(string tag)
        {
            if (string.Equals(tag, "home", StringComparison.OrdinalIgnoreCase))
            {
                return -32;
            }

            if (string.Equals(tag, "detail", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tag, "profile", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tag, "media", StringComparison.OrdinalIgnoreCase))
            {
                return 96;
            }

            return 32;
        }

        private static void SetPageEntranceTransition(UIElement element, double horizontalOffset)
        {
            if (element == null)
            {
                return;
            }

            element.Transitions = new TransitionCollection
            {
                new EntranceThemeTransition
                {
                    FromHorizontalOffset = horizontalOffset
                }
            };
        }

        private void RunCoolapkPopupTransition(string targetTag)
        {
            var target = GetPageTransitionTarget(targetTag);
            if (target == null || target.Visibility != Visibility.Visible)
            {
                return;
            }

            if (!(target.RenderTransform is CompositeTransform))
            {
                target.RenderTransform = new CompositeTransform();
            }

            target.RenderTransformOrigin = new Point(0.5, 0.5);
            target.Opacity = 0;

            var transform = (CompositeTransform)target.RenderTransform;
            transform.ScaleX = 0.90;
            transform.ScaleY = 0.90;
            transform.TranslateY = 30;

            var storyboard = new Storyboard();

            var scaleX = CreateCoolapkPopupScaleAnimation();
            Storyboard.SetTarget(scaleX, target);
            Storyboard.SetTargetProperty(scaleX, PopupScaleXPath);
            storyboard.Children.Add(scaleX);

            var scaleY = CreateCoolapkPopupScaleAnimation();
            Storyboard.SetTarget(scaleY, target);
            Storyboard.SetTargetProperty(scaleY, PopupScaleYPath);
            storyboard.Children.Add(scaleY);

            var translateY = CreateCoolapkPopupTranslateAnimation();
            Storyboard.SetTarget(translateY, target);
            Storyboard.SetTargetProperty(translateY, PopupTranslateYPath);
            storyboard.Children.Add(translateY);

            var opacity = CreateCoolapkPopupOpacityAnimation();
            Storyboard.SetTarget(opacity, target);
            Storyboard.SetTargetProperty(opacity, "Opacity");
            storyboard.Children.Add(opacity);

            storyboard.Completed += (sender, args) =>
            {
                target.Opacity = 1;
                transform.ScaleX = 1;
                transform.ScaleY = 1;
                transform.TranslateY = 0;
            };

            storyboard.Begin();
        }

        private FrameworkElement GetPageTransitionTarget(string tag)
        {
            if (string.Equals(tag, "search", StringComparison.OrdinalIgnoreCase))
            {
                return SearchPanel;
            }

            if (string.Equals(tag, "messages", StringComparison.OrdinalIgnoreCase))
            {
                return MessagePanel;
            }

            if (string.Equals(tag, "login", StringComparison.OrdinalIgnoreCase))
            {
                return LoginPanel;
            }

            if (string.Equals(tag, "settings", StringComparison.OrdinalIgnoreCase))
            {
                return SettingsPanel;
            }

            if (string.Equals(tag, "detail", StringComparison.OrdinalIgnoreCase))
            {
                return DetailPanel;
            }

            if (string.Equals(tag, "profile", StringComparison.OrdinalIgnoreCase))
            {
                return UserProfilePanel;
            }

            if (string.Equals(tag, "media", StringComparison.OrdinalIgnoreCase))
            {
                return MediaPreviewOverlay;
            }

            return HomePanel;
        }

        private static DoubleAnimationUsingKeyFrames CreateCoolapkPopupScaleAnimation()
        {
            return new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(920),
                KeyFrames =
                {
                    new DiscreteDoubleKeyFrame
                    {
                        KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0)),
                        Value = 0.90
                    },
                    new SplineDoubleKeyFrame
                    {
                        KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)),
                        Value = 1.045,
                        KeySpline = CreateCoolapkFrame1Spline()
                    },
                    new SplineDoubleKeyFrame
                    {
                        KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(920)),
                        Value = 1.0,
                        KeySpline = CreateCoolapkFrame2Spline()
                    }
                }
            };
        }

        private static DoubleAnimationUsingKeyFrames CreateCoolapkPopupTranslateAnimation()
        {
            return new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(920),
                KeyFrames =
                {
                    new DiscreteDoubleKeyFrame
                    {
                        KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0)),
                        Value = 30
                    },
                    new SplineDoubleKeyFrame
                    {
                        KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)),
                        Value = -4,
                        KeySpline = CreateCoolapkFrame1Spline()
                    },
                    new SplineDoubleKeyFrame
                    {
                        KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(920)),
                        Value = 0,
                        KeySpline = CreateCoolapkFrame2Spline()
                    }
                }
            };
        }

        private static DoubleAnimationUsingKeyFrames CreateCoolapkPopupOpacityAnimation()
        {
            return new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(420),
                KeyFrames =
                {
                    new DiscreteDoubleKeyFrame
                    {
                        KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0)),
                        Value = 0
                    },
                    new SplineDoubleKeyFrame
                    {
                        KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)),
                        Value = 1,
                        KeySpline = CreateCoolapkFrame2Spline()
                    }
                }
            };
        }

        private static KeySpline CreateCoolapkFrame1Spline()
        {
            return new KeySpline
            {
                ControlPoint1 = new Point(0.9, 0.1),
                ControlPoint2 = new Point(1.0, 0.2)
            };
        }

        private static KeySpline CreateCoolapkFrame2Spline()
        {
            return new KeySpline
            {
                ControlPoint1 = new Point(0.1, 0.9),
                ControlPoint2 = new Point(0.2, 1.0)
            };
        }

        private void RunCoolapkDrillInTransition(string sourceTag, string targetTag)
        {
            if (string.IsNullOrWhiteSpace(sourceTag) ||
                !IsDrillInPage(targetTag))
            {
                return;
            }

            var entranceTargetName = GetDrillInEntranceTargetName(targetTag);
            var exitTargetName = GetDrillInExitTargetName(sourceTag);
            if (string.IsNullOrWhiteSpace(entranceTargetName) ||
                string.IsNullOrWhiteSpace(exitTargetName))
            {
                return;
            }

            try
            {
                var storyboard = new Storyboard();
                storyboard.Children.Add(new DrillInThemeAnimation
                {
                    EntranceTargetName = entranceTargetName,
                    ExitTargetName = exitTargetName
                });
                storyboard.Begin();
            }
            catch
            {
            }
        }

        private static bool IsDrillInPage(string tag)
        {
            return string.Equals(tag, "detail", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(tag, "profile", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(tag, "media", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetDrillInEntranceTargetName(string tag)
        {
            if (string.Equals(tag, "detail", StringComparison.OrdinalIgnoreCase))
            {
                return "DetailPanel";
            }

            if (string.Equals(tag, "profile", StringComparison.OrdinalIgnoreCase))
            {
                return "UserProfilePanel";
            }

            if (string.Equals(tag, "media", StringComparison.OrdinalIgnoreCase))
            {
                return "MediaPreviewOverlay";
            }

            return null;
        }

        private static string GetDrillInExitTargetName(string tag)
        {
            if (string.Equals(tag, "search", StringComparison.OrdinalIgnoreCase))
            {
                return "SearchResultsList";
            }

            if (string.Equals(tag, "detail", StringComparison.OrdinalIgnoreCase))
            {
                return "DetailPanel";
            }

            if (string.Equals(tag, "profile", StringComparison.OrdinalIgnoreCase))
            {
                return "UserProfilePanel";
            }

            if (string.Equals(tag, "media", StringComparison.OrdinalIgnoreCase))
            {
                return "MediaPreviewOverlay";
            }

            return "WeiboListView";
        }

        private void SelectNavigationItem(string tag)
        {
            _isNavigatingProgrammatically = true;
            if (string.Equals(tag, "home", StringComparison.OrdinalIgnoreCase))
            {
                NavFooterListView.SelectedItem = null;
                NavListView.SelectedItem = NavHomeItem;
            }
            else if (string.Equals(tag, "search", StringComparison.OrdinalIgnoreCase))
            {
                NavFooterListView.SelectedItem = null;
                NavListView.SelectedItem = NavSearchItem;
            }
            else if (string.Equals(tag, "messages", StringComparison.OrdinalIgnoreCase))
            {
                NavFooterListView.SelectedItem = null;
                NavListView.SelectedItem = NavMessageItem;
            }
            else if (string.Equals(tag, "login", StringComparison.OrdinalIgnoreCase))
            {
                NavListView.SelectedItem = null;
                NavFooterListView.SelectedItem = NavLoginItem;
            }
            else if (string.Equals(tag, "settings", StringComparison.OrdinalIgnoreCase))
            {
                NavListView.SelectedItem = null;
                NavFooterListView.SelectedItem = NavSettingsItem;
            }
            else if (string.Equals(tag, "profile", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(tag, "detail", StringComparison.OrdinalIgnoreCase))
            {
                NavListView.SelectedItem = null;
                NavFooterListView.SelectedItem = null;
            }
            else
            {
                NavListView.SelectedItem = null;
                NavFooterListView.SelectedItem = null;
            }
            _isNavigatingProgrammatically = false;
        }

        private void UpdateBackButtonState()
        {
            if (IsMobile)
            {
                return;
            }

            var hasBack = _backStack.Count > 0;
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = hasBack
                ? AppViewBackButtonVisibility.Visible
                : AppViewBackButtonVisibility.Collapsed;
        }

        private async void WeiboListView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                AttachScrollViewer();
                SetLoadingState(true, true);
                await RestoreAuthCookiesAsync();
                await LoadCategoriesAsync();
                await LoadNextBatchAsync(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WeiboListView_Loaded failed: {ex}");
            }
        }

        private async Task RestoreAuthCookiesAsync()
        {
            if (_authRestored)
            {
                return;
            }

            _authRestored = true;
            try
            {
                var cookies = await _authStorage.LoadAsync();
                _apiService.ApplyStoredCookies(cookies);
                ApplyStoredCookiesToWebView(cookies);
                _isAuthenticated = _apiService.HasAuthenticatedSession;
                _liveTileService.EnsureRecommendedWhenUnauthenticated(_isAuthenticated);
                UpdateFollowCategoryVisibility();
            }
            catch
            {
            }
        }

        private async Task StartLoginAsync()
        {
            await RestoreAuthCookiesAsync();

            // 已有有效会话时，登录入口直接打开原生个人中心；无会话或会话已失效才进入网页登录。
            if (_apiService.HasAuthenticatedSession)
            {
                try
                {
                    var currentUser = await _apiService.GetCurrentUserAsync();
                    if (currentUser?.Id > 0)
                    {
                        _currentAuthenticatedUserUid = currentUser.Id;
                        _isAuthenticated = true;
                        _currentView = "home";
                        _backStack.Clear();
                        ShowPanel("home");
                        await OpenUserProfileAsync(currentUser.Id, true);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"读取当前登录用户失败，将进入网页登录: {ex.Message}");
                }
            }

            var webView = EnsureLoginWebView();
            _hasSavedLoginThisSession = false;
            webView.Navigate(WeiboLoginUri);
        }

        private WebView EnsureLoginWebView()
        {
            if (_loginWebView == null)
            {
                _loginWebView = new WebView();
                _loginWebView.NavigationCompleted += LoginWebView_NavigationCompleted;
                LoginWebViewHost.Children.Add(_loginWebView);
            }

            return _loginWebView;
        }

        private WebView EnsureMessageWebView()
        {
            if (_messageWebView == null)
            {
                _messageWebView = new WebView();
                _messageWebView.NavigationCompleted += MessageWebView_NavigationCompleted;
                MessageWebViewHost.Children.Add(_messageWebView);
            }

            return _messageWebView;
        }

        private void RefreshMessageWebViewIfOpened()
        {
            var source = _messageWebView?.Source;
            if (source != null)
            {
                _messageWebView.Navigate(source);
            }
        }

        private async void MessageWebView_NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            await ApplyMessageWebViewThemeAsync(sender);
        }

        private async Task ApplyMessageWebViewThemeAsync(WebView webView = null)
        {
            webView = webView ?? _messageWebView;
            if (webView == null)
            {
                return;
            }

            var script = ThemeHelper.IsDarkTheme()
                ? MessageWebViewDarkModeScript
                : MessageWebViewClearDarkModeScript;

            try
            {
                await webView.InvokeScriptAsync("eval", new[] { script });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"私信网页主题注入失败: {ex.Message}");
            }
        }

        private MediaElement EnsurePreviewVideo()
        {
            if (_previewVideo == null)
            {
                _previewVideo = new MediaElement
                {
                    AreTransportControlsEnabled = true,
                    AutoPlay = false,
                    Stretch = Stretch.Uniform
                };
                PreviewVideoHost.Children.Add(_previewVideo);
            }

            return _previewVideo;
        }

        private async void LoginWebView_NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            if (_isSavingLogin || _hasSavedLoginThisSession)
            {
                return;
            }

            var capturedCookies = CaptureWeiboCookiesFromWebView();
            if (!HasMobileWeiboAuthenticationCookie(capturedCookies))
            {
                if (args.Uri == null || !string.Equals(args.Uri.Host, MobileWeiboHost, StringComparison.OrdinalIgnoreCase))
                {
                    sender.Navigate(WeiboProfileUri);
                }

                return;
            }

            try
            {
                _isSavingLogin = true;
                var cookies = await WaitForMobileWeiboAuthenticationCookiesAsync(capturedCookies);
                await _authStorage.SaveAsync(cookies);
                _apiService.ApplyStoredCookies(cookies);
                ApplyStoredCookiesToWebView(cookies);
                _isAuthenticated = _apiService.HasAuthenticatedSession;
                if (!_isAuthenticated)
                {
                    throw new InvalidOperationException("微博移动站登录 Cookie 尚未完成同步，请稍后重试。");
                }

                var currentUser = await _apiService.GetCurrentUserAsync();
                if (currentUser?.Id > 0)
                {
                    _currentAuthenticatedUserUid = currentUser.Id;
                }

                _hasSavedLoginThisSession = true;

                if (args.Uri == null ||
                    !string.Equals(args.Uri.Host, WeiboProfileUri.Host, StringComparison.OrdinalIgnoreCase) ||
                    !args.Uri.AbsolutePath.StartsWith("/profile", StringComparison.OrdinalIgnoreCase))
                {
                    sender.Navigate(WeiboProfileUri);
                }

                RefreshMessageWebViewIfOpened();
                _ = RefreshHomeForAuthenticatedUserAsync();
                SelectNavigationItem("home");
                _currentView = "home";
                _backStack.Clear();
                ShowPanel("home");
                if (_currentAuthenticatedUserUid > 0)
                {
                    await OpenUserProfileAsync(_currentAuthenticatedUserUid, true);
                }
            }
            catch (Exception ex)
            {
                _hasSavedLoginThisSession = false;
                await ShowErrorAsync($"登录状态保存失败: {ex.Message}");
            }
            finally
            {
                _isSavingLogin = false;
            }
        }

        private async Task RefreshHomeForAuthenticatedUserAsync()
        {
            try
            {
                ResetTimeline();
                ClearDetail();
                CategoryPivot.Items.Clear();
                _hasMore = true;
                _nextSinceId = 0;
                _nextCursor = null;
                _selectedContainerId = DefaultContainerId;
                SetLoadingState(true, true);
                await RefreshAuthenticatedCookiesFromWebViewAsync();
                await LoadCategoriesAsync();
                await LoadNextBatchAsync(true);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"登录状态同步失败: {ex.Message}");
            }
        }

        private bool HasLoadedApiCategories()
        {
            return CategoryPivot.Items
                .OfType<PivotItem>()
                .Select(item => (item.Tag as WeiboChannel)?.Gid)
                .Any(gid => !string.IsNullOrWhiteSpace(gid) &&
                            !string.Equals(gid, FollowContainerId, StringComparison.Ordinal));
        }

        private void EnsureDefaultTimelineCategory()
        {
            if (CategoryPivot.Items
                .OfType<PivotItem>()
                .Any(item => string.Equals(
                    (item.Tag as WeiboChannel)?.Gid,
                    DefaultContainerId,
                    StringComparison.Ordinal)))
            {
                return;
            }

            CategoryPivot.Items.Add(new PivotItem
            {
                Header = "热门",
                Tag = new WeiboChannel
                {
                    Gid = DefaultContainerId,
                    Name = "热门"
                }
            });
        }

        private void SelectTimelineCategory(string preferredContainerId)
        {
            var items = CategoryPivot.Items.OfType<PivotItem>().ToList();
            if (items.Count == 0)
            {
                return;
            }

            PivotItem selectedItem = null;
            if (!string.IsNullOrWhiteSpace(preferredContainerId))
            {
                selectedItem = items.FirstOrDefault(item => string.Equals(
                    (item.Tag as WeiboChannel)?.Gid,
                    preferredContainerId,
                    StringComparison.Ordinal));
            }

            if (selectedItem == null)
            {
                selectedItem = items.FirstOrDefault(item => string.Equals(
                    (item.Tag as WeiboChannel)?.Gid,
                    DefaultContainerId,
                    StringComparison.Ordinal));
            }

            if (selectedItem == null)
            {
                selectedItem = items.FirstOrDefault(item => !string.Equals(
                    (item.Tag as WeiboChannel)?.Gid,
                    FollowContainerId,
                    StringComparison.Ordinal));
            }

            selectedItem = selectedItem ?? items.FirstOrDefault();
            var selectedChannel = selectedItem?.Tag as WeiboChannel;
            if (selectedChannel == null)
            {
                return;
            }

            _isUpdatingFollowCategory = true;
            try
            {
                CategoryPivot.SelectedItem = selectedItem;
                _selectedContainerId = selectedChannel.Gid;
            }
            finally
            {
                _isUpdatingFollowCategory = false;
            }
        }

        private void UpdateFollowCategoryVisibility()
        {
            var followItem = CategoryPivot.Items
                .OfType<PivotItem>()
                .FirstOrDefault(item => string.Equals(
                    (item.Tag as WeiboChannel)?.Gid,
                    FollowContainerId,
                    StringComparison.Ordinal));

            _isUpdatingFollowCategory = true;
            try
            {
                if (_isAuthenticated)
                {
                    if (followItem == null)
                    {
                        CategoryPivot.Items.Insert(0, new PivotItem
                        {
                            Header = "关注",
                            Tag = new WeiboChannel
                            {
                                Gid = FollowContainerId,
                                Name = "关注"
                            }
                        });
                    }
                }
                else if (followItem != null)
                {
                    var wasSelected = ReferenceEquals(CategoryPivot.SelectedItem, followItem);
                    CategoryPivot.Items.Remove(followItem);
                    if (wasSelected)
                    {
                        var replacement = CategoryPivot.Items.OfType<PivotItem>().FirstOrDefault();
                        CategoryPivot.SelectedItem = replacement;
                        _selectedContainerId = (replacement?.Tag as WeiboChannel)?.Gid ?? DefaultContainerId;
                    }
                }
            }
            finally
            {
                _isUpdatingFollowCategory = false;
            }
        }

        private static IReadOnlyList<WeiboStoredCookie> CaptureWeiboCookiesFromWebView()
        {
            var manager = new HttpBaseProtocolFilter().CookieManager;
            var domains = new[]
            {
                new Uri("https://m.weibo.cn/"),
                new Uri("https://weibo.cn/"),
                new Uri("https://weibo.com/"),
                new Uri("https://passport.weibo.com/")
            };

            return domains
                .SelectMany(uri =>
                {
                    try
                    {
                        return (IEnumerable<Windows.Web.Http.HttpCookie>)manager.GetCookies(uri);
                    }
                    catch
                    {
                        return Enumerable.Empty<Windows.Web.Http.HttpCookie>();
                    }
                })
                .Where(cookie => !string.IsNullOrWhiteSpace(cookie.Name) &&
                                 !string.IsNullOrWhiteSpace(cookie.Value) &&
                                 !string.IsNullOrWhiteSpace(cookie.Domain))
                .GroupBy(cookie => $"{cookie.Domain}|{cookie.Path}|{cookie.Name}")
                .Select(group => group.First())
                .Select(cookie => new WeiboStoredCookie
                {
                    Name = cookie.Name,
                    Value = cookie.Value,
                    Domain = cookie.Domain,
                    Path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path
                })
                .ToList();
        }

        private static bool HasMobileWeiboAuthenticationCookie(IEnumerable<WeiboStoredCookie> cookies)
        {
            return (cookies ?? Enumerable.Empty<WeiboStoredCookie>()).Any(cookie =>
                string.Equals(cookie.Name, "SUB", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(cookie.Value) &&
                IsCookieApplicableToHost(cookie.Domain, MobileWeiboHost));
        }

        private static bool HasMobileWeiboSubpCookie(IEnumerable<WeiboStoredCookie> cookies)
        {
            return (cookies ?? Enumerable.Empty<WeiboStoredCookie>()).Any(cookie =>
                string.Equals(cookie.Name, "SUBP", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(cookie.Value) &&
                IsCookieApplicableToHost(cookie.Domain, MobileWeiboHost));
        }

        private static string GetMobileWeiboSubValue(IEnumerable<WeiboStoredCookie> cookies)
        {
            return (cookies ?? Enumerable.Empty<WeiboStoredCookie>())
                .Where(cookie => string.Equals(cookie.Name, "SUB", StringComparison.OrdinalIgnoreCase) &&
                                 IsCookieApplicableToHost(cookie.Domain, MobileWeiboHost))
                .Select(cookie => cookie.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        private async Task<IReadOnlyList<WeiboStoredCookie>> WaitForMobileWeiboAuthenticationCookiesAsync(
            IReadOnlyList<WeiboStoredCookie> initialCookies = null)
        {
            var bestCookies = initialCookies ?? CaptureWeiboCookiesFromWebView();
            var bestSub = GetMobileWeiboSubValue(bestCookies);
            var stableSubSamples = 0;

            for (var attempt = 0; attempt < 8; attempt++)
            {
                var cookies = attempt == 0 && initialCookies != null
                    ? initialCookies
                    : CaptureWeiboCookiesFromWebView();
                var sub = GetMobileWeiboSubValue(cookies);
                if (!string.IsNullOrWhiteSpace(sub))
                {
                    if (string.Equals(sub, bestSub, StringComparison.Ordinal))
                    {
                        stableSubSamples++;
                    }
                    else
                    {
                        stableSubSamples = 1;
                        bestSub = sub;
                    }

                    bestCookies = cookies;
                    if (HasMobileWeiboSubpCookie(cookies) || stableSubSamples >= 3)
                    {
                        return cookies;
                    }
                }

                await Task.Delay(250);
            }

            if (HasMobileWeiboAuthenticationCookie(bestCookies))
            {
                return bestCookies;
            }

            throw new InvalidOperationException("微博移动站登录 Cookie 尚未完成同步，请稍后重试。");
        }

        private async Task RefreshAuthenticatedCookiesFromWebViewAsync()
        {
            var cookies = await WaitForMobileWeiboAuthenticationCookiesAsync();
            await _authStorage.SaveAsync(cookies);
            _apiService.ApplyStoredCookies(cookies);
            ApplyStoredCookiesToWebView(cookies);
            _isAuthenticated = _apiService.HasAuthenticatedSession;
            UpdateFollowCategoryVisibility();
        }

        private async Task<WeiboTimelinePage> GetFriendsTimelinePageWithFreshCookiesAsync(string cursor)
        {
            await RefreshAuthenticatedCookiesFromWebViewAsync();
            try
            {
                return await _apiService.GetFriendsTimelinePageAsync(cursor);
            }
            catch
            {
                await RefreshAuthenticatedCookiesFromWebViewAsync();
                return await _apiService.GetFriendsTimelinePageAsync(cursor);
            }
        }

        private static bool IsCookieApplicableToHost(string cookieDomain, string host)
        {
            if (string.IsNullOrWhiteSpace(cookieDomain) || string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            var normalizedDomain = cookieDomain.Trim().TrimStart('.');
            return string.Equals(host, normalizedDomain, StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith("." + normalizedDomain, StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyStoredCookiesToWebView(IEnumerable<WeiboStoredCookie> cookies)
        {
            var manager = new HttpBaseProtocolFilter().CookieManager;
            foreach (var storedCookie in cookies ?? Enumerable.Empty<WeiboStoredCookie>())
            {
                if (string.IsNullOrWhiteSpace(storedCookie.Name) ||
                    string.IsNullOrWhiteSpace(storedCookie.Value) ||
                    string.IsNullOrWhiteSpace(storedCookie.Domain))
                {
                    continue;
                }

                var domain = storedCookie.Domain.TrimStart('.');
                var cookie = new Windows.Web.Http.HttpCookie(
                    storedCookie.Name,
                    domain,
                    string.IsNullOrWhiteSpace(storedCookie.Path) ? "/" : storedCookie.Path)
                {
                    Value = storedCookie.Value
                };
                try
                {
                    manager.SetCookie(cookie, false);
                }
                catch
                {
                }
            }
        }

        private static void ClearWeiboCookiesFromWebView()
        {
            var manager = new HttpBaseProtocolFilter().CookieManager;
            var domains = new[]
            {
                new Uri("https://m.weibo.cn/"),
                new Uri("https://weibo.cn/"),
                new Uri("https://weibo.com/"),
                new Uri("https://passport.weibo.com/")
            };

            foreach (var uri in domains)
            {
                IReadOnlyList<Windows.Web.Http.HttpCookie> cookies;
                try
                {
                    cookies = manager.GetCookies(uri);
                }
                catch
                {
                    continue;
                }

                foreach (var cookie in cookies)
                {
                    try
                    {
                        manager.DeleteCookie(cookie);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private async Task LoadCategoriesAsync()
        {
            var preferredContainerId = string.Equals(_selectedContainerId, FollowContainerId, StringComparison.Ordinal)
                ? DefaultContainerId
                : _selectedContainerId;

            if (_isLoadingChannels)
            {
                return;
            }

            if (HasLoadedApiCategories())
            {
                SelectTimelineCategory(preferredContainerId);
                return;
            }

            try
            {
                _isLoadingChannels = true;

                var channels = await _apiService.GetChannelsAsync();
                var orderedChannels = channels
                    .Where(channel => ExpectedChannelNames.Contains(channel.Name))
                    .OrderBy(channel => Array.IndexOf(ExpectedChannelNames, channel.Name))
                    .ToList();

                foreach (var channel in orderedChannels)
                {
                    if (CategoryPivot.Items
                        .OfType<PivotItem>()
                        .Any(item => string.Equals(
                            (item.Tag as WeiboChannel)?.Gid,
                            channel.Gid,
                            StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    CategoryPivot.Items.Add(new PivotItem
                    {
                        Header = channel.Name,
                        Tag = channel
                    });
                }

                UpdateFollowCategoryVisibility();
                EnsureDefaultTimelineCategory();
                SelectTimelineCategory(preferredContainerId);
            }
            catch (Exception ex)
            {
                UpdateFollowCategoryVisibility();
                EnsureDefaultTimelineCategory();
                SelectTimelineCategory(preferredContainerId);
                await ShowErrorAsync($"分类加载失败: {ex.Message}");
            }
            finally
            {
                _isLoadingChannels = false;
            }
        }

        private async void CategoryPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingChannels || _isUpdatingFollowCategory)
            {
                return;
            }

            var selectedItem = CategoryPivot.SelectedItem as PivotItem;
            var selectedChannel = selectedItem?.Tag as WeiboChannel;
            if (selectedChannel == null || string.Equals(selectedChannel.Gid, _selectedContainerId, StringComparison.Ordinal))
            {
                return;
            }

            if (string.Equals(selectedChannel.Gid, FollowContainerId, StringComparison.Ordinal) &&
                (!_isAuthenticated || !_apiService.HasAuthenticatedSession))
            {
                try
                {
                    await RefreshAuthenticatedCookiesFromWebViewAsync();
                }
                catch
                {
                    _isAuthenticated = false;
                    UpdateFollowCategoryVisibility();
                    return;
                }
            }

            await Task.Yield();
            if (!ReferenceEquals(CategoryPivot.SelectedItem, selectedItem))
            {
                return;
            }

            _selectedContainerId = selectedChannel.Gid;
            ResetTimeline();
            ClearDetail();
            _timelineScrollViewer?.ChangeView(null, 0, null, true);
            var requestVersion = _timelineRequestVersion;
            if (IsSameCityChannel(selectedChannel))
            {
                await RefreshSameCityLocationAsync();
                if (requestVersion != _timelineRequestVersion)
                {
                    return;
                }
            }

            await LoadNextBatchAsync(true);
        }

        private async Task RefreshSameCityLocationAsync()
        {
            if (_sameCityLatitude.HasValue &&
                _sameCityLongitude.HasValue &&
                _sameCityLocationTimestamp.HasValue &&
                DateTimeOffset.UtcNow - _sameCityLocationTimestamp.Value < TimeSpan.FromMinutes(15))
            {
                return;
            }

            try
            {
                var access = await Geolocator.RequestAccessAsync();
                if (access != GeolocationAccessStatus.Allowed)
                {
                    return;
                }

                var geolocator = new Geolocator
                {
                    DesiredAccuracy = PositionAccuracy.Default
                };
                var position = await geolocator.GetGeopositionAsync(
                    TimeSpan.FromMinutes(30),
                    TimeSpan.FromSeconds(3));
                var coordinates = position?.Coordinate?.Point?.Position;
                if (coordinates != null)
                {
                    _sameCityLatitude = coordinates.Value.Latitude;
                    _sameCityLongitude = coordinates.Value.Longitude;
                    _sameCityLocationTimestamp = DateTimeOffset.UtcNow;
                }
            }
            catch
            {
            }
        }

        private static bool IsSameCityChannel(WeiboChannel channel)
        {
            return string.Equals(channel?.Name, "同城", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsSameCityContainerSelected()
        {
            var selectedItem = CategoryPivot.SelectedItem as PivotItem;
            return IsSameCityChannel(selectedItem?.Tag as WeiboChannel);
        }

        private async void WeiboListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as WeiboItemViewModel;
            if (item == null || string.IsNullOrWhiteSpace(item.Id))
            {
                return;
            }

            OpenDetailPage();
            await OpenDetailAsync(item);
        }

        private void OpenDetailPage()
        {
            if (string.Equals(_currentView, "search", StringComparison.OrdinalIgnoreCase) &&
                (IsMobile || IsNarrowOrPortrait()))
            {
                _detailParentView = _currentView;
                NavigateTo("searchDetail", true);
                return;
            }

            if (IsMobile || IsNarrowOrPortrait())
            {
                _detailParentView = _currentView;
                NavigateTo("detail", true);
                return;
            }

            if (string.Equals(_currentView, "search", StringComparison.OrdinalIgnoreCase))
            {
                ShowPanel("search");
                ApplySearchLayout();
                return;
            }

            ShowPanel("home");
            HomeListColumn.Width = WideListWidth;
            HomeDetailColumn.Width = new GridLength(1, GridUnitType.Star);
        }

        private static GridLength WideListWidth => IsMobile
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(430);

        private void ApplyHomeListLayout()
        {
            ApplyDetailScrollLayout();
            if (IsMobile || IsNarrowOrPortrait())
            {
                ApplyHomeStackSlideLayout(false);
                return;
            }

            ResetHomeStackSlideLayout();
            HomeListColumn.Width = WideListWidth;
            HomeDetailColumn.Width = new GridLength(1, GridUnitType.Star);
        }

        private void ApplyHomeDetailSlideLayout(bool showDetail)
        {
            ApplyDetailScrollLayout();
            if (IsMobile || IsNarrowOrPortrait())
            {
                ApplyHomeStackSlideLayout(showDetail);
                return;
            }

            ResetHomeStackSlideLayout();
            HomeListColumn.Width = new GridLength(0);
            HomeDetailColumn.Width = new GridLength(1, GridUnitType.Star);
        }

        private void ApplyHomeStackSlideLayout(bool showDetail)
        {
            var pageWidth = GetHomeStackPageWidth();
            var transform = EnsureHomePanelTransform();
            transform.ScaleX = 1;
            transform.ScaleY = 1;
            transform.TranslateY = 0;
            HomePanel.HorizontalAlignment = HorizontalAlignment.Left;
            HomePanel.Width = pageWidth * 2;
            HomeListColumn.Width = new GridLength(pageWidth);
            HomeDetailColumn.Width = new GridLength(pageWidth);

            var targetX = showDetail ? -pageWidth : 0;
            AnimateHomeStackTranslateX(targetX);
        }

        private void ResetHomeStackSlideLayout()
        {
            HomePanel.Width = double.NaN;
            HomePanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            var transform = EnsureHomePanelTransform();
            transform.ScaleX = 1;
            transform.ScaleY = 1;
            transform.TranslateY = 0;
            transform.TranslateX = 0;
        }

        private double GetHomeStackPageWidth()
        {
            return GetPageStackWidth();
        }

        private void AnimateHomeStackTranslateX(double targetX)
        {
            var transform = EnsureHomePanelTransform();
            if (Math.Abs(transform.TranslateX - targetX) < 0.1)
            {
                transform.TranslateX = targetX;
                return;
            }

            var animation = new DoubleAnimation
            {
                To = targetX,
                Duration = TimeSpan.FromMilliseconds(360),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };
            Storyboard.SetTarget(animation, HomePanel);
            Storyboard.SetTargetProperty(animation, SlideTranslateXPath);

            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            storyboard.Completed += (sender, args) => transform.TranslateX = targetX;
            storyboard.Begin();
        }

        private void SetHomeStackTranslateX(double targetX)
        {
            EnsureHomePanelTransform().TranslateX = targetX;
        }

        private CompositeTransform EnsureHomePanelTransform()
        {
            return EnsurePanelTransform(HomePanel);
        }

        private double GetPageStackWidth()
        {
            var width = MainContentHost.ActualWidth > 0
                ? MainContentHost.ActualWidth
                : Window.Current.Bounds.Width;
            return Math.Max(1, width);
        }

        private void AnimatePanelTranslateX(FrameworkElement panel, double targetX)
        {
            var transform = EnsurePanelTransform(panel);
            if (Math.Abs(transform.TranslateX - targetX) < 0.1)
            {
                transform.TranslateX = targetX;
                return;
            }

            var animation = new DoubleAnimation
            {
                To = targetX,
                Duration = TimeSpan.FromMilliseconds(360),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };
            Storyboard.SetTarget(animation, panel);
            Storyboard.SetTargetProperty(animation, SlideTranslateXPath);

            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            storyboard.Completed += (sender, args) => transform.TranslateX = targetX;
            storyboard.Begin();
        }

        private CompositeTransform EnsurePanelTransform(FrameworkElement panel)
        {
            var transform = panel.RenderTransform as CompositeTransform;
            if (transform == null)
            {
                transform = new CompositeTransform();
                panel.RenderTransform = transform;
            }

            return transform;
        }

        private void ApplySearchLayout()
        {
            if (IsMobile || IsNarrowOrPortrait())
            {
                ApplySearchStackSlideLayout(false);
                return;
            }

            ResetSearchStackSlideLayout();
            SearchListColumn.Width = WideListWidth;
            SearchDetailColumn.Width = new GridLength(1, GridUnitType.Star);
        }

        private void ApplySearchDetailSlideLayout(bool showDetail)
        {
            ApplyDetailScrollLayout();
            if (IsMobile || IsNarrowOrPortrait())
            {
                ApplySearchStackSlideLayout(showDetail);
                return;
            }

            ResetSearchStackSlideLayout();
            SearchListColumn.Width = new GridLength(0);
            SearchDetailColumn.Width = new GridLength(1, GridUnitType.Star);
        }

        private void ApplyDetailScrollLayout()
        {
            DetailScrollViewer.HorizontalAlignment = HorizontalAlignment.Stretch;
            DetailScrollViewer.MaxWidth = double.PositiveInfinity;
            DetailContent.MaxWidth = double.PositiveInfinity;
            SearchDetailScrollViewer.HorizontalAlignment = HorizontalAlignment.Stretch;
            SearchDetailScrollViewer.MaxWidth = double.PositiveInfinity;
            SearchDetailContent.MaxWidth = double.PositiveInfinity;
        }

        private void ApplySearchStackSlideLayout(bool showDetail)
        {
            var pageWidth = GetPageStackWidth();
            var transform = EnsurePanelTransform(SearchPanel);
            transform.ScaleX = 1;
            transform.ScaleY = 1;
            transform.TranslateY = 0;

            SearchPanel.HorizontalAlignment = HorizontalAlignment.Left;
            SearchPanel.Width = pageWidth * 2;
            SearchListColumn.Width = new GridLength(pageWidth);
            SearchDetailColumn.Width = new GridLength(pageWidth);

            AnimatePanelTranslateX(SearchPanel, showDetail ? -pageWidth : 0);
        }

        private void ResetSearchStackSlideLayout()
        {
            SearchPanel.Width = double.NaN;
            SearchPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            var transform = EnsurePanelTransform(SearchPanel);
            transform.ScaleX = 1;
            transform.ScaleY = 1;
            transform.TranslateY = 0;
            transform.TranslateX = 0;
        }

        private static bool IsNarrowOrPortrait()
        {
            var bounds = Window.Current.Bounds;
            return bounds.Width < 720 || bounds.Height > bounds.Width;
        }

        private void ConfigureTitleBar()
        {
            _coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            _coreTitleBar.ExtendViewIntoTitleBar = true;
            _coreTitleBar.LayoutMetricsChanged += CoreTitleBar_LayoutMetricsChanged;
            Window.Current.SetTitleBar(TitleBarDragRegion);

            var applicationView = ApplicationView.GetForCurrentView();
            applicationView.Title = string.Empty;
            var titleBar = applicationView.TitleBar;
            titleBar.BackgroundColor = Colors.Transparent;
            titleBar.InactiveBackgroundColor = Colors.Transparent;
            titleBar.ForegroundColor = Colors.Transparent;
            titleBar.InactiveForegroundColor = Colors.Transparent;
            ApplyTitleBarButtonTheme();
            UpdateTitleBarLayout();
        }

        private void ApplyTitleBarButtonTheme()
        {
            var titleBar = ApplicationView.GetForCurrentView().TitleBar;
            var buttonForeground = ThemeHelper.IsDarkTheme() ? Colors.White : Colors.Black;
            var hoverBackground = buttonForeground;
            hoverBackground.A = 35;
            var pressedBackground = buttonForeground;
            pressedBackground.A = 55;

            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = buttonForeground;
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(
                144,
                buttonForeground.R,
                buttonForeground.G,
                buttonForeground.B);
            titleBar.ButtonHoverForegroundColor = buttonForeground;
            titleBar.ButtonPressedForegroundColor = buttonForeground;
            titleBar.ButtonHoverBackgroundColor = hoverBackground;
            titleBar.ButtonPressedBackgroundColor = pressedBackground;
        }

        private void MainPage_ActualThemeChanged(FrameworkElement sender, object args)
        {
            if (!IsMobile)
            {
                ApplyTitleBarButtonTheme();
            }
            ApplySidebarBackdrop();
            _ = ApplyMessageWebViewThemeAsync();
        }

        private void CoreTitleBar_LayoutMetricsChanged(CoreApplicationViewTitleBar sender, object args)
        {
            UpdateTitleBarLayout();
        }

        private void ApplyShellLayout()
        {
            ApplySplitViewMode();
            ApplyDetailPadding();
            ApplyDetailIconSet(IsMobile);
            if (IsMobile)
            {
                var bounds = Window.Current.Bounds;
                RootSplitView.CompactPaneLength = 0;
                RootSplitView.OpenPaneLength = Math.Max(240, Math.Min(320, bounds.Width * 0.82));
                MainContentHost.Margin = new Thickness(0);
                CategoryPivot.Padding = new Thickness(52, 0, 4, 0);
                SearchTitle.Margin = new Thickness(0, 24, 24, 0);
                LoginHeader.Padding = new Thickness(0, 0, 16, 0);
                TitleBarDragRegion.Visibility = Visibility.Collapsed;
                SearchBackButton.Visibility = Visibility.Visible;
                LoginBackButton.Visibility = Visibility.Visible;
                DetailBackButton.Visibility = Visibility.Visible;
                SearchDetailBackButton.Visibility = Visibility.Visible;
                ApplyMobileDetailTransitions();

                DetailScrollViewer.HorizontalAlignment = HorizontalAlignment.Stretch;
                DetailScrollViewer.MaxWidth = double.PositiveInfinity;
                DetailContent.MaxWidth = double.PositiveInfinity;
                SearchDetailScrollViewer.HorizontalAlignment = HorizontalAlignment.Stretch;
                SearchDetailScrollViewer.MaxWidth = double.PositiveInfinity;
                SearchDetailContent.MaxWidth = double.PositiveInfinity;
                return;
            }

            RootSplitView.CompactPaneLength = 48;
            RootSplitView.OpenPaneLength = 320;
            CategoryPivot.Padding = new Thickness(4, 0, 4, 0);
            SearchTitle.Margin = new Thickness(24, 24, 24, 0);
            LoginHeader.Padding = new Thickness(16, 0, 16, 0);
            SearchBackButton.Visibility = Visibility.Collapsed;
            LoginBackButton.Visibility = Visibility.Collapsed;
            DetailBackButton.Visibility = Visibility.Collapsed;
            SearchDetailBackButton.Visibility = Visibility.Collapsed;

            DetailScrollViewer.HorizontalAlignment = HorizontalAlignment.Stretch;
            DetailScrollViewer.MaxWidth = double.PositiveInfinity;
            DetailContent.MaxWidth = double.PositiveInfinity;
            SearchDetailScrollViewer.HorizontalAlignment = HorizontalAlignment.Stretch;
            SearchDetailScrollViewer.MaxWidth = double.PositiveInfinity;
            SearchDetailContent.MaxWidth = double.PositiveInfinity;
            UpdateTitleBarLayout();
        }

        private void ApplyDetailIconSet(bool mobile)
        {
            var fluent = mobile ? Visibility.Collapsed : Visibility.Visible;
            var mdl2 = mobile ? Visibility.Visible : Visibility.Collapsed;

            DetailRepostFluentIcon.Visibility = fluent;
            DetailRepostMdl2Icon.Visibility = mdl2;
            DetailCommentStatFluentIcon.Visibility = fluent;
            DetailCommentStatMdl2Icon.Visibility = mdl2;
            DetailCommentFluentIcon.Visibility = fluent;
            DetailCommentMdl2Icon.Visibility = mdl2;
            DetailTopFluentIcon.Visibility = fluent;
            DetailTopMdl2Icon.Visibility = mdl2;

            SearchDetailRepostFluentIcon.Visibility = fluent;
            SearchDetailRepostMdl2Icon.Visibility = mdl2;
            SearchDetailCommentStatFluentIcon.Visibility = fluent;
            SearchDetailCommentStatMdl2Icon.Visibility = mdl2;
            SearchDetailCommentFluentIcon.Visibility = fluent;
            SearchDetailCommentMdl2Icon.Visibility = mdl2;
            SearchDetailTopFluentIcon.Visibility = fluent;
            SearchDetailTopMdl2Icon.Visibility = mdl2;
        }

        private void ApplyDetailPadding()
        {
            var bounds = Window.Current.Bounds;
            double sidePadding;
            if (bounds.Width < 480)
            {
                sidePadding = 12;
            }
            else if (bounds.Width < 720)
            {
                sidePadding = 16;
            }
            else
            {
                sidePadding = 24;
            }

            var detailTop = 16d;
            var detailBottom = 56d;
            var profileBottom = 24d;

            DetailScrollViewer.Padding = new Thickness(sidePadding, detailTop, sidePadding, detailBottom);
            SearchDetailScrollViewer.Padding = new Thickness(sidePadding, detailTop, sidePadding, detailBottom);
            UserProfileScrollViewer.Padding = new Thickness(sidePadding, detailTop, sidePadding, profileBottom);
        }

        private void ApplyMobileDetailTransitions()
        {
            // UWP 手机风格过渡动画：从右侧滑入
            if (DetailScrollViewer.Transitions == null || DetailScrollViewer.Transitions.Count == 0)
            {
                DetailScrollViewer.Transitions = new TransitionCollection
                {
                    new EntranceThemeTransition { FromHorizontalOffset = 64 }
                };
            }

            if (SearchDetailScrollViewer.Transitions == null || SearchDetailScrollViewer.Transitions.Count == 0)
            {
                SearchDetailScrollViewer.Transitions = new TransitionCollection
                {
                    new EntranceThemeTransition { FromHorizontalOffset = 64 }
                };
            }
        }

        private void UpdateTitleBarLayout(double? paneWidth = null)
        {
            if (IsMobile || _coreTitleBar == null)
            {
                return;
            }

            var titleBarHeight = Math.Max(32d, _coreTitleBar.Height);
            var leftInset = paneWidth ??
                            (RootSplitView.IsPaneOpen
                                ? RootSplitView.OpenPaneLength
                                : RootSplitView.CompactPaneLength);
            var rightInset = Math.Max(0d, _coreTitleBar.SystemOverlayRightInset);

            MainContentHost.Margin = new Thickness(0, titleBarHeight, 0, 0);
            TitleBarDragRegion.Height = titleBarHeight;
            TitleBarDragRegion.Margin = new Thickness(leftInset, 0, rightInset, 0);
        }

        internal void ApplySidebarBackdrop()
        {
            var color = ThemeHelper.IsDarkTheme()
                ? Color.FromArgb(255, 32, 32, 32)
                : Color.FromArgb(255, 247, 247, 247);
            RootSplitView.PaneBackground = new SolidColorBrush(color);
        }

        internal void UpdateSidebarOnThemeChange()
        {
            ApplySidebarBackdrop();
            _ = ApplyMessageWebViewThemeAsync();
        }

        private void ApplySplitViewMode()
        {
            RootSplitView.DisplayMode = IsMobile
                ? SplitViewDisplayMode.Overlay
                : SplitViewDisplayMode.CompactOverlay;
        }

        private void CloseMobilePane()
        {
            if (!IsMobile)
            {
                return;
            }

            RootSplitView.IsPaneOpen = false;
            HamburgerButton.IsChecked = false;
        }

        private void AttachScrollViewer()
        {
            if (_timelineScrollViewer != null)
            {
                return;
            }

            WeiboListView.ApplyTemplate();
            _timelineScrollViewer = FindDescendant<ScrollViewer>(WeiboListView);
            if (_timelineScrollViewer != null)
            {
                _timelineScrollViewer.ViewChanged += TimelineScrollViewer_ViewChanged;
            }
        }

        private void AttachSearchScrollViewer()
        {
            if (_searchScrollViewer != null)
            {
                return;
            }

            SearchResultsList.ApplyTemplate();
            _searchScrollViewer = FindDescendant<ScrollViewer>(SearchResultsList);
            if (_searchScrollViewer != null)
            {
                _searchScrollViewer.ViewChanged += SearchScrollViewer_ViewChanged;
            }
        }

        private async void TimelineScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_isResettingTimeline)
            {
                return;
            }

            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null || scrollViewer.ScrollableHeight <= 0)
            {
                return;
            }

            if (scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 120)
            {
                await LoadNextBatchAsync(false);
            }
        }

        private async void SearchScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null || scrollViewer.ScrollableHeight <= 0)
            {
                return;
            }

            if (scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 120)
            {
                await LoadMoreSearchResultsAsync(false);
            }
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!IsMobile)
            {
                ActualThemeChanged -= MainPage_ActualThemeChanged;
            }
            if (_loginWebView != null)
            {
                _loginWebView.NavigationCompleted -= LoginWebView_NavigationCompleted;
            }
            if (_messageWebView != null)
            {
                _messageWebView.NavigationCompleted -= MessageWebView_NavigationCompleted;
            }
            DataTransferManager.GetForCurrentView().DataRequested -= OnShareDataRequested;
            if (!IsMobile && _coreTitleBar != null)
            {
                _coreTitleBar.LayoutMetricsChanged -= CoreTitleBar_LayoutMetricsChanged;
            }

            if (_timelineScrollViewer != null)
            {
                _timelineScrollViewer.ViewChanged -= TimelineScrollViewer_ViewChanged;
                _timelineScrollViewer = null;
            }

            if (_searchScrollViewer != null)
            {
                _searchScrollViewer.ViewChanged -= SearchScrollViewer_ViewChanged;
                _searchScrollViewer = null;
            }

            if (_userTimelineScrollViewer != null)
            {
                _userTimelineScrollViewer.ViewChanged -= UserTimelineScrollViewer_ViewChanged;
                _userTimelineScrollViewer = null;
            }

            DetailScrollViewer.ViewChanged -= DetailScrollViewer_ViewChanged;
            SearchDetailScrollViewer.ViewChanged -= DetailScrollViewer_ViewChanged;
            UserProfileScrollViewer.ViewChanged -= UserTimelineScrollViewer_ViewChanged;
            CloseMediaPreview(true);
            ResetTimeline();
            ClearDetail();
        }

        private async Task LoadNextBatchAsync(bool isInitialLoad)
        {
            var requestVersion = _timelineRequestVersion;
            if (_timelineLoadingVersion == requestVersion || !_hasMore)
            {
                return;
            }

            _timelineLoadingVersion = requestVersion;
            var selectedChannel = (CategoryPivot.SelectedItem as PivotItem)?.Tag as WeiboChannel;
            if (!string.IsNullOrWhiteSpace(selectedChannel?.Gid))
            {
                _selectedContainerId = selectedChannel.Gid;
            }

            var containerId = string.IsNullOrWhiteSpace(_selectedContainerId)
                ? DefaultContainerId
                : _selectedContainerId;
            var isFollowingTimeline = string.Equals(containerId, FollowContainerId, StringComparison.Ordinal);
            if (isFollowingTimeline && (!_isAuthenticated || !_apiService.HasAuthenticatedSession))
            {
                try
                {
                    await RefreshAuthenticatedCookiesFromWebViewAsync();
                }
                catch
                {
                    _isAuthenticated = false;
                    UpdateFollowCategoryVisibility();
                    if (requestVersion == _timelineRequestVersion)
                    {
                        SetLoadingState(isInitialLoad, false);
                    }
                    if (_timelineLoadingVersion == requestVersion)
                    {
                        _timelineLoadingVersion = -1;
                    }
                    return;
                }
            }
            var isSameCityTimeline = IsSameCityChannel(selectedChannel);
            var latitude = isSameCityTimeline ? _sameCityLatitude : null;
            var longitude = isSameCityTimeline ? _sameCityLongitude : null;
            try
            {
                SetLoadingState(isInitialLoad, true);

                var nextItems = new List<WeiboItemViewModel>();
                while (nextItems.Count < BatchSize && _hasMore)
                {
                    if (_pendingMblogs.Count == 0)
                    {
                        WeiboTimelinePage page;
                        if (isFollowingTimeline)
                        {
                            page = await GetFriendsTimelinePageWithFreshCookiesAsync(_nextCursor);
                            if (requestVersion != _timelineRequestVersion)
                            {
                                return;
                            }
                            _nextCursor = page.NextCursor;
                            _hasMore = page.Mblogs.Count > 0 &&
                                       !string.IsNullOrWhiteSpace(_nextCursor) &&
                                       _nextCursor != "0";
                        }
                        else
                        {
                            page = await _apiService.GetTimelinePageAsync(
                                containerId,
                                _nextSinceId,
                                latitude,
                                longitude);
                            if (requestVersion != _timelineRequestVersion)
                            {
                                return;
                            }
                            _nextSinceId = page.NextSinceId;
                            _hasMore = page.Mblogs.Count > 0;
                        }

                        foreach (var mblog in page.Mblogs)
                        {
                            if (!string.IsNullOrEmpty(mblog.Id) && _loadedMblogIds.Add(mblog.Id))
                            {
                                _pendingMblogs.Enqueue(mblog);
                            }
                        }

                        if (_pendingMblogs.Count == 0)
                        {
                            _hasMore = false;
                            break;
                        }
                    }

                    nextItems.Add(new WeiboItemViewModel(_pendingMblogs.Dequeue()));
                }

                foreach (var item in nextItems)
                {
                    if (requestVersion != _timelineRequestVersion)
                    {
                        item.ReleaseImages();
                        return;
                    }

                    _weiboItems.Add(item);
                }

                await Task.WhenAll(nextItems.Select(item => item.LoadImagesAsync(_imageLoader)));
                if (requestVersion != _timelineRequestVersion)
                {
                    foreach (var item in nextItems)
                    {
                        item.ReleaseImages();
                    }
                    return;
                }

                if (isInitialLoad && _weiboItems.Count == 0)
                {
                    await ShowErrorAsync("暂无微博数据");
                }

                if (isInitialLoad)
                {
                    _ = RefreshLiveTileAsync();
                }
            }
            catch (Exception ex)
            {
                if (isFollowingTimeline && !_apiService.HasAuthenticatedSession)
                {
                    _isAuthenticated = false;
                    UpdateFollowCategoryVisibility();
                }

                if (requestVersion == _timelineRequestVersion)
                {
                    await ShowErrorAsync($"加载失败: {ex.Message}");
                }
            }
            finally
            {
                if (requestVersion == _timelineRequestVersion)
                {
                    SetLoadingState(isInitialLoad, false);
                }

                if (_timelineLoadingVersion == requestVersion)
                {
                    _timelineLoadingVersion = -1;
                }
            }
        }

        internal async Task RefreshLiveTileAsync()
        {
            try
            {
                _liveTileService.EnsureRecommendedWhenUnauthenticated(_isAuthenticated);
                var source = _liveTileService.GetFeedSource();
                if (source == LiveTileFeedSource.Following && _isAuthenticated)
                {
                    var page = await GetFriendsTimelinePageWithFreshCookiesAsync(null);
                    var followItems = page.Mblogs
                        .Where(mblog => mblog != null)
                        .Select(mblog => new WeiboItemViewModel(mblog))
                        .ToList();
                    _liveTileService.UpdateTiles(followItems);
                    return;
                }

                if (_weiboItems.Count > 0)
                {
                    _liveTileService.UpdateTiles(_weiboItems);
                    return;
                }

                var recommendedPage = await _apiService.GetTimelinePageAsync(DefaultContainerId);
                var recommendedItems = recommendedPage.Mblogs
                    .Where(mblog => mblog != null)
                    .Select(mblog => new WeiboItemViewModel(mblog))
                    .ToList();
                _liveTileService.UpdateTiles(recommendedItems);
            }
            catch
            {
            }
        }

        internal bool IsAuthenticatedForSettings => _isAuthenticated && _apiService.HasAuthenticatedSession;

        private void SetLoadingState(bool isInitialLoad, bool isLoading)
        {
            if (isInitialLoad)
            {
                LoadingIndicator.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
                return;
            }

            LoadingMoreIndicator.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ResetTimeline()
        {
            _timelineRequestVersion++;
            _isResettingTimeline = true;
            try
            {
                foreach (var item in _weiboItems)
                {
                    item.ReleaseImages();
                }

                _weiboItems.Clear();
                _pendingMblogs.Clear();
                _loadedMblogIds.Clear();
                _hasMore = true;
                _nextSinceId = 0;
                _nextCursor = null;
            }
            finally
            {
                _isResettingTimeline = false;
            }
        }

        private async Task OpenDetailAsync(WeiboItemViewModel item)
        {
            var requestVersion = ++_detailRequestVersion;
            SetDetailLoadingState(true);

            try
            {
                var detailMblog = await _apiService.GetMblogDetailAsync(item.Id);
                if (requestVersion != _detailRequestVersion)
                {
                    return;
                }

                var detailItem = new WeiboItemViewModel(detailMblog);
                DetailContent.DataContext = detailItem;
                SearchDetailContent.DataContext = detailItem;
                DetailScrollViewer.Visibility = Visibility.Visible;
                SearchDetailScrollViewer.Visibility = Visibility.Visible;
                DetailBottomBar.Visibility = Visibility.Visible;
                SearchDetailBottomBar.Visibility = Visibility.Visible;
                DetailPlaceholder.Visibility = Visibility.Collapsed;
                SearchDetailPlaceholder.Visibility = Visibility.Collapsed;
                DetailScrollViewer.ChangeView(null, 0, null, true);
                SearchDetailScrollViewer.ChangeView(null, 0, null, true);

                await detailItem.LoadImagesAsync(_imageLoader);
                if (requestVersion != _detailRequestVersion)
                {
                    detailItem.ReleaseImages();
                    return;
                }

                _detailItem?.ReleaseImages();
                _detailItem = detailItem;
                await LoadDetailCommentsAsync(item.Id, requestVersion);
            }
            catch (Exception ex)
            {
                if (requestVersion == _detailRequestVersion)
                {
                    await ShowErrorAsync($"详情加载失败: {ex.Message}");
                    if (_detailItem == null)
                    {
                        DetailPlaceholder.Visibility = Visibility.Visible;
                        DetailScrollViewer.Visibility = Visibility.Collapsed;
                    }
                }
            }
            finally
            {
                if (requestVersion == _detailRequestVersion)
                {
                    SetDetailLoadingState(false);
                }
            }
        }

        private async Task LoadDetailCommentsAsync(string id, int requestVersion)
        {
            _currentDetailId = id;
            _nextCommentMaxId = null;
            _hasMoreComments = true;
            _detailComments.Clear();
            await LoadMoreCommentsAsync(requestVersion, true);
        }

        private async Task LoadMoreCommentsAsync(int requestVersion, bool isInitialLoad)
        {
            if (_isLoadingMoreComments ||
                !_hasMoreComments ||
                string.IsNullOrWhiteSpace(_currentDetailId) ||
                requestVersion != _detailRequestVersion)
            {
                return;
            }

            try
            {
                _isLoadingMoreComments = true;
                SetCommentsLoadingState(!isInitialLoad);
                var page = await _apiService.GetCommentsPageAsync(_currentDetailId, _nextCommentMaxId);
                if (requestVersion != _detailRequestVersion)
                {
                    return;
                }

                _nextCommentMaxId = page.NextMaxId;
                _hasMoreComments = page.Comments.Count > 0 &&
                                   !string.IsNullOrWhiteSpace(_nextCommentMaxId) &&
                                   _nextCommentMaxId != "0";

                var commentItems = page.Comments.Select(comment => new WeiboCommentViewModel(comment)).ToList();
                foreach (var commentItem in commentItems)
                {
                    _detailComments.Add(commentItem);
                }

                await Task.WhenAll(commentItems.Select(comment => comment.LoadImagesAsync(_imageLoader)));
            }
            catch
            {
                _hasMoreComments = false;
                if (isInitialLoad)
                {
                    _detailComments.Clear();
                }
            }
            finally
            {
                _isLoadingMoreComments = false;
                SetCommentsLoadingState(false);
            }
        }

        private async void DetailScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null || scrollViewer.ScrollableHeight <= 0)
            {
                return;
            }

            if (scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 120)
            {
                await LoadMoreCommentsAsync(_detailRequestVersion, false);
            }
        }

        private async Task LoadHotSearchAsync()
        {
            if (_isLoadingHotSearch || _hotSearchItems.Count > 0)
            {
                return;
            }

            try
            {
                _isLoadingHotSearch = true;
                await RestoreAuthCookiesAsync();
                var hotItems = await _apiService.GetHotSearchAsync();
                var rank = 1;
                foreach (var hotItem in hotItems)
                {
                    _hotSearchItems.Add(new WeiboHotSearchViewModel(hotItem, rank++));
                }
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"热搜加载失败: {ex.Message}");
            }
            finally
            {
                _isLoadingHotSearch = false;
            }
        }

        private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            await ExecuteSearchAsync(args.QueryText);
        }

        private async void HotSearchList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as WeiboHotSearchViewModel;
            if (item == null || string.IsNullOrWhiteSpace(item.Title))
            {
                return;
            }

            SearchBox.Text = item.Title;
            await ExecuteSearchAsync(item.Title);
        }

        private async Task ExecuteSearchAsync(string queryText)
        {
            var keyword = queryText?.Trim();
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            foreach (var item in _searchResults)
            {
                item.ReleaseImages();
            }

            _currentSearchKeyword = keyword;
            _nextSearchSinceId = 0;
            _hasMoreSearch = true;
            _searchResults.Clear();
            _searchLoadedMblogIds.Clear();
            ClearDetail();
            SearchResultsList.Visibility = Visibility.Visible;
            HotSearchList.Visibility = Visibility.Collapsed;
            AttachSearchScrollViewer();
            _searchScrollViewer?.ChangeView(null, 0, null, true);

            await LoadMoreSearchResultsAsync(true);
        }

        private void SearchResultsList_Loaded(object sender, RoutedEventArgs e)
        {
            AttachSearchScrollViewer();
        }

        private async Task LoadMoreSearchResultsAsync(bool isInitialLoad)
        {
            if (_isLoadingMoreSearch || !_hasMoreSearch || string.IsNullOrWhiteSpace(_currentSearchKeyword))
            {
                return;
            }

            try
            {
                _isLoadingMoreSearch = true;
                SetSearchLoadingState(!isInitialLoad);
                await RestoreAuthCookiesAsync();

                var newItems = new List<WeiboItemViewModel>();
                while (newItems.Count < BatchSize && _hasMoreSearch)
                {
                    var page = await _apiService.SearchPageAsync(_currentSearchKeyword, _nextSearchSinceId);
                    _nextSearchSinceId = page.NextSinceId;
                    _hasMoreSearch = page.Mblogs.Count > 0 && _nextSearchSinceId > 0;

                    foreach (var mblog in page.Mblogs)
                    {
                        if (!string.IsNullOrEmpty(mblog.Id) && _searchLoadedMblogIds.Add(mblog.Id))
                        {
                            newItems.Add(new WeiboItemViewModel(mblog));
                        }
                    }

                    if (!_hasMoreSearch)
                    {
                        break;
                    }
                }

                foreach (var item in newItems)
                {
                    _searchResults.Add(item);
                }

                if (newItems.Count > 0)
                {
                    await Task.WhenAll(newItems.Select(item => item.LoadImagesAsync(_imageLoader)));
                }
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"搜索失败: {ex.Message}");
                _hasMoreSearch = false;
            }
            finally
            {
                _isLoadingMoreSearch = false;
                SetSearchLoadingState(false);
            }
        }

        private async void SearchResultsList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemIndex >= _searchResults.Count - 2)
            {
                await LoadMoreSearchResultsAsync(false);
            }
        }

        private async void SearchResultsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as WeiboItemViewModel;
            if (item == null || string.IsNullOrWhiteSpace(item.Id))
            {
                return;
            }

            OpenDetailPage();
            await OpenDetailAsync(item);
        }

        private void ClearDetail()
        {
            _detailRequestVersion++;
            _detailItem?.ReleaseImages();
            _detailItem = null;
            _detailComments.Clear();
            _currentDetailId = null;
            _nextCommentMaxId = null;
            _hasMoreComments = false;
            _isLoadingMoreComments = false;
            DetailContent.DataContext = null;
            SearchDetailContent.DataContext = null;
            DetailScrollViewer.Visibility = Visibility.Collapsed;
            SearchDetailScrollViewer.Visibility = Visibility.Collapsed;
            DetailBottomBar.Visibility = Visibility.Collapsed;
            SearchDetailBottomBar.Visibility = Visibility.Collapsed;
            UserProfilePanel.Visibility = Visibility.Collapsed;
            EnsurePanelTransform(UserProfilePanel).TranslateX = 0;
            UserProfileScrollViewer.Visibility = Visibility.Collapsed;
            UserProfileLoadingIndicator.Visibility = Visibility.Collapsed;
            _userProfile?.ReleaseImages();
            _userProfile = null;
            UserProfilePanel.DataContext = null;
            _userTimelineItems.Clear();
            _currentUserProfileUid = 0;
            DetailPlaceholder.Visibility = Visibility.Visible;
            SearchDetailPlaceholder.Visibility = Visibility.Visible;
            SetDetailLoadingState(false);
            SetCommentsLoadingState(false);
        }

        private void SetDetailLoadingState(bool isLoading)
        {
            DetailLoadingIndicator.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            SearchDetailLoadingIndicator.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            if (isLoading)
            {
                DetailPlaceholder.Visibility = Visibility.Collapsed;
                SearchDetailPlaceholder.Visibility = Visibility.Collapsed;
            }
        }

        private void SetCommentsLoadingState(bool isLoading)
        {
            DetailCommentsLoadingIndicator.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            SearchDetailCommentsLoadingIndicator.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetSearchLoadingState(bool isLoading)
        {
            SearchLoadingMoreIndicator.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DetailScrollTopButton_Click(object sender, RoutedEventArgs e)
        {
            var scrollViewer = SearchDetailScrollViewer.Visibility == Visibility.Visible &&
                               string.Equals(_currentView, "search", StringComparison.OrdinalIgnoreCase)
                ? SearchDetailScrollViewer
                : DetailScrollViewer;
            scrollViewer.ChangeView(null, 0, null, false);
        }

        private async void DetailFollowButton_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as WeiboItemViewModel ?? _detailItem;
            if (item?.User == null || item.User.Id <= 0)
            {
                await ShowErrorAsync("无法获取用户信息。");
                return;
            }

            var nextState = !item.IsFollowing;
            try
            {
                await _apiService.SetFollowAsync(item.User.Id, nextState);
                item.ApplyFollowing(nextState);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"关注操作失败: {ex.Message}");
            }
        }

        private async void DetailLikeButton_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as WeiboItemViewModel ?? _detailItem;
            if (item == null || string.IsNullOrWhiteSpace(item.Id))
            {
                await ShowErrorAsync("无法获取微博信息。");
                return;
            }

            var nextState = !item.IsLiked;
            try
            {
                await _apiService.SetLikeAsync(item.Id, nextState);
                item.ApplyLiked(nextState);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"点赞操作失败: {ex.Message}");
            }
        }

        private async void DetailFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as WeiboItemViewModel ?? _detailItem;
            if (item == null || string.IsNullOrWhiteSpace(item.Id))
            {
                await ShowErrorAsync("无法获取微博信息。");
                return;
            }

            var favorited = !item.IsFavorited;
            try
            {
                await _apiService.SetFavoriteAsync(item.Id, favorited);
                item.ApplyFavorited(favorited);
                await ShowErrorAsync(favorited ? "收藏成功。" : "已取消收藏。");
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"{(favorited ? "收藏" : "取消收藏")}失败: {ex.Message}");
            }
        }

        private void DetailBackButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateBack();
        }

        private async void DetailUserHeader_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as WeiboItemViewModel ?? _detailItem;
            if (item?.User == null || item.User.Id <= 0)
            {
                return;
            }

            e.Handled = true;
            await OpenUserProfileAsync(item.User.Id);
        }

        private async Task OpenUserProfileAsync(long uid, bool isAuthenticatedUserProfile = false)
        {
            _isViewingAuthenticatedUserProfile = isAuthenticatedUserProfile ||
                                                 (_currentAuthenticatedUserUid > 0 && uid == _currentAuthenticatedUserUid);
            _currentUserProfileUid = uid;
            _nextUserTimelineSinceId = 0;
            _hasMoreUserTimeline = true;
            _userTimelineItems.Clear();
            UserProfilePanel.Visibility = Visibility.Visible;
            UserProfileLoadingIndicator.Visibility = Visibility.Visible;
            UserProfileScrollViewer.Visibility = Visibility.Collapsed;
            DetailScrollViewer.Visibility = Visibility.Collapsed;
            DetailBottomBar.Visibility = Visibility.Collapsed;
            DetailPlaceholder.Visibility = Visibility.Collapsed;

            if (IsMobile)
            {
                _detailParentView = _currentView;
                NavigateTo("profile", true);
            }
            else
            {
                // Desktop keeps the profile as an overlay on the detail surface, so it
                // needs the same horizontal entrance when it is opened directly.
                EnsurePanelTransform(UserProfilePanel).TranslateX = GetPageStackWidth();
                AnimatePanelTranslateX(UserProfilePanel, 0);
            }

            try
            {
                var profile = await _apiService.GetUserProfileAsync(uid);
                if (_currentUserProfileUid != uid)
                {
                    return;
                }

                _userProfile?.ReleaseImages();
                _userProfile = new WeiboUserProfileViewModel(profile.User);
                if (_isViewingAuthenticatedUserProfile)
                {
                    _userProfile.MarkAsAuthenticatedUser();
                }
                UserProfilePanel.DataContext = _userProfile;
                await _userProfile.LoadImagesAsync(_imageLoader);
                UserProfileScrollViewer.Visibility = Visibility.Visible;
                await LoadMoreUserTimelineAsync(false);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"用户中心加载失败: {ex.Message}");
                UserProfilePanel.Visibility = Visibility.Collapsed;
                if (_detailItem != null)
                {
                    DetailScrollViewer.Visibility = Visibility.Visible;
                    DetailBottomBar.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                UserProfileLoadingIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private void SearchDetailAvatar_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var item = SearchDetailContent.DataContext as WeiboItemViewModel;
            if (item?.User?.Id > 0)
            {
                _ = OpenUserProfileAsync(item.User.Id);
            }
        }

        private void CommentAvatar_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var comment = (sender as FrameworkElement)?.DataContext as WeiboCommentViewModel;
            if (comment == null || comment.UserId <= 0)
            {
                return;
            }

            e.Handled = true;
            _ = OpenUserProfileAsync(comment.UserId);
        }

        private void UserProfileBackButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsMobile)
            {
                NavigateBack();
                return;
            }

            UserProfilePanel.Visibility = Visibility.Collapsed;
            EnsurePanelTransform(UserProfilePanel).TranslateX = 0;
            if (_detailItem != null)
            {
                DetailScrollViewer.Visibility = Visibility.Visible;
                DetailBottomBar.Visibility = Visibility.Visible;
            }
            else
            {
                DetailPlaceholder.Visibility = Visibility.Visible;
            }
        }

        private async void UserProfileFollowButton_Click(object sender, RoutedEventArgs e)
        {
            if (_userProfile == null || _userProfile.UserId <= 0)
            {
                return;
            }

            if (_isViewingAuthenticatedUserProfile)
            {
                await LogoutAsync();
                return;
            }

            var nextState = !_userProfile.IsFollowing;
            try
            {
                await _apiService.SetFollowAsync(_userProfile.UserId, nextState);
                _userProfile.ApplyFollowing(nextState);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"关注操作失败: {ex.Message}");
            }
        }

        private async Task LogoutAsync()
        {
            await _authStorage.ClearAsync();
            ClearWeiboCookiesFromWebView();
            _apiService.ClearAuthentication();
            _isAuthenticated = false;
            _currentAuthenticatedUserUid = 0;
            _isViewingAuthenticatedUserProfile = false;
            _hasSavedLoginThisSession = false;
            _authRestored = false;
            UpdateFollowCategoryVisibility();
            UserProfilePanel.Visibility = Visibility.Collapsed;
            SelectNavigationItem("home");
            NavigateTo("home", false);
            await ShowErrorAsync("已退出登录。");
        }

        private async Task LoadMoreUserTimelineAsync(bool resetScroll)
        {
            if (_isLoadingUserTimeline || !_hasMoreUserTimeline || _currentUserProfileUid <= 0)
            {
                return;
            }

            try
            {
                _isLoadingUserTimeline = true;
                var page = await _apiService.GetUserTimelinePageAsync(_currentUserProfileUid, _nextUserTimelineSinceId);
                _nextUserTimelineSinceId = page.NextSinceId;
                _hasMoreUserTimeline = page.Mblogs.Count > 0 && _nextUserTimelineSinceId > 0;
                var items = page.Mblogs.Select(mblog => new WeiboItemViewModel(mblog)).ToList();
                foreach (var item in items)
                {
                    _userTimelineItems.Add(item);
                }

                await Task.WhenAll(items.Select(item => item.LoadImagesAsync(_imageLoader)));
                if (resetScroll)
                {
                    UserProfileScrollViewer.ChangeView(null, 0, null, true);
                }
            }
            finally
            {
                _isLoadingUserTimeline = false;
            }
        }

        private void UserTimelineList_Loaded(object sender, RoutedEventArgs e)
        {
            if (_userTimelineScrollViewer != null)
            {
                return;
            }

            UserTimelineList.ApplyTemplate();
            _userTimelineScrollViewer = FindDescendant<ScrollViewer>(UserTimelineList);
            if (_userTimelineScrollViewer != null)
            {
                _userTimelineScrollViewer.ViewChanged += UserTimelineScrollViewer_ViewChanged;
            }
        }

        private async void UserTimelineScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer != null && scrollViewer.ScrollableHeight > 0 &&
                scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 120)
            {
                await LoadMoreUserTimelineAsync(false);
            }
        }

        private async void UserTimelineList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemIndex >= _userTimelineItems.Count - 2)
            {
                await LoadMoreUserTimelineAsync(false);
            }
        }

        private async void UserTimelineList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as WeiboItemViewModel;
            if (item == null)
            {
                return;
            }

            UserProfilePanel.Visibility = Visibility.Collapsed;
            OpenDetailPage();
            await OpenDetailAsync(item);
        }

        private async Task LoadMessageCenterAsync()
        {
            await RestoreAuthCookiesAsync();

            if (_isAuthenticated)
            {
                try
                {
                    await RefreshAuthenticatedCookiesFromWebViewAsync();
                }
                catch
                {
                }
            }

            var webView = EnsureMessageWebView();
            var source = webView.Source;
            if (source == null)
            {
                OpenMessageWeb("私信", "https://m.weibo.cn/msg/chat");
                return;
            }

            webView.Navigate(source);
        }

        private void MessageCenterList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as WeiboMessageCenterItemViewModel;
            if (item == null)
            {
                return;
            }

            OpenMessageWeb(item.Title, item.TargetUrl);
        }

        private void OpenMessageWeb(string title, string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                uri = new Uri("https://m.weibo.cn/msg/chat");
            }

            EnsureMessageWebView().Navigate(uri);
        }

        private void MessageBackButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateBack();
        }

        private void SearchBackButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateBack();
        }

        private void LoginBackButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateBack();
        }

        private async void DetailCommentButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentDetailId))
            {
                await ShowErrorAsync("请先打开一条微博正文。");
                return;
            }

            await ShowCommentDialogAsync();
        }

        private async Task ShowCommentDialogAsync()
        {
            var targetId = _currentDetailId;
            var requestVersion = _detailRequestVersion;
            var input = new TextBox
            {
                PlaceholderText = "发表评论",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 120
            };

            var emojiButton = new Button
            {
                Width = 44,
                Height = 36,
                Content = new FontIcon { Glyph = "\uE899" }
            };
            var sendButton = new Button
            {
                Content = "发送",
                MinWidth = 84
            };

            var bottomBar = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            bottomBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottomBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(emojiButton, 0);
            Grid.SetColumn(sendButton, 2);
            bottomBar.Children.Add(emojiButton);
            bottomBar.Children.Add(sendButton);

            var panel = new StackPanel();
            panel.Children.Add(input);
            panel.Children.Add(bottomBar);

            var dialog = new ContentDialog
            {
                Title = "发表评论",
                Content = panel,
                CloseButtonText = "取消"
            };

            var emojiGroups = await LoadEmojiGroupsAsync();
            var emojiFlyout = CreateEmojiFlyout(emojiGroups, input);
            emojiButton.Flyout = emojiFlyout;

            sendButton.Click += async (buttonSender, args) =>
            {
                var text = input.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                sendButton.IsEnabled = false;
                try
                {
                    await _apiService.PostCommentAsync(targetId, text);
                    if (requestVersion == _detailRequestVersion)
                    {
                        await LoadDetailCommentsAsync(targetId, requestVersion);
                    }
                    dialog.Hide();
                }
                catch (Exception ex)
                {
                    dialog.Title = $"评论发送失败: {ex.Message}";
                }
                finally
                {
                    sendButton.IsEnabled = true;
                }
            };

            await dialog.ShowAsync();
        }

        private async Task<List<WeiboEmojiGroupViewModel>> LoadEmojiGroupsAsync()
        {
            var groups = await _apiService.GetEmojiGroupsAsync();
            var viewModels = (groups ?? new List<WeiboEmojiGroup>())
                .Where(group => group.Items != null && group.Items.Count > 0)
                .Select(group => new WeiboEmojiGroupViewModel(group))
                .ToList();

            var loadTasks = viewModels
                .SelectMany(group => group.Items)
                .Where(item => !string.IsNullOrWhiteSpace(item.Url))
                .Take(120)
                .Select(item => item.LoadAsync(_imageLoader));
            await Task.WhenAll(loadTasks);
            return viewModels;
        }

        private Flyout CreateEmojiFlyout(IEnumerable<WeiboEmojiGroupViewModel> groups, TextBox input)
        {
            var groupList = (groups ?? Enumerable.Empty<WeiboEmojiGroupViewModel>())
                .Where(group => group.Items != null && group.Items.Count > 0)
                .ToList();
            if (groupList.Count == 0)
            {
                groupList.Add(new WeiboEmojiGroupViewModel(new WeiboEmojiGroup
                {
                    Name = "常用",
                    Items = new List<WeiboEmojiItem>
                    {
                        new WeiboEmojiItem { Phrase = "[微笑]" },
                        new WeiboEmojiItem { Phrase = "[哈哈]" },
                        new WeiboEmojiItem { Phrase = "[赞]" }
                    }
                }));
            }

            var root = new Grid
            {
                Width = 360,
                Height = 300
            };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var categoryList = new ListView
            {
                ItemsSource = groupList,
                SelectionMode = ListViewSelectionMode.Single,
                IsItemClickEnabled = true
            };

            var emojiGrid = new GridView
            {
                ItemsSource = groupList[0].Items,
                IsItemClickEnabled = true,
                SelectionMode = ListViewSelectionMode.None,
                Padding = new Thickness(8, 0, 0, 0)
            };
            emojiGrid.ItemContainerStyle = new Style(typeof(GridViewItem));
            emojiGrid.ItemContainerStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, 76d));
            emojiGrid.ItemContainerStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 58d));
            emojiGrid.ItemTemplate = (DataTemplate)XamlReader.Load(
                "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<StackPanel Width=\"72\" Height=\"54\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\">" +
                "<Image Source=\"{Binding Image}\" Width=\"28\" Height=\"28\" Stretch=\"Uniform\" HorizontalAlignment=\"Center\" Visibility=\"{Binding ImageVisibility}\"/>" +
                "<TextBlock Text=\"{Binding Phrase}\" FontSize=\"11\" TextAlignment=\"Center\" TextTrimming=\"CharacterEllipsis\" MaxLines=\"1\" Margin=\"0,3,0,0\"/>" +
                "</StackPanel>" +
                "</DataTemplate>");

            categoryList.ItemClick += (sender, args) =>
            {
                var group = args.ClickedItem as WeiboEmojiGroupViewModel;
                if (group != null)
                {
                    emojiGrid.ItemsSource = group.Items;
                    categoryList.SelectedItem = group;
                }
            };

            emojiGrid.ItemClick += (sender, args) =>
            {
                var item = args.ClickedItem as WeiboEmojiItemViewModel;
                if (item == null || string.IsNullOrWhiteSpace(item.Phrase))
                {
                    return;
                }

                input.Text += item.Phrase;
                input.Focus(FocusState.Programmatic);
            };

            Grid.SetColumn(categoryList, 0);
            Grid.SetColumn(emojiGrid, 1);
            root.Children.Add(categoryList);
            root.Children.Add(emojiGrid);
            categoryList.SelectedIndex = 0;

            return new Flyout { Content = root };
        }

        private async void MediaGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var media = e.ClickedItem as WeiboImageViewModel;
            if (media == null)
            {
                return;
            }

            await OpenMediaPreviewAsync(media);
        }

        private async Task OpenMediaPreviewAsync(WeiboImageViewModel media)
        {
            CloseMediaPreview(false);
            Uri videoUri = null;
            if (media.IsVideo && !Uri.TryCreate(media.VideoUrl, UriKind.Absolute, out videoUri))
            {
                await ShowErrorAsync("视频地址无效，无法播放。");
                return;
            }

            _activePreviewIsVideo = media.IsVideo;
            _activePreviewMediaUrl = media.IsVideo ? media.VideoUrl : media.PreviewUrl;
            _activePreviewLaunchUrl = media.IsVideo ? media.VideoUrl : media.PreviewUrl;
            _activePreviewShareUrl = _activePreviewLaunchUrl;
            MediaPreviewTitle.Text = media.IsVideo ? "视频预览" : "图片预览";
            MediaPreviewBottomBar.Visibility = media.IsVideo ? Visibility.Collapsed : Visibility.Visible;
            ApplyMediaPreviewLayout();
            NavigateTo("media", true);

            if (media.IsVideo)
            {
                PreviewImageScrollViewer.Visibility = Visibility.Collapsed;
                PreviewVideoHost.Visibility = Visibility.Visible;
                var video = EnsurePreviewVideo();
                video.Width = GetMediaPreviewContentWidth();
                video.Height = GetMediaPreviewContentHeight(true);
                video.Source = videoUri;
                video.Play();
                return;
            }

            PreviewVideoHost.Visibility = Visibility.Collapsed;
            PreviewImageScrollViewer.Visibility = Visibility.Visible;
            PreviewImageScrollViewer.ChangeView(0, 0, 1, true);
            PreviewImage.MaxWidth = GetMediaPreviewContentWidth();
            PreviewImage.MaxHeight = GetMediaPreviewContentHeight(false);
            PreviewImage.Source = media.Source;

            try
            {
                var largeImage = await _imageLoader.LoadAsync(media.PreviewUrl, 1400);
                if (MediaPreviewOverlay.Visibility == Visibility.Visible &&
                    string.Equals(_activePreviewMediaUrl, media.PreviewUrl, StringComparison.Ordinal))
                {
                    PreviewImage.Source = largeImage ?? media.Source;
                }
            }
            catch
            {
                PreviewImage.Source = media.Source;
            }
        }

        private void MediaPreviewBackdrop_Tapped(object sender, TappedRoutedEventArgs e)
        {
            CloseMediaPreview(true);
        }

        private void MediaPreviewContent_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void PreviewImageScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (PreviewImageScrollViewer.Visibility != Visibility.Visible)
            {
                return;
            }

            var delta = e.GetCurrentPoint(PreviewImageScrollViewer).Properties.MouseWheelDelta;
            var nextZoom = PreviewImageScrollViewer.ZoomFactor + (delta > 0 ? 0.18f : -0.18f);
            nextZoom = Math.Max(PreviewImageScrollViewer.MinZoomFactor, Math.Min(PreviewImageScrollViewer.MaxZoomFactor, nextZoom));
            PreviewImageScrollViewer.ChangeView(null, null, nextZoom, false);
            e.Handled = true;
        }

        private void ApplyMediaPreviewLayout()
        {
            var width = GetMediaPreviewContentWidth();
            var height = GetMediaPreviewContentHeight(_activePreviewIsVideo);
            MediaPreviewBottomRow.Height = _activePreviewIsVideo ? new GridLength(0) : new GridLength(56);
            MediaPreviewContentHost.Width = width;
            MediaPreviewContentHost.Height = height;
            PreviewImageScrollViewer.Width = width;
            PreviewImageScrollViewer.Height = height;
            PreviewImage.MaxWidth = width;
            PreviewImage.MaxHeight = height;
            if (_previewVideo != null)
            {
                _previewVideo.Width = width;
                _previewVideo.Height = height;
            }
        }

        private double GetMediaPreviewContentWidth()
        {
            var width = MainContentHost.ActualWidth > 0 ? MainContentHost.ActualWidth : Window.Current.Bounds.Width;
            return Math.Max(1, width);
        }

        private double GetMediaPreviewContentHeight(bool includeBottomBar)
        {
            var height = MainContentHost.ActualHeight > 0 ? MainContentHost.ActualHeight : Window.Current.Bounds.Height;
            height -= 48;
            if (!includeBottomBar)
            {
                height -= 56;
            }

            return Math.Max(1, height);
        }

        private void CloseMediaPreview(bool hideOverlay)
        {
            if (_previewVideo != null)
            {
                _previewVideo.Stop();
                _previewVideo.Source = null;
            }
            PreviewImage.Source = null;
            PreviewImage.MaxWidth = double.PositiveInfinity;
            PreviewImage.MaxHeight = double.PositiveInfinity;
            PreviewImageScrollViewer.ChangeView(0, 0, 1, true);
            MediaPreviewBottomRow.Height = new GridLength(56);
            _activePreviewMediaUrl = null;
            _activePreviewLaunchUrl = null;
            _activePreviewShareUrl = null;
            _activePreviewImageFile = null;
            _activePreviewIsVideo = false;
            if (hideOverlay)
            {
                MediaPreviewOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void MediaPreviewBackButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateBack();
        }

        private async void MediaPreviewSaveButton_Click(object sender, RoutedEventArgs e)
        {
            Uri uri;
            if (!Uri.TryCreate(_activePreviewLaunchUrl, UriKind.Absolute, out uri))
            {
                await ShowErrorAsync("媒体直链无效，无法打开浏览器下载。");
                return;
            }

            if (_activePreviewIsVideo)
            {
                await Launcher.LaunchUriAsync(uri);
                return;
            }

            try
            {
                var imageFile = await EnsureActivePreviewImageFileAsync();
                if (imageFile == null)
                {
                    await ShowErrorAsync("图片下载失败，无法保存。");
                    return;
                }

                var extension = GetImageFileExtension(uri);
                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                    SuggestedFileName = GetImageFileName(uri, extension)
                };
                picker.FileTypeChoices.Add(GetImageFileTypeName(extension), new List<string> { extension });

                var targetFile = await picker.PickSaveFileAsync();
                if (targetFile == null)
                {
                    return;
                }

                CachedFileManager.DeferUpdates(targetFile);
                await imageFile.CopyAndReplaceAsync(targetFile);
                await CachedFileManager.CompleteUpdatesAsync(targetFile);
                await ShowErrorAsync("图片已保存。");
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"图片保存失败: {ex.Message}");
            }
        }

        private void MediaPreviewShareButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_activePreviewShareUrl))
            {
                return;
            }

            DataTransferManager.ShowShareUI();
        }

        private void OnShareDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            var url = _activePreviewShareUrl;
            Uri uri;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                args.Request.FailWithDisplayText("当前没有可分享的媒体链接。");
                return;
            }

            args.Request.Data.Properties.Title = _activePreviewIsVideo ? "分享视频" : "分享图片";
            if (_activePreviewIsVideo)
            {
                args.Request.Data.SetWebLink(uri);
                args.Request.Data.SetText(url);
                return;
            }

            var deferral = args.Request.GetDeferral();
            _ = ShareActivePreviewImageAsync(args.Request, deferral);
        }

        private async Task ShareActivePreviewImageAsync(DataRequest request, DataRequestDeferral deferral)
        {
            try
            {
                var imageFile = await EnsureActivePreviewImageFileAsync();
                if (imageFile == null)
                {
                    request.FailWithDisplayText("图片下载失败，无法分享。");
                    return;
                }

                request.Data.SetBitmap(RandomAccessStreamReference.CreateFromFile(imageFile));
                request.Data.SetStorageItems(new[] { imageFile });
            }
            catch (Exception ex)
            {
                request.FailWithDisplayText($"图片分享失败: {ex.Message}");
            }
            finally
            {
                deferral.Complete();
            }
        }

        private async Task<StorageFile> EnsureActivePreviewImageFileAsync()
        {
            if (_activePreviewImageFile != null)
            {
                return _activePreviewImageFile;
            }

            Uri uri;
            if (_activePreviewIsVideo ||
                string.IsNullOrWhiteSpace(_activePreviewLaunchUrl) ||
                !Uri.TryCreate(_activePreviewLaunchUrl, UriKind.Absolute, out uri))
            {
                return null;
            }

            var extension = GetImageFileExtension(uri);
            var fileName = GetImageFileName(uri, extension);
            var tempFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(
                fileName,
                CreationCollisionOption.GenerateUniqueName);

            using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) })
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Referrer = new Uri("https://m.weibo.cn/");
                var bytes = await client.GetByteArrayAsync(uri);
                await FileIO.WriteBytesAsync(tempFile, bytes);
            }

            _activePreviewImageFile = tempFile;
            return _activePreviewImageFile;
        }

        private static string GetImageFileExtension(Uri uri)
        {
            var path = uri?.AbsolutePath ?? string.Empty;
            var dotIndex = path.LastIndexOf('.');
            if (dotIndex >= 0 && dotIndex < path.Length - 1)
            {
                var extension = path.Substring(dotIndex).ToLowerInvariant();
                if (extension == ".jpg" || extension == ".jpeg" || extension == ".png" || extension == ".gif" || extension == ".webp")
                {
                    return extension;
                }
            }

            return ".jpg";
        }

        private static string GetImageFileName(Uri uri, string extension)
        {
            var path = uri?.AbsolutePath ?? string.Empty;
            var slashIndex = path.LastIndexOf('/');
            var rawName = slashIndex >= 0 ? path.Substring(slashIndex + 1) : path;
            if (string.IsNullOrWhiteSpace(rawName))
            {
                rawName = "weibo-image";
            }

            var dotIndex = rawName.LastIndexOf('.');
            if (dotIndex > 0)
            {
                rawName = rawName.Substring(0, dotIndex);
            }

            var invalidChars = new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
            foreach (var invalidChar in invalidChars)
            {
                rawName = rawName.Replace(invalidChar, '_');
            }

            return string.IsNullOrWhiteSpace(rawName)
                ? "weibo-image" + extension
                : rawName + extension;
        }

        private static string GetImageFileTypeName(string extension)
        {
            switch (extension)
            {
                case ".png":
                    return "PNG 图片";
                case ".gif":
                    return "GIF 图片";
                case ".webp":
                    return "WebP 图片";
                case ".jpeg":
                case ".jpg":
                default:
                    return "JPEG 图片";
            }
        }

        private async Task ShowErrorAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = message,
                CloseButtonText = "确定"
            };

            if (_activeErrorDialog != null)
            {
                return;
            }

            _activeErrorDialog = dialog;
            try
            {
                await dialog.ShowAsync();
            }
            finally
            {
                if (ReferenceEquals(_activeErrorDialog, dialog))
                {
                    _activeErrorDialog = null;
                }
            }
        }

        private static T FindDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (var index = 0; index < childCount; index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                var typedChild = child as T;
                if (typedChild != null)
                {
                    return typedChild;
                }

                var descendant = FindDescendant<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }
    }

    public class WeiboItemViewModel : INotifyPropertyChanged
    {
        public string Id { get; }
        public WeiboUser User { get; }
        public string CreatedAt { get; }
        public string Text { get; }
        public string Source { get; }
        public string MetaText { get; }
        public int RepostsCount { get; }
        public int CommentsCount { get; }
        public ImageSource ProfileImage { get; private set; }
        public ObservableCollection<WeiboImageViewModel> Pics { get; }
        public bool IsFollowing { get; private set; }
        public string FollowText => IsFollowing ? "已关注" : "关注";
        public string FollowIconGlyph => IsFollowing ? "\uE73E" : "\uE710";
        public bool IsLiked { get; private set; }
        public string LikeIconGlyph => IsLiked ? "\uE8E1" : "\uE8E3";
        public bool IsFavorited { get; private set; }
        public string FavoriteIconGlyph => IsFavorited ? "\uE735" : "\uE734";

        private int _attitudesCount;
        public int AttitudesCount
        {
            get { return _attitudesCount; }
            private set
            {
                _attitudesCount = Math.Max(0, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AttitudesCount)));
            }
        }

        public WeiboItemViewModel(WeiboMblog mblog)
        {
            Id = mblog.Id;
            User = mblog.User;
            CreatedAt = FormatTime(mblog.CreatedAt);
            Text = HtmlHelper.StripHtml(mblog.Text);
            Source = HtmlHelper.StripHtml(mblog.Source);
            MetaText = string.IsNullOrWhiteSpace(Source) ? CreatedAt : $"{CreatedAt} 来自 {Source}";
            RepostsCount = mblog.RepostsCount;
            CommentsCount = mblog.CommentsCount;
            AttitudesCount = mblog.AttitudesCount;
            IsLiked = mblog.AttitudesStatus == 1;
            IsFavorited = mblog.Favorited;
            IsFollowing = mblog.User?.Following == true;

            var videoUrl = GetVideoUrl(mblog.PageInfo?.MediaInfo);
            var picsList = mblog.Pics ?? new List<WeiboPic>();
            var mediaItems = picsList
                .Where(pic => !string.IsNullOrWhiteSpace(pic.Url))
                .Select(pic => new WeiboImageViewModel(pic.Url, pic.Large?.Url))
                .ToList();

            if (!string.IsNullOrWhiteSpace(mblog.PageInfo?.PagePic?.Url) &&
                (mediaItems.Count == 0 || !string.IsNullOrWhiteSpace(videoUrl)))
            {
                mediaItems.Add(new WeiboImageViewModel(
                    mblog.PageInfo.PagePic.Url,
                    mblog.PageInfo.PagePic.Url,
                    videoUrl));
            }

            Pics = new ObservableCollection<WeiboImageViewModel>(mediaItems);
        }

        public async Task LoadImagesAsync(WeiboImageLoader imageLoader)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(User?.ProfileImageUrl))
                {
                    ProfileImage = await imageLoader.LoadAsync(User.ProfileImageUrl, 120);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProfileImage)));
                }

                await Task.WhenAll(Pics.Select(pic => pic.LoadAsync(imageLoader)));
            }
            catch
            {
            }
        }

        public void ReleaseImages()
        {
            ProfileImage = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProfileImage)));
            foreach (var pic in Pics)
            {
                pic.ReleaseImage();
            }
        }

        public void ApplyFollowing(bool isFollowing)
        {
            IsFollowing = isFollowing;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFollowing)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FollowText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FollowIconGlyph)));
        }

        public void ApplyLiked(bool isLiked)
        {
            if (IsLiked == isLiked)
            {
                return;
            }

            IsLiked = isLiked;
            AttitudesCount += isLiked ? 1 : -1;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLiked)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LikeIconGlyph)));
        }

        public void ApplyFavorited(bool isFavorited)
        {
            if (IsFavorited == isFavorited)
            {
                return;
            }

            IsFavorited = isFavorited;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorited)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FavoriteIconGlyph)));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private static string FormatTime(string rawTime)
        {
            if (string.IsNullOrEmpty(rawTime))
            {
                return string.Empty;
            }

            var parts = rawTime.Split(' ');
            if (parts.Length < 4)
            {
                return rawTime;
            }

            var timeParts = parts[3].Split(':');
            var shortTime = timeParts.Length >= 2 ? $"{timeParts[0]}:{timeParts[1]}" : parts[3];
            return $"{parts[1]} {parts[2]} {shortTime}";
        }

        private static string GetVideoUrl(WeiboMediaInfo mediaInfo)
        {
            var candidates = new[]
            {
                mediaInfo?.Mp41080pMp4,
                mediaInfo?.Mp4720pMp4,
                mediaInfo?.Mp4HdUrl,
                mediaInfo?.StreamUrlHd,
                mediaInfo?.Mp4SdUrl,
                mediaInfo?.StreamUrl
            };

            foreach (var candidate in candidates)
            {
                var url = NormalizeMediaUrl(candidate);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    return url;
                }
            }

            return null;
        }

        private static string NormalizeMediaUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            url = url.Trim();
            if (url.StartsWith("//", StringComparison.Ordinal))
            {
                url = "https:" + url;
            }

            Uri uri;
            if (Uri.TryCreate(url, UriKind.Absolute, out uri) &&
                (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)))
            {
                return url;
            }

            return null;
        }
    }

    public class WeiboImageViewModel : INotifyPropertyChanged
    {
        public string Url { get; }
        public string PreviewUrl { get; }
        public string VideoUrl { get; }
        public bool IsVideo => !string.IsNullOrWhiteSpace(VideoUrl);
        public Visibility VideoGlyphVisibility => IsVideo ? Visibility.Visible : Visibility.Collapsed;

        private ImageSource _source;
        public ImageSource Source
        {
            get { return _source; }
            private set
            {
                _source = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Source)));
            }
        }

        public WeiboImageViewModel(string url, string previewUrl = null, string videoUrl = null)
        {
            Url = url;
            PreviewUrl = string.IsNullOrWhiteSpace(previewUrl) ? url : previewUrl;
            VideoUrl = videoUrl;
        }

        public async Task LoadAsync(WeiboImageLoader imageLoader)
        {
            try
            {
                Source = await imageLoader.LoadAsync(Url, 360);
            }
            catch
            {
            }
        }

        public void ReleaseImage()
        {
            Source = null;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class WeiboCommentViewModel : INotifyPropertyChanged
    {
        private readonly string _profileImageUrl;

        public string UserName { get; }
        public long UserId { get; }
        public string Text { get; }
        public string MetaText { get; }

        public ImageSource ProfileImage { get; private set; }

        public WeiboCommentViewModel(WeiboComment comment)
        {
            UserName = comment.User?.ScreenName ?? string.Empty;
            UserId = comment.User?.Id ?? 0;
            Text = HtmlHelper.StripHtml(comment.Text);
            MetaText = $"{FormatTime(comment.CreatedAt)}    赞 {comment.LikeCounts}";
            _profileImageUrl = comment.User?.ProfileImageUrl;
        }

        public async Task LoadImagesAsync(WeiboImageLoader imageLoader)
        {
            if (string.IsNullOrWhiteSpace(_profileImageUrl))
            {
                return;
            }

            try
            {
                ProfileImage = await imageLoader.LoadAsync(_profileImageUrl, 96);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProfileImage)));
            }
            catch
            {
            }
        }

        private static string FormatTime(string rawTime)
        {
            if (string.IsNullOrEmpty(rawTime))
            {
                return string.Empty;
            }

            var parts = rawTime.Split(' ');
            if (parts.Length < 4)
            {
                return rawTime;
            }

            var timeParts = parts[3].Split(':');
            var shortTime = timeParts.Length >= 2 ? $"{timeParts[0]}:{timeParts[1]}" : parts[3];
            return $"{parts[1]} {parts[2]} {shortTime}";
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class WeiboEmojiGroupViewModel
    {
        public string Name { get; }
        public ObservableCollection<WeiboEmojiItemViewModel> Items { get; }

        public WeiboEmojiGroupViewModel(WeiboEmojiGroup group)
        {
            Name = string.IsNullOrWhiteSpace(group?.Name) ? "表情" : group.Name;
            Items = new ObservableCollection<WeiboEmojiItemViewModel>(
                (group?.Items ?? new List<WeiboEmojiItem>())
                .Where(item => !string.IsNullOrWhiteSpace(item.Phrase))
                .Select(item => new WeiboEmojiItemViewModel(item)));
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public class WeiboEmojiItemViewModel : INotifyPropertyChanged
    {
        public string Phrase { get; }
        public string Url { get; }

        private ImageSource _image;
        public ImageSource Image
        {
            get { return _image; }
            private set
            {
                _image = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageVisibility)));
            }
        }

        public Visibility ImageVisibility => Image == null ? Visibility.Collapsed : Visibility.Visible;

        public WeiboEmojiItemViewModel(WeiboEmojiItem item)
        {
            Phrase = item.Phrase;
            Url = item.Url;
        }

        public async Task LoadAsync(WeiboImageLoader imageLoader)
        {
            if (string.IsNullOrWhiteSpace(Url))
            {
                return;
            }

            try
            {
                Image = await imageLoader.LoadAsync(Url, 64);
            }
            catch
            {
            }
        }

        public override string ToString()
        {
            return Phrase ?? string.Empty;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class WeiboUserProfileViewModel : INotifyPropertyChanged
    {
        private readonly string _avatarUrl;

        public long UserId { get; }
        public string Name { get; }
        public string VerifiedText { get; }
        public string FollowCount { get; }
        public string FollowersCount { get; }
        public bool IsFollowing { get; private set; }
        public bool IsAuthenticatedUser { get; private set; }
        public string FollowText => IsAuthenticatedUser ? "退出登录" : (IsFollowing ? "已关注" : "关注");
        public string FollowIconGlyph => IsAuthenticatedUser ? "\uE8AC" : (IsFollowing ? "\uE73E" : "\uE710");

        public ImageSource AvatarImage { get; private set; }

        public WeiboUserProfileViewModel(WeiboUser user)
        {
            UserId = user?.Id ?? 0;
            Name = user?.ScreenName ?? string.Empty;
            VerifiedText = string.IsNullOrWhiteSpace(user?.VerifiedReason)
                ? HtmlHelper.StripHtml(user?.Description)
                : $"微博认证：{HtmlHelper.StripHtml(user.VerifiedReason)}";
            FollowCount = string.IsNullOrWhiteSpace(user?.FollowCount) ? "0" : user.FollowCount;
            FollowersCount = string.IsNullOrWhiteSpace(user?.FollowersCount) ? "0" : user.FollowersCount;
            IsFollowing = user?.Following == true;
            _avatarUrl = string.IsNullOrWhiteSpace(user?.AvatarHd) ? user?.ProfileImageUrl : user.AvatarHd;
        }

        public async Task LoadImagesAsync(WeiboImageLoader imageLoader)
        {
            if (string.IsNullOrWhiteSpace(_avatarUrl))
            {
                return;
            }

            try
            {
                AvatarImage = await imageLoader.LoadAsync(_avatarUrl, 180);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvatarImage)));
            }
            catch
            {
            }
        }

        public void ApplyFollowing(bool isFollowing)
        {
            IsFollowing = isFollowing;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFollowing)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FollowText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FollowIconGlyph)));
        }

        public void MarkAsAuthenticatedUser()
        {
            IsAuthenticatedUser = true;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAuthenticatedUser)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FollowText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FollowIconGlyph)));
        }

        public void ReleaseImages()
        {
            AvatarImage = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvatarImage)));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class WeiboMessageCenterItemViewModel : INotifyPropertyChanged
    {
        private readonly Color _fallbackColor;

        public string Title { get; }
        public string Subtitle { get; }
        public string IconGlyph { get; }
        public string TargetUrl { get; }
        public string TrailingText { get; }
        public bool HasIcon => !string.IsNullOrWhiteSpace(IconGlyph);
        public Visibility IconVisibility => HasIcon ? Visibility.Visible : Visibility.Collapsed;

        private Brush _circleFill;
        public Brush CircleFill
        {
            get { return _circleFill; }
            private set
            {
                _circleFill = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CircleFill)));
            }
        }

        public WeiboMessageCenterItemViewModel(string title, string subtitle, string iconGlyph, Color fallbackColor, string targetUrl, string trailingText = null)
        {
            Title = title;
            Subtitle = subtitle;
            IconGlyph = string.IsNullOrWhiteSpace(iconGlyph) ? "\uE77B" : iconGlyph;
            TargetUrl = targetUrl;
            TrailingText = trailingText;
            _fallbackColor = fallbackColor;
            CircleFill = new SolidColorBrush(fallbackColor);
        }

        public async Task LoadAvatarAsync(WeiboImageLoader imageLoader, string avatarUrl)
        {
            await Task.CompletedTask;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class WeiboHotSearchViewModel
    {
        public int Rank { get; }
        public string Title { get; }
        public string Heat { get; }
        public string Label { get; }

        public WeiboHotSearchViewModel(WeiboHotSearchItem item, int rank)
        {
            Rank = rank;
            Title = string.IsNullOrWhiteSpace(item.Note) ? item.Word : item.Note;
            Heat = item.Num > 0 ? item.Num.ToString() : string.Empty;
            Label = string.IsNullOrWhiteSpace(item.LabelName) ? item.SmallIconDesc : item.LabelName;
        }
    }

    public class CollectionToVisibilityConverter : Windows.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is System.Collections.ICollection collection && collection.Count > 0)
            {
                return Visibility.Visible;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToVisibilityConverter : Windows.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool boolValue = value is bool b && b;
            if (parameter is string s && bool.TryParse(s, out var invert) && invert)
            {
                boolValue = !boolValue;
            }

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
