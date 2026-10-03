using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Flow.Launcher.Plugin.GooglePreview
{
    public class Main : IAsyncPlugin, ISettingProvider
    {
        // Below a real app-name match (Flow scores those ~100+), above weak matches:
        // typed text first, ChatGPT second, then suggestions
        private const int BaseScore = 90;

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

        private PluginInitContext _context;
        private Settings _settings;
        private string _icon;
        private string _chatGptIcon;

        public Task InitAsync(PluginInitContext context)
        {
            _context = context;
            _settings = context.API.LoadSettingJsonStorage<Settings>();
            _icon = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "google-g.png");
            _chatGptIcon = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "chatgpt.png");
            PreviewHost.UserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FlowLauncher", "GooglePreviewWebView2");
            PreviewHost.OpenExternal = url => _context.API.OpenUrl(url);
            PreviewHost.Settings = _settings;
            return Task.CompletedTask;
        }

        public Control CreateSettingPanel() =>
            new SettingsPanel(_settings, () => _context.API.SaveSettingJsonStorage<Settings>());

        public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            var text = query.Search?.Trim();
            if (string.IsNullOrEmpty(text))
                return new List<Result>();

            var terms = new List<string> { text };
            if (_settings.ShowSuggestions)
            {
                // Wait for typing to pause before hitting the network
                await Task.Delay(120, token);
                var max = Math.Clamp(_settings.SuggestionCount, 1, 5);
                try
                {
                    foreach (var s in await FetchSuggestionsAsync(text, token))
                    {
                        if (terms.Count > max) break;
                        if (!terms.Exists(t => string.Equals(t, s, StringComparison.OrdinalIgnoreCase)))
                            terms.Add(s);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Offline or blocked: still offer the plain search
                }
            }

            var results = new List<Result> { MakeResult(terms[0], BaseScore) };
            if (_settings.ShowChatGpt)
                results.Add(MakeChatGptResult(text, BaseScore - 1));
            for (var i = 1; i < terms.Count; i++)
                results.Add(MakeResult(terms[i], BaseScore - 1 - i));
            return results;
        }

        private Result MakeChatGptResult(string text, int score)
        {
            var url = "https://chatgpt.com/?q=" + Uri.EscapeDataString(text);
            return new Result
            {
                Title = text,
                SubTitle = "Ask ChatGPT",
                IcoPath = _chatGptIcon,
                Score = score,
                Action = _ =>
                {
                    _context.API.OpenUrl(url);
                    return true;
                },
                // Works logged out; the answer appears right in the preview
                PreviewPanel = _settings.ChatGptPreview ? new Lazy<UserControl>(() => new PreviewHost(url)) : null,
            };
        }

        private Result MakeResult(string term, int score)
        {
            var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(term);
            return new Result
            {
                Title = term,
                SubTitle = "Search Google",
                IcoPath = _icon,
                Score = score,
                Action = _ =>
                {
                    _context.API.OpenUrl(url);
                    return true;
                },
                PreviewPanel = new Lazy<UserControl>(() => new PreviewHost(url + "&hl=" + CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)),
            };
        }

        private static async Task<List<string>> FetchSuggestionsAsync(string text, CancellationToken token)
        {
            var api = "https://suggestqueries.google.com/complete/search?client=chrome&q=" + Uri.EscapeDataString(text);
            var json = await Http.GetStringAsync(api, token);
            using var doc = JsonDocument.Parse(json);
            var list = new List<string>();
            foreach (var item in doc.RootElement[1].EnumerateArray())
                list.Add(item.GetString());
            return list;
        }
    }
}
