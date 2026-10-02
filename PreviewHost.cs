using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
  // Google's 16px body / ~19px titles * .75 ~ 12px / 14px, matching the result list
  // Voice input needs a speech service WebView2 doesn't have, so hide the mic buttons;
  // the top search bar and Lens buttons are unwanted too (tabs stay)
  const base = 'html{zoom:.75 !important}html,body{overflow-y:auto !important}'
    + '[aria-label*=""voice"" i],[aria-label=""Microphone"" i],#sfcnt,'
    + '[aria-label=""Upload image"" i],[aria-label*=""camera or photos"" i],[aria-label*=""Google Lens"" i]{display:none !important}';
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
})();";

        public static string UserDataFolder;
        public static Action<string> OpenExternal;

        private static DockPanel _root;
        private static Border _backBar;
        private static WebView2 _web;
        private static bool _initStarted;
        private static string _pendingUrl;
        private static string _currentSearchUrl;

        private readonly string _url;

        public PreviewHost(string url)
        {
            _url = url;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var root = GetRoot();

            // Move the shared browser into whichever host is on screen now
            if (root.Parent is ContentControl oldHost && !ReferenceEquals(oldHost, this))
                oldHost.Content = null;
            if (!ReferenceEquals(Content, root))
                Content = root;

            Navigate(_url);
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
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
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
            await core.AddScriptToExecuteOnDocumentCreatedAsync(ResultsOnlyScript);

            // Links clicked in the preview open in the real browser
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                OpenExternal?.Invoke(args.Uri);
            };
            core.NavigationStarting += (_, args) =>
            {
                // Google's own reloads (e.g. after updating location) stay in the preview
                if (args.Uri == _currentSearchUrl || !args.IsUserInitiated || IsGoogleSearchPage(args.Uri)) return;
                args.Cancel = true;
                OpenExternal?.Invoke(args.Uri);
            };

            core.DOMContentLoaded += (_, _) => _web.Visibility = Visibility.Visible;
            core.NavigationCompleted += (_, _) => _web.Visibility = Visibility.Visible;
            // Location for "near me", microphone for voice search, camera for Lens
            core.PermissionRequested += (_, args) =>
            {
                if (args.PermissionKind is not (CoreWebView2PermissionKind.Geolocation
                    or CoreWebView2PermissionKind.Microphone
                    or CoreWebView2PermissionKind.Camera)) return;
                args.State = CoreWebView2PermissionState.Allow;
                args.SavesInProfile = true;
            };

            if (_pendingUrl != null)
            {
                core.Navigate(_pendingUrl);
                _pendingUrl = null;
            }
        }

        // Google adds tracking params to the results URL, so only these mean "a different view"
        private static readonly string[] ViewParams = { "tbm=", "udm=", "lqi=", "rlst=", "rldimm=", "rlimm=", "start=", "aep=" };

        private static bool IsOffResults(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return true;
            if (u.AbsolutePath != "/search" || u.Fragment.Length > 1) return true;
            var q = u.Query.ToLowerInvariant();
            foreach (var p in ViewParams)
                if (q.Contains("&" + p) || q.Contains("?" + p)) return true;
            return false;
        }

        private static bool IsGoogleSearchPage(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return false;
            var host = u.Host;
            var isGoogle = host == "google.com" || host.EndsWith(".google.com") || host.StartsWith("www.google.");
            return isGoogle && (u.AbsolutePath == "/search" || u.AbsolutePath == "/");
        }
    }
}
