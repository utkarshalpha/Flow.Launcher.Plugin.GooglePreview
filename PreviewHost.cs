using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Flow.Launcher.Plugin.GooglePreview
{
    /// <summary>
    /// One lightweight host per result. All hosts share a single WebView2 so
    /// arrowing through results reuses one browser instead of spawning many.
    /// </summary>
    public class PreviewHost : UserControl
    {
        // Mobile layout fits the narrow preview pane
        private const string MobileUserAgent =
            "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Mobile Safari/537.36";
        // Runs before Google's page is parsed: shrink via CSS (no flash of big
        // text) and keep the page hidden until everything except the search bar,
        // tabs and results is hidden. Briefly watches the DOM so late header bits get hidden too.
        private const string ResultsOnlyScript = @"
(() => {
  // Zoom per site so text matches Flow's result list (filled in from the text size setting)
  const zoom = location.hostname.endsWith('chatgpt.com') ? __CHATGPT_ZOOM__ : __GOOGLE_ZOOM__;
  // Voice input needs a speech service WebView2 doesn't have, so hide the mic buttons;
  // the top search bar and Lens buttons are unwanted too (tabs stay)
  // On ChatGPT only the answer and the reply box stay (no sidebar, dictation, uploads, share)
  const base = 'html{zoom:' + zoom + ' !important}html,body{overflow-y:auto !important}'
    + '[aria-label*=""voice"" i],[aria-label=""Microphone"" i],#sfcnt,'
    + '[aria-label=""Upload image"" i],[aria-label*=""camera or photos"" i],[aria-label*=""Google Lens"" i],'
    + '[aria-label=""Open sidebar""],[aria-label=""Start dictation""],[aria-label=""Add files and more""],[aria-label=""Share""]'
    + '{display:none !important}';
  const style = document.createElement('style');
  style.textContent = base + 'html{visibility:hidden !important}';
  // At document creation <html> may not exist yet; attach as soon as it does
  if (document.documentElement) {
    document.documentElement.appendChild(style);
  } else {
    new MutationObserver((_, obs) => {
      if (!document.documentElement) return;
      document.documentElement.appendChild(style);
      obs.disconnect();
    }).observe(document, { childList: true });
  }

  const hide = n => n.style.setProperty('display', 'none', 'important');

  const isolate = () => {
    if (!document.body) return false;
    // Menu button and the logo / Sign in row; the search bar (voice, Lens)
    // and the AI Mode / All / Images tabs in the same header stay
    for (const sel of ['#navd', '#qslc', '#gb', '#footcnt', '#lfootercc'])
      document.querySelectorAll(sel).forEach(hide);

    // Only trim the normal results view; AI Mode, Images etc. have their own layouts
    if (/[?&](udm|tbm)=/.test(location.search)) return true;
    const results = document.querySelector('#center_col') || document.querySelector('#rso');
    if (!results) return false;
    const keep = [results, document.querySelector('body > header')].filter(Boolean);
    const onPath = new Set();
    for (const k of keep)
      for (let el = k; el && el !== document.body; el = el.parentElement) onPath.add(el);
    // The visible AI Mode / All / Images row sits beside the results, not in the header
    const isTabs = n => [...n.querySelectorAll('a, [role=""listitem""]')].some(e => /^(AI Mode|All)$/.test(e.textContent.trim()));
    for (const el of onPath)
      for (const sib of el.parentElement.children)
        if (!onPath.has(sib) && !['SCRIPT', 'STYLE', 'LINK'].includes(sib.tagName) && !isTabs(sib)) hide(sib);
    return true;
  };

  const reveal = () => {
    isolate();
    style.textContent = base;
  };

  document.addEventListener('DOMContentLoaded', () => {
    reveal();
    // Catch header bits Google adds while loading, then stop so menus and
    // popups the user opens (e.g. a result's ⋮ menu) are left alone
    const obs = new MutationObserver(isolate);
    obs.observe(document.body, { childList: true, subtree: false });
    setTimeout(() => obs.disconnect(), 2000);
  });
  // Never stay blank if Google's layout is unexpected
  setTimeout(reveal, 2500);

  // ChatGPT draws its Log in buttons after it starts up and they have no label to target in CSS
  if (location.hostname.endsWith('chatgpt.com')) {
    const hideLogin = () => document.querySelectorAll('button, a').forEach(b => {
      if (/^(Log in|Sign up for free|Sign up)$/.test(b.textContent.trim())) hide(b);
    });
    const timer = setInterval(hideLogin, 400);
    setTimeout(() => clearInterval(timer), 15000);
  }
})();";

        // Text size at 100%: Google's 16px titles / 14px text -> 14 / 12, ChatGPT's 16px text -> 12,
        // matching Flow's result list (measured on the live pages)
        private const double GoogleZoom = 0.875;
        private const double ChatGptZoom = 0.75;

        public static string UserDataFolder;
        public static Action<string> OpenExternal;
        public static Settings Settings = new();

        private static DockPanel _root;
        private static Border _backBar;
        private static WebView2 _web;
        private static bool _initStarted;
        private static string _pendingUrl;
        private static string _currentSearchUrl;
        private static string _scriptId;
        private static int _scriptTextSize;

        // When the user last moved the selection vs. last typed, to tell a pick from auto-selection
        private static long _lastPick;
        private static long _lastTyping;
        private static bool _inputHooked;
        private static readonly DispatcherTimer LoadTimer = new();
        private static PreviewHost _waitingHost;

        private readonly string _url;

        public PreviewHost(string url)
        {
            _url = url;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            HookInput();
            GetRoot();

            // Flow re-shows the same result (e.g. when other plugins' results arrive); keep it as is
            if (ReferenceEquals(_root.Parent, this) && _currentSearchUrl == _url) return;

            if (Settings.PreviewOnSelect)
            {
                if (_lastPick > _lastTyping)
                    ScheduleLoad(this, 150); // short pause so holding ↓ doesn't load every row
                else
                    ShowHint();
            }
            else
            {
                Content = null;
                ScheduleLoad(this, Settings.PreviewDelayMs);
            }
        }

        private void ShowHint()
        {
            var text = new TextBlock
            {
                Text = Settings.ShowHint ? Settings.HintText : "",
                Opacity = 0.6,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            // Clicking the hint loads this result right away
            var area = new Border { Background = Brushes.Transparent, Child = text, Cursor = Cursors.Hand };
            area.MouseLeftButtonUp += (_, _) => Load();
            Content = area;
        }

        private static void ScheduleLoad(PreviewHost host, int delayMs)
        {
            _waitingHost = host;
            LoadTimer.Stop();
            LoadTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMs));
            LoadTimer.Start();
        }

        private static void OnLoadTimer(object sender, EventArgs e)
        {
            LoadTimer.Stop();
            var host = _waitingHost;
            _waitingHost = null;
            if (host != null && host.IsLoaded) host.Load();
        }

        private void Load()
        {
            // Move the shared browser into whichever host is on screen now
            if (_root.Parent is ContentControl oldHost && !ReferenceEquals(oldHost, this))
                oldHost.Content = null;
            if (!ReferenceEquals(Content, _root))
                Content = _root;
            Navigate(_url);
        }

        private static void HookInput()
        {
            if (_inputHooked) return;
            _inputHooked = true;
            LoadTimer.Tick += OnLoadTimer;
            // Flow's window is in this process, so class handlers see its keys and clicks
            EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
            {
                switch (e.Key)
                {
                    case Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Tab:
                        _lastPick = Environment.TickCount64;
                        break;
                    case Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                        or Key.LWin or Key.RWin or Key.Left or Key.Right or Key.Escape or Key.Enter or Key.F1 or Key.System:
                        break;
                    default:
                        _lastTyping = Environment.TickCount64;
                        break;
                }
            }), true);
            EventManager.RegisterClassHandler(typeof(Window), Mouse.PreviewMouseDownEvent,
                new MouseButtonEventHandler((_, _) => _lastPick = Environment.TickCount64), true);
        }

        private static DockPanel GetRoot()
        {
            if (_root != null) return _root;

            _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.White };
            _web.CoreWebView2InitializationCompleted += OnInitCompleted;

            // WebView2 is a native window, so WPF can't draw over it; the back
            // control is a slim bar above it
            var back = new Button
            {
                Content = "← Back to results",
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 2, 8, 2),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 12,
            };
            // One step back, but never past this search's results into an older search
            back.Click += (_, _) =>
            {
                var core = _web.CoreWebView2;
                if (core != null && core.CanGoBack && IsOffResults(core.Source))
                    core.GoBack();
            };
            _backBar = new Border { Child = back, Padding = new Thickness(0, 2, 0, 2) };

            _root = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_backBar, Dock.Top);
            _root.Children.Add(_backBar);
            _root.Children.Add(_web);
            return _root;
        }

        private static async void Navigate(string url)
        {
            _currentSearchUrl = url;
            if (_web.CoreWebView2 != null)
            {
                // Hide the previous search until the new page is ready
                _web.Visibility = Visibility.Hidden;
                await ApplyTextSizeAsync(_web.CoreWebView2);
                if (_currentSearchUrl != url) return; // a newer pick arrived meanwhile
                _web.CoreWebView2.Navigate(url);
                return;
            }

            _pendingUrl = url;
            if (_initStarted) return;
            _initStarted = true;
            try
            {
                // Without this Google can guess the wrong language (AI Mode once answered in Polish)
                var options = new CoreWebView2EnvironmentOptions { Language = CultureInfo.CurrentUICulture.Name };
                var env = await CoreWebView2Environment.CreateAsync(null, UserDataFolder, options);
                await _web.EnsureCoreWebView2Async(env);
            }
            catch
            {
                _initStarted = false;
            }
        }

        private static async void OnInitCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess) return;

            var core = _web.CoreWebView2;
            core.Settings.UserAgent = MobileUserAgent;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            await ApplyTextSizeAsync(core);

            // Links clicked in the preview open in the real browser
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                OpenExternal?.Invoke(args.Uri);
            };
            core.NavigationStarting += (_, args) =>
            {
                // Google's own reloads (e.g. after updating location) and ChatGPT stay in the preview
                if (args.Uri == _currentSearchUrl || !args.IsUserInitiated || IsGoogleSearchPage(args.Uri) || IsChatGpt(args.Uri)) return;
                args.Cancel = true;
                OpenExternal?.Invoke(args.Uri);
            };

            core.DOMContentLoaded += (_, _) => _web.Visibility = Visibility.Visible;
            core.NavigationCompleted += (_, _) => _web.Visibility = Visibility.Visible;

            // Older versions saved an "allow" for these in the profile; clear it so
            // the setting below is what decides
            foreach (var kind in new[] { CoreWebView2PermissionKind.Geolocation, CoreWebView2PermissionKind.Microphone, CoreWebView2PermissionKind.Camera })
                await core.Profile.SetPermissionStateAsync(kind, "https://www.google.com", CoreWebView2PermissionState.Default);

            // Location only if the user turned it on; camera and mic are never used
            core.PermissionRequested += (_, args) =>
            {
                args.SavesInProfile = false;
                if (args.PermissionKind == CoreWebView2PermissionKind.Geolocation)
                    args.State = Settings.AllowLocation ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
                else if (args.PermissionKind is CoreWebView2PermissionKind.Microphone or CoreWebView2PermissionKind.Camera)
                    args.State = CoreWebView2PermissionState.Deny;
            };

            if (_pendingUrl != null)
            {
                core.Navigate(_pendingUrl);
                _pendingUrl = null;
            }
        }

        // The page script carries the zoom, so re-register it when the text size setting changes
        private static async System.Threading.Tasks.Task ApplyTextSizeAsync(CoreWebView2 core)
        {
            var size = Math.Clamp(Settings.TextSizePercent, 50, 200);
            if (_scriptId != null && _scriptTextSize == size) return;
            if (_scriptId != null) core.RemoveScriptToExecuteOnDocumentCreated(_scriptId);
            var scale = size / 100.0;
            var script = ResultsOnlyScript
                .Replace("__GOOGLE_ZOOM__", (GoogleZoom * scale).ToString("0.###", CultureInfo.InvariantCulture))
                .Replace("__CHATGPT_ZOOM__", (ChatGptZoom * scale).ToString("0.###", CultureInfo.InvariantCulture));
            _scriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            _scriptTextSize = size;
        }

        // Google adds tracking params to the results URL, so only these mean "a different view"
        private static readonly string[] ViewParams = { "tbm=", "udm=", "lqi=", "rlst=", "rldimm=", "rlimm=", "start=", "aep=" };

        private static bool IsOffResults(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return true;
            // A ChatGPT answer is its own page; going back would land on an older search
            if (IsChatGpt(uri)) return false;
            if (u.AbsolutePath != "/search" || u.Fragment.Length > 1) return true;
            var q = u.Query.ToLowerInvariant();
            foreach (var p in ViewParams)
                if (q.Contains("&" + p) || q.Contains("?" + p)) return true;
            return false;
        }

        private static bool IsChatGpt(string uri) =>
            Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Host == "chatgpt.com" || u.Host.EndsWith(".chatgpt.com"));

        private static bool IsGoogleSearchPage(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return false;
            var host = u.Host;
            var isGoogle = host == "google.com" || host.EndsWith(".google.com") || host.StartsWith("www.google.");
            return isGoogle && (u.AbsolutePath == "/search" || u.AbsolutePath == "/");
        }
    }
}
